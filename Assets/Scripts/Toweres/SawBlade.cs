using UnityEngine;

public class SawBlade : MonoBehaviour
{
    [SerializeField, Min(0f)] private float pushForce = 5f;

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
    }

    public void SawBladeHit()
    {
        Debug.Log("saw blade hit", this);
    }
}
