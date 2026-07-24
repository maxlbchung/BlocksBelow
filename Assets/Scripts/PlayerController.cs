using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : Entity
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 15f;
    [SerializeField] private float deceleration = 12f;
    [SerializeField, Min(0f)] private float knockbackControlLockTime = 0.3f;

    [Header("Jumping")]
    [SerializeField] private float jumpForce = 12f;
    [SerializeField] private float maxJumpHeight = 5f;
    [SerializeField, Range(0f, 1f)] private float jumpReleaseVelocityMultiplier = 0.5f;
    [SerializeField] private float fallAcceleration = 20f;
    [SerializeField] private float maxFallSpeed = 20f;
    [SerializeField] private LayerMask includeLayers;

    [Header("Air Jumps")]
    [SerializeField] private int maxAirJumps = 0;
    private int airJumpsRemaining;

    [Header("Coyote Time")]
    [SerializeField] private float coyoteTime = 0.1f;
    private float coyoteCounter;
    private bool wasGrounded;

    [Header("Jump Buffering")]
    [SerializeField] private float jumpBufferTime = 0.1f;
    private float jumpBufferCounter;
    [SerializeField] private float groundCheckLength = 1.0f;

    [Header("Passable Platforms")]
    [SerializeField] private float platformCheckRadius = 2f;

    private Rigidbody2D rb;
    private Collider2D playerCollider;
    private bool isGrounded;
    private float currentHorizontalVelocity;
    private float knockbackTimer;
    private bool jumpReleased;
    private bool jumpInProgress;
    private Collider2D[] nearbyColliders = new Collider2D[32];
    private readonly List<PlatformEffector2D> droppingThroughPlatforms = new();

    private bool alive = true;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        airJumpsRemaining = maxAirJumps;
        coyoteCounter = 0f;
        jumpBufferCounter = 0f;

    }

    void Update()
    {
        if (!alive)
            return;

        HandleInput();
        UpdateCoyoteTime();
        UpdateJumpBuffer();
        UpdateGroundedState();
        UpdatePlatformEffectors();
    }

    void FixedUpdate()
    {
        if (!alive)
            return;
        
        if (knockbackTimer > 0f)
        {
            knockbackTimer -= Time.fixedDeltaTime;
            ApplyGravity();

            if (knockbackTimer <= 0f)
            {
                currentHorizontalVelocity = rb.linearVelocity.x;
            }

            return;
        }

        HandleMovement();
        HandleJumping();
        ApplyJumpCut();
        ApplyGravity();
    }

    public void ApplyKnockback(Vector2 impulse)
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
        }

        if (rb == null)
        {
            return;
        }

        rb.AddForce(impulse, ForceMode2D.Impulse);
        knockbackTimer = Mathf.Max(knockbackTimer, knockbackControlLockTime);
    }

    private void HandleInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        float moveInput = 0f;
        if (keyboard.aKey.isPressed)
            moveInput -= 1f;
        if (keyboard.dKey.isPressed)
            moveInput += 1f;

        // Accelerate or decelerate based on input
        if (Mathf.Abs(moveInput) > 0.1f)
        {
            currentHorizontalVelocity = Mathf.Lerp(currentHorizontalVelocity, moveInput * moveSpeed, acceleration * Time.deltaTime);
        }
        else
        {
            currentHorizontalVelocity = Mathf.Lerp(currentHorizontalVelocity, 0f, deceleration * Time.deltaTime);
        }

        // Jump input buffering
        if (keyboard.spaceKey.wasPressedThisFrame)
        {
            jumpBufferCounter = jumpBufferTime;
        }

        if (keyboard.spaceKey.wasReleasedThisFrame)
        {
            jumpReleased = true;
        }
    }

    private void UpdateCoyoteTime()
    {
        if (isGrounded)
        {
            coyoteCounter = coyoteTime;
            wasGrounded = true;
        }
        else
        {
            coyoteCounter -= Time.deltaTime;
        }
    }

    private void UpdateJumpBuffer()
    {
        if (jumpBufferCounter > 0)
        {
            jumpBufferCounter -= Time.deltaTime;
        }
    }

    private void UpdateGroundedState()
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        isGrounded = false;
        bool isDroppingThrough = Keyboard.current != null
            && Keyboard.current.sKey.isPressed;

        RaycastHit2D[] hits = Physics2D.RaycastAll(
            transform.position,
            Vector2.down,
            groundCheckLength,
            includeLayers);

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null || hit.collider.isTrigger)
            {
                continue;
            }

            PlatformEffector2D platform = hit.collider.GetComponent<PlatformEffector2D>();
            if (platform != null
                && (isDroppingThrough || droppingThroughPlatforms.Contains(platform)))
            {
                continue;
            }

            if (hit.collider.GetComponentInParent<Ground>() != null
                || hit.collider.gameObject.layer == wallLayer)
            {
                isGrounded = true;
                break;
            }
        }

        if (!isGrounded && wasGrounded)
        {
            airJumpsRemaining = maxAirJumps;
        }

        wasGrounded = isGrounded;
    }

    private void HandleMovement()
    {
        rb.linearVelocity = new Vector2(currentHorizontalVelocity, rb.linearVelocity.y);
    }

    private void HandleJumping()
    {
        bool canJump = isGrounded || coyoteCounter > 0;
        bool hasAirJump = airJumpsRemaining > 0;
        Debug.Log(jumpBufferCounter + " " + isGrounded);

        if ((jumpBufferCounter > 0 || Keyboard.current.spaceKey.wasPressedThisFrame) && (canJump || hasAirJump))
        {
            jumpBufferCounter = 0;

            // Use coyote jump if available
            if (canJump)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
                coyoteCounter = 0f; // Use up coyote time
                jumpInProgress = true;
            }
            // Otherwise use air jump
            else if (hasAirJump)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
                airJumpsRemaining--;
                jumpInProgress = true;
            }
        }
    }

    private void ApplyJumpCut()
    {
        if (!jumpReleased)
        {
            return;
        }

        jumpReleased = false;

        // Releasing jump early removes some upward speed, producing a shorter jump.
        if (jumpInProgress && rb.linearVelocity.y > 0f)
        {
            rb.linearVelocity = new Vector2(
                rb.linearVelocity.x,
                rb.linearVelocity.y * jumpReleaseVelocityMultiplier);
        }

        jumpInProgress = false;
    }

    private void ApplyGravity()
    {
        if (!isGrounded)
        {
            float newVelocityY = rb.linearVelocity.y - (fallAcceleration * Time.fixedDeltaTime);
            newVelocityY = Mathf.Max(newVelocityY, -maxFallSpeed);
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, newVelocityY);
        }
    }

    // Optional: Visualize ground check in the editor
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + Vector3.down * 0.6f);
    }

    private void UpdatePlatformEffectors()
    {
        Keyboard keyboard = Keyboard.current;
        bool isHoldingS = keyboard != null && keyboard.sKey.isPressed;

        // Find all nearby colliders with PlatformEffector2D
        ContactFilter2D filter = new ContactFilter2D
        {
            layerMask = LayerMask.GetMask("Default", "Wall"),
            useLayerMask = true
        };

        int colliderCount = Physics2D.OverlapCircle(transform.position, platformCheckRadius, filter, nearbyColliders);

        if (isHoldingS)
        {
            for (int i = 0; i < colliderCount; i++)
            {
                Collider2D collider = nearbyColliders[i];
                if (collider == null)
                    continue;

                PlatformEffector2D effector = collider.GetComponent<PlatformEffector2D>();
                if (effector == null)
                    continue;

                effector.surfaceArc = 0f;
                if (!droppingThroughPlatforms.Contains(effector))
                {
                    droppingThroughPlatforms.Add(effector);
                }
            }
        }

        // Do not restore a platform merely because S was released. Keep it
        // passable until the player's whole collider is below the edge.
        for (int i = droppingThroughPlatforms.Count - 1; i >= 0; i--)
        {
            PlatformEffector2D effector = droppingThroughPlatforms[i];
            if (effector == null)
            {
                droppingThroughPlatforms.RemoveAt(i);
                continue;
            }

            Collider2D platformCollider = effector.GetComponent<Collider2D>();
            bool fullyBelow = playerCollider == null
                || platformCollider == null
                || playerCollider.bounds.max.y < platformCollider.bounds.min.y - 0.01f;

            if (fullyBelow)
            {
                effector.surfaceArc = 180f;
                droppingThroughPlatforms.RemoveAt(i);
            }
        }

        // Clear remaining array
        for (int i = colliderCount; i < nearbyColliders.Length; i++)
        {
            nearbyColliders[i] = null;
        }
    }

    public void DamagePlayer(int damage, Vector2 knockback)
    {
        ApplyKnockback(knockback);
        health -= damage;
        if (health <= 0)
        {
            alive = false;
        }

    }
}

