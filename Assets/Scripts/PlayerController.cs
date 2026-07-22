using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

public class PlayerController : MonoBehaviour
{
    private Rigidbody2D rb;
    private BoxCollider2D boxCollider;

    [SerializeField] private float strafeTime = 0.5f;

    [SerializeField] private int jumps = 0;
    [SerializeField] private int right = 0;
    [SerializeField] private int left = 0;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();

        StartCoroutine(Strafe());
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            rb.linearVelocity += new Vector2(0, 5f);
        }
    }

    private IEnumerator Strafe()
    {
        while (true)
        {
            if (Input.GetKey(KeyCode.A))
            {
                float posX = transform.position.x;

                while(posX > transform.position.x + 1)
                {
                    posX -= 0.1f;
                    transform.position = new Vector2(posX, transform.position.y);
                    yield return null;
                }

                yield return new WaitForSeconds(strafeTime);
            }
            else if (Input.GetKey(KeyCode.D))
            {
                float posX = transform.position.x;

                yield return new WaitForSeconds(strafeTime);
            }

            yield return null;
        }
    }


    public GameObject Raycast(Vector2 direction, float distance = Mathf.Infinity)
    {
        if (direction.sqrMagnitude == 0f || distance < 0f)
        {
            return null;
        }

        RaycastHit2D[] hits = Physics2D.RaycastAll(
            transform.position,
            direction.normalized,
            distance
        );

        foreach (RaycastHit2D hit in hits)
        {
            // A ray starting inside this object can detect its own colliders.
            if (hit.collider != null && !hit.collider.transform.IsChildOf(transform))
            {
                return hit.collider.gameObject;
            }
        }

        return null;
    }

}
