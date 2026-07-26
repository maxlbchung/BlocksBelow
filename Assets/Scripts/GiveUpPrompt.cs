using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Offers a way out once the board can no longer fight back - every cage that
/// powered an offensive tower is gone, so nothing is shooting and nothing is
/// capturing the enemies that would refill the cages.
/// <para>
/// Watching for that is close to free. <see cref="TowerCageStack"/> already
/// recomputes its power every frame for the towers themselves, so this only
/// reads the numbers back: one int per offensive tower per frame. The tower list
/// behind it is rebuilt when <see cref="TowerGrid.Version"/> moves, i.e. when
/// something was actually placed, never per frame.
/// </para>
/// <para>
/// Builds its own canvas and spawns itself on scene load, so the scene needs no
/// wiring. The panel is built once and left hidden rather than rebuilt on every
/// appearance, because the condition flickers - a cage refills, a tower is
/// placed - and rebuilding it each time would churn the heap exactly when the
/// field is busiest.
/// </para>
/// </summary>
public class GiveUpPrompt : MonoBehaviour
{
    /// <summary>
    /// How long the board has to stay dark before the prompt appears. Long
    /// enough that a cage broken and immediately refilled never shows it.
    /// </summary>
    private const float GraceSeconds = 2f;
    private const float FadeDuration = 0.25f;

    private static readonly Color PanelColor = new Color(0.08f, 0.1f, 0.14f, 0.96f);
    private static readonly Color MessageColor = new Color(0.72f, 0.76f, 0.82f, 1f);
    private static readonly Color GiveUpColor = new Color(0.55f, 0.16f, 0.16f, 1f);

    private static GiveUpPrompt instance;

    private readonly List<TowerCageStack> offensiveStacks = new List<TowerCageStack>(32);

    private GameObject root;
    private CanvasGroup canvasGroup;
    private PlayerController player;
    private int cachedGridVersion = int.MinValue;
    private float darkSince = -1f;
    private bool armed;
    private bool shown;

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

        // There is no run to give up on in the menu.
        if (FindFirstObjectByType<MainMenuNavigation>(FindObjectsInactive.Include) != null)
        {
            return;
        }

