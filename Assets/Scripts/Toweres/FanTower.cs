using UnityEngine;

public class FanTower : MonoBehaviour
{
    [SerializeField, Min(0f)] private float pushForce = 10f;

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!other.CompareTag("Enemy"))
        {
            return;
        }

        Rigidbody2D enemyBody = other.attachedRigidbody;
        if (enemyBody != null)
        {
            enemyBody.AddForce((Vector2)transform.right * pushForce, ForceMode2D.Force);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.right);
    }
}
