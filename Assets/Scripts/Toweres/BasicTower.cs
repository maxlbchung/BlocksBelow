using UnityEngine;

public class BasicTower : MonoBehaviour
{
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField, Min(0f), Tooltip("Shots per second granted by each full cage below.")]
    private float fireRatePerPower = 1f;
    [SerializeField] private float damage = 1f;
    [SerializeField] private AudioClip shootSfx;
    [SerializeField, Min(0), Tooltip("Projectiles prepared before this tower starts firing.")]
    private int projectilePrewarmCount = 30;
    [SerializeField, Min(1)] private int projectilePoolMaxSize = 512;

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
        float fireRate = (cageStack != null ? cageStack.PowerLevel : 0) * fireRatePerPower;
        if (fireRate <= 0f || !WaveSpawner.IsWaveActive)
        {
            return;
        }

        if (Time.time < nextShotTime)
        {
            return;
        }

        Shoot();
        nextShotTime = Time.time + 1f / fireRate;
    }

    private void Shoot()
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"{name} needs a projectile prefab assigned.", this);
            return;
        }

        Projectile projectile = Projectile.Spawn(
            projectilePrefab,
            transform.position,
            Quaternion.identity,
            transform.rotation * Vector2.left,
            damage);
        if (projectile != null)
        {
            PlaySfx();
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
