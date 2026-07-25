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

        if (other.TryGetComponent<BasicEnemy>(out BasicEnemy enemy))
        {
            playerController.DamagePlayer(
                enemy.damage,
                CalculateKnockbackDirection(transform.position, other.transform.position));
        }
        else if (other.TryGetComponent<EnemyBullet>(out EnemyBullet bullet))
        {
            playerController.DamagePlayer(
                bullet.damage,
                CalculateKnockbackDirection(transform.position, other.transform.position));
        }
    }

    private Vector2 CalculateKnockbackDirection(Vector3 playerPosition, Vector3 enemyPosition)
    {
        Vector2 knockbackDirection = (playerPosition - enemyPosition).normalized;
        return knockbackDirection;
    }
}
