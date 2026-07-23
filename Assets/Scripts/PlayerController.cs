using UnityEngine;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 15f;
    [SerializeField] private float deceleration = 12f;

    [Header("Jumping")]
    [SerializeField] private float jumpForce = 12f;
    [SerializeField] private float maxJumpHeight = 5f;
    [SerializeField] private float fallAcceleration = 20f;
    [SerializeField] private float maxFallSpeed = 20f;
    [SerializeField] private LayerMask ignoreLayers;

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

    private Rigidbody2D rb;
    private bool isGrounded;
    private float currentHorizontalVelocity;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        airJumpsRemaining = maxAirJumps;
        coyoteCounter = 0f;
        jumpBufferCounter = 0f;
    }

    void Update()
    {
        HandleInput();
        UpdateCoyoteTime();
        UpdateJumpBuffer();
        UpdateGroundedState();
    }

    void FixedUpdate()
    {
        HandleMovement();
        HandleJumping();
        ApplyGravity();
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
        // Implement raycasting or collider-based ground check
        // This example uses a simple raycast downward
        RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, groundCheckLength, ~ignoreLayers);
        isGrounded = hit.collider != null && hit.collider.CompareTag("Ground");

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

        if ((jumpBufferCounter > 0  || Keyboard.current.spaceKey.wasPressedThisFrame) && (canJump || hasAirJump))
        {
            jumpBufferCounter = 0;

            // Use coyote jump if available
            if (canJump)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
                coyoteCounter = 0f; // Use up coyote time
            }
            // Otherwise use air jump
            else if (hasAirJump)
            {
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
                airJumpsRemaining--;
            }
        }
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
}
