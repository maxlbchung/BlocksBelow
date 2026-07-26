using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Marks a run's landmark rounds: the one the campaign was aimed at, where the enemy
/// leader falls, and the last round on the list. Shows the run's stats so far, then
/// offers the way on - back to the fight while there are rounds left, home once there
/// are not.
/// <para>
/// Built the same way as <see cref="GameOverScreen"/>: its own canvas, spawned on
/// demand, so the scene needs no wiring. The game is paused while it is up.
/// </para>
/// </summary>
public class VictoryScreen : MonoBehaviour
{
    private const string MainMenuSceneName = "MainMenu";
    private const float FadeDuration = 0.35f;

    // The main menu's scheme, the same one the pause popup and the game over screen use.
    private static readonly Color LabelColor = Color.white;
    private static readonly Color HighlightColor = new Color(0.32549f, 0.494118f, 0.423529f, 1f);
    private static readonly Color OutlineColor = new Color(1f, 1f, 1f, 0.85f);
    private static readonly Color DimLabelColor = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color BackdropColor = new Color(0f, 0f, 0f, 0.75f);
    private static readonly Color PanelColor = new Color(0.03f, 0.05f, 0.08f, 0.9f);
    private static readonly Color StatRowColor = new Color(1f, 1f, 1f, 0.05f);

    private const float OutlineThickness = 3f;

    /// <summary>
    /// Panel height with both buttons. Sized for the menu font, which asks for noticeably
    /// more height per point than the built-in one.
    /// </summary>
    private const float BasePanelHeight = 690f;

    /// <summary>Dropped on the last round, which has nothing left to continue into.</summary>
    private const float ExtraButtonHeight = 82f;

    private static VictoryScreen instance;

    private CanvasGroup canvasGroup;
    private int roundCleared;
    private bool isFinalRound;

    /// <summary>
    /// Builds and shows the screen for <paramref name="roundCleared"/>. Later calls while
    /// it is up do nothing. <paramref name="isFinalRound"/> drops the continue button:
    /// there is no round after the last one to go back to.
    /// </summary>
    public static void Show(int roundCleared, bool isFinalRound)
    {
        if (instance != null)
        {
            return;
        }

        VictoryScreen screen = new GameObject("Victory Screen").AddComponent<VictoryScreen>();
        screen.roundCleared = roundCleared;
        screen.isFinalRound = isFinalRound;
        screen.Build();
    }

    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    /// <summary>
    /// Raised out of Awake so the round it is reporting on is set before the panel that
    /// reads it is built.
    /// </summary>
    private void Build()
    {
        EnsureEventSystem();
        BuildUI();

        // Freeze the field behind the screen. Every button restores this.
        Time.timeScale = 0f;
        StartCoroutine(FadeIn());
    }

    /// <summary>
    /// Hands the run back. The build phase for the next round is already up behind the
    /// screen, so there is nothing to restart - only the pause to lift.
    /// </summary>
    public void Continue()
    {
        Time.timeScale = 1f;
        Destroy(gameObject);
    }

