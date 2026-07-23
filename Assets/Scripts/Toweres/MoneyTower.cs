using UnityEngine;

public class MoneyTower : MonoBehaviour
{
    [SerializeField, Min(0)] private int coinsPerSecond = 1;
    [SerializeField] private TowerShopUI towerShop;

    private float nextPaymentTime;

    private void Start()
    {
        if (towerShop == null)
        {
            towerShop = FindFirstObjectByType<TowerShopUI>();
        }

        nextPaymentTime = Time.time + 1f;
    }

    private void Update()
    {
        if (towerShop == null || Time.time < nextPaymentTime)
        {
            return;
        }

        towerShop.AddMoney(coinsPerSecond);
        nextPaymentTime = Time.time + 1f;
    }
}
