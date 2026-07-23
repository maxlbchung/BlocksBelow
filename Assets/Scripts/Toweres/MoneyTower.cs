using UnityEngine;

public class MoneyTower : MonoBehaviour
{
    [SerializeField, Min(0)] private int coinsPerSecond;
    [SerializeField] private TowerShopUI towerShop;

    private float nextPaymentTime;
    private TowerCageStack cageStack;

    private void Start()
    {
        cageStack = GetComponent<TowerCageStack>();
        if (towerShop == null)
        {
            towerShop = FindFirstObjectByType<TowerShopUI>();
        }

        nextPaymentTime = Time.time + 1f;
    }

    private void Update()
    {
        coinsPerSecond = cageStack != null ? cageStack.PowerLevel : 0;
        if (towerShop == null || Time.time < nextPaymentTime)
        {
            return;
        }

        towerShop.AddMoney(coinsPerSecond);
        nextPaymentTime = Time.time + 1f;
    }
}