    public void GoToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(MainMenuSceneName);
    }

    private IEnumerator FadeIn()
    {
        // Unscaled, because showing the screen is what paused the game.
        for (float elapsed = 0f; elapsed < FadeDuration; elapsed += Time.unscaledDeltaTime)
        {
            canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, elapsed / FadeDuration);
            yield return null;
        }

        canvasGroup.alpha = 1f;
    }

    private void BuildUI()
    {
        GameObject canvasObject = new GameObject(
            "Victory Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the tower shop, which sorts at 100, alongside the game over screen.
        canvas.sortingOrder = 500;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        canvasGroup = canvasObject.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;

        // Full-screen dim that also swallows clicks aimed at the game behind it.
        GameObject backdrop = CreateUIObject("Backdrop", canvasObject.transform);
        backdrop.AddComponent<Image>().color = BackdropColor;
        RectTransform backdropRect = backdrop.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;

        GameObject panel = CreateUIObject("Panel", canvasObject.transform);
        panel.AddComponent<Image>().color = PanelColor;
        AddOutline(panel);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(
            660f,
            isFinalRound ? BasePanelHeight - ExtraButtonHeight : BasePanelHeight);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(40, 40, 36, 36);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        Text title = CreateText("Title", panel.transform, 72, TextAnchor.MiddleCenter);
        title.text = "VICTORY";
        title.color = HighlightColor;
        title.fontStyle = FontStyle.Bold;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 110f;

        Text subtitle = CreateText("Subtitle", panel.transform, 24, TextAnchor.MiddleCenter);
        subtitle.text = "Their leader has been conquered.";
        subtitle.color = DimLabelColor;
        subtitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;

        CreateSpacer(panel.transform, 14f);

        // The round is reported as cleared rather than reached: unlike the game over
        // screen, this one only ever appears with the round already behind the player.
        CreateStatRow(panel.transform, "Round Cleared", roundCleared.ToString());
        CreateStatRow(panel.transform, "Enemies Defeated", RunStats.EnemiesDefeated.ToString());
        CreateStatRow(panel.transform, "Towers Placed", RunStats.TowersPlaced.ToString());

        CreateSpacer(panel.transform, 18f);

        // Carrying on is the offer being made, so it takes the accent colour and leads.
        // The last round has nothing to carry on into, which leaves one way out.
        if (!isFinalRound)
        {
            CreateButton(panel.transform, "Continue", HighlightColor, Continue);
        }

        CreateButton(
            panel.transform, "Main Menu", isFinalRound ? HighlightColor : LabelColor, GoToMainMenu);
    }

    private static void CreateStatRow(Transform parent, string label, string value)
    {
        GameObject row = CreateUIObject(label + " Row", parent);
        row.AddComponent<LayoutElement>().preferredHeight = 58f;

        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(16, 16, 0, 0);
        rowLayout.spacing = 12f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlHeight = true;
        rowLayout.childControlWidth = true;

        Image rowBackground = row.AddComponent<Image>();
        rowBackground.color = StatRowColor;

        Text labelText = CreateText("Label", row.transform, 28, TextAnchor.MiddleLeft);
        labelText.text = label;
        labelText.color = DimLabelColor;
        labelText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        Text valueText = CreateText("Value", row.transform, 32, TextAnchor.MiddleRight);
        valueText.text = value;
        valueText.color = HighlightColor;
        valueText.fontStyle = FontStyle.Bold;
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 140f;
    }

    /// <summary>
    /// A button with no fill of its own: it lights up faintly under the pointer and leaves the
    /// wireframe box and the label to carry it, the way the pause popup and the shop do.
    /// </summary>
    private static Button CreateButton(
        Transform parent,
        string label,
        Color labelColor,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = CreateUIObject(label, parent);
        Image background = buttonObject.AddComponent<Image>();
        background.color = Color.white;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);
        buttonObject.AddComponent<LayoutElement>().preferredHeight = 68f;

        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(1f, 1f, 1f, 0f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.22f);
        colors.selectedColor = new Color(1f, 1f, 1f, 0f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0f);
        button.colors = colors;

        AddOutline(buttonObject);

        Text buttonLabel = CreateText("Label", buttonObject.transform, 28, TextAnchor.MiddleCenter);
        buttonLabel.text = label;
        buttonLabel.color = labelColor;
        RectTransform labelRect = buttonLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
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

    private static void CreateSpacer(Transform parent, float height)
    {
        GameObject spacer = CreateUIObject("Spacer", parent);
        spacer.AddComponent<LayoutElement>().preferredHeight = height;
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
        text.font = MenuFont.Regular;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        // Truncate, the default, drops a line whole rather than clipping it, so a label whose
        // line runs past its box vanishes outright - and the menu font is taller per point
        // than the built-in one these boxes were first measured against.
        text.verticalOverflow = VerticalWrapMode.Overflow;
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
