using TMPro;
using UnityEngine;

public class Bird : Enemy
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 5f;

    [Header("Countdown")]
    [SerializeField, Min(0f)] private float countdownDuration = 10f;
    [SerializeField, Min(0f)] private float escapeSpeed = 14f;
    [SerializeField, Min(0.01f), Tooltip("Maximum time spent rushing upward before despawning.")]
    private float escapeDuration = 2f;
    [SerializeField] private Vector2 countdownOffset = new Vector2(0f, 1.2f);
    [SerializeField, Min(1f)] private float countdownFontSize = 10f;
    [SerializeField] private TextMeshPro countdownText;

    [Header("Timer From Damage")]
    [SerializeField] private bool damageIncreasesTimer = true;
    [SerializeField, Min(0.01f)] private float firstDamageThreshold = 1f;
    [SerializeField, Min(1.01f)] private float damageThresholdMultiplier = 2f;
    [SerializeField, Min(0f)] private float secondsAddedPerThreshold = 1f;

    [Header("Cage")]
    [SerializeField, Min(0f)] private float uncageableDurationAfterRelease = 2f;

    public float currentSpeed;

    private float countdownRemaining;
    private float damageTowardThreshold;
    private float nextDamageThreshold;
    private float cageableAgainTime;
    private float escapeTimeRemaining;
    private bool escaping;

    public float CountdownRemaining => countdownRemaining;
    public override bool CanBeCaged =>
        base.CanBeCaged && Time.time >= cageableAgainTime && !escaping;

    protected override void Awake()
    {
        base.Awake();
        EnsureCountdownText();
        ResetBirdState();
    }

    private void Update()
    {
        if (escaping)
        {
            escapeTimeRemaining -= Time.deltaTime;
            Camera mainCamera = Camera.main;
            bool isAboveScreen = mainCamera != null
                && mainCamera.WorldToViewportPoint(transform.position).y > 1.1f;
            if (isAboveScreen || escapeTimeRemaining <= 0f)
            {
                ReleaseOrDestroy();
            }

            return;
        }

        currentSpeed = Mathf.Clamp(currentSpeed + acceleration * Time.deltaTime, 0f, moveSpeed);
        countdownRemaining = Mathf.Max(0f, countdownRemaining - Time.deltaTime);
        UpdateCountdownText();

        if (countdownRemaining <= 0f)
        {
            BeginEscape();
        }
    }

    private void LateUpdate()
    {
        if (countdownText == null)
        {
            return;
        }

        countdownText.transform.position = transform.position + (Vector3)countdownOffset;
        countdownText.transform.rotation = Quaternion.identity;
    }

    protected override Vector2 CalculateDesiredVelocity(Transform player, float elapsed)
    {
        if (escaping)
        {
            return Vector2.up * escapeSpeed;
        }

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

        return direction * (currentSpeed / Mathf.Sqrt(distanceSquared));
    }

    public override bool TryTakeDamage(float damage)
    {
        if (!CanTakeDamage || IsInvincible || damage <= 0f)
        {
            return false;
        }

        PlayHitFeedback(Position);
        if (!damageIncreasesTimer || escaping)
        {
            return true;
        }

        damageTowardThreshold += damage;
        while (damageTowardThreshold >= nextDamageThreshold)
        {
            damageTowardThreshold -= nextDamageThreshold;
            countdownRemaining += secondsAddedPerThreshold;
            nextDamageThreshold *= damageThresholdMultiplier;
        }

        UpdateCountdownText();
        return true;
    }

    public override void OnReleasedFromCage()
    {
        cageableAgainTime = Time.time + uncageableDurationAfterRelease;
    }

    protected override void ResetEnemyState()
    {
        ResetBirdState();
    }

    private void ResetBirdState()
    {
        currentSpeed = 0f;
        countdownRemaining = Mathf.Max(0f, countdownDuration);
        damageTowardThreshold = 0f;
        nextDamageThreshold = Mathf.Max(0.01f, firstDamageThreshold);
        cageableAgainTime = 0f;
        escapeTimeRemaining = 0f;
        escaping = false;
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
        }
        UpdateCountdownText();
    }

    private void BeginEscape()
    {
        escaping = true;
        escapeTimeRemaining = Mathf.Max(0.01f, escapeDuration);
        currentSpeed = 0f;
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }
    }

    private void EnsureCountdownText()
    {
        if (countdownText == null)
        {
            countdownText = GetComponentInChildren<TextMeshPro>(true);
        }

        if (countdownText == null)
        {
            GameObject textObject = new GameObject("Bird Countdown");
            textObject.transform.SetParent(transform, false);
            countdownText = textObject.AddComponent<TextMeshPro>();
            countdownText.rectTransform.sizeDelta = new Vector2(20f, 5f);
            countdownText.transform.localScale = Vector3.one * 0.1f;
        }

        countdownText.alignment = TextAlignmentOptions.Center;
        countdownText.fontSize = Mathf.Max(1f, countdownFontSize);
        countdownText.textWrappingMode = TextWrappingModes.NoWrap;
        countdownText.sortingOrder = 100;
        countdownText.gameObject.SetActive(true);
    }

    private void UpdateCountdownText()
    {
        if (countdownText != null)
        {
            countdownText.text = Mathf.CeilToInt(countdownRemaining).ToString();
        }
    }
}
