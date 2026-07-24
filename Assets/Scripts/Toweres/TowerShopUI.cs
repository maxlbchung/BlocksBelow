using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
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
    }

    [Header("Shop")]
    [SerializeField, Min(0)] private int startingMoney = 100;
    [SerializeField] private List<TowerOffer> towers = new List<TowerOffer>();
    [SerializeField] private SquarePlacement placement;

    [Header("Health Potion")]
    [SerializeField, Min(1)] private int potionHealAmount = 5;
    [SerializeField, Min(0)] private int potionPrice = 25;
    [SerializeField] private PlayerController player;

    [Header("Cage Repair")]
    [SerializeField, Min(0)] private int cageRepairPrice = 10;

    [Header("Appearance")]
    [SerializeField] private Color panelColor = new Color(0.08f, 0.1f, 0.14f, 0.92f);
    [SerializeField] private Color buttonColor = new Color(0.2f, 0.24f, 0.3f, 1f);
    [SerializeField] private Color selectedColor = new Color(0.25f, 0.55f, 0.3f, 1f);
    [SerializeField] private Color startRoundColor = new Color(0.16f, 0.45f, 0.22f, 1f);
    [SerializeField] private Color coinPayoutColor = new Color(1f, 0.84f, 0.25f, 1f);

    [Header("Shop SFX")]
    [Tooltip("Per-tower sounds live on the tower prefabs; these two are shop actions.")]
    [SerializeField, AudioClipDropdown] private AudioClip placementSfx;
    [SerializeField, AudioClipDropdown] private AudioClip cageRepairSfx;

    private readonly List<Button> towerButtons = new List<Button>();
    private static Sprite aimIndicatorSprite;
    private Text moneyText;
    private Button potionButton;
    private Button repairButton;
    private Text repairLabel;
    private RectTransform canvasRect;
    private int money;
    private float displayedMoney;
    private Coroutine moneyTickRoutine;
    private int selectedIndex = -1;
    private bool repairMode;

    public int Money => money;
    public Button StartRoundButton { get; private set; }

    /// <summary>The configured offers, exposed for the prefab build tool.</summary>
    public IReadOnlyList<TowerOffer> Towers => towers;

    /// <summary>True while clicking a broken cage should repair it instead of placing a tower.</summary>
    public bool RepairMode => repairMode;

    GameObject canvasObject;

    private void Awake()
    {
        money = startingMoney;
        displayedMoney = money;

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

        BuildShopUI();
        RefreshUI();

        for (int i = 0; i < towers.Count; i++)
        {
            if (CanAfford(towers[i].price))
            {
                SelectTower(i);
                break;
            }
        }
    }

    public bool CanAfford(int price)
    {
        return money >= Mathf.Max(0, price);
    }

    public bool TrySpend(int price)
    {
        price = Mathf.Max(0, price);
        if (!CanAfford(price))
        {
            return false;
        }

        money -= price;
        SyncDisplayedMoney();
        RefreshUI();
        return true;
    }

    public void AddMoney(int amount)
    {
        money = Mathf.Max(0, money + amount);
        SyncDisplayedMoney();
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

    /// <summary>Adds money and rolls the displayed counter up to the new total.</summary>
    public void AddMoneyAnimated(int amount)
    {
        money = Mathf.Max(0, money + amount);
        if (moneyTickRoutine == null)
        {
            moneyTickRoutine = StartCoroutine(TickDisplayedMoney());
        }

        RefreshUI();
    }

    private void SyncDisplayedMoney()
    {
        // While a count-up is running it converges to the new total on its own.
        if (moneyTickRoutine == null)
        {
            displayedMoney = money;
        }
    }

    private IEnumerator TickDisplayedMoney()
    {
        while (!Mathf.Approximately(displayedMoney, money))
        {
            float gap = Mathf.Abs(money - displayedMoney);
            float speed = Mathf.Max(60f, gap * 4f);
            displayedMoney = Mathf.MoveTowards(displayedMoney, money, speed * Time.deltaTime);
            RefreshUI();
            yield return null;
        }

        displayedMoney = money;
        moneyTickRoutine = null;
        RefreshUI();
    }

    /// <summary>
    /// Shows coins earned above a tower, flies the text to the money display,
    /// then adds the amount with an animated count-up.
    /// </summary>
    public void ShowCoinPayout(Vector3 worldPosition, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        if (canvasObject == null || canvasRect == null || moneyText == null)
        {
            AddMoneyAnimated(amount);
            return;
        }

        StartCoroutine(CoinPayoutRoutine(worldPosition, amount));
    }

    private IEnumerator CoinPayoutRoutine(Vector3 worldPosition, int amount)
    {
        Camera worldCamera = Camera.main;
        if (worldCamera == null)
        {
            AddMoneyAnimated(amount);
            yield break;
        }

        Text coinText = CreateText("Coin Payout", canvasObject.transform, 34, TextAnchor.MiddleCenter);
        coinText.text = "+$" + amount;
        coinText.color = coinPayoutColor;
        coinText.fontStyle = FontStyle.Bold;
        coinText.raycastTarget = false;
        coinText.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);

        RectTransform coinRect = coinText.rectTransform;
        coinRect.sizeDelta = new Vector2(240f, 64f);
        // Last sibling of the canvas root, so the payout number draws on top of the shop panel.
        coinRect.SetAsLastSibling();

        const float holdDuration = 0.6f;
        const float driftDistance = 0.4f;
        for (float elapsed = 0f; elapsed < holdDuration; elapsed += Time.deltaTime)
        {
            Vector3 driftedPosition = worldPosition
                + Vector3.up * (0.9f + driftDistance * (elapsed / holdDuration));
            coinRect.anchoredPosition = WorldToCanvasPoint(worldCamera, driftedPosition);
            yield return null;
        }

        Vector2 flightStart = coinRect.anchoredPosition;
        const float flightDuration = 0.45f;
        for (float elapsed = 0f; elapsed < flightDuration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / flightDuration);
            coinRect.anchoredPosition = Vector2.Lerp(flightStart, GetMoneyTextCanvasPoint(), t);
            yield return null;
        }

        Destroy(coinText.gameObject);
        AddMoneyAnimated(amount);
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

    private Vector2 GetMoneyTextCanvasPoint()
    {
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, moneyText.rectTransform.position);
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
        if (GetOfferSprite(offer) == null || !CanAfford(offer.price))
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

        // The indicator's sprite is generated at runtime, so it cannot be stored in the prefab.
        if (rotatable)
        {
            CreateAimIndicator(tower.transform, GetAimDirection(offer), gridCellSize);
        }

        // Which cages a tower stands on depends on where it was dropped, so it is
        // resolved per placement rather than baked into the asset.
        TowerCageStack cageStack = tower.GetComponent<TowerCageStack>();
        if (cageStack != null)
        {
            cageStack.Initialize(gridCellSize);
        }

        return tower;
    }

    /// <summary>
    /// Adds a small barrel marker showing which way a directional tower aims.
    /// Rotates with the parent, so it stays accurate after R-key rotations.
    /// </summary>
    public static GameObject CreateAimIndicator(Transform parent, Vector2 aimDirection, float cellSize)
    {
        GameObject indicator = new GameObject("Aim Indicator");
        indicator.transform.SetParent(parent, false);
        indicator.transform.localPosition = aimDirection * (cellSize * 0.55f);
        indicator.transform.localScale = new Vector3(cellSize * 0.35f, cellSize * 0.1f, 1f);

        SpriteRenderer renderer = indicator.AddComponent<SpriteRenderer>();
        renderer.sprite = GetAimIndicatorSprite();
        renderer.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

        SpriteRenderer parentRenderer = parent.GetComponent<SpriteRenderer>();
        if (parentRenderer != null)
        {
            renderer.sortingLayerID = parentRenderer.sortingLayerID;
            renderer.sortingOrder = parentRenderer.sortingOrder + 1;
        }

        return indicator;
    }

    private static Sprite GetAimIndicatorSprite()
    {
        if (aimIndicatorSprite == null)
        {
            Texture2D texture = Texture2D.whiteTexture;
            aimIndicatorSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                texture.width);
        }

        return aimIndicatorSprite;
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

        GameObject panel = CreateUIObject("Tower Shop", canvasObject.transform);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = panelColor;

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0.5f);
        panelRect.anchorMax = new Vector2(0f, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(20f, 0f);
        panelRect.sizeDelta = new Vector2(250f, Mathf.Max(150f, 306f + towers.Count * 72f));

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;

        moneyText = CreateText("Money", panel.transform, 28, TextAnchor.MiddleCenter);
        moneyText.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;

        for (int i = 0; i < towers.Count; i++)
        {
            int capturedIndex = i;
            TowerOffer offer = towers[i];
            Button button = CreateButton(panel.transform, offer, capturedIndex);
            button.onClick.AddListener(() => SelectTower(capturedIndex));
            towerButtons.Add(button);
        }

        potionButton = CreatePotionButton(panel.transform);
        repairButton = CreateRepairButton(panel.transform);
        StartRoundButton = CreateStartRoundButton(panel.transform);
    }

    private Button CreateRepairButton(Transform parent)
    {
        GameObject buttonObject = CreateUIObject("Repair Cage", parent);
        Image background = buttonObject.AddComponent<Image>();
        background.color = buttonColor;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(ToggleRepairMode);
        buttonObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        repairLabel = CreateText("Label", buttonObject.transform, 20, TextAnchor.MiddleCenter);
        RectTransform labelRect = repairLabel.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 0f);
        labelRect.offsetMax = new Vector2(-8f, 0f);
        return button;
    }

    private Button CreatePotionButton(Transform parent)
    {
        GameObject buttonObject = CreateUIObject("Health Potion", parent);
        Image background = buttonObject.AddComponent<Image>();
        background.color = buttonColor;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(BuyHealthPotion);
        buttonObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        Text label = CreateText("Label", buttonObject.transform, 20, TextAnchor.MiddleCenter);
        label.text = "Health Potion (+" + potionHealAmount + ")  $" + potionPrice;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 0f);
        labelRect.offsetMax = new Vector2(-8f, 0f);
        return button;
    }

    private Button CreateStartRoundButton(Transform parent)
    {
        GameObject buttonObject = CreateUIObject("Start Round", parent);
        Image background = buttonObject.AddComponent<Image>();
        background.color = startRoundColor;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        buttonObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        Text label = CreateText("Label", buttonObject.transform, 24, TextAnchor.MiddleCenter);
        label.text = "Start Round";
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        return button;
    }

    private Button CreateButton(Transform parent, TowerOffer offer, int index)
    {
        GameObject buttonObject = CreateUIObject("Tower " + index, parent);
        Image background = buttonObject.AddComponent<Image>();
        background.color = buttonColor;
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        buttonObject.AddComponent<LayoutElement>().preferredHeight = 62f;

        HorizontalLayoutGroup row = buttonObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(8, 8, 6, 6);
        row.spacing = 10f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlHeight = true;
        row.childControlWidth = false;

        GameObject iconObject = CreateUIObject("Icon", buttonObject.transform);
        Image icon = iconObject.AddComponent<Image>();
        icon.sprite = GetOfferSprite(offer);
        icon.preserveAspect = true;
        LayoutElement iconLayout = iconObject.AddComponent<LayoutElement>();
        iconLayout.preferredWidth = 48f;
        iconLayout.preferredHeight = 48f;

        Text label = CreateText("Label", buttonObject.transform, 20, TextAnchor.MiddleLeft);
        label.text = offer.displayName + "  $" + offer.price;
        LayoutElement labelLayout = label.gameObject.AddComponent<LayoutElement>();
        labelLayout.flexibleWidth = 1f;
        labelLayout.preferredHeight = 48f;
        return button;
    }

    private void RefreshUI()
    {
        if (moneyText != null)
        {
            moneyText.text = "Money: $" + Mathf.RoundToInt(displayedMoney);
        }

        for (int i = 0; i < towerButtons.Count; i++)
        {
            Button button = towerButtons[i];
            TowerOffer offer = towers[i];
            button.interactable = GetOfferSprite(offer) != null && CanAfford(offer.price);
            button.GetComponent<Image>().color = i == selectedIndex ? selectedColor : buttonColor;
        }

        if (potionButton != null)
        {
            potionButton.interactable = player != null && CanAfford(potionPrice);
        }

        if (repairButton != null)
        {
            repairButton.interactable = CanAfford(cageRepairPrice);
            repairButton.GetComponent<Image>().color = repairMode ? selectedColor : buttonColor;
            repairLabel.text = repairMode
                ? "Click a Cage to Repair"
                : "Repair Cage  $" + cageRepairPrice;
        }
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

    private void OnDisable()
    {
        if (canvasObject != null)
            canvasObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (canvasObject != null)
            canvasObject.SetActive(true);

        // Each build phase starts out of repair mode.
        if (repairMode)
        {
            SetRepairMode(false);
        }
    }
}
