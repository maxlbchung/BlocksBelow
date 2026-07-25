using UnityEngine;
using System.Collections.Generic;

public class SawBlade : MonoBehaviour
{
    [SerializeField, Min(0f)] private float pushForce = 12f;
    [SerializeField] private float damage = 1f;
    // Degrees per second the blade spins on its own axis, on top of the tower's orbit. Purely
    // visual — the trigger is a centered circle, so spinning can't change what it hits.
    [SerializeField] private float spinSpeed = -1080f;
    [SerializeField, Min(0f), Tooltip("Seconds before this blade can damage the same enemy again. Must outlast one sweep across an enemy, or the blade's own knockback bounces the enemy back into the trigger for extra hits.")]
    private float hitCooldown = 0.5f;
    [SerializeField] private AudioClip hitSfx;

    // Time each enemy becomes hittable by THIS blade again. Entity's own 0.1s invincibility is
    // shorter than one sweep, so it cannot stop a bounced enemy from being hit twice per pass.
    // Enemies are pooled, so the key set stays bounded by the pool size.
    private readonly Dictionary<Enemy, float> nextHitTimes = new Dictionary<Enemy, float>();

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
        // Keyed by the enemy, not the collider, so an enemy with several colliders still takes one
        // hit. Checked before TryTakeDamage so a blocked hit doesn't burn the cooldown.
        if (enemy == null || !CanHit(enemy) || !enemy.TryTakeDamage(damage))
        {
            return;
        }

        nextHitTimes[enemy] = Time.time + hitCooldown;

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

    private bool CanHit(Enemy enemy)
    {
        return !nextHitTimes.TryGetValue(enemy, out float nextTime) || Time.time >= nextTime;
    }

    private void OnDisable()
    {
        nextHitTimes.Clear();
    }

    public void SawBladeHit()
    {
        if (hitSfx != null)
        {
            AudioController.Play(hitSfx);
        }
    }
}
