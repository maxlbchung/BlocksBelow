using UnityEngine;
using UnityEngine.Serialization;

public class BasicTower : MonoBehaviour
{
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField, Min(0f), FormerlySerializedAs("fireRatePerPower"),
     Tooltip("Shots per second. Constant: power changes the shot, not the cadence.")]
    private float fireRate = 1f;
    [SerializeField, FormerlySerializedAs("damage"),
     Tooltip("Damage granted by each full cage below. Power 2 hits twice as hard as power 1.")]
    private float damagePerPower = 1f;
    [SerializeField, Min(0f), Tooltip("Extra bullet size per power above the first. 0.35 makes a power 3 shot 1.7x as wide.")]
    private float bulletSizePerPower = 0.35f;
    [SerializeField] private AudioClip shootSfx;
    [SerializeField, Min(0), Tooltip("Projectiles prepared before this tower starts firing.")]
    private int projectilePrewarmCount = 30;
    [SerializeField, Min(1)] private int projectilePoolMaxSize = 512;

    [Header("Muzzle Flash")]
    [SerializeField, Min(0), Tooltip("Sparks thrown out of the barrel per shot. 0 turns the flash off.")]
    private int muzzleSparkCount = 8;
    [SerializeField, Min(0f), Tooltip("How far along the shot direction the flash sits, in world units.")]
    private float muzzleOffset = 0.5f;
    [SerializeField, Range(0f, 180f), Tooltip("Width of the spark cone.")]
    private float muzzleSparkSpread = 26f;

    private float nextShotTime;
    private TowerCageStack cageStack;
    private TowerShootAnimation shootAnimation;

    private void Start()
    {
        cageStack = GetComponent<TowerCageStack>();
        shootAnimation = GetComponent<TowerShootAnimation>();
        if (projectilePrefab != null)
        {
            CombatObjectPool.Configure(
                projectilePrefab.gameObject,
                projectilePrewarmCount,
                projectilePoolMaxSize,
                false);
        }
    }

    private void Update()
    {
        int power = cageStack != null ? cageStack.PowerLevel : 0;
        if (power <= 0 || fireRate <= 0f || !WaveSpawner.IsWaveActive)
        {
            return;
        }

        if (Time.time < nextShotTime)
        {
            return;
        }

        Shoot(power);
        nextShotTime = Time.time + 1f / fireRate;
    }

    private void Shoot(int power)
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"{name} needs a projectile prefab assigned.", this);
            return;
        }

        Vector2 direction = transform.rotation * Vector2.left;
        Projectile projectile = Projectile.Spawn(
            projectilePrefab,
            transform.position,
            Quaternion.identity,
            direction,
            power * damagePerPower,
            1f + (power - 1) * bulletSizePerPower);
        if (projectile != null)
        {
            PlaySfx();
            // Nothing fired when the pool is empty, so the flash stays with the shot.
            MuzzleParticles.EmitShot(
                (Vector2)transform.position + direction * muzzleOffset,
                direction,
                muzzleSparkCount,
                muzzleSparkSpread);
            if (shootAnimation != null)
            {
                shootAnimation.Play();
            }
        }
    }

    private void PlaySfx()
    {
        if (shootSfx != null)
            AudioController.Play(shootSfx);
    }
}
