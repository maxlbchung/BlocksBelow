using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class AudioClipDropdownAttribute : PropertyAttribute
{
}

public class AudioController : MonoBehaviour
{
    private const string SfxGroupName = "SFX";
    private const string MusicGroupName = "Music";
    private const string SfxVolumeParameter = "SFXVolume";
    private const string MusicVolumeParameter = "MusicVolume";
    private const string SfxVolumePreference = "Audio.SFXVolume";
    private const string MusicVolumePreference = "Audio.MusicVolume";

    // The mixer's own floor. Anything at or below this reads back as silence rather than as
    // the very small fraction the decibel curve would otherwise turn it into.
    private const float MinimumDecibels = -80f;

    [Serializable]
    public class AudioEntry
    {
        public string clipName;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        [Range(0.01f, 3f)] public float pitch = 1f;
        [Tooltip("Random pitch variation above or below the base pitch.")]
        [Range(0f, 1f)] public float pitchShift;
    }

    [Header("Audio Library")]
    [SerializeField] private List<AudioEntry> audioClips = new();
    [SerializeField] private List<AudioEntry> musicTracks = new();

    [Header("Audio Mixer")]
    [SerializeField] private AudioMixer audioMixer;

    [Header("Starting Volumes")]
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
    [SerializeField, Range(0f, 1f)] private float musicVolume = 1f;
    [SerializeField, Tooltip("Optional music track started when this scene loads.")]
    private string musicOnStart;
    [SerializeField, Min(0f)] private float musicCrossfadeDuration = 1.5f;

    [Header("SFX Voices")]
    [SerializeField, Min(1), Tooltip("Sound effects that may overlap. Built once at Awake and "
        + "reused; the oldest voice is stolen when they are all busy.")]
    private int sfxVoiceCount = 24;

    private static AudioController instance;

    /// <summary>
    /// Raised whenever a volume is written to the mixer, so every settings page showing that
    /// volume redraws from the mixer instead of holding whatever it was built with.
    /// </summary>
    public static event Action VolumesChanged;

    private AudioSource[] musicSources;
    private int activeMusicSource;
    private Coroutine musicCrossfadeRoutine;
    private AudioMixerGroup sfxMixerGroup;
    private AudioMixerGroup musicMixerGroup;

    // Creating a GameObject and AddComponent<AudioSource> per sound was the single most
    // expensive thing towers did during a wave - a saw blade plays one per enemy touched.
    // These voices are built once and round-robined instead.
    private AudioSource[] sfxVoices;
    private int nextVoiceIndex;

