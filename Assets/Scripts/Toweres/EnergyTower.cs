using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Turns the birds caged beneath it into the energy the shop is paid in. It banks one
/// round's worth at a time, so its output arrives at the end of a round rather than
/// trickling in while one is being fought.
/// </summary>
public class EnergyTower : MonoBehaviour
{
    [FormerlySerializedAs("coinsPerPower")]
    [SerializeField, Min(0)] private int energyPerPower = 100;
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

    [Header("First Capture")]
    [SerializeField, Tooltip("Played when the run's first captured bird's energy lands here.")]
    private AudioClip powerUpSfx;

    private TowerCageStack cageStack;
    private SpriteRenderer towerRenderer;
    private float powerHeldUntil;
    private int heldPowerLevels;

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

        int power = cageStack != null ? cageStack.PowerLevel : 0;

        // A cage counts the moment its bird lands in it, which is well before the
        // first-capture orb has carried that power up here. Showing the level the tower
        // was on until then is what makes the orb read as the thing that powers it up.
        if (Time.time < powerHeldUntil)
        {
            power = Mathf.Max(0, power - heldPowerLevels);
        }

        Sprite powerSprite = GetSpriteForPower(power);
        if (powerSprite != null && towerRenderer.sprite != powerSprite)
        {
            towerRenderer.sprite = powerSprite;
        }
    }

    /// <summary>
    /// Shows <paramref name="levels"/> power levels lower for the next
    /// <paramref name="seconds"/>, holding the tower back while the first-capture energy
    /// orb is still climbing the cages it collected them from.
    /// </summary>
    public void HoldPowerLevelBack(float seconds, int levels)
    {
        heldPowerLevels = Mathf.Max(0, levels);
        powerHeldUntil = Time.time + Mathf.Max(0f, seconds);
    }

    /// <summary>Lands that orb: the held-back levels come through and the power-up plays.</summary>
    public void ReceiveFirstCaptureEnergy()
    {
        powerHeldUntil = 0f;
        heldPowerLevels = 0;
        RefreshSprite();

        if (powerUpSfx != null)
        {
            AudioController.Play(powerUpSfx);
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
    /// Pays out once at the end of a round. The energy is shown above the tower,
    /// flies to the shop's energy display, and is then added to the balance.
    /// </summary>
    public void PayOutRound()
    {
        // Caging the last bird of a round both ends the round and launches the
        // first-capture orb, which would otherwise put the payout on screen while that
        // energy is still visibly on its way here. It waits its turn.
        if (FirstCaptureCinematic.IsPlaying)
        {
            StartCoroutine(PayOutWhenOrbLands());
            return;
        }

        PayOut();
    }

    private IEnumerator PayOutWhenOrbLands()
    {
        while (FirstCaptureCinematic.IsPlaying)
        {
            yield return null;
        }

        PayOut();
    }

    private void PayOut()
    {
        cageStack.FindContinuousCagesBelow();
        cageStack.RefreshTowerValue();
        int power = cageStack != null ? cageStack.PowerLevel : 0;
        int amount = power * energyPerPower;
        if (amount <= 0 || towerShop == null)
        {
            return;
        }

        if (paymentSfx != null)
        {
            AudioController.Play(paymentSfx);
        }

        towerShop.ShowEnergyPayout(transform.position, amount);
    }
}
