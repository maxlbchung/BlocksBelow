using UnityEngine;

public class BasicEnemy : Enemy
{
    [SerializeField] private float moveSpeed = 5f;
    public int damage;

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

        return direction * (moveSpeed / Mathf.Sqrt(distanceSquared));
    }
}
