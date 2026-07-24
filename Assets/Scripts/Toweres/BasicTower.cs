using UnityEngine;

public class BasicTower : MonoBehaviour
{
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField, Min(0f)] private float fireRate;
    [SerializeField] private float damage = 1f;
    [SerializeField] private AudioClip shootSfx;
    [SerializeField, Min(0), Tooltip("Projectiles prepared before this tower starts firing.")]
    private int projectilePrewarmCount = 128;
    [SerializeField, Min(1)] private int projectilePoolMaxSize = 512;

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
        fireRate = cageStack != null ? cageStack.PowerLevel : 0f;
        if (fireRate <= 0f)
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
        }
    }

    private void PlaySfx()
    {
        if (shootSfx != null)
            AudioController.Play(shootSfx);
    }
}