        new GameObject("Give Up Prompt").AddComponent<GiveUpPrompt>();
    }

    private void Awake()
    {
        instance = this;

        EnsureEventSystem();
        BuildUI();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        UpdateFade();

        if (player == null)
        {
            player = FindFirstObjectByType<PlayerController>();
        }

        // A dead player already has the game over screen on the way, so the offer is moot.
        if (player == null || !player.IsAlive)
        {
            shown = false;
            return;
        }

        RefreshOffensiveStacks();

        if (AnyTowerPowered())
        {
            // Seeing power at all is what arms the prompt. A board that has not been
            // built yet is unpowered too, and that is not a lost run.
            armed = true;
            darkSince = -1f;
            shown = false;
            return;
        }

        if (!armed)
        {
            return;
        }

        if (darkSince < 0f)
        {
            darkSince = Time.time;
        }

        // Scaled time, so the grace period does not tick away behind a pause.
        shown = Time.time - darkSince >= GraceSeconds;
    }

    /// <summary>
    /// The whole per-frame cost of the feature: one <see cref="TowerCageStack.PowerLevel"/>
    /// read per offensive tower, off a value those towers already recomputed this frame.
    /// </summary>
    private bool AnyTowerPowered()
    {
        bool powered = false;
        bool sawDestroyed = false;

        for (int i = 0; i < offensiveStacks.Count; i++)
        {
            TowerCageStack stack = offensiveStacks[i];
            if (stack == null)
            {
                sawDestroyed = true;
                continue;
            }

            if (stack.PowerLevel > 0)
            {
                powered = true;
            }
        }

        // A tower blown up mid-round leaves a hole the grid version never reports,
        // because nothing unregisters it. Sweep the list on the next frame instead.
        if (sawDestroyed)
        {
            cachedGridVersion = int.MinValue;
        }

        return powered;
    }

    /// <summary>
    /// Rebuilds the tower list only when the board actually changed. Placement bumps
    /// <see cref="TowerGrid.Version"/>, which happens a handful of times a round, so
    /// the scan here never lands on a frame that was going to be busy anyway.
    /// </summary>
    private void RefreshOffensiveStacks()
    {
        if (TowerGrid.Version == cachedGridVersion)
        {
            return;
        }

        cachedGridVersion = TowerGrid.Version;
        offensiveStacks.Clear();

        TowerCageStack[] stacks = FindObjectsByType<TowerCageStack>(FindObjectsSortMode.None);
        for (int i = 0; i < stacks.Length; i++)
        {
            if (IsOffensive(stacks[i]))
            {
                offensiveStacks.Add(stacks[i]);
            }
        }
    }

    /// <summary>
    /// A tower counts as offensive when it can kill something. Energy and fan towers
    /// draw the same cage power, but a board holding only those is as lost as an
    /// empty one, so they do not keep the prompt away.
    /// </summary>
    private static bool IsOffensive(TowerCageStack stack)
    {
        return stack != null
            && (stack.GetComponent<BasicTower>() != null
                || stack.GetComponent<ShotgunTower>() != null
                || stack.GetComponent<TeslaTower>() != null
                || stack.GetComponent<SawBladeTower>() != null);
    }

    private void OnGiveUpClicked()
    {
        shown = false;
        if (player != null)
        {
            player.Surrender();
        }
    }

    /// <summary>
    /// Unscaled, so a fade already running when something pauses still finishes. The
    /// canvas is switched off outright once hidden rather than left at alpha zero.
    /// </summary>
    private void UpdateFade()
    {
        if (canvasGroup == null || root == null)
        {
            return;
        }

        float target = shown ? 1f : 0f;
        if (!Mathf.Approximately(canvasGroup.alpha, target))
        {
            canvasGroup.alpha = FadeDuration > 0f
                ? Mathf.MoveTowards(canvasGroup.alpha, target, Time.unscaledDeltaTime / FadeDuration)
                : target;
        }

        bool visible = shown || canvasGroup.alpha > 0f;
        if (root.activeSelf != visible)
        {
            root.SetActive(visible);
        }

        // Clicks are only taken once the panel has faded in, so a prompt on its way
        // out cannot swallow one aimed at the game behind it.
        canvasGroup.blocksRaycasts = shown;
        canvasGroup.interactable = shown;
    }

    private void BuildUI()
    {
        root = new GameObject(
            "Give Up Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup));
        root.transform.SetParent(transform, false);

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the tower shop at 100, below the game over screen at 500.
        canvas.sortingOrder = 400;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        GameObject panel = CreateUIObject("Panel", root.transform);
        panel.AddComponent<Image>().color = PanelColor;

        // Top centre: the shop owns the left edge, and the middle of the screen is
        // where the player is still trying to run away from something.
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -40f);
        panelRect.sizeDelta = new Vector2(560f, 176f);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 22, 22);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        Text message = CreateText("Message", panel.transform, 26, TextAnchor.MiddleCenter);
        message.text = "No cages are powering your towers.";
        message.color = MessageColor;
        message.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

        CreateButton(panel.transform, "Give Up?", GiveUpColor, OnGiveUpClicked);

        root.SetActive(false);
    }

    private static Button CreateButton(
        Transform parent,
        string label,
        Color color,
        UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonObject = CreateUIObject(label, parent);
        Image background = buttonObject.AddComponent<Image>();
        background.color = color;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);
        buttonObject.AddComponent<LayoutElement>().preferredHeight = 64f;

        Text buttonLabel = CreateText("Label", buttonObject.transform, 28, TextAnchor.MiddleCenter);
        buttonLabel.text = label;
        buttonLabel.fontStyle = FontStyle.Bold;
        RectTransform labelRect = buttonLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        return button;
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
