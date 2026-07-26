using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The gear button in the top right corner of a gameplay scene and the popup it opens.
/// The popup freezes the game while it is up and carries the two settings that are worth
/// changing mid-run: fullscreen and the two volumes.
///
/// Builds its own canvas and spawns itself into every scene that is not the main menu -
/// which has a settings page of its own - so no scene wiring is needed. Drawn as
/// wireframe boxes over a dimmed backdrop to match the tower shop.
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    private static readonly Color OutlineColor = new Color(0.78f, 0.88f, 1f, 0.85f);
    private static readonly Color LabelColor = new Color(0.9f, 0.94f, 1f, 1f);
    private static readonly Color HighlightColor = new Color(0.45f, 0.95f, 0.6f, 1f);
    private static readonly Color DimLabelColor = new Color(0.72f, 0.76f, 0.82f, 1f);
    private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.65f);
    private static readonly Color PanelFillColor = new Color(0.03f, 0.05f, 0.08f, 0.85f);
    private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.14f);

    private const float OutlineThickness = 3f;
    private const float ButtonSize = 68f;
    private const float ScreenEdgePadding = 20f;
    private const float HandleSize = 14f;
    private const float TrackHeight = 6f;

    private const string FullscreenPreference = "Display.Fullscreen";

    private static SettingsMenu instance;
    private static Sprite gearSprite;

    private GameObject popupRoot;
    private Text fullscreenValueLabel;
    private Text musicValueLabel;
    private Text sfxValueLabel;
    private Slider musicSlider;
    private Slider sfxSlider;

    private float timeScaleBeforePause = 1f;
    private bool paused;

    public bool IsOpen => popupRoot != null && popupRoot.activeSelf;

    /// <summary>
    /// What the player last picked, rather than what the screen currently is. Screen.fullScreen
    /// reads back false in the editor whatever it is set to, so using it as the record of the
    /// choice made the row snap back to Off every time the popup reopened.
    /// </summary>
    private static bool FullscreenPreferred
    {
        get => PlayerPrefs.GetInt(FullscreenPreference, Screen.fullScreen ? 1 : 0) != 0;
        set
        {
            PlayerPrefs.SetInt(FullscreenPreference, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // Statics outlive a scene, so the spawn hook is re-subscribed on every load rather
    // than left from whichever scene happened to run first.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void HookSceneSpawn()
    {
        SceneManager.sceneLoaded -= SpawnForScene;
        SceneManager.sceneLoaded += SpawnForScene;
    }

    private static void SpawnForScene(Scene scene, LoadSceneMode mode)
    {
        if (instance != null)
        {
            return;
        }

        // The main menu already reaches these settings through its own settings page.
        if (FindFirstObjectByType<MainMenuNavigation>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        new GameObject("Settings Menu").AddComponent<SettingsMenu>();
    }

    private void Awake()
    {
        instance = this;

        // The window is rebuilt from the saved choice on load, so the setting survives the
        // run rather than lasting only as long as the popup is up.
        ApplyFullscreen(FullscreenPreferred);

        EnsureEventSystem();
        BuildUI();

        AudioController.VolumesChanged += RefreshVolumeControls;
    }

    private void OnDestroy()
    {
        AudioController.VolumesChanged -= RefreshVolumeControls;

        // A scene that loads while the popup is up would otherwise start frozen.
        if (paused)
        {
            Time.timeScale = timeScaleBeforePause;
            paused = false;
        }

        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Toggle();
        }
    }

    public void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        if (IsOpen || popupRoot == null)
        {
            return;
        }

        // Something else already froze the clock - the game over screen does - so the
        // popup stays out of the way rather than handing time back at 1 when it closes.
        if (Time.timeScale <= 0f)
        {
            return;
        }

        // Activated first: a slider rebuilds its handle and fill from its own stored value
        // when it is enabled, which would undo a refresh applied while it was still hidden.
        popupRoot.SetActive(true);
        RefreshControls();

        timeScaleBeforePause = Time.timeScale;
        Time.timeScale = 0f;
        paused = true;
    }

    public void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        popupRoot.SetActive(false);

        if (paused)
        {
            Time.timeScale = timeScaleBeforePause;
            paused = false;
        }
    }

    /// <summary>Reads the live values back into the controls each time the popup opens.</summary>
    private void RefreshControls()
    {
        SetFullscreenLabel(FullscreenPreferred);
        RefreshVolumeControls();
    }

    /// <summary>
    /// Pulls both volume rows back off the mixer. Runs on every mixer write as well as on
    /// open, so a handle sits where the game is actually mixing rather than where it was
    /// dropped - if the mixer clamps or refuses a level, the row shows that.
    /// </summary>
    private void RefreshVolumeControls()
    {
        float musicVolume = AudioController.MusicVolume;
        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(musicVolume);
            musicValueLabel.text = FormatPercent(musicVolume);
        }

        float sfxVolume = AudioController.SfxVolume;
        if (sfxSlider != null)
        {
            sfxSlider.SetValueWithoutNotify(sfxVolume);
            sfxValueLabel.text = FormatPercent(sfxVolume);
        }
    }

    private void ToggleFullscreen()
    {
        // Flipped off the saved choice, not off Screen.fullScreen, so the row does not stick
        // on whichever value the screen reports back in the editor.
        bool fullscreen = !FullscreenPreferred;
        ApplyFullscreen(fullscreen);
        SetFullscreenLabel(fullscreen);
    }

    /// <summary>What the fullscreen setting is currently saved as, for other settings pages.</summary>
    public static bool FullscreenEnabled => FullscreenPreferred;

    /// <summary>
    /// The one way the fullscreen setting is changed. The main menu page goes through here
    /// too, so a choice made there is the one this popup restores on the next scene.
    /// </summary>
    public static void ApplyFullscreen(bool fullscreen)
    {
        FullscreenPreferred = fullscreen;
        Screen.fullScreenMode = fullscreen
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;
    }

    private void SetFullscreenLabel(bool fullscreen)
    {
        if (fullscreenValueLabel == null)
        {
            return;
        }

        fullscreenValueLabel.text = fullscreen ? "On" : "Off";
        fullscreenValueLabel.color = fullscreen ? HighlightColor : DimLabelColor;
    }

    // Neither of these touches its own row: the write raises the change event, and the refresh
    // that follows redraws the handle and the readout from what the mixer took.
    private void SetMusicVolume(float volume)
    {
        AudioController.SetMusicVolume(volume);
    }

    private void SetSfxVolume(float volume)
    {
        AudioController.SetSfxVolume(volume);
    }

    private static string FormatPercent(float normalized)
    {
        return Mathf.RoundToInt(Mathf.Clamp01(normalized) * 100f) + "%";
    }

    private void BuildUI()
    {
        GameObject canvasObject = new GameObject(
            "Settings Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the tower shop, which sorts at 100, and below the game over screen at 500.
        canvas.sortingOrder = 400;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        BuildSettingsButton(canvasObject.transform);
        // Built after the button so it draws over it, and the backdrop takes the clicks
        // aimed at the gear while the popup is open.
        BuildPopup(canvasObject.transform);
    }

    private void BuildSettingsButton(Transform parent)
    {
        Button button = CreateBareButton("Settings Button", parent);
        button.onClick.AddListener(Open);

        RectTransform buttonRect = button.GetComponent<RectTransform>();
        buttonRect.anchorMin = Vector2.one;
        buttonRect.anchorMax = Vector2.one;
        buttonRect.pivot = Vector2.one;
        buttonRect.sizeDelta = new Vector2(ButtonSize, ButtonSize);
        buttonRect.anchoredPosition = new Vector2(-ScreenEdgePadding, -ScreenEdgePadding);

        AddOutline(button.gameObject);

        GameObject icon = CreateUIObject("Gear", button.transform);
        Image gear = icon.AddComponent<Image>();
        gear.sprite = GetGearSprite();
        gear.color = LabelColor;
        gear.raycastTarget = false;

        RectTransform iconRect = icon.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(14f, 14f);
        iconRect.offsetMax = new Vector2(-14f, -14f);
    }

    private void BuildPopup(Transform parent)
    {
        popupRoot = CreateUIObject("Settings Popup", parent);
        RectTransform popupRect = popupRoot.GetComponent<RectTransform>();
        popupRect.anchorMin = Vector2.zero;
        popupRect.anchorMax = Vector2.one;
        popupRect.offsetMin = Vector2.zero;
        popupRect.offsetMax = Vector2.zero;

        // Dims the fight and swallows the clicks that would otherwise reach the shop or
        // place a tower behind the popup.
        GameObject backdrop = CreateUIObject("Backdrop", popupRoot.transform);
        backdrop.AddComponent<Image>().color = BackdropColor;
        RectTransform backdropRect = backdrop.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;

        GameObject panel = CreateUIObject("Panel", popupRoot.transform);
        panel.AddComponent<Image>().color = PanelFillColor;
        AddOutline(panel);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(680f, 520f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(36, 36, 30, 30);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        Text title = CreateText("Title", panel.transform, 46, TextAnchor.MiddleCenter);
        title.text = "SETTINGS";
        title.color = LabelColor;
        title.fontStyle = FontStyle.Bold;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        Text subtitle = CreateText("Subtitle", panel.transform, 22, TextAnchor.MiddleCenter);
        subtitle.text = "Paused   |   Esc to close";
        subtitle.color = DimLabelColor;
        subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

        BuildFullscreenRow(panel.transform);
        musicSlider = BuildVolumeRow(
            panel.transform, "Music", AudioController.MusicVolume, SetMusicVolume, out musicValueLabel);
        sfxSlider = BuildVolumeRow(
            panel.transform, "Sound", AudioController.SfxVolume, SetSfxVolume, out sfxValueLabel);

        // Takes up whatever the rows leave over, so Resume rides the bottom edge.
        CreateUIObject("Spacer", panel.transform).AddComponent<LayoutElement>().flexibleHeight = 1f;

        Button resume = CreateBareButton("Resume", panel.transform);
        resume.onClick.AddListener(Close);
        LayoutElement resumeSize = resume.gameObject.AddComponent<LayoutElement>();
        resumeSize.preferredHeight = 64f;
        resumeSize.flexibleHeight = 0f;
        AddOutline(resume.gameObject);

        Text resumeLabel = CreateText("Label", resume.transform, 30, TextAnchor.MiddleCenter);
        resumeLabel.text = "Resume";
        resumeLabel.color = HighlightColor;
        StretchLabel(resumeLabel);

        popupRoot.SetActive(false);
    }

    private void BuildFullscreenRow(Transform parent)
    {
        GameObject row = CreateRow(parent, "Fullscreen Row", out Text label);
        label.text = "Fullscreen";

        // Pushes the toggle to the right edge of the row, where the volume readouts sit.
        CreateUIObject("Spacer", row.transform).AddComponent<LayoutElement>().flexibleWidth = 1f;

        Button toggle = CreateBareButton("Fullscreen Toggle", row.transform);
        toggle.onClick.AddListener(ToggleFullscreen);

        LayoutElement toggleSize = toggle.gameObject.AddComponent<LayoutElement>();
        toggleSize.preferredWidth = 150f;
        toggleSize.flexibleWidth = 0f;
        AddOutline(toggle.gameObject);

        fullscreenValueLabel = CreateText("Value", toggle.transform, 26, TextAnchor.MiddleCenter);
        StretchLabel(fullscreenValueLabel);
        SetFullscreenLabel(Screen.fullScreen);
    }

    private Slider BuildVolumeRow(
        Transform parent,
        string label,
        float startingValue,
        UnityEngine.Events.UnityAction<float> onChanged,
        out Text valueLabel)
    {
        GameObject row = CreateRow(parent, label + " Row", out Text rowLabel);
        rowLabel.text = label;

        Slider slider = BuildSlider(row.transform, startingValue);
        LayoutElement sliderSize = slider.gameObject.AddComponent<LayoutElement>();
        sliderSize.flexibleWidth = 1f;

        valueLabel = CreateText("Value", row.transform, 26, TextAnchor.MiddleRight);
        valueLabel.text = FormatPercent(startingValue);
        valueLabel.color = DimLabelColor;
        valueLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 90f;

        slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    /// <summary>
    /// A slider built out of plain images: a thin track, a fill up to the current value
    /// and a square handle, which reads as part of the same blocky menu as the shop.
    /// </summary>
    private static Slider BuildSlider(Transform parent, float startingValue)
    {
        GameObject sliderObject = CreateUIObject("Slider", parent);
        Slider slider = sliderObject.AddComponent<Slider>();

        // The only things drawn here are a six pixel track and a small handle, so those were
        // also the only things a click could land on - a drag anywhere else in the row missed
        // the slider entirely and the volume never moved. This invisible graphic fills the row
        // and takes those clicks, leaving the thin track purely as the way it looks.
        Image hitArea = sliderObject.AddComponent<Image>();
        hitArea.color = Color.clear;

        GameObject background = CreateUIObject("Background", sliderObject.transform);
        background.AddComponent<Image>().color = TrackColor;
        RectTransform backgroundRect = background.GetComponent<RectTransform>();
        backgroundRect.anchorMin = new Vector2(0f, 0.5f);
        backgroundRect.anchorMax = new Vector2(1f, 0.5f);
        backgroundRect.anchoredPosition = Vector2.zero;
        backgroundRect.sizeDelta = new Vector2(0f, TrackHeight);

        // Inset by half a handle at each end, so the handle stays inside the track at the
        // extremes instead of hanging off the ends of the row.
        GameObject fillArea = CreateUIObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.anchoredPosition = Vector2.zero;
        fillAreaRect.sizeDelta = new Vector2(-HandleSize, TrackHeight);

        GameObject fill = CreateUIObject("Fill", fillArea.transform);
        fill.AddComponent<Image>().color = HighlightColor;
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        // Only the ends are set here; the slider drives the anchors from its value.
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        // The slider stretches the handle over the full height of this area, so the area
        // is a band one handle tall rather than the whole row: that is what keeps the
        // handle a small square instead of a bar running the height of the row.
        GameObject handleArea = CreateUIObject("Handle Slide Area", sliderObject.transform);
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = new Vector2(0f, 0.5f);
        handleAreaRect.anchorMax = new Vector2(1f, 0.5f);
        handleAreaRect.anchoredPosition = Vector2.zero;
        handleAreaRect.sizeDelta = new Vector2(-HandleSize, HandleSize);

        GameObject handle = CreateUIObject("Handle", handleArea.transform);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = LabelColor;
        // Width only: the height comes from the band above.
        handle.GetComponent<RectTransform>().sizeDelta = new Vector2(HandleSize, 0f);

        slider.fillRect = fillRect;
        slider.handleRect = handle.GetComponent<RectTransform>();
        slider.targetGraphic = handleImage;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.SetValueWithoutNotify(startingValue);
        return slider;
    }

    /// <summary>A label on the left with room for its control on the right.</summary>
    private static GameObject CreateRow(Transform parent, string rowName, out Text label)
    {
        GameObject row = CreateUIObject(rowName, parent);

        LayoutElement rowSize = row.AddComponent<LayoutElement>();
        rowSize.preferredHeight = 56f;
        rowSize.flexibleHeight = 0f;

        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 18f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlHeight = true;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandWidth = false;

        label = CreateText("Label", row.transform, 28, TextAnchor.MiddleLeft);
        label.color = LabelColor;
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 220f;
        return row;
    }

    /// <summary>
    /// A button with no background of its own: it lights up faintly under the pointer and
    /// leaves whatever is drawn inside it to carry the button, the way the shop does.
    /// </summary>
    private static Button CreateBareButton(string objectName, Transform parent)
    {
        GameObject buttonObject = CreateUIObject(objectName, parent);

        Image background = buttonObject.AddComponent<Image>();
        background.color = Color.white;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;

        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(1f, 1f, 1f, 0f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.22f);
        colors.selectedColor = new Color(1f, 1f, 1f, 0f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0f);
        button.colors = colors;
        return button;
    }

    /// <summary>Traces a wireframe box around <paramref name="target"/> without joining its layout.</summary>
    private static void AddOutline(GameObject target)
    {
        GameObject outlineObject = CreateUIObject("Outline", target.transform);

        RectTransform outlineRect = outlineObject.GetComponent<RectTransform>();
        outlineRect.anchorMin = Vector2.zero;
        outlineRect.anchorMax = Vector2.one;
        outlineRect.offsetMin = Vector2.zero;
        outlineRect.offsetMax = Vector2.zero;

        outlineObject.AddComponent<LayoutElement>().ignoreLayout = true;

        UIWireframeBox outline = outlineObject.AddComponent<UIWireframeBox>();
        outline.Color = OutlineColor;
        outline.Thickness = OutlineThickness;
    }

    /// <summary>
    /// Draws the gear once into a shared texture: a toothed ring around a hollow centre.
    /// Generated rather than imported so the button needs no art asset.
    /// </summary>
    private static Sprite GetGearSprite()
    {
        if (gearSprite != null)
        {
            return gearSprite;
        }

        const int size = 64;
        const int toothCount = 8;
        // All measured in pixels of the square texture, out from its centre.
        const float toothRadius = 30f;
        const float bodyRadius = 23f;
        const float holeRadius = 9f;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Settings Gear Texture",
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            float offsetY = y - center;
            for (int x = 0; x < size; x++)
            {
                float offsetX = x - center;
                float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY);
                float angle = Mathf.Atan2(offsetY, offsetX);

                // A tooth every other slice: the wave rides from the body out to the
                // tooth tips, and the short ramp keeps the flanks straight rather than
                // rounded while still leaving a pixel of fade to smooth them.
                float tooth = Mathf.Clamp01(Mathf.Cos(angle * toothCount) * 4f + 0.5f);
                float radius = Mathf.Lerp(bodyRadius, toothRadius, tooth);

                float alpha = Mathf.Clamp01(radius - distance)
                    * Mathf.Clamp01(distance - holeRadius);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        gearSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        gearSprite.name = "Settings Gear";
        return gearSprite;
    }

    private static void StretchLabel(Text label)
    {
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    private static GameObject CreateUIObject(string objectName, Transform parent)
    {
        GameObject result = new GameObject(objectName, typeof(RectTransform));
        result.transform.SetParent(parent, false);
        return result;
    }

    private static Text CreateText(string objectName, Transform parent, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = CreateUIObject(objectName, parent);
        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }
}
