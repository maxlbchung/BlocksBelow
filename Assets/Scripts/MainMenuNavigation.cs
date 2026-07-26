using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuNavigation : MonoBehaviour
{
    private const string GameSceneName = "MainGame";

    [Header("Button Hover")]
    [Tooltip("How quickly buttons grow and return to normal size.")]
    [Min(0.01f)]
    [SerializeField] private float x = 8f;

    [Tooltip("Button size while the pointer is hovering over it.")]
    [Min(1f)]
    [SerializeField] private float hoverSize = 1.1f;

    [Header("Pages")]
    [SerializeField] private GameObject mainPage;
    [SerializeField] private GameObject settingsPage;
    [SerializeField] private GameObject aboutPage;

    [Header("Settings UI")]
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;

    public float X => x;
    public float HoverSize => hoverSize;

    private void Start()
    {
        SetUpButtonHoverEffects();

        if (fullscreenToggle != null)
        {
            fullscreenToggle.SetIsOnWithoutNotify(SettingsMenu.FullscreenEnabled);
        }

        RefreshVolumeSliders();
        AudioController.VolumesChanged += RefreshVolumeSliders;

        ShowMainMenu();
    }

    private void SetUpButtonHoverEffects()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons)
        {
            ButtonHoverScale hoverEffect = button.GetComponent<ButtonHoverScale>();
            if (hoverEffect == null)
            {
                hoverEffect = button.gameObject.AddComponent<ButtonHoverScale>();
            }

            hoverEffect.Initialize(this);
        }
    }

    private void OnDestroy()
    {
        AudioController.VolumesChanged -= RefreshVolumeSliders;
    }

    /// <summary>Puts both sliders back on the levels the mixer is actually running at.</summary>
    private void RefreshVolumeSliders()
    {
        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.SetValueWithoutNotify(AudioController.SfxVolume);
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.SetValueWithoutNotify(AudioController.MusicVolume);
        }
    }

    public void ShowMainMenu()
    {
        ShowPage(mainPage);
    }

    public void ShowSettings()
    {
        ShowPage(settingsPage);
    }

    public void ShowAbout()
    {
        ShowPage(aboutPage);
    }

    public void PlayGame()
    {
        SceneManager.LoadScene(GameSceneName);
    }

    public void SetSfxVolume(float volume)
    {
        AudioController.SetSfxVolume(volume);
    }

    public void SetMusicVolume(float volume)
    {
        AudioController.SetMusicVolume(volume);
    }

    public void SetFullscreen(bool fullscreen)
    {
        // Saved rather than only applied, so the in-game settings popup shows and restores
        // the same choice instead of overwriting it when the game scene loads.
        SettingsMenu.ApplyFullscreen(fullscreen);
    }

    private void ShowPage(GameObject pageToShow)
    {
        mainPage.SetActive(pageToShow == mainPage);
        settingsPage.SetActive(pageToShow == settingsPage);
        aboutPage.SetActive(pageToShow == aboutPage);
    }
}
