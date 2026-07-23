using UnityEngine;

public class ShotgunTower : MonoBehaviour
{
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField, Min(0.01f)] private float fireRate = 1f;
    [SerializeField, Min(1)] private int bulletsPerShot = 3;
    [SerializeField, Range(0f, 180f)] private float spread = 30f;

    private float nextShotTime;

    public void Configure(Projectile newProjectilePrefab)
    {
        projectilePrefab = newProjectilePrefab;
    }

    private void Update()
    {
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

        int shotCount = Mathf.Max(1, bulletsPerShot);

        for (int i = 0; i < shotCount; i++)
        {
            float angle = shotCount == 1
                ? 0f
                : Mathf.Lerp(-spread * 0.5f, spread * 0.5f, i / (shotCount - 1f));

            Vector2 direction = Quaternion.Euler(0f, 0f, angle) * Vector2.left;
            Projectile projectile = Instantiate(projectilePrefab, transform.position, Quaternion.identity);
            projectile.SetDirection(direction);
        }
    }
}
