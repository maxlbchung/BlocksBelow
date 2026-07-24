using UnityEngine;

public class Bird : Enemy
{
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 5f;
    public float currentSpeed;

    private void Update()
    {
        currentSpeed = Mathf.Clamp(currentSpeed + acceleration * Time.deltaTime, 0f, moveSpeed);
    }

    protected override Vector2 CalculateDesiredVelocity(Transform player, float elapsed)
    {
        if (player == null)
        {
            return Vector2.zero;
        }

        Vector2 direction = (Vector2)player.position - Position;
        float distanceSquared = direction.sqrMagnitude;
        if (distanceSquared <= 0.000001f)
        {
            return Vector2.zero;
        }

        return direction * (currentSpeed / Mathf.Sqrt(distanceSquared));
    }

    private void resetSpeed()
    {
        currentSpeed = 0f;
    }
}
