using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour
{
    [SerializeField, Min(0f)] private float speed = 8f;

    private Rigidbody2D body;
    private Vector2 direction = Vector2.left;

    public float damage;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
    }

    private void Start()
    {
        ApplyVelocity();
    }

    public void SetDirection(Vector2 newDirection)
    {
        if (newDirection.sqrMagnitude > 0f)
        {
            direction = newDirection.normalized;
        }

        if (body != null)
        {
            ApplyVelocity();
        }
    }

    private void ApplyVelocity()
    {
        body.linearVelocity = direction * speed;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if(other.CompareTag("Enemy"))
        {
            other.GetComponent<Enemy>().health -= damage;
        }
        
    }
}
