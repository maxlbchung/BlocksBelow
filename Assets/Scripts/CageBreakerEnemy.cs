using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class CageBreakerEnemy : Enemy
{
    public enum BreakerState
    {
        /// <summary>On the field with no cage worth attacking yet, holding position until one appears.</summary>
        Waiting,
        Sneaking,
        Breaking
    }

    [Header("Sneaking")]
    [SerializeField, Min(0f)] private float moveSpeed = 4f;
    [SerializeField, Range(0f, 1f)] private float sneakingOpacity = 0.25f;
    [SerializeField, Min(0.1f)] private float spawnRadius = 12f;
    [SerializeField, Min(0f)] private float breakingDistance = 1.25f;
    [SerializeField] private bool takesDamageInSneakingState;

    [Header("Breaking")]
    [SerializeField, Min(0f)] private float breakCountdown = 5f;
    [SerializeField, Min(0f)] private float explosionRadius = 3f;
    [SerializeField] private bool takesDamageInBreakingState = true;
    [SerializeField] private Vector2 countdownOffset = new Vector2(0f, 1.2f);
    [SerializeField, Min(1f)] private float countdownFontSize = 10f;
    [SerializeField] private Sprite countdownBackgroundSprite;
    [SerializeField, Min(0f)] private float countdownScreenEdgeInset = 48f;
    [SerializeField] private TextMeshPro countdownText;
    [SerializeField] private SpriteRenderer countdownBackground;

    private readonly List<CageTower> cagesInExplosion = new List<CageTower>(16);
    private SpriteRenderer[] spriteRenderers;
    private CageTower targetCage;
    private BreakerState state;
    private float countdownRemaining;

    public BreakerState State => state;
    public CageTower TargetCage => targetCage;
    // Waiting looks the same as sneaking - faded out, no countdown - so it follows the
    // sneaking damage rule rather than the breaking one.
    public override bool CanTakeDamage =>
        state == BreakerState.Breaking
            ? takesDamageInBreakingState
            : takesDamageInSneakingState;

    /// <summary>
    /// A waiting breaker has nothing to do and may well be invincible, so the round is
    /// not held open for it. It still spawned, which is what the wave's count promises.
    /// </summary>
    public override bool BlocksWaveCompletion => state != BreakerState.Waiting;

    internal override bool UsesSeparation => false;

    protected override void Awake()
    {
        base.Awake();
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        EnsureCountdownText();
    }

    protected override void OnEnable()
    {
        base.OnEnable();

        // Spawning is unconditional: a wave that asks for three breakers gets three, even
        // with nothing to break yet. Without a cage the breaker waits on the field instead
        // of despawning, and picks one up as soon as the player fills a cage.
        if (TryClaimTarget())
        {
            PositionForAmbush();
            EnterSneakingState();
        }
        else
        {
            EnterWaitingState();
        }
    }

    protected override void OnDisable()
    {
        ReleaseTarget();
        base.OnDisable();
    }

    private void Update()
    {
        if (state != BreakerState.Breaking)
        {
            return;
        }

        countdownRemaining -= Time.deltaTime;
        UpdateCountdownText();
        if (countdownRemaining < 0f)
        {
            Explode();
        }
    }

    private void LateUpdate()
    {
        if (state == BreakerState.Breaking)
        {
            UpdateCountdownPosition();
        }
    }

    protected override Vector2 CalculateDesiredVelocity(Transform player, float elapsed)
    {
        // Waiting breakers fall through to zero here, so they hover where they spawned.
        if (state != BreakerState.Sneaking || !IsValidTarget(targetCage))
        {
            return Vector2.zero;
        }

        Vector2 toTarget = (Vector2)targetCage.transform.position - Position;
        float distanceSquared = toTarget.sqrMagnitude;
        float stopDistance = Mathf.Max(0f, breakingDistance);
        if (distanceSquared <= stopDistance * stopDistance)
        {
            EnterBreakingState();
            return Vector2.zero;
        }

        return toTarget * (Mathf.Max(0f, moveSpeed) / Mathf.Sqrt(distanceSquared));
    }

    protected override void OnStrategicTick(Transform player, float elapsed)
    {
        // A countdown already running is committed; it explodes wherever it stands.
        if (state == BreakerState.Breaking
            || (state == BreakerState.Sneaking && IsValidTarget(targetCage)))
        {
            return;
        }

        // Losing a cage mid-flight drops the breaker back to waiting rather than
        // despawning it, so it can pick up the next cage the player fills.
        if (TryClaimTarget())
        {
            EnterSneakingState();
        }
        else
        {
            EnterWaitingState();
        }
    }

    protected override void ResetEnemyState()
    {
        ReleaseTarget();
        state = BreakerState.Waiting;
        countdownRemaining = 0f;
        SetSpriteOpacity(1f);

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (countdownBackground != null)
        {
            countdownBackground.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Picks the cage the fewest other breakers are already going for, and the farthest
    /// from the player among those. Breakers spread out while there are cages to go round
    /// and double up on one only once every cage is spoken for.
    /// </summary>
    private bool TryClaimTarget()
    {
        ReleaseTarget();

        Transform player = EnemySimulationManager.Instance.Player;
        Vector2 playerPosition = player != null ? player.position : Position;
        int fewestClaims = int.MaxValue;
        float farthestDistanceSquared = float.NegativeInfinity;
        CageTower bestCage = null;
        CageTower[] cages = FindObjectsByType<CageTower>(FindObjectsSortMode.None);
        CageBreakerEnemy[] breakers =
            FindObjectsByType<CageBreakerEnemy>(FindObjectsSortMode.None);

        for (int i = 0; i < cages.Length; i++)
        {
            CageTower cage = cages[i];
            if (!IsValidTarget(cage))
            {
                continue;
            }

            int claims = CountClaims(cage, breakers);
            float distanceSquared =
                ((Vector2)cage.transform.position - playerPosition).sqrMagnitude;

            // Fewer claims always wins; distance only breaks ties within the same tier.
            if (claims > fewestClaims
                || (claims == fewestClaims && distanceSquared <= farthestDistanceSquared))
            {
                continue;
            }

            fewestClaims = claims;
            farthestDistanceSquared = distanceSquared;
            bestCage = cage;
        }

        if (bestCage == null)
        {
            return false;
        }

        targetCage = bestCage;
        return true;
    }

    /// <summary>
    /// How many other live breakers are heading for <paramref name="cage"/>. Counted from
    /// the breakers themselves rather than a claim table, so a breaker that dies, despawns
    /// or is pooled mid-run cannot leave a phantom claim behind.
    /// </summary>
    private int CountClaims(CageTower cage, CageBreakerEnemy[] breakers)
    {
        int claims = 0;
        for (int i = 0; i < breakers.Length; i++)
        {
            CageBreakerEnemy breaker = breakers[i];
            if (breaker != null
                && breaker != this
                && breaker.isActiveAndEnabled
                && breaker.targetCage == cage)
            {
                claims++;
            }
        }

        return claims;
    }

    private void PositionForAmbush()
    {
        Transform player = EnemySimulationManager.Instance.Player;
        if (player == null || targetCage == null)
        {
            return;
        }

        Vector2 playerPosition = player.position;
        Vector2 targetDirection = (Vector2)targetCage.transform.position - playerPosition;
        if (targetDirection.sqrMagnitude <= 0.000001f)
        {
            targetDirection = Position - playerPosition;
        }

        if (targetDirection.sqrMagnitude <= 0.000001f)
        {
            targetDirection = Vector2.right;
        }

        // The ambush point is mirrored through the player, so a cage above the player puts
        // it below - and that can land inside the island. Lift it back out.
        Vector2 spawnPosition = ClampAboveGround(
            playerPosition - targetDirection.normalized * Mathf.Max(0.1f, spawnRadius),
            GroundClearance);
        rb.position = spawnPosition;
        transform.position = spawnPosition;
    }

    private void EnterSneakingState()
    {
        EnterHiddenState(BreakerState.Sneaking);
    }

    private void EnterWaitingState()
    {
        EnterHiddenState(BreakerState.Waiting);
    }

    /// <summary>
    /// Shared setup for the two pre-explosion states. They look identical - faded out with
    /// no countdown - and differ only in whether a cage has been claimed yet.
    /// </summary>
    private void EnterHiddenState(BreakerState hiddenState)
    {
        state = hiddenState;
        countdownRemaining = 0f;
        SetSpriteOpacity(sneakingOpacity);
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (countdownBackground != null)
        {
            countdownBackground.gameObject.SetActive(false);
        }
    }

    private void EnterBreakingState()
    {
        if (state == BreakerState.Breaking)
        {
            return;
        }

        state = BreakerState.Breaking;
        countdownRemaining = Mathf.Max(0f, breakCountdown);
        rb.linearVelocity = Vector2.zero;
        SetSpriteOpacity(1f);
        countdownText.gameObject.SetActive(true);
        if (countdownBackground != null)
        {
            countdownBackground.gameObject.SetActive(
                countdownBackgroundSprite != null);
        }

        UpdateCountdownText();
        UpdateCountdownPosition();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsPlayerCollider(other))
        {
            ReleaseOrDestroy();
        }
    }

    private void Explode()
    {
        cagesInExplosion.Clear();
        CageTower[] cages = FindObjectsByType<CageTower>(FindObjectsSortMode.None);
        float radiusSquared = Mathf.Max(0f, explosionRadius);
        radiusSquared *= radiusSquared;

        for (int i = 0; i < cages.Length; i++)
        {
            CageTower cage = cages[i];
            if (cage != null
                && ((Vector2)cage.transform.position - Position).sqrMagnitude
                    <= radiusSquared)
            {
                cagesInExplosion.Add(cage);
            }
        }

        for (int i = 0; i < cagesInExplosion.Count; i++)
        {
            cagesInExplosion[i].BreakCage();
        }

        ReleaseOrDestroy();
    }

    private void EnsureCountdownText()
    {
        if (countdownText == null)
        {
            countdownText = GetComponentInChildren<TextMeshPro>(true);
        }

        if (countdownText == null)
        {
            GameObject textObject = new GameObject("Break Countdown");
            textObject.transform.SetParent(transform, false);
            countdownText = textObject.AddComponent<TextMeshPro>();
            countdownText.rectTransform.sizeDelta = new Vector2(20f, 5f);
            countdownText.transform.localScale = Vector3.one * 0.1f;
        }

        countdownText.transform.localPosition = countdownOffset;
        countdownText.alignment = TextAlignmentOptions.Center;
        countdownText.fontSize = Mathf.Max(1f, countdownFontSize);
        countdownText.textWrappingMode = TextWrappingModes.NoWrap;
        countdownText.sortingOrder = 100;
        countdownText.gameObject.SetActive(false);

        EnsureCountdownBackground();
    }

    private void EnsureCountdownBackground()
    {
        if (countdownBackground == null)
        {
            Transform existingBackground = transform.Find("Break Countdown Background");
            if (existingBackground != null)
            {
                countdownBackground = existingBackground.GetComponent<SpriteRenderer>();
            }
        }

        if (countdownBackground == null)
        {
            GameObject backgroundObject = new GameObject("Break Countdown Background");
            backgroundObject.transform.SetParent(transform, false);
            countdownBackground = backgroundObject.AddComponent<SpriteRenderer>();
        }

        countdownBackground.sprite = countdownBackgroundSprite;
        countdownBackground.sortingLayerID = countdownText.sortingLayerID;
        countdownBackground.sortingOrder = countdownText.sortingOrder - 1;
        countdownBackground.gameObject.SetActive(false);
    }

    private void UpdateCountdownText()
    {
        if (countdownText != null)
        {
            countdownText.text =
                Mathf.Max(0, Mathf.CeilToInt(countdownRemaining)).ToString();
        }
    }

    private void UpdateCountdownPosition()
    {
        if (countdownText == null)
        {
            return;
        }

        Vector3 normalWorldPosition = transform.position + (Vector3)countdownOffset;
        Vector3 displayWorldPosition = normalWorldPosition;
        Camera worldCamera = Camera.main;

        if (worldCamera != null)
        {
            Vector3 breakerViewportPosition =
                worldCamera.WorldToViewportPoint(transform.position);
            bool breakerIsOnScreen =
                breakerViewportPosition.z > 0f
                && breakerViewportPosition.x >= 0f
                && breakerViewportPosition.x <= 1f
                && breakerViewportPosition.y >= 0f
                && breakerViewportPosition.y <= 1f;

            if (!breakerIsOnScreen)
            {
                Vector3 breakerScreenPosition =
                    worldCamera.WorldToScreenPoint(transform.position);
                Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Vector2 direction =
                    (Vector2)breakerScreenPosition - screenCenter;

                if (breakerScreenPosition.z < 0f)
                {
                    direction = -direction;
                }

                if (direction.sqrMagnitude <= 0.0001f)
                {
                    direction = Vector2.up;
                }

                float inset = Mathf.Clamp(
                    countdownScreenEdgeInset,
                    0f,
                    Mathf.Min(Screen.width, Screen.height) * 0.5f);
                Vector2 halfBounds = new Vector2(
                    Mathf.Max(0f, Screen.width * 0.5f - inset),
                    Mathf.Max(0f, Screen.height * 0.5f - inset));
                float scaleToEdge = Mathf.Min(
                    direction.x == 0f
                        ? float.PositiveInfinity
                        : halfBounds.x / Mathf.Abs(direction.x),
                    direction.y == 0f
                        ? float.PositiveInfinity
                        : halfBounds.y / Mathf.Abs(direction.y));
                Vector2 clampedScreenPosition =
                    screenCenter + direction * scaleToEdge;
                Vector3 screenPosition = new Vector3(
                    clampedScreenPosition.x,
                    clampedScreenPosition.y,
                    worldCamera.WorldToScreenPoint(normalWorldPosition).z);
                displayWorldPosition =
                    worldCamera.ScreenToWorldPoint(screenPosition);
            }
        }

        countdownText.transform.position = displayWorldPosition;
        if (countdownBackground != null)
        {
            countdownBackground.transform.position = displayWorldPosition;
        }
    }

    private void SetSpriteOpacity(float opacity)
    {
        if (spriteRenderers == null)
        {
            return;
        }

        float alpha = Mathf.Clamp01(opacity);
        for (int i = 0; i < spriteRenderers.Length; i++)
        {
            SpriteRenderer spriteRenderer = spriteRenderers[i];
            if (spriteRenderer == null)
            {
                continue;
            }

            Color color = spriteRenderer.color;
            color.a = alpha;
            spriteRenderer.color = color;
        }
    }

    private static bool IsPlayerCollider(Collider2D other)
    {
        Transform current = other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : other.transform;

        while (current != null)
        {
            if (current.CompareTag("Player"))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private void ReleaseTarget()
    {
        targetCage = null;
    }

    private static bool IsValidTarget(CageTower cage)
    {
        return cage != null
            && cage.State == CageTower.CageState.Full
            && cage.CapturedEnemy != null;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, Mathf.Max(0f, explosionRadius));
    }
}
