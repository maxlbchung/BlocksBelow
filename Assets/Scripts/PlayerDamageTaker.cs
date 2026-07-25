using UnityEngine;

public class PlayerDamageTaker : MonoBehaviour
{
    [SerializeField] private PlayerController playerController;

    // Every contact is forwarded as-is; the player's own invincibility frames
    // (PlayerController.DamagePlayer) decide whether the hit actually lands.
    private void OnTriggerStay2D(Collider2D other)
    {
        if (playerController == null || !other.CompareTag("Enemy"))
        {
            return;
        }

        // Read off the base type rather than one subclass, so every enemy that declares
        // contact damage lands it. Enemies that hurt the player another way, or not at
        // all, report zero and are skipped.
        if (other.TryGetComponent<Enemy>(out Enemy enemy))
        {
            if (enemy.ContactDamage > 0)
            {
                playerController.DamagePlayer(
                    enemy.ContactDamage,
                    CalculateKnockbackDirection(transform.position, other.transform.position));
            }
        }
        else if (other.TryGetComponent<EnemyBullet>(out EnemyBullet bullet))
        {
            playerController.DamagePlayer(
                bullet.damage,
                CalculateKnockbackDirection(transform.position, other.transform.position));
            Destroy(bullet.gameObject);
        }
    }

    private Vector2 CalculateKnockbackDirection(Vector3 playerPosition, Vector3 enemyPosition)
    {
        Vector2 knockbackDirection = (playerPosition - enemyPosition).normalized;
        return knockbackDirection;
    }
}
