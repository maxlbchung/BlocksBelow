using UnityEngine;

public class ShotgunTower : MonoBehaviour
{
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField, Min(0.01f)] private float fireRate = 1f;
    [SerializeField, Min(0)] private int bulletsPerShot;
    [SerializeField, Range(0f, 180f)] private float spread = 30f;
    [SerializeField] private float damage = 1f;
    [SerializeField] private AudioClip shootSfx;
    [SerializeField, Min(0), Tooltip("Projectiles prepared before this tower starts firing.")]
    private int projectilePrewarmCount = 256;
    [SerializeField, Min(1)] private int projectilePoolMaxSize = 1024;

    private float nextShotTime;
    private TowerCageStack cageStack;

    public void Configure(Projectile newProjectilePrefab, AudioClip newShootSfx = null)
    {
        projectilePrefab = newProjectilePrefab;
        shootSfx = newShootSfx;
    }

    private void Start()
    {
        cageStack = GetComponent<TowerCageStack>();
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
        bulletsPerShot = cageStack != null ? cageStack.PowerLevel : 0;
        if (bulletsPerShot <= 0 || !WaveSpawner.IsWaveActive)
        {
            return;
        }

        if (Time.time < nextShotTime)
        {
            return;
        }

        Shoot();
        nextShotTime = Time.time + 1f / Mathf.Max(0.01f, fireRate);
    }

    private void Shoot()
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"{name} needs a projectile prefab assigned.", this);
            return;
        }

        int shotCount = bulletsPerShot;

        for (int i = 0; i < shotCount; i++)
        {
            float angle = shotCount == 1
                ? 0f
                : Mathf.Lerp(-spread * 0.5f, spread * 0.5f, i / (shotCount - 1f));

            Vector2 direction = transform.rotation * Quaternion.Euler(0f, 0f, angle) * Vector2.left;
            Projectile.Spawn(
                projectilePrefab,
                transform.position,
                Quaternion.identity,
                direction,
                damage);
        }

        if (shootSfx != null)
            AudioController.Play(shootSfx);
    }
}
