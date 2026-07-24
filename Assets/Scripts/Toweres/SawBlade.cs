using UnityEngine;

public class SawBlade : MonoBehaviour
{
    [SerializeField, Min(0f)] private float pushForce = 12f;
    [SerializeField] private float damage = 1f;
    [SerializeField] private AudioClip hitSfx;

    public void ConfigureSfx(AudioClip newHitSfx)
    {
        hitSfx = newHitSfx;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHitEnemy(other);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryHitEnemy(collision.collider);
    }

    private void TryHitEnemy(Collider2D other)
    {
        bool isPlayer = other.CompareTag("Player");
        bool isEnemy = other.CompareTag("Enemy");
        if (!isPlayer && !isEnemy)
        {
            return;
        }

        SawBladeHit();

        Rigidbody2D enemyBody = other.attachedRigidbody;
        if (enemyBody == null)
        {
            return;
        }

        Vector2 pushDirection = (enemyBody.worldCenterOfMass - (Vector2)transform.position).normalized;
        if (pushDirection.sqrMagnitude == 0f)
        {
            pushDirection = -transform.right;
        }

        if (isPlayer)
        {
            // Only the player routes knockback through its control-lock; look it up only here.
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                player.ApplyKnockback(pushDirection * pushForce);
            }
            else
            {
                enemyBody.AddForce(pushDirection * pushForce, ForceMode2D.Impulse);
            }

            return;
        }

        // Enemy hit: apply the impulse and damage directly, no component lookup.
        enemyBody.AddForce(pushDirection * pushForce, ForceMode2D.Impulse);

        Enemy enemy = EnemySimulationManager.InstanceOrNull != null
            ? EnemySimulationManager.InstanceOrNull.FindEnemy(other)
            : null;
        if (enemy != null)
        {
            enemy.health -= damage;
        }
    }

    public void SawBladeHit()
    {
        if (hitSfx != null)
        {
            AudioController.Play(hitSfx);
        }
    }
}
