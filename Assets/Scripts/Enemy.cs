using UnityEngine;

public class Enemy : Entity
{
    [SerializeField, Min(0.1f)] protected float repulsionForce = 1.5f;
    [SerializeField, Min(0.1f)] protected float repulsionRadius = 3f;
    [SerializeField, Min(1f)] protected float exponentialFalloff = 2f;

    protected Rigidbody2D rb;
    protected Collider2D enemyCollider;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        enemyCollider = GetComponent<Collider2D>();
    }

    protected virtual void FixedUpdate()
    {
        ApplyEnemyRepulsion();

        if (health < 0)
        {
            Destroy(gameObject);
        }
    }

    private void ApplyEnemyRepulsion()
    {
        if (rb == null)
            return;

        // Cache the position to avoid multiple property accesses
        Vector3 myPosition = transform.position;
        Vector2 repulsionVector = Vector2.zero;

        // Use a cheap non-allocating physics check
        Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(myPosition, repulsionRadius);

        for (int i = 0; i < nearbyColliders.Length; i++)
        {
            Debug.Log($"Checking nearby collider: {nearbyColliders[i].name}");
            Collider2D collider = nearbyColliders[i];
            
            // Skip self and non-enemies
            if (collider == enemyCollider || !collider.CompareTag("Enemy"))
                continue;

            Vector2 direction = (Vector2)myPosition - (Vector2)collider.transform.position;
            float distanceSqr = direction.sqrMagnitude;

            // Skip if too close to zero to avoid division issues
            if (distanceSqr < 0.01f)
                continue;

            float distance = Mathf.Sqrt(distanceSqr);
            
            // Exponential falloff: stronger the closer enemies are
            float normalizedDistance = Mathf.Clamp01(distance / repulsionRadius);
            float force = repulsionForce * Mathf.Pow(1f - normalizedDistance, exponentialFalloff);

            repulsionVector += (direction / distance) * force;

            Debug.Log($"Repulsion from {collider.name}: direction={direction.normalized}, distance={distance}, force={force}");
        }

        if (repulsionVector.sqrMagnitude > 0f)
        {
            Debug.Log($"Applying repulsion force: {repulsionVector}");
            rb.AddForce(repulsionVector, ForceMode2D.Force);
        }
    }
}
