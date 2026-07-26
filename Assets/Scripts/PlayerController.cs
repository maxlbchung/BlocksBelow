using System.Collections;
using System.Collections.Generic;
using System.Net;
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
    [SerializeField, Min(0f), Tooltip("How close the player must already be to a platform for S to make it passable. Platforms further away stay solid, so a drop never carries through the next one down.")]
    private float platformContactDistance = 0.15f;
    [SerializeField, Min(0f), Tooltip("Minimum time a platform stays passable once a drop starts, so a quick tap of S still drops the player through.")]
    private float platformDropGraceTime = 0.05f;

    [Header("Movement Dust")]
    [SerializeField, Min(0f), Tooltip("Seconds between dust puffs while running on the ground.")]
    private float runDustInterval = 0.18f;
    [SerializeField, Min(0f), Tooltip("Horizontal speed below which running kicks up no dust.")]
    private float runDustMinSpeed = 1.5f;
    [SerializeField, Min(0)] private int jumpDustCount = 6;
    [SerializeField, Min(0f), Tooltip("Fall speed below which landing kicks up no dust.")]
    private float landDustMinFallSpeed = 4f;
    [SerializeField, Min(0)] private int landDustMaxCount = 12;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField, Min(0f)] private float walkAnimationThreshold = 0.1f;

    [Header("Death")]
    [SerializeField, Min(0f), Tooltip("Seconds the dead player stands still before the game over screen appears.")]
    private float gameOverDelay = 2.5f;
    [SerializeField, Min(0), Tooltip("White debris burst shown at the moment of death.")]
    private int deathParticleCount = 40;

    [Header("Fall Recovery")]
    [SerializeField, Tooltip("World Y the player has to drop below to count as fallen off the map. They are then put back on top of the highest block with 1 HP.")]
    private float fallLimitY = -12f;
    [SerializeField, Min(0f), Tooltip("Gap left between the player's feet and the block they are dropped back onto.")]
    private float fallRespawnClearance = 0.2f;

    [Header("Health Bar")]
    [SerializeField] private Vector2 healthBarSize = new Vector2(2.8f, 0.4f);
    [SerializeField] private float healthBarHeight = 1.1f;
    [SerializeField] private Color healthBarColor = new Color(0.2f, 0.85f, 0.25f, 1f);
    [SerializeField] private Color healthBarBackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.9f);
    [SerializeField] private int healthBarSortingOrder = 100;

    private Rigidbody2D rb;
    private Collider2D playerCollider;
    private bool isGrounded;
    private float currentHorizontalVelocity;
    private float knockbackTimer;
    private bool jumpReleased;
    private bool jumpInProgress;
    private float runDustTimer;
    private float peakFallSpeed;
    private Collider2D[] nearbyColliders = new Collider2D[32];

    private struct DroppingPlatform
    {
        public PlatformEffector2D effector;
        public Collider2D collider;
        public float originalSurfaceArc;
        public float graceTimer;
    }

    private readonly List<DroppingPlatform> droppingThroughPlatforms = new();
    private const float PlatformOverlapEpsilon = 0.02f;

    private bool alive = true;
    private RigidbodyConstraints2D constraintsBeforeDeath;
    public int maxHealth;
    private Transform healthBarRoot;
    private Transform healthBarFill;
    private Texture2D healthBarTexture;
    private Sprite healthBarSprite;
    private Vector2 pendingWindForce;
    private static readonly int IsWalkingAnimationParameter = Animator.StringToHash("IsWalking");

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        health = maxHealth;
        CreateHealthBar();
        UpdateHealthBar();
        airJumpsRemaining = maxAirJumps;
        coyoteCounter = 0f;
        jumpBufferCounter = 0f;

    }

    private void OnEnable()
    {
        EnemySimulationManager.SetPlayer(transform);
    }

    private void OnDisable()
    {
        EnemySimulationManager.ClearPlayer(transform);
        RestoreDroppingPlatforms();
    }

    void Update()
    {
        if (!alive)
        {
            UpdateAnimation();
            return;
        }

        if (transform.position.y < fallLimitY)
        {
            RespawnOnHighestBlock();
            return;
        }

        HandleInput();
        UpdateCoyoteTime();
        UpdateJumpBuffer();
        UpdateGroundedState();
        UpdateRunDust();
        UpdatePlatformEffectors();
        UpdateAnimation();
    }

    private Vector2 FeetPosition => playerCollider != null
        ? new Vector2(playerCollider.bounds.center.x, playerCollider.bounds.min.y)
        : (Vector2)transform.position;

    private void UpdateRunDust()
    {
        if (!isGrounded || Mathf.Abs(currentHorizontalVelocity) < runDustMinSpeed)
        {
            // Zeroed so the first puff appears the moment running resumes.
            runDustTimer = 0f;
            return;
        }

        runDustTimer -= Time.deltaTime;
        if (runDustTimer > 0f)
        {
            return;
        }

        runDustTimer = runDustInterval;
        DustParticles.EmitRun(FeetPosition, Mathf.Sign(currentHorizontalVelocity));
    }

    private void UpdateAnimation()
    {
        bool isWalking = alive
            && Mathf.Abs(currentHorizontalVelocity) > walkAnimationThreshold;

        if (animator != null)
        {
            animator.SetBool(IsWalkingAnimationParameter, isWalking);
        }

        if (spriteRenderer != null)
        {
            if (currentHorizontalVelocity > walkAnimationThreshold)
            {
                spriteRenderer.flipX = false;
            }
            else if (currentHorizontalVelocity < -walkAnimationThreshold)
            {
                spriteRenderer.flipX = true;
            }
        }
    }

    void FixedUpdate()
    {
        if (!alive)
        {
            pendingWindForce = Vector2.zero;
            return;
        }

        if (knockbackTimer > 0f)
        {
            knockbackTimer -= Time.fixedDeltaTime;
            ApplyGravity();
            ApplyPendingWindForce();

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
        ApplyPendingWindForce();
    }

    /// <summary>
    /// Queues a continuous external force (e.g. a FanTower's wind) for this
    /// physics step. Wind gets its own path because ApplyKnockback locks player
    /// control and HandleMovement/ApplyGravity rewrite velocity every step,
    /// which together erase a plain AddForce.
    /// </summary>
    public void ApplyWindForce(Vector2 force)
    {
        pendingWindForce += force;
    }

    private void ApplyPendingWindForce()
    {
        if (pendingWindForce == Vector2.zero)
            return;

        Vector2 windVelocity = pendingWindForce * (Time.fixedDeltaTime / rb.mass);
        pendingWindForce = Vector2.zero;

        // Integrated after ApplyGravity so vertical wind composes with it: an
        // updraft stronger than fallAcceleration genuinely lifts the player.
        rb.linearVelocity += windVelocity;

        // Feed the horizontal part into the movement model so HandleMovement
        // doesn't erase it next step; input then naturally fights the wind.
        if (knockbackTimer <= 0f)
        {
            currentHorizontalVelocity += windVelocity.x;
        }
    }

    private void CreateHealthBar()
    {
        healthBarTexture = new Texture2D(1, 1)
        {
            name = "Player Health Bar Texture",
            filterMode = FilterMode.Point
        };
        healthBarTexture.SetPixel(0, 0, Color.white);
        healthBarTexture.Apply();

        healthBarSprite = Sprite.Create(
            healthBarTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        healthBarSprite.name = "Player Health Bar Sprite";

        GameObject rootObject = new GameObject("Health Bar");
        healthBarRoot = rootObject.transform;
        healthBarRoot.SetParent(transform, false);
        healthBarRoot.localPosition = GetHealthBarLocalPosition();

        CreateHealthBarRenderer(
            "Background",
            healthBarRoot,
            healthBarBackgroundColor,
            healthBarSize,
            healthBarSortingOrder);

        GameObject fillObject = CreateHealthBarRenderer(
            "Fill",
            healthBarRoot,
            healthBarColor,
            healthBarSize,
            healthBarSortingOrder + 1);
        healthBarFill = fillObject.transform;
        healthBarFill.localPosition = new Vector3(0f, 0f, -0.01f);
    }

    private GameObject CreateHealthBarRenderer(
        string objectName,
        Transform parent,
        Color color,
        Vector2 size,
        int sortingOrder)
    {
        GameObject barObject = new GameObject(objectName);
        barObject.transform.SetParent(parent, false);
        barObject.transform.localScale = new Vector3(size.x, size.y, 1f);

        SpriteRenderer renderer = barObject.AddComponent<SpriteRenderer>();
        renderer.sprite = healthBarSprite;
        renderer.color = color;
        renderer.sortingLayerName = "Foreground";
        renderer.sortingOrder = sortingOrder;

        return barObject;
    }

    private Vector3 GetHealthBarLocalPosition()
    {
        float playerTop = playerCollider != null
            ? transform.InverseTransformPoint(playerCollider.bounds.max).y
            : 0.5f;

        return new Vector3(0f, playerTop + healthBarHeight, 0f);
    }

    private void UpdateHealthBar()
    {
        
        if (healthBarRoot == null || healthBarFill == null)
        {
            return;
        }

        float healthPercent = Mathf.Clamp01(health / maxHealth);
        if (healthPercent == 1)
        {
            healthBarRoot.gameObject.SetActive(false);
            return;
        }
        else
        {
            healthBarRoot.gameObject.SetActive(true);
        }

        healthBarFill.localScale = new Vector3(
            healthBarSize.x * healthPercent,
            healthBarSize.y,
            1f);
        healthBarFill.localPosition = new Vector3(
            -(healthBarSize.x * (1f - healthPercent)) * 0.5f,
            0f,
            -0.01f);
        healthBarRoot.gameObject.SetActive(healthPercent > 0f);
    }

    private void OnDestroy()
    {
        if (healthBarSprite != null)
        {
            Destroy(healthBarSprite);
        }

        if (healthBarTexture != null)
        {
            Destroy(healthBarTexture);
        }
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
                && (isDroppingThrough || IndexOfDroppingPlatform(platform) >= 0))
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

        if (!isGrounded)
        {
            // The impact frame reports a near-zero velocity because physics has
            // already resolved the collision, so the landing puff is scaled by
            // the fastest fall speed seen while airborne instead.
            peakFallSpeed = Mathf.Max(peakFallSpeed, -rb.linearVelocity.y);
        }
        else
        {
            if (!wasGrounded && peakFallSpeed >= landDustMinFallSpeed)
            {
                int count = Mathf.RoundToInt(Mathf.Lerp(
                    3f,
                    landDustMaxCount,
                    Mathf.InverseLerp(landDustMinFallSpeed, maxFallSpeed, peakFallSpeed)));
                DustParticles.EmitLand(FeetPosition, count);
            }

            peakFallSpeed = 0f;
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
        bool jumpHeld = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;

        // A buffered/fresh press can jump off the ground or spend an air jump.
        // Simply holding space keeps re-jumping off the ground (bunny hop) but
        // never auto-consumes air jumps while airborne.
        bool wantsJump = jumpBufferCounter > 0 || (jumpHeld && canJump);
        if (!wantsJump)
            return;

        // Use coyote jump if available
        if (canJump)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            coyoteCounter = 0f; // Use up coyote time
            jumpBufferCounter = 0;
            jumpInProgress = true;
            DustParticles.EmitJump(FeetPosition, jumpDustCount);
        }
        // Otherwise use air jump, but only on an actual press (not a held key)
        else if (hasAirJump && jumpBufferCounter > 0)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            airJumpsRemaining--;
            jumpBufferCounter = 0;
            jumpInProgress = true;
            DustParticles.EmitJump(FeetPosition, jumpDustCount);
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

        // Only the platform the player is actually standing on (or already
        // sunk into) is opened up. Arming every effector within the search
        // radius used to punch a hole through the whole stack below, so one
        // tap of S dropped the player past several platforms in a row.
        if (isHoldingS && playerCollider != null)
        {
            ContactFilter2D filter = new ContactFilter2D
            {
                layerMask = LayerMask.GetMask("Default", "Wall"),
                useLayerMask = true
            };

            int colliderCount = Physics2D.OverlapCircle(transform.position, platformCheckRadius, filter, nearbyColliders);

            for (int i = 0; i < colliderCount; i++)
            {
                Collider2D collider = nearbyColliders[i];
                if (collider == null)
                    continue;

                PlatformEffector2D effector = collider.GetComponent<PlatformEffector2D>();
                if (effector == null || !IsStandingOnPlatform(collider))
                    continue;

                int existingIndex = IndexOfDroppingPlatform(effector);
                if (existingIndex >= 0)
                {
                    DroppingPlatform existing = droppingThroughPlatforms[existingIndex];
                    existing.graceTimer = platformDropGraceTime;
                    droppingThroughPlatforms[existingIndex] = existing;
                    continue;
                }

                droppingThroughPlatforms.Add(new DroppingPlatform
                {
                    effector = effector,
                    collider = collider,
                    originalSurfaceArc = effector.surfaceArc,
                    graceTimer = platformDropGraceTime
                });
                effector.surfaceArc = 0f;
            }

            // Clear remaining array
            for (int i = colliderCount; i < nearbyColliders.Length; i++)
            {
                nearbyColliders[i] = null;
            }
        }

        // Releasing S ends the drop as soon as the player is no longer inside
        // the platform - whether they finished passing through it or never
        // sank into it at all. The grace timer only covers the first moments
        // of a tap, before gravity has pulled the player into the collider.
        for (int i = droppingThroughPlatforms.Count - 1; i >= 0; i--)
        {
            DroppingPlatform dropping = droppingThroughPlatforms[i];
            if (dropping.effector == null)
            {
                droppingThroughPlatforms.RemoveAt(i);
                continue;
            }

            dropping.graceTimer = Mathf.Max(0f, dropping.graceTimer - Time.deltaTime);
            droppingThroughPlatforms[i] = dropping;

            if (dropping.graceTimer > 0f || IsInsidePlatform(dropping.collider))
            {
                continue;
            }

            dropping.effector.surfaceArc = dropping.originalSurfaceArc;
            droppingThroughPlatforms.RemoveAt(i);
        }
    }

    private int IndexOfDroppingPlatform(PlatformEffector2D effector)
    {
        for (int i = 0; i < droppingThroughPlatforms.Count; i++)
        {
            if (droppingThroughPlatforms[i].effector == effector)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// True when the player is resting on, or already inside, this platform.
    /// Collider-shape distance rather than bounds, so tilemap and composite
    /// platforms are measured at the surface the player is touching.
    /// </summary>
    private bool IsStandingOnPlatform(Collider2D platformCollider)
    {
        if (playerCollider == null || platformCollider == null || !platformCollider.enabled)
        {
            return false;
        }

        ColliderDistance2D distance = Physics2D.Distance(playerCollider, platformCollider);
        if (!distance.isValid || distance.distance > platformContactDistance)
        {
            return false;
        }

        // Ignore platforms the player is merely brushing from below or the side.
        return distance.pointB.y < playerCollider.bounds.center.y;
    }

    /// <summary>
    /// True while the player's collider still overlaps the platform, i.e. the
    /// drop through it is still in progress.
    /// </summary>
    private bool IsInsidePlatform(Collider2D platformCollider)
    {
        if (playerCollider == null || platformCollider == null || !platformCollider.enabled)
        {
            return false;
        }

        ColliderDistance2D distance = Physics2D.Distance(playerCollider, platformCollider);
        return distance.isValid && distance.distance < -PlatformOverlapEpsilon;
    }

    private void RestoreDroppingPlatforms()
    {
        for (int i = 0; i < droppingThroughPlatforms.Count; i++)
        {
            DroppingPlatform dropping = droppingThroughPlatforms[i];
            if (dropping.effector != null)
            {
                dropping.effector.surfaceArc = dropping.originalSurfaceArc;
            }
        }

        droppingThroughPlatforms.Clear();
    }

    /// <summary>
    /// Drops the player back on top of the highest block and leaves them on 1 HP.
    /// Falling off the map costs the run's health cushion rather than the run, so
    /// the player is set down standing and unhurt-but-fragile instead of dying.
    /// </summary>
    private void RespawnOnHighestBlock()
    {
        Collider2D block = FindHighestBlock();
        if (block != null)
        {
            // Measured from the collider rather than the transform, whose origin
            // sits inside the body, so the feet land on the surface either way.
            float feetToCentre = playerCollider != null
                ? transform.position.y - playerCollider.bounds.min.y
                : 0f;

            transform.position = new Vector3(
                block.bounds.center.x,
                block.bounds.max.y + feetToCentre + fallRespawnClearance,
                transform.position.z);
        }

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // The fall is over, so nothing it built up carries into the landing: no
        // leftover shove, no wind, and no landing puff sized by the whole drop.
        currentHorizontalVelocity = 0f;
        knockbackTimer = 0f;
        pendingWindForce = Vector2.zero;
        jumpBufferCounter = 0f;
        jumpInProgress = false;
        peakFallSpeed = 0f;
        airJumpsRemaining = maxAirJumps;
        RestoreDroppingPlatforms();

        health = 1;
        UpdateHealthBar();
    }

    /// <summary>
    /// The topmost surface the player could stand on - the island, or whatever
    /// has been built above it. Only runs on a fall, so a full scan is cheaper
    /// than keeping a sorted list up to date on every placement.
    /// </summary>
    private Collider2D FindHighestBlock()
    {
        Collider2D highest = null;
        Collider2D[] colliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);

        foreach (Collider2D candidate in colliders)
        {
            // Same mask the ground check uses, so the player only ever lands back
            // on something they could have been standing on in the first place.
            if (candidate.isTrigger
                || (includeLayers.value & (1 << candidate.gameObject.layer)) == 0)
            {
                continue;
            }

            if (highest == null || candidate.bounds.max.y > highest.bounds.max.y)
            {
                highest = candidate;
            }
        }

        return highest;
    }

    public void DamagePlayer(int damage, Vector2 knockback)
    {
        // A corpse takes no further hits, so it is not shoved around while the
        // game over screen counts down. Invincibility frames swallow the hit
        // entirely - no damage and no knockback.
        if (!alive || IsInvincible)
        {
            return;
        }

        ApplyKnockback(knockback);
        health = Mathf.Max(0f, health - damage);
        UpdateHealthBar();
        PlayHitFeedback(transform.position);
        if (health <= 0)
        {
            Die();
        }

    }

    /// <summary>False once the killing blow has landed, i.e. the run is over.</summary>
    public bool IsAlive => alive;

    /// <summary>
    /// Stops the player where they are and queues the game over screen. Movement,
    /// input, and knockback are all dropped; Unity's own gravity still settles the
    /// body onto the ground if the killing blow landed mid-air.
    /// </summary>
    private void Die()
    {
        if (!alive)
        {
            return;
        }

        alive = false;
        // Update() stops running the platform logic once dead, so hand back any
        // platform still held open instead of leaving a permanent hole.
        RestoreDroppingPlatforms();
        currentHorizontalVelocity = 0f;
        knockbackTimer = 0f;
        pendingWindForce = Vector2.zero;
        jumpBufferCounter = 0f;
        jumpInProgress = false;

        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            // Enemies still crowd the body, so lock sideways motion. Vertical stays
            // free, letting a player killed mid-air drop onto the ground. Recorded
            // first, so a revive puts back what the body actually had rather than
            // assuming this one flag was the only thing set.
            constraintsBeforeDeath = rb.constraints;
            rb.constraints |= RigidbodyConstraints2D.FreezePositionX;
        }

        UpdateAnimation();

        // The body stays behind for physics (gravity settles it, enemies crowd
        // it), but visually the player is gone: the hit flash is stopped, the
        // sprite hidden, and a debris burst marks the spot.
        ResetHitFeedback();
        HitParticles.EmitDeathBurst(transform.position, deathParticleCount);
        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = false;
        }

        StartCoroutine(ShowGameOverAfterDelay());
    }

    private IEnumerator ShowGameOverAfterDelay()
    {
        // Realtime, so the pause the screen applies cannot stall its own entrance.
        yield return new WaitForSecondsRealtime(gameOverDelay);
        GameOverScreen.Show();
    }

    /// <summary>
    /// Restores health up to maxHealth. Returns false when no healing was needed.
    /// </summary>
    /// <summary>
    /// True while a heal would actually do something. Mirrors the guard in
    /// <see cref="Heal"/> so the shop can hide healing it would refuse to apply.
    /// </summary>
    public bool CanBeHealed => alive && health < maxHealth;

    public bool Heal(int amount)
    {
        if (!alive || amount <= 0 || health >= maxHealth)
        {
            return false;
        }

        health = Mathf.Min(maxHealth, health + amount);
        UpdateHealthBar();
        return true;
    }

    /// <summary>
    /// Undoes <see cref="Die"/>, standing the player back up at
    /// <paramref name="position"/> on <paramref name="restoredHealth"/>. Everything the
    /// death left behind is handed back: the sideways lock, the hidden sprite, and the
    /// movement state a fresh round has no business inheriting.
    /// </summary>
    public void Revive(float restoredHealth, Vector3 position)
    {
        alive = true;
        health = Mathf.Clamp(restoredHealth, 1f, maxHealth);

        transform.position = position;
        if (rb != null)
        {
            rb.constraints = constraintsBeforeDeath;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }

        // The fall, shove, and jump the player died in the middle of are all over.
        currentHorizontalVelocity = 0f;
        knockbackTimer = 0f;
        pendingWindForce = Vector2.zero;
        jumpBufferCounter = 0f;
        jumpReleased = false;
        jumpInProgress = false;
        peakFallSpeed = 0f;
        coyoteCounter = 0f;
        airJumpsRemaining = maxAirJumps;
        RestoreDroppingPlatforms();

        // Closes the invincibility window left open by the killing blow, so the round
        // does not start with free hits going spare.
        ResetHitFeedback();

        if (spriteRenderer != null)
        {
            spriteRenderer.enabled = true;
        }

        UpdateHealthBar();
        UpdateAnimation();
    }
}

