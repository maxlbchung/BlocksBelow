using UnityEngine;

public class ShotgunTower : MonoBehaviour
{
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField, Min(0.01f)] private float fireRate = 1f;
    [SerializeField, Range(0f, 180f)] private float spread = 30f;
    [SerializeField] private float damage = 1f;
    [SerializeField] private AudioClip shootSfx;
    [SerializeField, Min(0), Tooltip("Projectiles prepared before this tower starts firing.")]
    private int projectilePrewarmCount = 40;
    [SerializeField, Min(1)] private int projectilePoolMaxSize = 1024;

    [Header("Muzzle Flash")]
    [SerializeField, Min(0), Tooltip("Sparks per pellet in the blast, so a taller stack flashes bigger. 0 turns the flash off.")]
    private int muzzleSparkCountPerPellet = 4;
    [SerializeField, Min(0f), Tooltip("How far along the shot direction the flash sits, in world units.")]
    private float muzzleOffset = 0.5f;
    [SerializeField, Tooltip("Sparks and smoke take this tint. Defaults to the tower's pale purple.")]
    private Color muzzleColor = new Color(0.76f, 0.62f, 1f, 1f);

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
        // One bullet per full cage below — the count is locked to cage power.
        int bulletsPerShot = cageStack != null ? cageStack.PowerLevel : 0;
        if (bulletsPerShot <= 0)
        {
            return;
        }

        if (Time.time < nextShotTime)
        {
            return;
        }

        Shoot(bulletsPerShot);
        nextShotTime = Time.time + 1f / Mathf.Max(0.01f, fireRate);
    }

    private void Shoot(int shotCount)
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"{name} needs a projectile prefab assigned.", this);
            return;
        }

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

        // The flash covers the same fan the pellets leave in, widened a little so
        // the sparks frame the outermost pellets instead of stopping at them.
        Vector2 blastDirection = transform.rotation * Vector2.left;
        MuzzleParticles.EmitShot(
            (Vector2)transform.position + blastDirection * muzzleOffset,
            blastDirection,
            muzzleSparkCountPerPellet * shotCount,
            muzzleColor,
            spread + 10f);

        if (shootAnimation != null)
        {
            shootAnimation.Play();
        }
    }
}
