using UnityEngine;

public class MoneyTower : MonoBehaviour
{
    [SerializeField, Min(0)] private int coinsPerPower = 100;
    [SerializeField] private TowerShopUI towerShop;
    [SerializeField] private AudioClip paymentSfx;

    private TowerCageStack cageStack;

    public void Configure(AudioClip newPaymentSfx)
    {
        paymentSfx = newPaymentSfx;
    }

    private void Start()
    {
        cageStack = GetComponent<TowerCageStack>();
        if (towerShop == null)
        {
            towerShop = FindFirstObjectByType<TowerShopUI>();
        }
    }

    /// <summary>
    /// Pays out once at the end of a round. The coins are shown above the tower,
    /// fly to the shop's money display, and are then added to the balance.
    /// </summary>
    public void PayOutRound()
    {
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
