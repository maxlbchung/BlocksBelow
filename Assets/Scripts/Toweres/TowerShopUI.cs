using System;
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
        public GameObject prefab;
        public Sprite shopIcon;
        [Min(0)] public int price = 10;
    }

    [Header("Shop")]
    [SerializeField, Min(0)] private int startingMoney = 100;
    [SerializeField] private List<TowerOffer> towers = new List<TowerOffer>();
    [SerializeField] private SquarePlacement placement;

    [Header("Appearance")]
    [SerializeField] private Color panelColor = new Color(0.08f, 0.1f, 0.14f, 0.92f);
    [SerializeField] private Color buttonColor = new Color(0.2f, 0.24f, 0.3f, 1f);
    [SerializeField] private Color selectedColor = new Color(0.25f, 0.55f, 0.3f, 1f);

    private readonly List<Button> towerButtons = new List<Button>();
    private Text moneyText;
    private int money;
    private int selectedIndex = -1;

    public int Money => money;

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
        if (offer.prefab == null || !CanAfford(offer.price))
        {
            return;
        }

        selectedIndex = index;
        Sprite preview = offer.shopIcon;
        if (preview == null)
        {
            SpriteRenderer renderer = offer.prefab.GetComponentInChildren<SpriteRenderer>();
            preview = renderer != null ? renderer.sprite : null;
        }

        placement.SetSelectedTower(offer.prefab, preview, offer.price);
        RefreshUI();
    }

    private void BuildShopUI()
    {
        EnsureEventSystem();

        GameObject canvasObject = new GameObject("Tower Shop Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
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

        Sprite iconSprite = offer.shopIcon;
        if (iconSprite == null && offer.prefab != null)
        {
            SpriteRenderer renderer = offer.prefab.GetComponentInChildren<SpriteRenderer>();
            iconSprite = renderer != null ? renderer.sprite : null;
        }

        GameObject iconObject = CreateUIObject("Icon", buttonObject.transform);
        Image icon = iconObject.AddComponent<Image>();
        icon.sprite = iconSprite;
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
            button.interactable = offer.prefab != null && CanAfford(offer.price);
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
}
