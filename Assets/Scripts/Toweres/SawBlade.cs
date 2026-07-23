using UnityEngine;

public class SawBlade : MonoBehaviour
{
    [SerializeField, Min(0f)] private float pushForce = 12f;
    [SerializeField] private float damage = 1f;

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
        if (!other.CompareTag("Enemy") && !other.CompareTag("Player"))
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

        if (other.CompareTag("Enemy"))
        {
            Enemy enemy = other.GetComponentInParent<Enemy>();
            if (enemy != null)
            {
                enemy.health -= damage;
            }
        }
    }

    public void SawBladeHit()
    {
        Debug.Log("saw blade hit", this);
    }
}
