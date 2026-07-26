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
            fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.SetValueWithoutNotify(AudioController.SfxVolume);
        }

        if (musicVolumeSlider != null)
        {
            musicVolumeSlider.SetValueWithoutNotify(AudioController.MusicVolume);
        }

        ShowMainMenu();
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
        Screen.fullScreen = fullscreen;
    }

    private void ShowPage(GameObject pageToShow)
    {
        mainPage.SetActive(pageToShow == mainPage);
        settingsPage.SetActive(pageToShow == settingsPage);
        aboutPage.SetActive(pageToShow == aboutPage);
    }
}
