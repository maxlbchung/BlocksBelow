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

    // The blade is a trigger, so it never physically pushes or blocks anything;
    // enemies are the only thing it reacts to.
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Enemy"))
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
