using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class CageBreakerEnemy : Enemy
{
    public enum BreakerState
    {
        Sneaking,
        Breaking
    }

    private static readonly Dictionary<CageTower, CageBreakerEnemy> TargetClaims =
        new Dictionary<CageTower, CageBreakerEnemy>();

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
    public override bool CanTakeDamage =>
        state == BreakerState.Sneaking
            ? takesDamageInSneakingState
            : takesDamageInBreakingState;

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

        if (!TryClaimTarget())
        {
            ReleaseOrDestroy();
            return;
        }

        PositionForAmbush();
        EnterSneakingState();
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
        if (state == BreakerState.Sneaking && !IsValidTarget(targetCage))
        {
            ReleaseOrDestroy();
        }
    }

    protected override void ResetEnemyState()
    {
        ReleaseTarget();
        state = BreakerState.Sneaking;
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

    private bool TryClaimTarget()
    {
        ReleaseTarget();

        Transform player = EnemySimulationManager.Instance.Player;
        Vector2 playerPosition = player != null ? player.position : Position;
        float farthestDistanceSquared = float.NegativeInfinity;
        CageTower bestCage = null;
        CageTower[] cages = FindObjectsByType<CageTower>(FindObjectsSortMode.None);

        for (int i = 0; i < cages.Length; i++)
        {
            CageTower cage = cages[i];
            if (!IsValidTarget(cage) || IsClaimedByAnother(cage))
            {
                continue;
            }

            float distanceSquared =
                ((Vector2)cage.transform.position - playerPosition).sqrMagnitude;
            if (distanceSquared > farthestDistanceSquared)
            {
                farthestDistanceSquared = distanceSquared;
                bestCage = cage;
            }
        }

        if (bestCage == null)
        {
            return false;
        }

        targetCage = bestCage;
        TargetClaims[bestCage] = this;
        return true;
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
        state = BreakerState.Sneaking;
        countdownRemaining = 0f;
        SetSpriteOpacity(sneakingOpacity);
        countdownText.gameObject.SetActive(false);
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
        if (targetCage != null
            && TargetClaims.TryGetValue(targetCage, out CageBreakerEnemy owner)
            && owner == this)
        {
            TargetClaims.Remove(targetCage);
        }

        targetCage = null;
    }

    private bool IsClaimedByAnother(CageTower cage)
    {
        if (!TargetClaims.TryGetValue(cage, out CageBreakerEnemy owner))
        {
            return false;
        }

        if (owner == null || !owner.isActiveAndEnabled)
        {
            TargetClaims.Remove(cage);
            return false;
        }

        return owner != this;
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
