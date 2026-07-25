using UnityEngine;

public class MoneyTower : MonoBehaviour
{
    [SerializeField, Min(0)] private int coinsPerPower = 100;
    [SerializeField] private TowerShopUI towerShop;
    [SerializeField] private AudioClip paymentSfx;

    [Header("Power Level Sprites")]
    [SerializeField, Tooltip("Shown with no full cages below.")]
    private Sprite unpoweredSprite;
    [SerializeField] private Sprite powerLevel1Sprite;
    [SerializeField] private Sprite powerLevel2Sprite;
    [SerializeField] private Sprite powerLevel3Sprite;
    [SerializeField] private Sprite powerLevel4Sprite;
    [SerializeField, Tooltip("Also used for any power level above 5.")]
    private Sprite powerLevel5Sprite;

    private TowerCageStack cageStack;
    private SpriteRenderer towerRenderer;

    private void Start()
    {
        cageStack = GetComponent<TowerCageStack>();
        towerRenderer = GetComponent<SpriteRenderer>();
        if (unpoweredSprite == null && towerRenderer != null)
        {
            unpoweredSprite = towerRenderer.sprite;
        }

        if (towerShop == null)
        {
            towerShop = FindFirstObjectByType<TowerShopUI>();
        }

        RefreshSprite();
    }

    private void Update()
    {
        RefreshSprite();
    }

    private void RefreshSprite()
    {
        if (towerRenderer == null)
        {
            return;
        }

        Sprite powerSprite = GetSpriteForPower(cageStack != null ? cageStack.PowerLevel : 0);
        if (powerSprite != null && towerRenderer.sprite != powerSprite)
        {
            towerRenderer.sprite = powerSprite;
        }
    }

    private Sprite GetSpriteForPower(int power)
    {
        if (power <= 0)
        {
            return unpoweredSprite;
        }

        switch (power)
        {
            case 1: return powerLevel1Sprite;
            case 2: return powerLevel2Sprite;
            case 3: return powerLevel3Sprite;
            case 4: return powerLevel4Sprite;
            default: return powerLevel5Sprite;
        }
    }

    /// <summary>
    /// Pays out once at the end of a round. The coins are shown above the tower,
    /// fly to the shop's money display, and are then added to the balance.
    /// </summary>
    public void PayOutRound()
    {
        cageStack.FindContinuousCagesBelow();
        cageStack.RefreshTowerValue();
        int power = cageStack != null ? cageStack.PowerLevel : 0;
        int amount = power * coinsPerPower;
        if (amount <= 0 || towerShop == null)
        {
            return;
        }

        if (paymentSfx != null)
        {
            AudioController.Play(paymentSfx);
        }

        towerShop.ShowCoinPayout(transform.position, amount);
    }
}
