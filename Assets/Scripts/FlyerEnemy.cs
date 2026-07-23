using UnityEngine;

public class FlyerEnemy : Enemy
{
    [SerializeField] private float moveSpeed = 1f;
    [SerializeField] private float desiredRange = 5f; // Desired range from the player
    [SerializeField] private float runRange = 1f; // Range at which the enemy will start to run away from the player
    [SerializeField] private float shootInterval = 2f; // Time interval between shots
    [SerializeField] private GameObject bulletPrefab; // Prefab for the bullet
    [SerializeField] private float bulletSpeed = 5f; // Speed of the bullet
    private GameObject player;

    private Vector3 target;

    private float shootCounter = 0f;

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
    private void Update()
    {
        shootCounter += Time.deltaTime;
        if (shootCounter >= shootInterval)
        {
            ShootAtPlayer();
            shootCounter = 0f;
        }
    }

    protected override void FixedUpdate()
    {
        if (player == null)
        {
            player = GameObject.FindGameObjectWithTag("Player");
        }
        if (Vector3.Distance(target, transform.position) > 0.1f)
        {
            Vector2 direction = (target - transform.position).normalized;
            Vector2 targetVelocity = direction * moveSpeed;

            // Apply force towards player instead of setting velocity directly
            Vector2 velocityDifference = targetVelocity - rb.linearVelocity;
            rb.AddForce(velocityDifference * rb.mass, ForceMode2D.Force);
        }
        if (player != null && rb != null)
        {
            if (Vector3.Distance(player.transform.position, transform.position) > desiredRange)
            {
                target = GetClosestPointOnCircle(player.transform.position, desiredRange, transform.position);
            }
            else if (Vector3.Distance(player.transform.position, transform.position) < runRange)
            {
                Vector3 directionAwayFromPlayer = (transform.position - player.transform.position).normalized;
                target = transform.position + (directionAwayFromPlayer * desiredRange);
            }
            else
            {
                target = transform.position; // Stay in place if within the desired range
            }
        }

        // Apply repulsion from other enemies   
        base.FixedUpdate();
    }

    public static Vector3 GetClosestPointOnCircle(Vector3 circleCenter, float radius, Vector3 targetPosition)
    {
        // Vector pointing from circle center to target
        Vector3 direction = targetPosition - circleCenter;

        // Handle edge case where target is exactly at the center
        if (direction.sqrMagnitude < 0.0001f)
        {
            return circleCenter + Vector3.forward * radius; // Default fallback direction
        }

        // direction.normalized gets the unit vector (length 1)
        // Multiply by radius and add circleCenter to project onto the edge
        return circleCenter + (direction.normalized * radius);
    }

    public void ShootAtPlayer()
    {
        if (player != null)
        {
            Vector3 directionToPlayer = (player.transform.position - transform.position).normalized;
            GameObject bullet = Instantiate(bulletPrefab, transform.position, Quaternion.identity);
            bullet.GetComponent<Rigidbody2D>().linearVelocity = directionToPlayer * bulletSpeed;
        }
    }
}
