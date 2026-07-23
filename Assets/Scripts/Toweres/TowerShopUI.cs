using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class TowerShopUI : MonoBehaviour
{
    public enum TowerScript
    {
        BasicTower,
        ShotgunTower,
        SawBladeTower,
        FanTower,
        MoneyTower,
        CageTower,
        Scaffolding,
        TeslaTower
    }

    [Serializable]
    public class TowerOffer
    {
        public string displayName = "Tower";
        public TowerScript script;
        public Sprite sprite;
        [Min(0)] public int price = 10;
    }

    [Header("Shop")]
    [SerializeField, Min(0)] private int startingMoney = 100;
    [SerializeField] private List<TowerOffer> towers = new List<TowerOffer>();
    [SerializeField] private SquarePlacement placement;

    [Header("Runtime Prefabs")]
    [SerializeField] private Projectile basicProjectilePrefab;
    [SerializeField] private Projectile shotgunProjectilePrefab;
    [SerializeField] private GameObject sawBladePrefab;
    [SerializeField] private Sprite brokenCageSprite;
    [SerializeField, Min(0.1f)] private float cageCaptureRadius = 1.25f;

    [Header("Appearance")]
    [SerializeField] private Color panelColor = new Color(0.08f, 0.1f, 0.14f, 0.92f);
    [SerializeField] private Color buttonColor = new Color(0.2f, 0.24f, 0.3f, 1f);
    [SerializeField] private Color selectedColor = new Color(0.25f, 0.55f, 0.3f, 1f);

    [Header("Tower SFX")]
    [SerializeField, AudioClipDropdown] private AudioClip placementSfx;
    [SerializeField, AudioClipDropdown] private AudioClip basicShootSfx;
    [SerializeField, AudioClipDropdown] private AudioClip shotgunShootSfx;
    [SerializeField, AudioClipDropdown] private AudioClip sawHitSfx;
    [SerializeField, AudioClipDropdown] private AudioClip moneySfx;
    [SerializeField, AudioClipDropdown] private AudioClip cageCaptureSfx;
    [SerializeField, AudioClipDropdown] private AudioClip cageBreakSfx;
    [SerializeField, AudioClipDropdown] private AudioClip teslaSfx;


    private readonly List<Button> towerButtons = new List<Button>();
    private Text moneyText;
    private int money;
    private int selectedIndex = -1;

    public int Money => money;

    GameObject canvasObject;

    private void Awake()
    {
        money = startingMoney;

        if (placement == null)
        {
            placement = FindFirstObjectByType<SquarePlacement>();
        }

        if (placement != null)
        {
            placement.SetTowerShop(this);
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
        RefreshUI();
        return true;
    }

    public void AddMoney(int amount)
    {
        money = Mathf.Max(0, money + amount);
        RefreshUI();
    }

    public void SelectTower(int index)
    {
        if (index < 0 || index >= towers.Count || placement == null)
        {
            return;
        }

        TowerOffer offer = towers[index];
        if (offer.sprite == null || !CanAfford(offer.price))
        {
            return;
        }

        selectedIndex = index;
        placement.SetSelectedTower(offer);
        RefreshUI();
    }

    public GameObject CreateTower(TowerOffer offer, Vector2 position, float gridCellSize)
    {
        if (offer == null || offer.sprite == null)
        {
            return null;
        }

        GameObject tower = new GameObject(offer.displayName);
        tower.transform.position = position;
        tower.tag = offer.script == TowerScript.CageTower ? "cage" : "tower";
        int wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer >= 0)
        {
            tower.layer = wallLayer;
        }

        SpriteRenderer renderer = tower.AddComponent<SpriteRenderer>();
        renderer.sprite = offer.sprite;
        renderer.sortingLayerName = "Towers";
        renderer.sortingOrder = 1;

        BoxCollider2D collider = tower.AddComponent<BoxCollider2D>();
        collider.size = Vector2.one;
        collider.isTrigger = offer.script == TowerScript.Scaffolding;

        switch (offer.script)
        {
            case TowerScript.BasicTower:
                tower.AddComponent<BasicTower>().Configure(basicProjectilePrefab, basicShootSfx);
                break;
            case TowerScript.ShotgunTower:
                tower.AddComponent<ShotgunTower>().Configure(shotgunProjectilePrefab, shotgunShootSfx);
                break;
            case TowerScript.SawBladeTower:
                tower.AddComponent<SawBladeTower>().Configure(sawBladePrefab, sawHitSfx);
                break;
            case TowerScript.FanTower:
                tower.AddComponent<FanTower>();
                break;
            case TowerScript.MoneyTower:
                tower.AddComponent<MoneyTower>().Configure(moneySfx);
                break;
            case TowerScript.CageTower:
                tower.AddComponent<CageTower>().Configure(
                    brokenCageSprite,
                    cageCaptureRadius,
                    cageCaptureSfx,
                    cageBreakSfx);
                break;
            case TowerScript.Scaffolding:
                // Scaffolding intentionally has no behavior component.
                SpriteRenderer sr = tower.GetComponent<SpriteRenderer>();
                Color color = sr.color;
                color.a = 0.5f;

                sr.color = color;

                CreateScaffoldingPlatforms(tower.transform, gridCellSize, wallLayer);
                break;
            case TowerScript.TeslaTower:
                tower.AddComponent<TeslaTower>().Configure(teslaSfx);
                break;
        }

        if (offer.script != TowerScript.CageTower)
        {
            tower.AddComponent<TowerCageStack>().Initialize(gridCellSize);
        }

        SetLayerRecursively(tower, wallLayer);
        PlaySfx(placementSfx);
        return tower;
    }

    private static void PlaySfx(AudioClip clip)
    {
        if (clip != null)
        {
            AudioController.Play(clip);
        }
    }

    private static void CreateScaffoldingPlatforms(
        Transform parent,
        float size,
        int layer)
    {
        float halfSize = size * 0.5f;
        CreateOneWayEdge(parent, "Top Platform", halfSize, halfSize, layer);
        CreateOneWayEdge(parent, "Bottom Platform", -halfSize, halfSize, layer);
    }

    private static void CreateOneWayEdge(
        Transform parent,
        string objectName,
        float localY,
        float halfWidth,
        int layer)
    {
        GameObject platform = new GameObject(objectName);
        platform.transform.SetParent(parent, false);
        platform.transform.localPosition = new Vector3(0f, localY, 0f);

        if (layer >= 0)
        {
            platform.layer = layer;
        }

        EdgeCollider2D edge = platform.AddComponent<EdgeCollider2D>();
        edge.points = new[]
        {
            new Vector2(-halfWidth, 0f),
            new Vector2(halfWidth, 0f)
        };
        edge.usedByEffector = true;

        PlatformEffector2D effector = platform.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.useSideFriction = false;
        effector.surfaceArc = 180f;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        if (target == null || layer < 0)
        {
            return;
        }

        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
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

        GameObject panel = CreateUIObject("Tower Shop", canvasObject.transform);
        Image panelImage = panel.AddComponent<Image>();
        panelImage.color = panelColor;

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0.5f);
        panelRect.anchorMax = new Vector2(0f, 0.5f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.anchoredPosition = new Vector2(20f, 0f);
        panelRect.sizeDelta = new Vector2(250f, Mathf.Max(150f, 90f + towers.Count * 72f));

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
        icon.sprite = offer.sprite;
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
            moneyText.text = "Money: $" + money;
        }

        for (int i = 0; i < towerButtons.Count; i++)
        {
            Button button = towerButtons[i];
            TowerOffer offer = towers[i];
            button.interactable = offer.sprite != null && CanAfford(offer.price);
            button.GetComponent<Image>().color = i == selectedIndex ? selectedColor : buttonColor;
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
    }
}
