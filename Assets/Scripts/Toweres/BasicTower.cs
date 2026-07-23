using UnityEngine;

public class BasicTower : MonoBehaviour
{
    [SerializeField] private Projectile projectilePrefab;
    [SerializeField, Min(0.01f)] private float fireRate = 1f;

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

        Projectile projectile = Instantiate(projectilePrefab, transform.position, Quaternion.identity);
        projectile.SetDirection(Vector2.left);
    }
}
