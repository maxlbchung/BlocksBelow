using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

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
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
        {
            rb.linearVelocity += new Vector2(0, 5f);
        }
    }

    private IEnumerator Strafe()
    {
        while (true)
        {
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.aKey.isPressed)
            {
                transform.position += Vector3.left * Time.deltaTime * 5f;
            }
            else if (keyboard != null && keyboard.dKey.isPressed)
            {
                transform.position += Vector3.right * Time.deltaTime * 5f;
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