    private readonly Dictionary<AudioClip, AudioEntry> entriesByClip = new();
    private readonly Dictionary<string, AudioEntry> entriesByName =
        new(StringComparer.OrdinalIgnoreCase);

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("More than one AudioController exists in the scene.", this);
            enabled = false;
            return;
        }

        instance = this;

        sfxMixerGroup = FindMixerGroup(SfxGroupName);
        musicMixerGroup = FindMixerGroup(MusicGroupName);

        sfxVolume = PlayerPrefs.GetFloat(SfxVolumePreference, sfxVolume);
        musicVolume = PlayerPrefs.GetFloat(MusicVolumePreference, musicVolume);
        ApplySavedVolumes();
        VolumesChanged?.Invoke();

        BuildEntryLookups();

        musicSources = new AudioSource[2];
        for (int i = 0; i < musicSources.Length; i++)
        {
            GameObject musicObject = new($"Music Audio Source {i + 1}");
            musicObject.transform.SetParent(transform);
            AudioSource source = musicObject.AddComponent<AudioSource>();
            source.outputAudioMixerGroup = musicMixerGroup;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            musicSources[i] = source;
        }

        BuildSfxVoices();
    }

    /// <summary>
    /// Indexes the library once so playing a sound is a dictionary hit instead of a
    /// linear scan with a per-call closure allocation.
    /// </summary>
    private void BuildEntryLookups()
    {
        entriesByClip.Clear();
        entriesByName.Clear();

        for (int i = 0; i < audioClips.Count; i++)
        {
            AudioEntry entry = audioClips[i];
            if (entry == null)
            {
                continue;
            }

            if (entry.clip != null)
            {
                entriesByClip[entry.clip] = entry;
            }

            if (!string.IsNullOrEmpty(entry.clipName))
            {
                entriesByName[entry.clipName] = entry;
            }
        }
    }

    private void BuildSfxVoices()
    {
        GameObject voiceRoot = new("SFX Voices");
        voiceRoot.transform.SetParent(transform, false);

        sfxVoices = new AudioSource[Mathf.Max(1, sfxVoiceCount)];
        for (int i = 0; i < sfxVoices.Length; i++)
        {
            AudioSource voice = voiceRoot.AddComponent<AudioSource>();
            voice.outputAudioMixerGroup = sfxMixerGroup;
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0f;
            sfxVoices[i] = voice;
        }
    }

    /// <summary>
    /// Returns an idle voice, or steals the oldest one when every voice is busy.
    /// Round-robin means the stolen voice is always the longest-running.
    /// </summary>
    private AudioSource GetFreeVoice()
    {
        if (sfxVoices == null || sfxVoices.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < sfxVoices.Length; i++)
        {
            int index = (nextVoiceIndex + i) % sfxVoices.Length;
            AudioSource candidate = sfxVoices[index];
            if (candidate != null && !candidate.isPlaying)
            {
                nextVoiceIndex = (index + 1) % sfxVoices.Length;
                return candidate;
            }
        }

        AudioSource stolen = sfxVoices[nextVoiceIndex];
        nextVoiceIndex = (nextVoiceIndex + 1) % sfxVoices.Length;
        return stolen;
    }

    private void PlayOnVoice(AudioClip clip, float volume, float pitch)
    {
        AudioSource voice = GetFreeVoice();
        if (voice == null)
        {
            return;
        }

        voice.Stop();
        voice.clip = clip;
        voice.volume = Mathf.Clamp01(volume);
        voice.pitch = pitch;
        voice.outputAudioMixerGroup = sfxMixerGroup;
        voice.Play();
    }

    /// <summary>
    /// Puts the saved levels back onto the mixer once the scene is up.
    ///
    /// The mixer brings its start snapshot up after Awake has run, and that snapshot carries
    /// its own values for the exposed volumes - this one has SFX baked in near full - so the
    /// levels written during Awake are quietly undone. That is what kept turning the sound
    /// back on at the start of a run however many times it had been set to silent. Asserted
    /// again a frame later because the snapshot lands on the audio system's own schedule
    /// rather than ours, and Start is not reliably after it.
    /// </summary>
    private void Start()
    {
        ApplySavedVolumes();
        StartCoroutine(ReassertSavedVolumes());
        if (!string.IsNullOrWhiteSpace(musicOnStart))
        {
            CrossfadeMusic(musicOnStart, musicCrossfadeDuration);
        }
    }

    private IEnumerator ReassertSavedVolumes()
    {
        yield return null;
        ApplySavedVolumes();
    }

    private void ApplySavedVolumes()
    {
        ApplyMixerVolume(SfxVolumeParameter, sfxVolume);
        ApplyMixerVolume(MusicVolumeParameter, musicVolume);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public static void Play(
        string clipName,
        float volume = 1f,
        float pitch = 1f,
        float pitchVariance = 0f)
    {
        if (instance == null)
        {
            Debug.LogWarning("Cannot play audio because there is no AudioController in the scene.");
            return;
        }

        if (!instance.entriesByName.TryGetValue(clipName, out AudioEntry entry)
            || entry.clip == null)
        {
            Debug.LogWarning($"Audio clip '{clipName}' was not found in the AudioController.", instance);
            return;
        }

        float variance = Mathf.Abs(entry.pitchShift + pitchVariance);
        float finalPitch = Mathf.Clamp(
            entry.pitch * pitch + UnityEngine.Random.Range(-variance, variance),
            0.01f,
            3f
        );

        instance.PlayOnVoice(entry.clip, entry.volume * volume, finalPitch);
    }

    public static void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (instance == null || clip == null)
        {
            return;
        }

        instance.entriesByClip.TryGetValue(clip, out AudioEntry entry);
        float entryVolume = entry != null ? entry.volume : 1f;
        float entryPitch = entry != null ? entry.pitch : 1f;
        float pitchShift = entry != null ? entry.pitchShift : 0f;
        float finalPitch = Mathf.Clamp(
            entryPitch * pitch + UnityEngine.Random.Range(-pitchShift, pitchShift),
            0.01f,
            3f);

        instance.PlayOnVoice(clip, entryVolume * volume, finalPitch);
    }

    /// <summary>
    /// Starts a looping SFX on its owner. Looping sounds use a dedicated source so the
    /// shared one-shot voice pool cannot cut them off. The source still runs through the
    /// SFX mixer and honours the clip's library volume and pitch.
    /// </summary>
    public static AudioSource PlayLoop(
        AudioClip clip,
        GameObject owner,
        float volume = 1f,
        float pitch = 1f)
    {
        if (instance == null || clip == null || owner == null)
        {
            return null;
        }

        AudioSource source = owner.GetComponent<AudioSource>();
        if (source == null)
        {
            source = owner.AddComponent<AudioSource>();
        }

        instance.entriesByClip.TryGetValue(clip, out AudioEntry entry);
        source.Stop();
        source.clip = clip;
        source.volume = Mathf.Clamp01((entry != null ? entry.volume : 1f) * volume);
        source.pitch = Mathf.Clamp(
            (entry != null ? entry.pitch : 1f) * pitch,
            0.01f,
            3f);
        source.outputAudioMixerGroup = instance.sfxMixerGroup;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.loop = true;
        source.Play();
        return source;
    }

    public static void StopLoop(AudioSource source)
    {
        if (source == null)
        {
            return;
        }

        source.Stop();
        source.clip = null;
        source.loop = false;
    }

    public static void PlayMusic(string trackName, float volume = 1f, float pitch = 1f)
    {
        if (instance == null)
        {
            Debug.LogWarning("Cannot play music because there is no AudioController in the scene.");
            return;
        }

        AudioClip track = instance.FindClip(trackName, instance.musicTracks);

        if (track == null)
        {
            Debug.LogWarning($"Music track '{trackName}' was not found in the AudioController.", instance);
            return;
        }

        if (instance.musicCrossfadeRoutine != null)
        {
            instance.StopCoroutine(instance.musicCrossfadeRoutine);
            instance.musicCrossfadeRoutine = null;
        }

        AudioSource source = instance.musicSources[instance.activeMusicSource];
        source.clip = track;
        source.volume = Mathf.Clamp01(volume);
        source.pitch = Mathf.Clamp(pitch, 0.01f, 3f);
        source.Play();
    }

    public static void CrossfadeMusic(string trackName, float duration = 1.5f)
    {
        if (instance == null)
        {
            return;
        }

        AudioEntry entry = instance.FindEntry(trackName, instance.musicTracks);
        if (entry == null || entry.clip == null)
        {
            Debug.LogWarning($"Music track '{trackName}' was not found in the AudioController.", instance);
            return;
        }

        AudioSource current = instance.musicSources[instance.activeMusicSource];
        if (current.isPlaying && current.clip == entry.clip)
        {
            return;
        }

        if (instance.musicCrossfadeRoutine != null)
        {
            instance.StopCoroutine(instance.musicCrossfadeRoutine);
        }

        instance.musicCrossfadeRoutine =
            instance.StartCoroutine(instance.CrossfadeMusicRoutine(entry, duration));
    }

    private IEnumerator CrossfadeMusicRoutine(AudioEntry entry, float duration)
    {
        AudioSource outgoing = musicSources[activeMusicSource];
        int incomingIndex = 1 - activeMusicSource;
        AudioSource incoming = musicSources[incomingIndex];
        float targetVolume = Mathf.Clamp01(entry.volume);
        float outgoingStartVolume = outgoing.volume;

        incoming.Stop();
        incoming.clip = entry.clip;
        incoming.pitch = Mathf.Clamp(entry.pitch, 0.01f, 3f);
        incoming.volume = 0f;
        incoming.Play();

        float fadeDuration = Mathf.Max(0f, duration);
        if (fadeDuration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / fadeDuration);
                outgoing.volume = Mathf.Lerp(outgoingStartVolume, 0f, progress);
                incoming.volume = Mathf.Lerp(0f, targetVolume, progress);
                yield return null;
            }
        }

        outgoing.Stop();
        outgoing.clip = null;
        outgoing.volume = 0f;
        incoming.volume = targetVolume;
        activeMusicSource = incomingIndex;
        musicCrossfadeRoutine = null;
    }

    public static void StopMusic()
    {
        if (instance != null)
        {
            if (instance.musicCrossfadeRoutine != null)
            {
                instance.StopCoroutine(instance.musicCrossfadeRoutine);
                instance.musicCrossfadeRoutine = null;
            }

            for (int i = 0; i < instance.musicSources.Length; i++)
            {
                instance.musicSources[i].Stop();
            }
        }
    }

    // Both setters record the choice before looking for a controller: a settings page in a
    // scene with no AudioManager used to drop the change on the floor, so the slider sprang
    // back to its old value the next time it was opened.
    public static void SetSfxVolume(float volume)
    {
        float clamped = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(SfxVolumePreference, clamped);
        PlayerPrefs.Save();

        if (instance != null)
        {
            instance.sfxVolume = clamped;
            instance.ApplyMixerVolume(SfxVolumeParameter, clamped);
        }

        VolumesChanged?.Invoke();
    }

    public static void SetMusicVolume(float volume)
    {
        float clamped = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MusicVolumePreference, clamped);
        PlayerPrefs.Save();

        if (instance != null)
        {
            instance.musicVolume = clamped;
            instance.ApplyMixerVolume(MusicVolumeParameter, clamped);
        }

        VolumesChanged?.Invoke();
    }

    // The saved level is what these report, not what the mixer reads back. Reading the mixer
    // looked like the more honest answer, but an AudioMixer is an output, not a store: the
    // editor bakes runtime SetFloat calls into the asset's snapshot, and a parameter that was
    // never written is simply absent and answers 0 dB. Either way GetFloat can hand back a
    // level nobody chose, and because the sliders redraw from these, the popup would overwrite
    // the player's choice with that stale snapshot the instant they made it.
    //
    // SetFloat is checked and warned about below, so this is the level the mixer is running at
    // whenever the write is landing at all.
    public static float SfxVolume => instance != null
        ? Mathf.Clamp01(instance.sfxVolume)
        : PlayerPrefs.GetFloat(SfxVolumePreference, 1f);

    public static float MusicVolume => instance != null
        ? Mathf.Clamp01(instance.musicVolume)
        : PlayerPrefs.GetFloat(MusicVolumePreference, 1f);

    private static float NormalizedToDecibels(float normalized)
    {
        return normalized <= 0f ? MinimumDecibels : Mathf.Log10(normalized) * 20f;
    }

    private AudioClip FindClip(string clipName)
    {
        return FindClip(clipName, audioClips);
    }

    private AudioClip FindClip(string clipName, List<AudioEntry> entries)
    {
        return FindEntry(clipName, entries)?.clip;
    }

    private AudioEntry FindEntry(string clipName, List<AudioEntry> entries)
    {
        return entries.Find(entry => entry != null
            && string.Equals(entry.clipName, clipName, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyMixerVolume(string parameterName, float normalizedVolume)
    {
        if (audioMixer == null)
        {
            Debug.LogWarning("AudioController needs an AudioMixer assigned.", this);
            return;
        }

        if (!audioMixer.SetFloat(parameterName, NormalizedToDecibels(normalizedVolume)))
        {
            Debug.LogWarning(
                $"Audio Mixer parameter '{parameterName}' is not exposed or does not exist.",
                this
            );
        }
    }

    private AudioMixerGroup FindMixerGroup(string groupName)
    {
        if (audioMixer == null)
        {
            Debug.LogWarning("AudioController needs NewAudioMixer assigned.", this);
            return null;
        }

        AudioMixerGroup[] groups = audioMixer.FindMatchingGroups(groupName);
        if (groups.Length > 0)
        {
            return groups[0];
        }

        Debug.LogWarning(
            $"Audio Mixer group '{groupName}' was not found in NewAudioMixer.",
            this
        );
        return null;
    }

#if UNITY_EDITOR
    [ContextMenu("Scan Assets/Audio Into Library")]
    public void ScanAudioLibrary()
    {
        const string audioRoot = "Assets/Audio";

        Dictionary<AudioClip, AudioEntry> existingEntries = audioClips
            .Concat(musicTracks)
            .Where(entry => entry != null && entry.clip != null)
            .GroupBy(entry => entry.clip)
            .ToDictionary(group => group.Key, group => group.First());

        var discoveredClips = UnityEditor.AssetDatabase
            .FindAssets("t:AudioClip", new[] { audioRoot })
            .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
            .Select(path => new
            {
                Path = path,
                Clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path)
            })
            .Where(item => item.Clip != null)
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        UnityEditor.Undo.RecordObject(this, "Scan Audio Library");

        audioClips = BuildAudioEntries(
            discoveredClips
                .Where(item => !IsInMusicFolder(item.Path))
                .Select(item => item.Clip),
            existingEntries);

        musicTracks = BuildAudioEntries(
            discoveredClips
                .Where(item => IsInMusicFolder(item.Path))
                .Select(item => item.Clip),
            existingEntries);

        UnityEditor.EditorUtility.SetDirty(this);
    }

    private static List<AudioEntry> BuildAudioEntries(
        IEnumerable<AudioClip> clips,
        IReadOnlyDictionary<AudioClip, AudioEntry> existingEntries)
    {
        return clips.Select(clip =>
        {
            if (existingEntries.TryGetValue(clip, out AudioEntry existing))
            {
                if (string.IsNullOrWhiteSpace(existing.clipName))
                {
                    existing.clipName = clip.name;
                }

                return existing;
            }

            return new AudioEntry
            {
                clipName = clip.name,
                clip = clip
            };
        }).ToList();
    }

    private static bool IsInMusicFolder(string assetPath)
    {
        string normalizedPath = assetPath.Replace('\\', '/');
        return normalizedPath.IndexOf(
            "/Music/",
            StringComparison.OrdinalIgnoreCase) >= 0;
    }
#endif
}

/// <summary>
/// Adds the shared click cue to every scene-authored and runtime-created UI button.
/// Buttons are discovered continuously because most gameplay UI is built after scene load.
/// </summary>
public sealed class GlobalButtonSound : MonoBehaviour
{
    private const string ButtonClipName = "Button";
    private readonly HashSet<Button> hookedButtons = new HashSet<Button>();
    private readonly WaitForSecondsRealtime scanDelay = new WaitForSecondsRealtime(0.5f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (FindFirstObjectByType<GlobalButtonSound>() != null)
        {
            return;
        }

        GameObject soundObject = new GameObject("Global Button Sound");
        DontDestroyOnLoad(soundObject);
        soundObject.AddComponent<GlobalButtonSound>();
    }

    private IEnumerator Start()
    {
        while (true)
        {
            Button[] buttons = FindObjectsByType<Button>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < buttons.Length; i++)
            {
                Button button = buttons[i];
                if (button != null && hookedButtons.Add(button))
                {
                    button.onClick.AddListener(PlayButtonSound);
                }
            }

            hookedButtons.RemoveWhere(button => button == null);
            yield return scanDelay;
        }
    }

    private static void PlayButtonSound()
    {
        AudioController.Play(ButtonClipName);
    }
}
