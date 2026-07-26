using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuNavigation : MonoBehaviour
{
    private const string GameSceneName = "MainGame";

    [Header("Pages")]
    [SerializeField] private GameObject mainPage;
    [SerializeField] private GameObject settingsPage;
    [SerializeField] private GameObject aboutPage;

    [Header("Settings UI")]
    [SerializeField] private Toggle fullscreenToggle;
    [SerializeField] private Slider sfxVolumeSlider;
    [SerializeField] private Slider musicVolumeSlider;

    private void Start()
    {
        if (fullscreenToggle != null)
        {
            fullscreenToggle.SetIsOnWithoutNotify(SettingsMenu.FullscreenEnabled);
        }

        RefreshVolumeSliders();
        AudioController.VolumesChanged += RefreshVolumeSliders;

        ShowMainMenu();
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
