using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class TowerShopUI : MonoBehaviour
{
    [Serializable]
    public class TowerOffer
    {
        public string displayName = "Tower";

        [Tooltip("The tower spawned when this offer is placed. Everything about the tower "
            + "- art, damage, audio, firing frames - lives on this prefab.")]
        public GameObject prefab;

        [Min(0)] public int price = 10;

        [Tooltip("The round this piece is released in. It first appears in the shop that "
            + "opens once that round has been reached, and stays available afterwards. "
            + "Leave at 0 to take the round from this entry's position in the list - first "
            + "tower round 1, second tower round 2, and so on. Pieces that stand on their "
            + "own, like cages and scaffolding, default to round 1 instead, since nothing "
            + "else can be built without them.")]
        [Min(0)] public int unlockRound;
    }

    [Header("Shop")]
    [FormerlySerializedAs("startingMoney")]
    [SerializeField, Min(0)] private int startingEnergy = 100;
    [SerializeField] private List<TowerOffer> towers = new List<TowerOffer>();
    [SerializeField] private SquarePlacement placement;

    [Header("Health Potion")]
    [SerializeField, Min(1)] private int potionHealAmount = 5;
    [SerializeField, Min(0)] private int potionPrice = 25;
    [SerializeField] private PlayerController player;

    [Header("Cage Repair")]
    [SerializeField, Min(0)] private int cageRepairPrice = 10;

    [Header("Appearance")]
    [Tooltip("Manual horizontal scale applied before the menu is fitted to the screen.")]
    [SerializeField, Min(0.1f)] private float menuScaleX = 1f;
    [Tooltip("Manual vertical scale applied before the menu is fitted to the screen.")]
    [SerializeField, Min(0.1f)] private float menuScaleY = 1f;
    [Tooltip("Scales button icons and text without changing the size of the button itself.")]
    [SerializeField, Range(0.25f, 2f)] private float buttonContentScale = 1f;
    [Tooltip("Pixel offset for the Energy label inside the menu.")]
    [FormerlySerializedAs("moneyTextOffset")]
    [SerializeField] private Vector2 energyTextOffset = Vector2.zero;
    [Tooltip("Pixel offset for the Coming next label inside the Round tab.")]
    [SerializeField] private Vector2 comingNextTextOffset = Vector2.zero;
    [Tooltip("Vertical spacing between menu rows and buttons.")]
    [SerializeField, Min(0f)] private float menuItemSpacing = 10f;
    [Tooltip("Empty space kept between the menu and the edges of the screen.")]
    [SerializeField, Min(0f)] private float screenEdgePadding = 20f;
    [Tooltip("Line colour of the wireframe boxes the menu is drawn as.")]
    [SerializeField] private Color outlineColor = new Color(0.78f, 0.88f, 1f, 0.85f);
    [Tooltip("Line width of those boxes, in reference pixels.")]
    [SerializeField, Min(1f)] private float outlineThickness = 3f;
    [Tooltip("Text colour of an idle button. Buttons have no background of their own.")]
    [SerializeField] private Color labelColor = new Color(0.9f, 0.94f, 1f, 1f);
    [Tooltip("Text colour of the selected tower, the open tab and armed repair mode.")]
    [SerializeField] private Color highlightColor = new Color(0.45f, 0.95f, 0.6f, 1f);
    [SerializeField] private Color startRoundLabelColor = new Color(0.55f, 1f, 0.65f, 1f);
    [Tooltip("The \"+N\" that rises off an energy tower when it pays out. The text carries a "
        + "black outline, so white stays readable over the shop panel and the sky alike.")]
    [SerializeField] private Color energyPayoutColor = Color.white;

    [Header("Shop SFX")]
    [Tooltip("Per-tower sounds live on the tower prefabs; these two are shop actions.")]
    [SerializeField, AudioClipDropdown] private AudioClip placementSfx;
    [SerializeField, AudioClipDropdown] private AudioClip cageRepairSfx;

    /// <summary>
    /// Width of the menu column in reference pixels, before <see cref="menuScaleX"/> and
    /// the screen fit are applied.
    /// </summary>
    private const float MenuWidth = 400f;

    private readonly List<Button> towerButtons = new List<Button>();
    private readonly List<Text> towerLabels = new List<Text>();
    private readonly List<Image> towerIcons = new List<Image>();
    private readonly List<WaveSpawner.WavePreviewEntry> wavePreview =
        new List<WaveSpawner.WavePreviewEntry>(8);
    private static Sprite aimArrowSprite;
    private Text energyText;
    private Button potionButton;
    private Text potionLabel;
    private Button repairButton;
    private Text repairLabel;
    private Text descriptionTitle;
    private Text descriptionBody;
    private Text descriptionHint;
    private int describedIndex = int.MinValue;
    private bool describedRepairMode;
    private int hoveredIndex = -1;
    private RectTransform canvasRect;
    private RectTransform shopRootRect;
    private Vector2 lastCanvasSize;
    private int energy;
    private float displayedEnergy;
    private Coroutine energyTickRoutine;
    private int selectedIndex = -1;
    private bool repairMode;
    private int[] unlockRounds;

    private GameObject buildPage;
    private GameObject roundPage;
    private Text buildTabLabel;
    private Text roundTabLabel;
    private UIWireframeBox buildTabOutline;
    private UIWireframeBox roundTabOutline;
    private Text roundTitleText;
    private Text roundSubtitleText;
    private Text enemyTitle;
    private Text enemyBody;
    private Text enemyHint;
    private Transform enemyListRoot;
    private WaveSpawner waveSpawner;
    private bool showingRoundTab;

    public int Energy => energy;
    public Button StartRoundButton { get; private set; }

    /// <summary>The configured offers, exposed for the prefab build tool.</summary>
    public IReadOnlyList<TowerOffer> Towers => towers;

    /// <summary>True while clicking a broken cage should repair it instead of placing a tower.</summary>
    public bool RepairMode => repairMode;

    /// <summary>
    /// The highest round the player has reached, which is what pieces are released by.
    /// A scene with no spawner - the stress test, the prefab builder - has no rounds to
    /// gate on, so everything in the list is offered.
    /// </summary>
    private int ReleasedThroughRound =>
        waveSpawner != null ? waveSpawner.CurrentRoundNumber : int.MaxValue;

    /// <summary>Label colour of an offer the player cannot pay for.</summary>
    private Color DimmedLabelColor =>
        new Color(labelColor.r, labelColor.g, labelColor.b, labelColor.a * 0.35f);

    GameObject canvasObject;

    private void Awake()
    {
        energy = startingEnergy;
        displayedEnergy = energy;

        if (placement == null)
        {
            placement = FindFirstObjectByType<SquarePlacement>();
        }

        if (placement != null)
        {
            placement.SetTowerShop(this);
        }

        if (player == null)
        {
            player = FindFirstObjectByType<PlayerController>();
        }

        // Which pieces the shop may show depends on the round, so the spawner has to be
        // known before the first list is built rather than when the Round tab first opens.
        if (waveSpawner == null)
        {
            waveSpawner = FindFirstObjectByType<WaveSpawner>();
        }

        CacheUnlockRounds();
        BuildShopUI();
        RefreshUI();

        SelectFirstAvailableTower();
    }

    /// <summary>
    /// Puts the cursor on something buildable, so a build phase never opens with nothing
    /// selected. Skips pieces that are still locked or out of reach on the current energy.
    /// </summary>
    private void SelectFirstAvailableTower()
    {
        for (int i = 0; i < towers.Count; i++)
        {
            if (IsReleased(i) && CanAfford(towers[i].price))
            {
                SelectTower(i);
                return;
            }
        }
    }

    /// <summary>The round an offer joins the shop in.</summary>
    public int GetUnlockRound(int index)
    {
        if (unlockRounds == null || index < 0 || index >= unlockRounds.Length)
        {
            return int.MaxValue;
        }

        return unlockRounds[index];
    }

    /// <summary>
    /// Works out when each offer is released, once, since none of it changes during a run.
    /// An entry carrying its own <see cref="TowerOffer.unlockRound"/> keeps it. The rest
    /// come out one tower per round in list order - except the pieces that stand on their
    /// own, which are there from round 1: cages and scaffolding are what everything else
    /// is built on, so holding them back would leave the player nowhere to place a tower.
    /// </summary>
    private void CacheUnlockRounds()
    {
        unlockRounds = new int[towers.Count];

        int towerPosition = 0;
        for (int i = 0; i < towers.Count; i++)
        {
            bool support = IsSupportPiece(towers[i]);
            if (!support)
            {
                towerPosition++;
            }

            unlockRounds[i] = towers[i].unlockRound > 0
                ? towers[i].unlockRound
                : (support ? 1 : towerPosition);
        }
    }

    /// <summary>True once the player has reached the round that releases this offer.</summary>
    public bool IsReleased(int index)
    {
        return ReleasedThroughRound >= GetUnlockRound(index);
    }

    public bool CanAfford(int price)
    {
        return energy >= Mathf.Max(0, price);
    }

    public bool TrySpend(int price)
    {
        price = Mathf.Max(0, price);
        if (!CanAfford(price))
        {
            return false;
        }

        energy -= price;
        SyncDisplayedEnergy();
        RefreshUI();
        return true;
    }

    public void AddEnergy(int amount)
    {
        energy = Mathf.Max(0, energy + amount);
        SyncDisplayedEnergy();
        RefreshUI();
    }

    public void BuyHealthPotion()
    {
        if (player == null)
        {
            player = FindFirstObjectByType<PlayerController>();
        }

        if (player == null || !CanAfford(potionPrice))
        {
            return;
        }

        // Heal first so a full-health player is never charged.
        if (!player.Heal(potionHealAmount))
        {
            return;
        }

        TrySpend(potionPrice);
    }

    public void ToggleRepairMode()
    {
        SetRepairMode(!repairMode);
    }

    /// <summary>
    /// Enters or leaves cage-repair mode. Entering clears the selected tower so a
    /// click repairs the cage under the cursor instead of placing a piece there.
    /// </summary>
    public void SetRepairMode(bool active)
    {
        repairMode = active;

        if (repairMode)
        {
            selectedIndex = -1;
            if (placement != null)
            {
                placement.SetSelectedTower(null);
            }
        }

        RefreshUI();
    }

    /// <summary>Pays for and fixes a broken cage. Returns false when it cannot be repaired.</summary>
    public bool TryRepairCage(CageTower cage)
    {
        if (cage == null || !cage.IsBroken || !TrySpend(cageRepairPrice))
        {
            return false;
        }

        cage.FixCage();
        PlaySfx(cageRepairSfx);

        // Nothing left to spend on the next cage, so drop out of repair mode.
        if (!CanAfford(cageRepairPrice))
        {
            SetRepairMode(false);
        }

        return true;
    }

    /// <summary>Adds energy and rolls the displayed counter up to the new total.</summary>
    public void AddEnergyAnimated(int amount)
    {
        energy = Mathf.Max(0, energy + amount);
        if (energyTickRoutine == null)
        {
            energyTickRoutine = StartCoroutine(TickDisplayedEnergy());
        }

        RefreshUI();
    }

    private void SyncDisplayedEnergy()
    {
        // While a count-up is running it converges to the new total on its own.
        if (energyTickRoutine == null)
        {
            displayedEnergy = energy;
        }
    }

    private IEnumerator TickDisplayedEnergy()
    {
        while (!Mathf.Approximately(displayedEnergy, energy))
        {
            float gap = Mathf.Abs(energy - displayedEnergy);
            float speed = Mathf.Max(60f, gap * 4f);
            displayedEnergy = Mathf.MoveTowards(displayedEnergy, energy, speed * Time.deltaTime);
            RefreshUI();
            yield return null;
        }

        displayedEnergy = energy;
        energyTickRoutine = null;
        RefreshUI();
    }

    /// <summary>
    /// Shows the energy earned above a tower, flies the number to the energy display,
    /// then adds the amount with an animated count-up.
    /// </summary>
    public void ShowEnergyPayout(Vector3 worldPosition, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        if (canvasObject == null || canvasRect == null || energyText == null)
        {
            AddEnergyAnimated(amount);
            return;
        }

        StartCoroutine(EnergyPayoutRoutine(worldPosition, amount));
    }

    private IEnumerator EnergyPayoutRoutine(Vector3 worldPosition, int amount)
    {
        Camera worldCamera = Camera.main;
        if (worldCamera == null)
        {
            AddEnergyAnimated(amount);
            yield break;
        }

        Text payoutText = CreateText("Energy Payout", canvasObject.transform, 40, TextAnchor.MiddleCenter);
        payoutText.text = "+" + amount;
        payoutText.color = energyPayoutColor;
        payoutText.fontStyle = FontStyle.Bold;
        payoutText.raycastTarget = false;
        payoutText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);

        RectTransform payoutRect = payoutText.rectTransform;
        payoutRect.sizeDelta = new Vector2(240f, 64f);
        // Last sibling of the canvas root, so the payout number draws on top of the shop panel.
        payoutRect.SetAsLastSibling();

        const float holdDuration = 0.6f;
        const float driftDistance = 0.4f;
        for (float elapsed = 0f; elapsed < holdDuration; elapsed += Time.deltaTime)
        {
            Vector3 driftedPosition = worldPosition
                + Vector3.up * (0.9f + driftDistance * (elapsed / holdDuration));
            payoutRect.anchoredPosition = WorldToCanvasPoint(worldCamera, driftedPosition);
            yield return null;
        }

        Vector2 flightStart = payoutRect.anchoredPosition;
        const float flightDuration = 0.45f;
        for (float elapsed = 0f; elapsed < flightDuration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / flightDuration);
            payoutRect.anchoredPosition = Vector2.Lerp(flightStart, GetEnergyTextCanvasPoint(), t);
            yield return null;
        }

        Destroy(payoutText.gameObject);
        AddEnergyAnimated(amount);
    }

    private Vector2 WorldToCanvasPoint(Camera worldCamera, Vector3 worldPosition)
    {
        Vector2 screenPoint = worldCamera.WorldToScreenPoint(worldPosition);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPoint,
            null,
            out Vector2 localPoint);
        return localPoint;
    }

    private Vector2 GetEnergyTextCanvasPoint()
    {
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, energyText.rectTransform.position);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect,
            screenPoint,
            null,
            out Vector2 localPoint);
        return localPoint;
    }

    public void SelectTower(int index)
    {
        if (index < 0 || index >= towers.Count || placement == null)
        {
            return;
        }

        TowerOffer offer = towers[index];
        if (GetOfferSprite(offer) == null || !CanAfford(offer.price) || !IsReleased(index))
        {
            return;
        }

        selectedIndex = index;
        repairMode = false;
        placement.SetSelectedTower(offer);
        RefreshUI();
    }

    /// <summary>The art this offer shows in the shop and under the placement ghost.</summary>
    public static Sprite GetOfferSprite(TowerOffer offer)
    {
        if (offer == null || offer.prefab == null)
        {
            return null;
        }

        SpriteRenderer prefabRenderer = offer.prefab.GetComponent<SpriteRenderer>();
        return prefabRenderer != null ? prefabRenderer.sprite : null;
    }

    /// <summary>What this piece does, as written on its prefab. Empty when none was set.</summary>
    public static string GetDescription(TowerOffer offer)
    {
        TowerPlacementInfo info = GetPlacementInfo(offer);
        return info != null ? info.Description : string.Empty;
    }

    /// <summary>Towers that fire or push in a specific direction and may be rotated.</summary>
    public static bool IsRotatable(TowerOffer offer)
    {
        TowerPlacementInfo info = GetPlacementInfo(offer);
        return info != null && info.Rotatable;
    }

    /// <summary>The local-space direction an un-rotated tower aims at.</summary>
    public static Vector2 GetAimDirection(TowerOffer offer)
    {
        TowerPlacementInfo info = GetPlacementInfo(offer);
        return info != null ? info.AimDirection : Vector2.left;
    }

    /// <summary>Pieces that may be placed without a cage directly beneath them.</summary>
    public static bool IsSupportPiece(TowerOffer offer)
    {
        TowerPlacementInfo info = GetPlacementInfo(offer);
        return info != null && info.SupportPiece;
    }

    /// <summary>Pieces the player can stand inside, so their cell stays placeable.</summary>
    public static bool IsWalkThrough(TowerOffer offer)
    {
        TowerPlacementInfo info = GetPlacementInfo(offer);
        return info != null && info.WalkThrough;
    }

    private static TowerPlacementInfo GetPlacementInfo(TowerOffer offer)
    {
        return offer != null && offer.prefab != null
            ? offer.prefab.GetComponent<TowerPlacementInfo>()
            : null;
    }

    public GameObject CreateTower(TowerOffer offer, Vector2 position, float gridCellSize, Quaternion rotation)
    {
        if (offer == null || offer.prefab == null)
        {
            Debug.LogWarning(
                $"Tower offer '{offer?.displayName}' has no prefab assigned, so nothing was placed.",
                this);
            return null;
        }

        bool rotatable = IsRotatable(offer);
        GameObject tower = Instantiate(
            offer.prefab,
            position,
            rotatable ? rotation : Quaternion.identity);
        tower.name = offer.displayName;

        // No aim marker on a placed tower: the arrow is a placement-time preview only,
        // and the tower's own art shows which way it ended up facing.

        // Which cages a tower stands on depends on where it was dropped, so it is
        // resolved per placement rather than baked into the asset.
        TowerCageStack cageStack = tower.GetComponent<TowerCageStack>();
        if (cageStack != null)
        {
            cageStack.Initialize(gridCellSize);
        }

        // Recording the cell here is what lets placement answer "what is in this cell?"
        // from a dictionary instead of a physics overlap every frame.
        TowerGrid.Register(tower);

        PlaySfx(placementSfx);
        return tower;
    }

    /// <summary>
    /// Adds the white arrow that shows which way a directional piece will fire while it
    /// is still being placed. Parented to the placement ghost, so it turns with the
    /// R-key rotations. Placed towers get no marker.
    /// </summary>
    public static GameObject CreateAimIndicator(Transform parent, Vector2 aimDirection, float cellSize)
    {
        GameObject indicator = new GameObject("Aim Arrow");
        indicator.transform.SetParent(parent, false);
        PointAimIndicator(indicator.transform, aimDirection, cellSize);

        SpriteRenderer renderer = indicator.AddComponent<SpriteRenderer>();
        renderer.sprite = GetAimArrowSprite();
        renderer.color = Color.white;

        SpriteRenderer parentRenderer = parent.GetComponent<SpriteRenderer>();
        if (parentRenderer != null)
        {
            renderer.sortingLayerID = parentRenderer.sortingLayerID;
            renderer.sortingOrder = parentRenderer.sortingOrder + 1;
        }

        return indicator;
    }

    /// <summary>
    /// Puts the arrow just outside the ghost's edge, turned to point down the aim
    /// direction. Re-run whenever the selected piece changes, since each one aims
    /// its own way.
    /// </summary>
    public static void PointAimIndicator(Transform indicator, Vector2 aimDirection, float cellSize)
    {
        if (indicator == null)
        {
            return;
        }

        Vector2 direction = aimDirection.sqrMagnitude > 0.000001f
            ? aimDirection.normalized
            : Vector2.left;

        indicator.localPosition = direction * (cellSize * 0.55f);
        // The arrow art points along +X, so one turn to the aim direction orients it.
        indicator.localRotation = Quaternion.Euler(
            0f,
            0f,
            Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        indicator.localScale = new Vector3(cellSize * 0.45f, cellSize * 0.45f, 1f);
    }

    /// <summary>
    /// Draws the arrow once into a shared texture: a shaft that opens into a head
    /// tapering to a point at the right edge. Generated rather than imported so the
    /// marker needs no art asset.
    /// </summary>
    private static Sprite GetAimArrowSprite()
    {
        if (aimArrowSprite != null)
        {
            return aimArrowSprite;
        }

        const int size = 64;
        // All measured in pixels of the square texture, along its +X axis.
        const float shaftEnd = 34f;
        const float shaftHalfHeight = 7f;
        const float headHalfHeight = 20f;

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Aim Arrow Texture",
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32 arrowPixel = new Color32(255, 255, 255, 255);
        Color32 emptyPixel = new Color32(255, 255, 255, 0);
        Color32[] pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            float distanceFromAxis = Mathf.Abs(y - center);
            for (int x = 0; x < size; x++)
            {
                float halfHeight = x < shaftEnd
                    ? shaftHalfHeight
                    : Mathf.Lerp(headHalfHeight, 0f, (x - shaftEnd) / (size - shaftEnd));
                pixels[y * size + x] = distanceFromAxis <= halfHeight ? arrowPixel : emptyPixel;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        aimArrowSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        aimArrowSprite.name = "Aim Arrow";
        return aimArrowSprite;
    }

    private static void PlaySfx(AudioClip clip)
    {
        if (clip != null)
        {
            AudioController.Play(clip);
        }
    }

    private void BuildShopUI()
    {
        EnsureEventSystem();

        canvasObject = new GameObject("Tower Shop Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        canvasRect = canvasObject.GetComponent<RectTransform>();

        // The wireframe box belongs to the whole menu and stays unchanged between tabs.
        GameObject root = CreateUIObject("Tower Shop", canvasObject.transform);

        shopRootRect = root.GetComponent<RectTransform>();
        // Stretched down the full height of the canvas rather than sized to its contents,
        // so the menu is a column running from the top of the screen to the bottom. The
        // edge padding is taken off both ends by the negative height in sizeDelta.
        shopRootRect.anchorMin = new Vector2(0f, 0f);
        shopRootRect.anchorMax = new Vector2(0f, 1f);
        shopRootRect.pivot = new Vector2(0f, 0.5f);
        shopRootRect.anchoredPosition = new Vector2(screenEdgePadding, 0f);
        shopRootRect.sizeDelta = new Vector2(MenuWidth, -screenEdgePadding * 2f);

        VerticalLayoutGroup rootLayout = root.AddComponent<VerticalLayoutGroup>();
        rootLayout.spacing = 0f;
        rootLayout.childAlignment = TextAnchor.UpperCenter;
        rootLayout.childControlHeight = true;
        rootLayout.childControlWidth = true;
        rootLayout.childForceExpandHeight = false;

        BuildTabBar(root.transform);

        GameObject panel = CreateMenuPanel(root.transform, "Menu Panel");

        // The tab bar takes its band off the top and the box below it takes everything
        // that is left, which is what carries the menu down to the bottom of the screen
        // however much - or little - the open page has to show.
        panel.AddComponent<LayoutElement>().flexibleHeight = 1f;

        GameObject energyRow = CreateUIObject("Energy Row", panel.transform);
        // Tall enough that the total is not crowded against the tabs above it.
        energyRow.AddComponent<LayoutElement>().preferredHeight = 74f;
        energyText = CreateText("Energy", energyRow.transform, 34, TextAnchor.MiddleCenter);
        energyText.color = labelColor;
        StretchLabel(energyText, 0f);
        energyText.rectTransform.anchoredPosition = energyTextOffset;
        buildPage = BuildBuildPage(panel.transform);
        roundPage = BuildRoundPage(panel.transform);

        ShowTab(false);
        FitMenuToScreen();
    }

    private void BuildTabBar(Transform parent)
    {
        GameObject bar = CreateUIObject("Tabs", parent);

        // The bar keeps its band and nothing more. Without the explicit 0 the row layout
        // added below reports a flexible height of its own, and the tabs would take half
        // of the space the full-height menu leaves over instead of all of it going to
        // the box underneath.
        LayoutElement barSize = bar.AddComponent<LayoutElement>();
        barSize.preferredHeight = 56f;
        barSize.flexibleHeight = 0f;

        HorizontalLayoutGroup row = bar.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 6f;
        row.childControlHeight = true;
        row.childControlWidth = true;
        row.childForceExpandWidth = true;

        CreateTabButton(bar.transform, "Build", false, out buildTabOutline, out buildTabLabel);
        CreateTabButton(bar.transform, "Round", true, out roundTabOutline, out roundTabLabel);
    }

    private void CreateTabButton(
        Transform parent,
        string tabName,
        bool opensRoundTab,
        out UIWireframeBox outline,
        out Text label)
    {
        Button button = CreateBareButton(tabName + " Tab", parent, 56f);
        button.onClick.AddListener(() => ShowTab(opensRoundTab));

        // Open at the bottom: the top line of the box below closes the tab off, so the
        // tab and the menu it opens read as one shape rather than two stacked boxes.
        outline = AddOutline(button.gameObject, drawBottom: false);

        label = CreateText(
            "Label", button.transform, ScaledFontSize(26), TextAnchor.MiddleCenter);
        label.text = tabName;
        label.color = labelColor;
        StretchLabel(label, 4f);
    }

    /// <summary>
    /// A button with no artwork of its own. The background image stays fully transparent
    /// and exists only to take clicks and carry a faint hover wash, so nothing but the
    /// label and icon show against the wireframe.
    /// </summary>
    private Button CreateBareButton(string objectName, Transform parent, float preferredHeight)
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

        // Buttons are a fixed size wherever they appear. The tower rows carry a layout
        // group of their own, which would otherwise offer to grow and leave the list
        // stretched across the menu instead of the spacer taking up the slack.
        LayoutElement buttonSize = buttonObject.AddComponent<LayoutElement>();
        buttonSize.preferredHeight = preferredHeight;
        buttonSize.flexibleHeight = 0f;
        return button;
    }

    /// <summary>Traces a wireframe box around <paramref name="target"/> without joining its layout.</summary>
    private UIWireframeBox AddOutline(GameObject target, bool drawBottom)
    {
        GameObject outlineObject = CreateUIObject("Outline", target.transform);

        RectTransform outlineRect = outlineObject.GetComponent<RectTransform>();
        outlineRect.anchorMin = Vector2.zero;
        outlineRect.anchorMax = Vector2.one;
        outlineRect.offsetMin = Vector2.zero;
        outlineRect.offsetMax = Vector2.zero;

        // Decoration, not content: a layout group on the target must leave it alone.
        outlineObject.AddComponent<LayoutElement>().ignoreLayout = true;

        UIWireframeBox outline = outlineObject.AddComponent<UIWireframeBox>();
        outline.Color = outlineColor;
        outline.Thickness = outlineThickness;
        outline.SetSides(true, true, true, drawBottom);
        return outline;
    }

    /// <summary>The outline colour a closed tab and the description box are drawn in.</summary>
    private Color FadedOutlineColor(float alphaScale)
    {
        return new Color(outlineColor.r, outlineColor.g, outlineColor.b, outlineColor.a * alphaScale);
    }

    /// <summary>
    /// Applies the editable X/Y scale, then uniformly reduces both axes further if
    /// either dimension would leave the canvas. Uniform fitting preserves the chosen
    /// relationship between the two manual scale values.
    /// </summary>
    private void FitMenuToScreen()
    {
        if (shopRootRect == null || canvasRect == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        // Re-stated rather than left from construction, so an edge padding edited in the
        // Inspector still leaves the column reaching both screen edges.
        shopRootRect.sizeDelta = new Vector2(MenuWidth, -screenEdgePadding * 2f);
        LayoutRebuilder.ForceRebuildLayoutImmediate(shopRootRect);

        Vector2 canvasSize = canvasRect.rect.size;
        float availableWidth = Mathf.Max(1f, canvasSize.x - screenEdgePadding * 2f);
        float availableHeight = Mathf.Max(1f, canvasSize.y - screenEdgePadding * 2f);
        float preferredWidth = Mathf.Max(1f, shopRootRect.rect.width) * menuScaleX;
        float preferredHeight = Mathf.Max(1f, shopRootRect.rect.height) * menuScaleY;
        float fitMultiplier = Mathf.Min(
            1f,
            availableWidth / preferredWidth,
            availableHeight / preferredHeight);

        shopRootRect.localScale = new Vector3(
            menuScaleX * fitMultiplier,
            menuScaleY * fitMultiplier,
            1f);
        shopRootRect.anchoredPosition = new Vector2(screenEdgePadding, 0f);
        lastCanvasSize = canvasSize;
    }

    /// <summary>
    /// The container both tabs open into: a closed wireframe box with the game visible
    /// through it, rather than a filled background sprite.
    /// </summary>
    private GameObject CreateMenuPanel(Transform parent, string panelName)
    {
        GameObject panel = CreateUIObject(panelName, parent);
        AddOutline(panel, drawBottom: true);
        AddPageLayout(panel);
        return panel;
    }

    private GameObject BuildBuildPage(Transform parent)
    {
        GameObject page = CreateUIObject("Build Page", parent);
        AddPageLayout(page).padding = new RectOffset(0, 0, 0, 0);

        // Fills the full-height box the way the Round page does, so the description and
        // the repair button ride the bottom edge instead of trailing the tower list.
        page.AddComponent<LayoutElement>().flexibleHeight = 1f;

        for (int i = 0; i < towers.Count; i++)
        {
            int capturedIndex = i;
            TowerOffer offer = towers[i];
            Button button = CreateButton(page.transform, offer, capturedIndex);
            button.onClick.AddListener(() => SelectTower(capturedIndex));
            towerButtons.Add(button);
        }

        CreateSpacer(page.transform);
        BuildDescriptionBox(
            page.transform, out descriptionTitle, out descriptionBody, out descriptionHint);
        repairButton = CreateRepairButton(page.transform);
        return page;
    }

    /// <summary>
    /// The box at the bottom of a page that explains whatever is selected or hovered.
    /// Its height is fixed, so a short description and a long one leave the menu the
    /// same size. Both tabs get one: the Build page describes the selected piece, the
    /// Round page the enemy under the cursor.
    /// </summary>
    private void BuildDescriptionBox(Transform parent, out Text title, out Text body, out Text hint)
    {
        const float boxHeight = 158f;

        GameObject box = CreateUIObject("Description Box", parent);
        LayoutElement boxSize = box.AddComponent<LayoutElement>();
        boxSize.minHeight = boxHeight;
        boxSize.preferredHeight = boxHeight;
        // The body text inside is free to grow into the box, but the box itself is not:
        // the slack in a full-height menu belongs to the spacer above it.
        boxSize.flexibleHeight = 0f;

        // Drawn thinner and dimmer than the menu box, so it reads as a section inside
        // the menu instead of competing with it.
        UIWireframeBox outline = AddOutline(box, drawBottom: true);
        outline.Color = FadedOutlineColor(0.5f);
        outline.Thickness = Mathf.Max(1f, outlineThickness * 0.6f);

        VerticalLayoutGroup layout = box.AddComponent<VerticalLayoutGroup>();
        // Only enough side padding to clear the outline itself. Every pixel taken off the
        // sides is a pixel the body text has to wrap in, and a narrow box is what makes
        // the wrap fall inside a word instead of between two of them.
        layout.padding = new RectOffset(4, 4, 8, 8);
        layout.spacing = 2f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        title = CreateText(
            "Title", box.transform, ScaledFontSize(22), TextAnchor.UpperLeft);
        title.fontStyle = FontStyle.Bold;
        title.color = highlightColor;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        body = CreateText(
            "Body", box.transform, ScaledFontSize(18), TextAnchor.UpperLeft);
        body.color = labelColor;
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Truncate;
        // A wordy tower shrinks its text to fit rather than being cut off mid-sentence.
        body.resizeTextForBestFit = true;
        body.resizeTextMinSize = 12;
        body.resizeTextMaxSize = Mathf.Max(12, ScaledFontSize(18));
        body.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

        hint = CreateText(
            "Hint", box.transform, ScaledFontSize(16), TextAnchor.LowerLeft);
        hint.color = DimmedLabelColor;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
    }

    private GameObject BuildRoundPage(Transform parent)
    {
        GameObject page = CreateUIObject("Round Page", parent);
        AddPageLayout(page).padding = new RectOffset(0, 0, 0, 0);

        // The box keeps the taller Build page's height, so this page stretches into the
        // leftover space instead of ending halfway down it.
        page.AddComponent<LayoutElement>().flexibleHeight = 1f;

        roundTitleText = CreateText("Round Title", page.transform, 32, TextAnchor.MiddleCenter);
        roundTitleText.fontStyle = FontStyle.Bold;
        roundTitleText.color = labelColor;
        roundTitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        // "Coming next" labels the list, so the two are one block with its own tight
        // spacing. The page's row spacing then applies around the pair rather than
        // between them, which is what used to leave the label floating.
        GameObject nextWaveBlock = CreateUIObject("Next Wave", page.transform);
        VerticalLayoutGroup blockLayout = nextWaveBlock.AddComponent<VerticalLayoutGroup>();
        blockLayout.spacing = 2f;
        blockLayout.childAlignment = TextAnchor.UpperLeft;
        blockLayout.childControlHeight = true;
        blockLayout.childControlWidth = true;
        blockLayout.childForceExpandHeight = false;

        GameObject subtitleRow = CreateUIObject("Round Subtitle Row", nextWaveBlock.transform);
        subtitleRow.AddComponent<LayoutElement>().preferredHeight = 30f;
        // Sits on the bottom of its row, so the label rests on the first enemy rather
        // than being centred in a band of empty space.
        roundSubtitleText = CreateText(
            "Round Subtitle", subtitleRow.transform, 20, TextAnchor.LowerLeft);
        roundSubtitleText.color = new Color(0.75f, 0.8f, 0.88f, 1f);
        StretchLabel(roundSubtitleText, 0f);
        roundSubtitleText.rectTransform.anchoredPosition = comingNextTextOffset;

        GameObject list = CreateUIObject("Enemy List", nextWaveBlock.transform);
        VerticalLayoutGroup listLayout = list.AddComponent<VerticalLayoutGroup>();
        listLayout.spacing = 2f;
        listLayout.childAlignment = TextAnchor.UpperLeft;
        listLayout.childControlHeight = true;
        listLayout.childControlWidth = true;
        listLayout.childForceExpandHeight = false;
        ContentSizeFitter listFitter = list.AddComponent<ContentSizeFitter>();
        listFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        enemyListRoot = list.transform;

        CreateSpacer(page.transform);

        // Sits below the spacer so it reads off the bottom of the menu, the way the
        // Build tab's description box does under the tower list.
        BuildDescriptionBox(page.transform, out enemyTitle, out enemyBody, out enemyHint);
        ClearEnemyDescription();

        // Under the description rather than up with the preview: reading what is coming
        // is what decides whether to heal, so the offer sits between that and the button
        // that starts the round.
        potionButton = CreatePotionButton(page.transform);

        StartRoundButton = CreateStartRoundButton(page.transform);
        return page;
    }

    /// <summary>
    /// An empty row that swallows whatever height the rows around it do not use. It is
    /// what holds the description box and the button under it against the bottom of the
    /// menu while the content above stays at the top.
    /// </summary>
    private static void CreateSpacer(Transform parent)
    {
        GameObject spacer = CreateUIObject("Spacer", parent);
        LayoutElement spacerLayout = spacer.AddComponent<LayoutElement>();
        spacerLayout.minHeight = 0f;
        spacerLayout.preferredHeight = 0f;
        spacerLayout.flexibleHeight = 1f;
    }

    /// <summary>The shared vertical stack used by the panel and by each tab page.</summary>
    private VerticalLayoutGroup AddPageLayout(GameObject target)
    {
        VerticalLayoutGroup layout = target.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = menuItemSpacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        return layout;
    }

    /// <summary>Switches content while leaving the wireframe box around it unchanged.</summary>
    public void ShowTab(bool roundTab)
    {
        showingRoundTab = roundTab;
        buildPage?.SetActive(!roundTab);
        roundPage?.SetActive(roundTab);

        // The hidden page's rows report no pointer exit, so a tab switch clears the
        // hover itself rather than leaving the last row described.
        hoveredIndex = -1;

        if (roundTab)
        {
            RefreshRoundPage();
        }

        RefreshUI();
        FitMenuToScreen();
    }

    /// <summary>
    /// Rewrites the round tab from the spawner: which round is next and what it fields.
    /// Called when the tab opens and when a build phase begins, which is the only time
    /// the upcoming wave can have changed.
    /// </summary>
    private void RefreshRoundPage()
    {
        if (roundTitleText == null || enemyListRoot == null)
        {
            return;
        }

        for (int i = enemyListRoot.childCount - 1; i >= 0; i--)
        {
            // Destroy only takes effect at the end of the frame, so the old rows are
            // detached now to keep them out of a rebuild that happens this frame.
            GameObject oldRow = enemyListRoot.GetChild(i).gameObject;
            oldRow.transform.SetParent(null, false);
            Destroy(oldRow);
        }

        // A destroyed row never reports the pointer leaving it, so the description of
        // whatever was hovered when the list was rebuilt is cleared here instead.
        ClearEnemyDescription();

        if (waveSpawner == null)
        {
            waveSpawner = FindFirstObjectByType<WaveSpawner>();
        }

        if (waveSpawner == null)
        {
            roundTitleText.text = "Round";
            roundSubtitleText.text = string.Empty;
            return;
        }

        if (!waveSpawner.HasNextWave)
        {
            roundTitleText.text = "Round " + waveSpawner.CurrentRoundNumber;
            roundSubtitleText.text = "All rounds cleared.";
            return;
        }

        roundTitleText.text = "Round " + waveSpawner.NextRoundNumber + " / " + waveSpawner.TotalRounds;
        roundSubtitleText.text = "Coming next:";

        waveSpawner.GetNextWavePreview(wavePreview);
        float rowBudget = GetEnemyPreviewRowBudget(wavePreview.Count);
        for (int i = 0; i < wavePreview.Count; i++)
        {
            CreateEnemyPreviewRow(wavePreview[i], rowBudget);
        }

        if (wavePreview.Count == 0)
        {
            roundSubtitleText.text = "No enemies this round.";
        }
    }

    /// <summary>
    /// One enemy in the next wave, read across rather than down: the art in a square cell
    /// on the left with the name and count beside it, so a wave of several types stays a
    /// short list instead of a column of full-width pictures.
    /// </summary>
    private void CreateEnemyPreviewRow(WaveSpawner.WavePreviewEntry entry, float maxRowHeight)
    {
        // Padding around the art inside its cell, and the gap between cell and name.
        const float artInset = 4f;
        const float labelGap = 12f;

        float rowHeight = Mathf.Clamp(maxRowHeight, 34f, 84f);
        float artCellSize = rowHeight;

        GameObject rowObject = CreateUIObject(entry.prefab.name + " Preview", enemyListRoot);
        rowObject.AddComponent<LayoutElement>().preferredHeight = rowHeight;

        // The row itself is what the pointer hits: an invisible graphic behind the name
        // and the art, both of which stay raycast-free so the whole row is one target.
        Image hoverWash = rowObject.AddComponent<Image>();
        hoverWash.color = RowWashColor(false);

        GameObject artObject = CreateUIObject("Art", rowObject.transform);
        Image art = artObject.AddComponent<Image>();
        art.sprite = GetEnemySprite(entry.prefab);
        art.preserveAspect = true;
        art.raycastTarget = false;
        art.enabled = art.sprite != null;

        // A square cell pinned to the left edge. Aspect is preserved inside it, so a wide
        // enemy and a tall one both sit on the same left margin and the same baseline.
        RectTransform artRect = art.rectTransform;
        artRect.anchorMin = new Vector2(0f, 0f);
        artRect.anchorMax = new Vector2(0f, 1f);
        artRect.pivot = new Vector2(0f, 0.5f);
        artRect.sizeDelta = new Vector2(artCellSize, -artInset * 2f);
        artRect.anchoredPosition = new Vector2(artInset, 0f);

        Text label = CreateText(
            "Label", rowObject.transform, ScaledFontSize(22), TextAnchor.MiddleLeft);
        label.text = entry.prefab.name + "   x" + entry.count;
        label.color = labelColor;
        label.fontStyle = FontStyle.Bold;
        label.raycastTarget = false;
        // A long enemy name shrinks to stay inside the column rather than running out
        // past the edge of the menu box.
        label.resizeTextForBestFit = true;
        label.resizeTextMinSize = 12;
        label.resizeTextMaxSize = Mathf.Max(12, ScaledFontSize(22));

        // Starts where the art cell ends, so a long enemy name never runs under the art.
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(artCellSize + labelGap, 0f);
        labelRect.offsetMax = new Vector2(-6f, 0f);

        // Captured now rather than read back off the row, since the row is destroyed and
        // rebuilt whenever the upcoming wave changes.
        GameObject enemyPrefab = entry.prefab;
        int enemyCount = entry.count;
        EventTrigger hover = rowObject.AddComponent<EventTrigger>();
        AddPointerTrigger(hover, EventTriggerType.PointerEnter, () =>
        {
            hoverWash.color = RowWashColor(true);
            label.color = highlightColor;
            ShowEnemyDescription(enemyPrefab, enemyCount);
        });
        AddPointerTrigger(hover, EventTriggerType.PointerExit, () =>
        {
            hoverWash.color = RowWashColor(false);
            label.color = labelColor;
            ClearEnemyDescription();
        });
    }

    /// <summary>Wires one pointer event on <paramref name="trigger"/> to <paramref name="response"/>.</summary>
    private static void AddPointerTrigger(EventTrigger trigger, EventTriggerType eventID, Action response)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry { eventID = eventID };
        entry.callback.AddListener(_ => response());
        trigger.triggers.Add(entry);
    }

    /// <summary>The faint wash that marks the enemy row under the cursor.</summary>
    private static Color RowWashColor(bool hovered)
    {
        return new Color(1f, 1f, 1f, hovered ? 0.1f : 0f);
    }

    /// <summary>
    /// Fills the round tab's description box from the hovered enemy's own prefab, so the
    /// text and the numbers cannot fall out of step with how that enemy actually plays.
    /// </summary>
    private void ShowEnemyDescription(GameObject enemyPrefab, int count)
    {
        if (enemyTitle == null || enemyPrefab == null)
        {
            return;
        }

        enemyTitle.text = enemyPrefab.name + "   x" + count;

        Enemy enemy = enemyPrefab.GetComponent<Enemy>();
        string description = enemy != null ? enemy.Description : string.Empty;
        enemyBody.text = string.IsNullOrWhiteSpace(description)
            ? "No description yet. Add one on this prefab's Enemy component."
            : description;
        enemyHint.text = BuildEnemyHint(enemy);
    }

    /// <summary>What the box says with the cursor off the list, and after a rebuild.</summary>
    private void ClearEnemyDescription()
    {
        if (enemyTitle == null)
        {
            return;
        }

        enemyTitle.text = "Enemies";
        enemyBody.text = "Hover an enemy above to read what it does.";
        enemyHint.text = string.Empty;
    }

    /// <summary>
    /// The stat line under an enemy's description. Read off the prefab rather than a live
    /// enemy, so only traits that are settled before it spawns belong here.
    /// </summary>
    private static string BuildEnemyHint(Enemy enemy)
    {
        if (enemy == null)
        {
            return string.Empty;
        }

        // A breaker's prefab health stands in for "cannot be shot down", so the number is
        // only worth showing for enemies that can be damaged as they arrive.
        string hint = enemy.CanTakeDamage
            ? Mathf.Max(1, Mathf.RoundToInt(enemy.health)) + " HP"
            : "Cannot be shot down";

        return enemy.isCagable ? hint + "   |   Can be caged" : hint;
    }

    /// <summary>
    /// The height each preview may take. The list as a whole is capped so the round page
    /// cannot outgrow the box, which means a wave of one enemy type spends the lot on it
    /// and four types share it. The row itself clamps this, so a short list is a list of
    /// readable rows rather than a few enormous ones.
    /// </summary>
    private static float GetEnemyPreviewRowBudget(int entryCount)
    {
        const float listHeight = 300f;
        const float rowSpacing = 2f;

        if (entryCount <= 0)
        {
            return 0f;
        }

        return Mathf.Max(28f, (listHeight - rowSpacing * (entryCount - 1)) / entryCount);
    }

    /// <summary>
    /// Enemy art can sit on a child of the prefab root, unlike tower offers, so this
    /// searches the whole prefab rather than only its root renderer.
    /// </summary>
    private static Sprite GetEnemySprite(GameObject enemyPrefab)
    {
        if (enemyPrefab == null)
        {
            return null;
        }

        SpriteRenderer renderer = enemyPrefab.GetComponentInChildren<SpriteRenderer>(true);
        return renderer != null ? renderer.sprite : null;
    }

    private Button CreateRepairButton(Transform parent)
    {
        Button button = CreateBareButton("Repair Cage", parent, 68f);
        button.onClick.AddListener(ToggleRepairMode);

        repairLabel = CreateText(
            "Label", button.transform, ScaledFontSize(24), TextAnchor.MiddleCenter);
        repairLabel.color = labelColor;
        StretchLabel(repairLabel, 8f);
        return button;
    }

    private Button CreatePotionButton(Transform parent)
    {
        Button button = CreateBareButton("Health Potion", parent, 68f);
        button.onClick.AddListener(BuyHealthPotion);

        potionLabel = CreateText(
            "Label", button.transform, ScaledFontSize(24), TextAnchor.MiddleCenter);
        potionLabel.text = PotionLabelText();
        potionLabel.color = labelColor;
        StretchLabel(potionLabel, 8f);
        return button;
    }

    /// <summary>
    /// The potion heals a flat amount, but reads as the share of the bar it fills so the
    /// offer stays meaningful without the player knowing their exact max health.
    /// </summary>
    private string PotionLabelText()
    {
        if (player == null || player.maxHealth <= 0)
        {
            return "Health Potion (+" + potionHealAmount + ")  " + potionPrice;
        }

        int percent = Mathf.Clamp(
            Mathf.RoundToInt(100f * potionHealAmount / player.maxHealth), 1, 100);
        return "Health Potion (+" + percent + "%)  " + potionPrice;
    }

    private Button CreateStartRoundButton(Transform parent)
    {
        Button button = CreateBareButton("Start Round", parent, 68f);

        Text label = CreateText(
            "Label", button.transform, ScaledFontSize(28), TextAnchor.MiddleCenter);
        label.text = "Start Round";
        label.color = startRoundLabelColor;
        StretchLabel(label, 0f);
        return button;
    }

    private Button CreateButton(Transform parent, TowerOffer offer, int index)
    {
        Button button = CreateBareButton("Tower " + index, parent, 66f);
        GameObject buttonObject = button.gameObject;

        HorizontalLayoutGroup row = buttonObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(8, 8, 6, 6);
        row.spacing = 10f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlHeight = true;
        // Widths under the layout's control, so the name label's flexible width below is
        // honoured and it spans everything the icon leaves rather than keeping the narrow
        // default box, which is what broke long tower names across lines. Force-expand
        // stays off so the slack all goes to the label instead of stretching the icon too.
        row.childControlWidth = true;
        row.childForceExpandWidth = false;

        GameObject iconObject = CreateUIObject("Icon", buttonObject.transform);
        Image icon = iconObject.AddComponent<Image>();
        icon.sprite = GetOfferSprite(offer);
        icon.preserveAspect = true;
        LayoutElement iconLayout = iconObject.AddComponent<LayoutElement>();
        // The cell is as wide as this sprite needs to fill that height, not a fixed square.
        // preserveAspect fits the art inside whichever side is tighter, so a square cell
        // drew every wider-than-tall icon smaller than its height allowed. Sizing the cell
        // to the sprite draws the art at full height again and leaves the width it does not
        // use to the name beside it - which is where the extra room for long names comes
        // from, rather than from taking it off the icon. A minimum as well as a preferred
        // size, so a long name cannot squeeze the icon back down to make room for itself.
        float iconHeight = 52f * buttonContentScale;
        float iconWidth = IconCellWidth(icon.sprite, iconHeight);
        iconLayout.minWidth = iconWidth;
        iconLayout.preferredWidth = iconWidth;
        iconLayout.preferredHeight = iconHeight;

        Text label = CreateText(
            "Label", buttonObject.transform, ScaledFontSize(24), TextAnchor.MiddleLeft);
        label.text = offer.displayName + "  " + offer.price;
        label.color = labelColor;
        LayoutElement labelLayout = label.gameObject.AddComponent<LayoutElement>();
        labelLayout.flexibleWidth = 1f;
        labelLayout.preferredHeight = 52f;

        // The description box reads the row under the cursor rather than the selected
        // one, so a piece can be read up on before any of it is paid for. The label and
        // icon still mark what is selected; this only moves what the box is describing.
        EventTrigger hover = buttonObject.AddComponent<EventTrigger>();
        AddPointerTrigger(hover, EventTriggerType.PointerEnter, () => SetHoveredTower(index));
        AddPointerTrigger(hover, EventTriggerType.PointerExit, () => ClearHoveredTower(index));

        // Recorded so the selected offer can be lit up without a button background.
        towerLabels.Add(label);
        towerIcons.Add(icon);
        return button;
    }

    /// <summary>
    /// Width a button's icon cell needs to draw <paramref name="sprite"/> at the full
    /// <paramref name="height"/> with its aspect kept. Capped at twice the height, so one
    /// unusually wide piece of art cannot take the row from the name next to it.
    /// </summary>
    private static float IconCellWidth(Sprite sprite, float height)
    {
        if (sprite == null || sprite.rect.height <= 0f)
        {
            return height;
        }

        return Mathf.Min(height * sprite.rect.width / sprite.rect.height, height * 2f);
    }

    private void RefreshUI()
    {
        if (energyText != null)
        {
            energyText.text = "Energy: " + Mathf.RoundToInt(displayedEnergy);
        }

        // With no button backgrounds left, affordability and selection are carried by
        // the label and icon instead of by a panel colour.
        for (int i = 0; i < towerButtons.Count; i++)
        {
            // A piece the round has not released yet leaves the list entirely rather than
            // sitting in it greyed out, so the menu only ever lists what can be built now.
            bool released = IsReleased(i);
            towerButtons[i].gameObject.SetActive(released);
            if (!released)
            {
                // A row that leaves the list never reports the pointer leaving it, so
                // the description it was filling is dropped here instead.
                if (hoveredIndex == i)
                {
                    hoveredIndex = -1;
                }

                continue;
            }

            TowerOffer offer = towers[i];
            bool available = GetOfferSprite(offer) != null && CanAfford(offer.price);
            towerButtons[i].interactable = available;

            towerLabels[i].color = i == selectedIndex
                ? highlightColor
                : (available ? labelColor : DimmedLabelColor);
            towerIcons[i].color = available ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        }

        if (potionButton != null)
        {
            // A potion at full health is a no-op the shop refuses anyway, so the
            // offer disappears entirely rather than sitting there greyed out.
            bool healingWouldHelp = player != null && player.CanBeHealed;
            potionButton.gameObject.SetActive(healingWouldHelp);

            bool affordable = CanAfford(potionPrice);
            potionButton.interactable = healingWouldHelp && affordable;
            potionLabel.color = affordable ? labelColor : DimmedLabelColor;
            potionLabel.text = PotionLabelText();
        }

        if (repairButton != null)
        {
            bool affordable = CanAfford(cageRepairPrice);
            repairButton.interactable = affordable;
            repairLabel.color = repairMode
                ? highlightColor
                : (affordable ? labelColor : DimmedLabelColor);
            repairLabel.text = repairMode
                ? "Click a Cage to Repair"
                : "Repair Cage  " + cageRepairPrice;
        }

        SetTabState(buildTabLabel, buildTabOutline, !showingRoundTab);
        SetTabState(roundTabLabel, roundTabOutline, showingRoundTab);
        RefreshDescription();
    }

    /// <summary>The open tab is lit; the closed one keeps a dimmer outline.</summary>
    private void SetTabState(Text label, UIWireframeBox outline, bool active)
    {
        if (label != null)
        {
            label.color = active ? highlightColor : labelColor;
        }

        if (outline != null)
        {
            outline.Color = active ? outlineColor : FadedOutlineColor(0.4f);
        }
    }

    /// <summary>Points the description box at the row the cursor has moved onto.</summary>
    private void SetHoveredTower(int index)
    {
        if (hoveredIndex == index)
        {
            return;
        }

        hoveredIndex = index;
        RefreshDescription();
    }

    /// <summary>
    /// Empties the box as the cursor leaves a row. Moving straight from one row to the
    /// next can report the new row's enter before the old row's exit, so only the row
    /// currently being described is allowed to clear it.
    /// </summary>
    private void ClearHoveredTower(int index)
    {
        if (hoveredIndex != index)
        {
            return;
        }

        hoveredIndex = -1;
        RefreshDescription();
    }

    /// <summary>
    /// Rewrites the description box for the piece under the cursor. Prices and text never
    /// change on their own, so the work is skipped unless the hover has moved - this runs
    /// from every energy tick.
    /// </summary>
    private void RefreshDescription()
    {
        if (descriptionTitle == null
            || (hoveredIndex == describedIndex && repairMode == describedRepairMode))
        {
            return;
        }

        describedIndex = hoveredIndex;
        describedRepairMode = repairMode;

        TowerOffer offer = hoveredIndex >= 0 && hoveredIndex < towers.Count
            ? towers[hoveredIndex]
            : null;
        if (offer == null)
        {
            // Armed repair still explains itself with the cursor off the list, since
            // that is the mode the next click is going to act in.
            if (repairMode)
            {
                descriptionTitle.text = "Repair Cage";
                descriptionBody.text = "Click a broken cage to fix it. A repaired cage can hold "
                    + "an enemy again and powers the towers stacked above it.";
                descriptionHint.text = cageRepairPrice + " energy per cage";
                return;
            }

            descriptionTitle.text = "Pieces";
            descriptionBody.text = "Hover a piece above to read what it does.";
            descriptionHint.text = string.Empty;
            return;
        }

        descriptionTitle.text = offer.displayName + "   " + offer.price;

        string description = GetDescription(offer);
        descriptionBody.text = string.IsNullOrWhiteSpace(description)
            ? "No description yet. Add one on this prefab's Tower Placement Info component."
            : description;
        descriptionHint.text = BuildPlacementHint(offer);
    }

    /// <summary>
    /// The one-line placement rules underneath the description. These come from the
    /// prefab's own flags rather than from the prose, so they cannot fall out of step
    /// with how the piece actually places.
    /// </summary>
    private static string BuildPlacementHint(TowerOffer offer)
    {
        string hint = IsSupportPiece(offer) ? "Stands on its own" : "Needs support below";

        if (IsRotatable(offer))
        {
            hint += "   |   R to rotate";
        }

        if (IsWalkThrough(offer))
        {
            hint += "   |   Walk through";
        }

        return hint;
    }

    private int ScaledFontSize(int baseSize)
    {
        return Mathf.Max(1, Mathf.RoundToInt(baseSize * buttonContentScale));
    }

    /// <summary>Makes a label fill its button, inset by <paramref name="horizontalPadding"/>.</summary>
    private static void StretchLabel(Text label, float horizontalPadding)
    {
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(horizontalPadding, 0f);
        labelRect.offsetMax = new Vector2(-horizontalPadding, 0f);
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
        // Truncate, the default, drops a line whole instead of clipping it, so a label whose
        // line runs a pixel past its box disappears outright. The menu font is taller per
        // point than the built-in one these boxes were measured against. The tower blurb sets
        // this back to Truncate on purpose - it shrinks itself to fit instead.
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

    /// <summary>
    /// Keeps the potion offer in step with the player's health. Health is not owned by
    /// the shop and is initialised after this component's Awake, so the offer is checked
    /// per frame instead of only when energy changes. Only runs during build phases,
    /// since the spawner disables this component while a wave is being fought.
    /// </summary>
    private void Update()
    {
        if (canvasRect != null && canvasRect.rect.size != lastCanvasSize)
        {
            FitMenuToScreen();
        }

        if (potionButton == null)
        {
            return;
        }

        bool healingWouldHelp = player != null && player.CanBeHealed;
        if (healingWouldHelp != potionButton.gameObject.activeSelf)
        {
            RefreshUI();
            FitMenuToScreen();
        }
    }

    private void OnDisable()
    {
        if (canvasObject != null)
            canvasObject.SetActive(false);

        // Repair is a build-phase action. Left armed, the cage markers would stay lit
        // through the wave and a stray click would repair a cage mid-fight.
        repairMode = false;
    }

    private void OnEnable()
    {
        if (canvasObject != null)
            canvasObject.SetActive(true);

        // Each build phase returns to Build with the next-round preview ready.
        if (repairMode)
        {
            SetRepairMode(false);
        }

        if (buildPage != null)
        {
            RefreshRoundPage();
            ShowTab(false);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        menuScaleX = Mathf.Max(0.1f, menuScaleX);
        menuScaleY = Mathf.Max(0.1f, menuScaleY);
        buttonContentScale = Mathf.Clamp(buttonContentScale, 0.25f, 2f);
        outlineThickness = Mathf.Max(1f, outlineThickness);
        menuItemSpacing = Mathf.Max(0f, menuItemSpacing);
        screenEdgePadding = Mathf.Max(0f, screenEdgePadding);

        if (Application.isPlaying)
        {
            FitMenuToScreen();
        }
    }
#endif
}
