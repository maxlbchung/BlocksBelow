using UnityEngine;

public class BasicEnemy : Enemy
{
    [SerializeField] private float moveSpeed = 5f;

    private GameObject player;

    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player");

        // Disable gravity and set body type to dynamic
        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
    }

    protected override void FixedUpdate()
    {
        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player");
        }
        if (player != null && rb != null)
        {
            Vector2 direction = (player.transform.position - transform.position).normalized;
            Vector2 targetVelocity = direction * moveSpeed;
            
            // Apply force towards player instead of setting velocity directly
            Vector2 velocityDifference = targetVelocity - rb.linearVelocity;
            rb.AddForce(velocityDifference * rb.mass, ForceMode2D.Force);
        }

        // Apply repulsion from other enemies   
        base.FixedUpdate();
    }
}

