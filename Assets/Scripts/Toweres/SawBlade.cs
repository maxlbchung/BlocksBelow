using UnityEngine;

public class SawBlade : MonoBehaviour
{
    [SerializeField, Min(0f)] private float pushForce = 12f;
    [SerializeField] private float damage = 1f;
    // Degrees per second the blade spins on its own axis, on top of the tower's orbit. Purely
    // visual — the trigger is a centered circle, so spinning can't change what it hits.
    [SerializeField] private float spinSpeed = -1080f;
    [SerializeField] private AudioClip hitSfx;

    public void Configure(AudioClip newHitSfx, float newDamage)
    {
        hitSfx = newHitSfx;
        damage = newDamage;
    }

    private void Update()
    {
        transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime);
    }

    // The blade is a trigger, so it never physically pushes or blocks anything;
    // enemies are the only thing it reacts to.
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Enemy"))
        {
            return;
        }

        Rigidbody2D enemyBody = other.attachedRigidbody;
        Enemy enemy = EnemySimulationManager.InstanceOrNull != null
            ? EnemySimulationManager.InstanceOrNull.FindEnemy(other)
            : null;
        if (enemy == null || !enemy.TryTakeDamage(damage))
        {
            return;
        }

        SawBladeHit();
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
    }

    public void SawBladeHit()
    {
        if (hitSfx != null)
        {
            AudioController.Play(hitSfx);
        }
    }
}
