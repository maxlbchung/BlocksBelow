using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    private Rigidbody2D rb;
    private BoxCollider2D boxCollider;
    private bool isStrafing;

    [SerializeField] private float strafeTime = 0.5f;

    [SerializeField] private int jumps = 0;
    [SerializeField] private int right = 0;
    [SerializeField] private int left = 0;

    [Header("Input Actions")]
    [SerializeField] private InputActionReference selectAction;
    [SerializeField] private InputActionReference consumeAction;

    private List<Block> selectedBlocks = new List<Block>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        boxCollider = GetComponent<BoxCollider2D>();
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        StartCoroutine(Strafe());
    }

    

    private void Update()
    {
        //jump
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame && jumps > 0 && !isStrafing)
        {
            rb.linearVelocity = new Vector2(0, 5f);
            jumps--;
        }

        if(keyboard != null && keyboard.sKey.wasPressedThisFrame)
        {
            //absorb blocks below
        }

        HandleSelecting();
    }

    private IEnumerator Strafe()
    {
        while (true)
        {
            Keyboard keyboard = Keyboard.current;
            int direction = 0;

            if (keyboard != null && keyboard.aKey.isPressed && left > 0)
            {
                direction = -1;
            }
            else if (keyboard != null && keyboard.dKey.isPressed && right > 0)
            {
                direction = 1;
            }

            if (direction != 0)
            {
                isStrafing = true;

                if (direction < 0)
                {
                    left--;
                }
                else
                {
                    right--;
                }

                // Buffer the strafe while rising, then start it at or after the apex.
                while (rb.linearVelocity.y > 0f)
                {
                    yield return null;
                }

                float duration = Mathf.Max(strafeTime, Time.fixedDeltaTime);
                float startX = rb.position.x;
                float lockedY = rb.position.y;
                float targetX = Mathf.Round(startX + direction);
                float savedVerticalVelocity = rb.linearVelocity.y;
                float elapsedTime = 0f;

                rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);

                // This loop locks out new strafe input until the move is finished.
                while (elapsedTime < duration)
                {
                    yield return new WaitForFixedUpdate();

                    elapsedTime += Time.fixedDeltaTime;
                    float progress = Mathf.Clamp01(elapsedTime / duration);
                    float easedProgress = 1f - Mathf.Pow(1f - progress, 2f);
                    float nextX = Mathf.Lerp(startX, targetX, easedProgress);

                    // MovePosition keeps movement in the 2D physics simulation so
                    // collisions can block the player and dynamic bodies can be pushed.
                    rb.MovePosition(new Vector2(nextX, lockedY));
                }

                // Wait for the last MovePosition call to be resolved by physics.
                yield return new WaitForFixedUpdate();

                float endingX = rb.position.x;
                bool isCloserToStart = Mathf.Abs(endingX - startX)
                    < Mathf.Abs(endingX - targetX);

                if (isCloserToStart)
                {
                    float returnStartX = endingX;
                    elapsedTime = 0f;

                    while (elapsedTime < duration)
                    {
                        yield return new WaitForFixedUpdate();

                        elapsedTime += Time.fixedDeltaTime;
                        float progress = Mathf.Clamp01(elapsedTime / duration);
                        float easedProgress = 1f - Mathf.Pow(1f - progress, 2f);
                        float nextX = Mathf.Lerp(returnStartX, startX, easedProgress);

                        rb.MovePosition(new Vector2(nextX, lockedY));
                    }

                    rb.position = new Vector2(startX, lockedY);
                }
                else
                {
                    rb.position = new Vector2(targetX, lockedY);
                }

                rb.linearVelocity = new Vector2(0f, savedVerticalVelocity);

                keyboard = Keyboard.current;
                bool isDirectionHeld = keyboard != null
                    && ((keyboard.aKey.isPressed && left > 0)
                        || (keyboard.dKey.isPressed && right > 0));

                // Start the next grid step immediately so gravity never gets a
                // physics frame between lerps while a direction is held.
                if (isDirectionHeld)
                {
                    continue;
                }

                isStrafing = false;
            }

            yield return null;
        }
    }

    public GameObject FindBoxBelow()
    {
        Debug.Log("Finding box below player");
        GameObject[] objectsBelow = RaycastFindAll(Vector2.down).ToArray();
        foreach (GameObject obj in objectsBelow)
        {
            if (obj.TryGetComponent<Block>(out Block block) && !selectedBlocks.Contains(block))
            {
                Debug.Log("Found block below player: " + block.name);
                block.selectBlock();
                selectedBlocks.Add(block);
                return block.gameObject;
            }
        }

        return null;

    }

    public void HandleSelecting()
    {
        if (selectAction.action.WasPressedThisFrame())
        {
            FindBoxBelow();
        }

        if (consumeAction.action.WasPressedThisFrame())
        {
            Debug.Log("Consuming selected blocks");
            foreach (Block block in selectedBlocks)
            {
                block.DestroyBlock();

                switch (block.blockPower)
                {
                    case Block.Power.Jump:
                    {
                        jumps++;
                        break;
                    }
                    case Block.Power.Right:
                    {
                        right++;
                        break;
                    }
                     case Block.Power.Left:
                    {
                        left++;
                        break;
                    }
                }
            }
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

    public List<GameObject> RaycastFindAll(Vector2 direction, float distance = Mathf.Infinity)
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

        List<GameObject> returns = new List<GameObject>();

        foreach (RaycastHit2D hit in hits)
        {
            // A ray starting inside this object can detect its own colliders.
            if (hit.collider != null && !hit.collider.transform.IsChildOf(transform))
            {
                returns.Add(hit.collider.gameObject);
            }
        }

        return returns;
    }

}
