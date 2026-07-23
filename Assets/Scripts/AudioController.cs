using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

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

    private static AudioController instance;
    private AudioSource musicSource;
    private AudioMixerGroup sfxMixerGroup;
    private AudioMixerGroup musicMixerGroup;

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
        ApplyMixerVolume(SfxVolumeParameter, sfxVolume);
        ApplyMixerVolume(MusicVolumeParameter, musicVolume);

        GameObject musicObject = new("Music Audio Source");
        musicObject.transform.SetParent(transform);

        musicSource = musicObject.AddComponent<AudioSource>();
        musicSource.outputAudioMixerGroup = musicMixerGroup;
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
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

        AudioEntry entry = instance.FindEntry(clipName, instance.audioClips);

        if (entry == null || entry.clip == null)
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

        GameObject audioObject = new($"Audio - {clipName}");
        audioObject.transform.SetParent(instance.transform);

        AudioSource source = audioObject.AddComponent<AudioSource>();
        source.clip = entry.clip;
        source.volume = Mathf.Clamp01(entry.volume * volume);
        source.pitch = finalPitch;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = instance.sfxMixerGroup;
        source.Play();

        Destroy(audioObject, entry.clip.length / finalPitch + 0.1f);
    }

    public static void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (instance == null || clip == null)
        {
            return;
        }

        AudioEntry entry = instance.audioClips.Find(item => item != null && item.clip == clip);
        float entryVolume = entry != null ? entry.volume : 1f;
        float entryPitch = entry != null ? entry.pitch : 1f;
        float pitchShift = entry != null ? entry.pitchShift : 0f;
        float finalPitch = Mathf.Clamp(
            entryPitch * pitch + UnityEngine.Random.Range(-pitchShift, pitchShift),
            0.01f,
            3f);

        GameObject audioObject = new($"Audio - {clip.name}");
        audioObject.transform.SetParent(instance.transform);

        AudioSource source = audioObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = Mathf.Clamp01(entryVolume * volume);
        source.pitch = finalPitch;
        source.spatialBlend = 0f;
        source.outputAudioMixerGroup = instance.sfxMixerGroup;
        source.Play();

        Destroy(audioObject, clip.length / finalPitch + 0.1f);
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

        instance.musicSource.clip = track;
        instance.musicSource.volume = Mathf.Clamp01(volume);
        instance.musicSource.pitch = Mathf.Clamp(pitch, 0.01f, 3f);
        instance.musicSource.Play();
    }

    public static void StopMusic()
    {
        if (instance != null)
        {
            instance.musicSource.Stop();
        }
    }

    public static void SetSfxVolume(float volume)
    {
        if (instance == null)
        {
            return;
        }

        instance.sfxVolume = Mathf.Clamp01(volume);
        instance.ApplyMixerVolume(SfxVolumeParameter, instance.sfxVolume);
        PlayerPrefs.SetFloat(SfxVolumePreference, instance.sfxVolume);
        PlayerPrefs.Save();
    }

    public static void SetMusicVolume(float volume)
    {
        if (instance == null)
        {
            return;
        }

        instance.musicVolume = Mathf.Clamp01(volume);
        instance.ApplyMixerVolume(MusicVolumeParameter, instance.musicVolume);
        PlayerPrefs.SetFloat(MusicVolumePreference, instance.musicVolume);
        PlayerPrefs.Save();
    }

    public static float SfxVolume => instance != null
        ? instance.sfxVolume
        : PlayerPrefs.GetFloat(SfxVolumePreference, 1f);

    public static float MusicVolume => instance != null
        ? instance.musicVolume
        : PlayerPrefs.GetFloat(MusicVolumePreference, 1f);

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

        float decibels = normalizedVolume <= 0f
            ? -80f
            : Mathf.Log10(normalizedVolume) * 20f;

        if (!audioMixer.SetFloat(parameterName, decibels))
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
