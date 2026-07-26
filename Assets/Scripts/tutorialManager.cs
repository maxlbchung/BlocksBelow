using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Creates one world-space tutorial message at a time. Tutorial conditions are
/// zero-based: wave index 0 is the first wave configured on WaveSpawner.
/// </summary>
public class tutorialManager : MonoBehaviour
{
    public enum TextLifetime
    {
        UntilStateChanges,
        UntilWaveChanges,
        UntilStateOrWaveChanges,
        ForSeconds,
        Manual,
        UntilFightEnds
    }

    [Header("References")]
    [SerializeField] private WaveSpawner waveSpawner;
    [SerializeField, Tooltip("Optional styled world-space TextMesh prefab.")]
    private TextMesh textPrefab;
    [SerializeField, Tooltip("Camera the text faces. Main Camera is used when empty.")]
    private Camera facingCamera;

    [Header("World Text Defaults")]
    [SerializeField] private Font font;
    [SerializeField, Min(1)] private int fontSize = 64;
    [SerializeField, Min(0.001f)] private float characterSize = 0.08f;
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private TextAnchor anchor = TextAnchor.MiddleCenter;
    [SerializeField] private TextAlignment alignment = TextAlignment.Center;
    [SerializeField, Min(0f), Tooltip("Maximum world-space line width. Zero disables wrapping.")]
    private float lineWidth = 8f;
    [SerializeField, Tooltip("Keep tutorial text facing the camera.")]
    private bool faceCamera = true;
    [SerializeField, Tooltip("Reverse the camera-facing direction if the text appears backwards.")]
    private bool reverseFacingDirection;
    [SerializeField, Min(0f), Tooltip("Seconds used to fade every message and talking head in and out.")]
    private float messageFadeSeconds = 0.35f;

    [Header("Talking Head (All Messages)")]
    [SerializeField, Tooltip("Optional sprite displayed to the left of every tutorial message.")]
    private Sprite talkingHeadSprite;
    [SerializeField, Min(0f), Tooltip("Uniform world-space scale of the talking-head sprite.")]
    private float talkingHeadSize = 1f;
    [SerializeField, Min(0f), Tooltip("Empty space between the text's actual left edge and the sprite's right edge.")]
    private float talkingHeadDistanceFromText = 0.35f;
    [SerializeField, Tooltip("Additional local offset after positioning the sprite against the text's left edge.")]
    private Vector3 talkingHeadAdditionalOffset;
    [SerializeField, Range(0f, 180f), Tooltip("The head rotates between this positive angle and the matching negative angle.")]
    private float talkingHeadMaxAngle = 12f;
    [SerializeField, Min(0f), Tooltip("Rotation speed in degrees per second.")]
    private float talkingHeadAngleSpeed = 45f;
    [SerializeField, Tooltip("Sprite sorting layer used by the talking head.")]
    private string talkingHeadSortingLayer = "Default";
    [SerializeField, Tooltip("Sprite sorting order used by the talking head.")]
    private int talkingHeadSortingOrder = 1;

    [Header("Wave 0 - Delayed Help")]
    [SerializeField] private bool enableWaveZeroMessage = true;
    [SerializeField, Min(0)] private int delayedMessageWaveIndex = 0;
    [SerializeField, Min(0f), Tooltip("Seconds after the game scene starts before message one appears.")]
    private float delayedMessageSeconds = 10f;
    [SerializeField, TextArea(2, 5)] private string delayedMessage =
        "Still fighting? Use your towers to help defeat the remaining enemies.";
    [SerializeField, Tooltip("The fixed world location where this message appears.")]
    private Transform delayedMessageLocation;
    [SerializeField, Tooltip("Added to the location transform's world position.")]
    private Vector3 delayedMessageOffset;
    [SerializeField] private TextLifetime delayedMessageLifetime = TextLifetime.UntilStateChanges;
    [SerializeField, Tooltip("Keep this opening message visible when wave 0 starts, then remove it when that fight ends.")]
    private bool keepDelayedMessageThroughFight = true;
    [SerializeField, Min(0f)] private float delayedMessageDuration = 5f;
    [SerializeField, Min(0f), Tooltip("Zero uses the global Talking Head Size.")]
    private float delayedMessageTalkingHeadSize;
    [SerializeField, Min(0f), Tooltip("Zero uses the global/default text Character Size.")]
    private float delayedMessageTextSize;
    [SerializeField, Tooltip("Fine adjustment added only to this message's talking-head position.")]
    private Vector3 delayedMessageTalkingHeadOffset = new Vector3(0f, -0.05f, 0f);

    [Header("Building Before Wave 1 - First Message")]
    [SerializeField] private bool enableBuildingMessage = true;
    [SerializeField, Min(0), Tooltip("Upcoming zero-based wave index. 1 means the build phase before the second fight.")]
    private int buildingMessageUpcomingWaveIndex = 1;
    [SerializeField, TextArea(2, 5)] private string buildingIntroMessage =
        "You have time to prepare before the next wave.";
    [SerializeField, Tooltip("Fixed world location of the first building message.")]
    private Transform buildingIntroMessageLocation;
    [SerializeField, Tooltip("World-space offset from the first message location.")]
    private Vector3 buildingIntroMessageOffset;
    [SerializeField, Min(0f), Tooltip("Seconds the first message remains before the ghost-tower message begins.")]
    private float buildingIntroMessageSeconds = 4f;
    [SerializeField, Min(0f), Tooltip("Extra pause after the first message has completely faded out before showing the ghost message.")]
    private float buildingGhostMessageDelay = 0.15f;
    [SerializeField, Min(0f), Tooltip("Zero uses the global Talking Head Size.")]
    private float buildingIntroTalkingHeadSize;
    [SerializeField, Min(0f), Tooltip("Zero uses the global/default text Character Size.")]
    private float buildingIntroTextSize;
    [SerializeField, Tooltip("Fine adjustment added only to this message's talking-head position.")]
    private Vector3 buildingIntroTalkingHeadOffset = new Vector3(0f, -0.05f, 0f);

    [Header("Building Before Wave 1 - Ghost Defense Message")]
    [SerializeField, TextArea(2, 5)] private string buildingMessage =
        "Build and upgrade your defenses before starting the next wave.";
    [SerializeField, Tooltip("Activated while this tutorial message is visible. The text follows this object's transform.")]
    private GameObject buildingGhostTower;
    [SerializeField, Tooltip("World-space text offset from the ghost tower.")]
    private Vector3 buildingMessageGhostOffset = new Vector3(0f, 1f, 0f);
    [SerializeField, Tooltip("Make sure the ghost is hidden when the scene begins.")]
    private bool deactivateGhostTowerOnStart = true;
    [SerializeField] private TextLifetime buildingMessageLifetime = TextLifetime.UntilStateChanges;
    [SerializeField, Min(0f)] private float buildingMessageDuration = 5f;
    [SerializeField, Min(0f), Tooltip("Zero uses the global Talking Head Size.")]
    private float buildingGhostTalkingHeadSize;
    [SerializeField, Min(0f), Tooltip("Zero uses the global/default text Character Size.")]
    private float buildingGhostTextSize;
    [SerializeField, Tooltip("Fine adjustment added only to this message's talking-head position.")]
    private Vector3 buildingGhostTalkingHeadOffset = new Vector3(0f, 0.15f, 0f);

    [Header("Building Before Wave 3 - Cage Breaker Warning")]
    [SerializeField] private bool enableCageBreakerMessage = true;
    [SerializeField, Min(0), Tooltip("Upcoming zero-based wave index. 3 means the build phase before wave index 3.")]
    private int cageBreakerMessageWaveIndex = 3;
    [SerializeField, TextArea(2, 5)] private string cageBreakerMessage =
        "Cage Breakers target captured enemies. Stop them before they reach a cage!";
    [SerializeField, Tooltip("Fixed world location where the warning appears during the build phase.")]
    private Transform cageBreakerMessageLocation;
    [SerializeField, Tooltip("World offset from the assigned warning location.")]
    private Vector3 cageBreakerMessageOffset = new Vector3(1.5f, 1f, 0f);
    [SerializeField, Min(0f), Tooltip("Zero uses the global Talking Head Size.")]
    private float cageBreakerTalkingHeadSize;
    [SerializeField, Min(0f), Tooltip("Zero uses the global/default text Character Size.")]
    private float cageBreakerTextSize;
    [SerializeField, Tooltip("Fine adjustment added only to this message's talking-head position.")]
    private Vector3 cageBreakerTalkingHeadOffset = new Vector3(0f, 0.1f, 0f);

    private readonly HashSet<string> shownMessageIds = new HashSet<string>();
    private TextMesh currentText;
    private SpriteRenderer currentTalkingHeadRenderer;
    private Transform currentTalkingHead;
    private Transform followedTarget;
    private Vector3 followedOffset;
    private GameObject activeMessageObject;
    private TextLifetime currentLifetime;
    private WaveSpawner.GameState stateWhenShown;
    private int waveWhenShown;
    private float hideAtTime;
    private bool currentTextHasEnteredFight;
    private Color currentTextFullColor;
    private Color currentHeadFullColor;
    private float fadeStartedAt;
    private bool fadingOut;
    private WaveSpawner.GameState previousState;
    private float stateStartedAt;
    private float gameStartedAt;
    private bool firstFightEnded;
    private bool buildingIntroStarted;
    private float buildingIntroEndsAt;

    public WaveSpawner.GameState CurrentState =>
        waveSpawner != null ? waveSpawner.gameState : WaveSpawner.GameState.Building;
    public int CurrentWaveIndex => waveSpawner != null ? waveSpawner.CurrentWaveIndex : -1;
    public int CurrentWaveNumber => CurrentWaveIndex + 1;
    public int NextWaveIndex => CurrentWaveIndex + 1;
    public int TotalWaves => waveSpawner != null ? waveSpawner.TotalRounds : 0;
    public bool IsBuilding => CurrentState == WaveSpawner.GameState.Building;
    public bool IsFighting => CurrentState == WaveSpawner.GameState.Wave;
    public float SecondsInCurrentState => Time.time - stateStartedAt;
    public bool HasText => currentText != null;

    private void Awake()
    {
        ResolveReferences();

        if (deactivateGhostTowerOnStart && buildingGhostTower != null)
        {
            buildingGhostTower.SetActive(false);
        }

        previousState = CurrentState;
        stateStartedAt = Time.time;
        gameStartedAt = Time.time;
    }

    private void Update()
    {
        ResolveReferences();
        UpdateStateTimer();
        HideTextWhenItsLifetimeEnds();
        UpdateMessageFade();
        RunTutorial();
    }

    private void LateUpdate()
    {
        if (currentText == null)
        {
            return;
        }

        if (followedTarget != null)
        {
            currentText.transform.position = followedTarget.position + followedOffset;
        }

        FaceTextTowardCamera();
        AnimateTalkingHead();
    }

    private void RunTutorial()
    {
        // If the first fight is still running after the configured delay, display
        // help at the location assigned in the Inspector.

        

        if (enableWaveZeroMessage
            && !firstFightEnded
            && CurrentWaveIndex <= delayedMessageWaveIndex
            && Time.time - gameStartedAt >= delayedMessageSeconds
            && delayedMessageLocation != null)
        {
            ShowTextOnce(
                "delayed-wave-help-" + delayedMessageWaveIndex,
                delayedMessage,
                delayedMessageLocation.position + delayedMessageOffset,
                keepDelayedMessageThroughFight
                    ? TextLifetime.UntilFightEnds
                    : delayedMessageLifetime,
                delayedMessageDuration,
                delayedMessageTalkingHeadSize,
                delayedMessageTextSize,
                delayedMessageTalkingHeadOffset);
        }

        // The build tutorial is a two-message sequence. The first message stays at
        // a scene transform for a configured time. Only after it finishes does the
        // ghost tower appear with the second message attached to it.
        if (enableBuildingMessage
            && IsBuilding
            && NextWaveIndex == buildingMessageUpcomingWaveIndex
            && buildingIntroMessageLocation != null)
        {
            if (!buildingIntroStarted)
            {
                bool introWasShown = ShowTextOnce(
                    "building-intro-before-wave-" + buildingMessageUpcomingWaveIndex,
                    buildingIntroMessage,
                    buildingIntroMessageLocation.position + buildingIntroMessageOffset,
                    TextLifetime.ForSeconds,
                    buildingIntroMessageSeconds,
                    buildingIntroTalkingHeadSize,
                    buildingIntroTextSize,
                    buildingIntroTalkingHeadOffset);

                if (introWasShown)
                {
                    buildingIntroStarted = true;
                    buildingIntroEndsAt =
                        Time.time
                        + buildingIntroMessageSeconds
                        + messageFadeSeconds
                        + buildingGhostMessageDelay;
                }
            }
            else if (Time.time >= buildingIntroEndsAt
                && currentText == null
                && buildingGhostTower != null)
            {
                ShowFollowingTextAndObjectOnce(
                    "building-defense-before-wave-" + buildingMessageUpcomingWaveIndex,
                    buildingMessage,
                    buildingGhostTower,
                    buildingMessageGhostOffset,
                    buildingMessageLifetime,
                    buildingMessageDuration,
                    buildingGhostTalkingHeadSize,
                    buildingGhostTextSize,
                    buildingGhostTalkingHeadOffset);
            }
        }

        // Show the Cage Breaker warning at a fixed scene location throughout the
        // build phase before wave 3. Starting the fight changes state and removes it.
        if (enableCageBreakerMessage
            && IsBuilding
            && NextWaveIndex == cageBreakerMessageWaveIndex
            && cageBreakerMessageLocation != null)
        {
            ShowTextOnce(
                "cage-breaker-build-before-wave-" + cageBreakerMessageWaveIndex,
                cageBreakerMessage,
                cageBreakerMessageLocation.position + cageBreakerMessageOffset,
                TextLifetime.UntilStateChanges,
                0f,
                cageBreakerTalkingHeadSize,
                cageBreakerTextSize,
                cageBreakerTalkingHeadOffset);
        }
    }

    public bool ShowTextOnce(
        string messageId,
        string message,
        Vector3 worldPosition,
        TextLifetime lifetime = TextLifetime.UntilStateChanges,
        float seconds = 0f,
        float headSizeOverride = 0f,
        float textSizeOverride = 0f,
        Vector3 messageHeadOffset = default)
    {
        if (currentText != null || !ReserveMessageId(messageId))
        {
            return false;
        }

        ShowText(
            message,
            worldPosition,
            lifetime,
            seconds,
            headSizeOverride,
            textSizeOverride,
            messageHeadOffset);
        return true;
    }

    public bool ShowFollowingTextOnce(
        string messageId,
        string message,
        Transform target,
        Vector3 worldOffset,
        TextLifetime lifetime = TextLifetime.UntilStateChanges,
        float seconds = 0f,
        float headSizeOverride = 0f,
        float textSizeOverride = 0f,
        Vector3 messageHeadOffset = default)
    {
        if (currentText != null || target == null || !ReserveMessageId(messageId))
        {
            return false;
        }

        ShowFollowingText(
            message,
            target,
            worldOffset,
            lifetime,
            seconds,
            headSizeOverride,
            textSizeOverride,
            messageHeadOffset);
        return true;
    }

    /// <summary>
    /// Activates an object, follows its transform with the message, and deactivates
    /// the object whenever the message is hidden or replaced.
    /// </summary>
    public bool ShowFollowingTextAndObjectOnce(
        string messageId,
        string message,
        GameObject targetObject,
        Vector3 worldOffset,
        TextLifetime lifetime = TextLifetime.UntilStateChanges,
        float seconds = 0f,
        float headSizeOverride = 0f,
        float textSizeOverride = 0f,
        Vector3 messageHeadOffset = default)
    {
        if (currentText != null || targetObject == null || !ReserveMessageId(messageId))
        {
            return false;
        }

        targetObject.SetActive(true);
        ShowFollowingText(
            message,
            targetObject.transform,
            worldOffset,
            lifetime,
            seconds,
            headSizeOverride,
            textSizeOverride,
            messageHeadOffset);
        activeMessageObject = targetObject;
        return true;
    }

    /// <summary>Replaces the active message at a fixed world position.</summary>
    public void ShowText(
        string message,
        Vector3 worldPosition,
        TextLifetime lifetime = TextLifetime.UntilStateChanges,
        float seconds = 0f,
        float headSizeOverride = 0f,
        float textSizeOverride = 0f,
        Vector3 messageHeadOffset = default)
    {
        CreateCurrentText(
            message,
            worldPosition,
            lifetime,
            seconds,
            headSizeOverride,
            textSizeOverride,
            messageHeadOffset);
        followedTarget = null;
    }

    /// <summary>Replaces the active message and keeps it attached to a transform.</summary>
    public void ShowFollowingText(
        string message,
        Transform target,
        Vector3 worldOffset,
        TextLifetime lifetime = TextLifetime.UntilStateChanges,
        float seconds = 0f,
        float headSizeOverride = 0f,
        float textSizeOverride = 0f,
        Vector3 messageHeadOffset = default)
    {
        if (target == null)
        {
            return;
        }

        followedTarget = target;
        followedOffset = worldOffset;
        CreateCurrentText(
            message,
            target.position + worldOffset,
            lifetime,
            seconds,
            headSizeOverride,
            textSizeOverride,
            messageHeadOffset);
        // CreateCurrentText calls HideText, which clears the followed target.
        followedTarget = target;
        followedOffset = worldOffset;
    }

    public void HideText()
    {
        BeginFadeOut();
    }

    private void BeginFadeOut()
    {
        if (currentText == null || fadingOut)
        {
            return;
        }

        if (messageFadeSeconds <= 0f)
        {
            DestroyCurrentTextImmediately();
            return;
        }

        fadingOut = true;
        fadeStartedAt = Time.time;
    }

    private void DestroyCurrentTextImmediately()
    {
        if (currentText != null)
        {
            Destroy(currentText.gameObject);
            currentText = null;
        }

        currentTalkingHead = null;
        currentTalkingHeadRenderer = null;
        followedTarget = null;
        fadingOut = false;

        if (activeMessageObject != null)
        {
            activeMessageObject.SetActive(false);
            activeMessageObject = null;
        }
    }

    public void ResetMessage(string messageId)
    {
        shownMessageIds.Remove(messageId);
    }

    public void ResetAllMessages()
    {
        shownMessageIds.Clear();
        buildingIntroStarted = false;
        buildingIntroEndsAt = 0f;
        HideText();
    }

    private void CreateCurrentText(
        string message,
        Vector3 worldPosition,
        TextLifetime lifetime,
        float seconds,
        float headSizeOverride,
        float textSizeOverride,
        Vector3 messageHeadOffset)
    {
        DestroyCurrentTextImmediately();

        currentText = textPrefab != null
            ? Instantiate(textPrefab)
            : CreateGeneratedText();

        currentText.gameObject.name = "Current Tutorial Text";
        currentText.gameObject.SetActive(true);
        if (textSizeOverride > 0f)
        {
            currentText.characterSize = textSizeOverride;
        }
        currentText.text = message;
        currentText.transform.position = worldPosition;
        CreateTalkingHead(headSizeOverride, messageHeadOffset);
        currentTextFullColor = currentText.color;
        SetMessageAlpha(messageFadeSeconds > 0f ? 0f : 1f);

        currentLifetime = lifetime;
        stateWhenShown = CurrentState;
        waveWhenShown = CurrentWaveIndex;
        hideAtTime = Time.time + Mathf.Max(0f, seconds);
        currentTextHasEnteredFight = IsFighting;
        fadeStartedAt = Time.time;
        fadingOut = false;
        FaceTextTowardCamera();
    }

    private TextMesh CreateGeneratedText()
    {
        GameObject textObject = new GameObject("Current Tutorial Text", typeof(TextMesh));
        TextMesh generatedText = textObject.GetComponent<TextMesh>();
        generatedText.font = font;
        generatedText.fontSize = fontSize;
        generatedText.characterSize = characterSize;
        generatedText.color = textColor;
        generatedText.anchor = anchor;
        generatedText.alignment = alignment;
        generatedText.lineSpacing = 1f;
        generatedText.tabSize = 4;

        if (lineWidth > 0f)
        {
            generatedText.anchor = anchor;
            generatedText.richText = true;
        }

        MeshRenderer renderer = generatedText.GetComponent<MeshRenderer>();
        if (font != null && renderer != null)
        {
            renderer.sharedMaterial = font.material;
        }

        return generatedText;
    }

    private void CreateTalkingHead(float headSizeOverride, Vector3 messageHeadOffset)
    {
        currentTalkingHead = null;
        if (talkingHeadSprite == null || currentText == null)
        {
            return;
        }

        GameObject headObject = new GameObject(
            "Tutorial Talking Head",
            typeof(SpriteRenderer));
        headObject.transform.SetParent(currentText.transform, false);

        SpriteRenderer spriteRenderer = headObject.GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = talkingHeadSprite;
        spriteRenderer.sortingLayerName = talkingHeadSortingLayer;
        spriteRenderer.sortingOrder = talkingHeadSortingOrder;
        currentHeadFullColor = spriteRenderer.color;

        float selectedSize = headSizeOverride > 0f ? headSizeOverride : talkingHeadSize;
        headObject.transform.localScale = Vector3.one * selectedSize;

        float textLeftEdge = 0f;
        MeshFilter textMeshFilter = currentText.GetComponent<MeshFilter>();
        if (textMeshFilter != null && textMeshFilter.sharedMesh != null)
        {
            textLeftEdge = textMeshFilter.sharedMesh.bounds.min.x;
        }

        float spriteHalfWidth = talkingHeadSprite.bounds.extents.x * selectedSize;
        headObject.transform.localPosition = new Vector3(
            textLeftEdge - talkingHeadDistanceFromText - spriteHalfWidth,
            0f,
            0f) + talkingHeadAdditionalOffset + messageHeadOffset;
        currentTalkingHead = headObject.transform;
        currentTalkingHeadRenderer = spriteRenderer;
    }

    private void AnimateTalkingHead()
    {
        if (currentTalkingHead == null)
        {
            return;
        }

        float maximum = Mathf.Abs(talkingHeadMaxAngle);
        float angle = maximum <= 0f
            ? 0f
            : Mathf.PingPong(Time.time * talkingHeadAngleSpeed, maximum * 2f) - maximum;
        currentTalkingHead.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    private void UpdateMessageFade()
    {
        if (currentText == null)
        {
            return;
        }

        if (messageFadeSeconds <= 0f)
        {
            SetMessageAlpha(1f);
            if (fadingOut)
            {
                DestroyCurrentTextImmediately();
            }
            return;
        }

        float progress = Mathf.Clamp01((Time.time - fadeStartedAt) / messageFadeSeconds);
        SetMessageAlpha(fadingOut ? 1f - progress : progress);

        if (fadingOut && progress >= 1f)
        {
            DestroyCurrentTextImmediately();
        }
    }

    private void SetMessageAlpha(float alpha)
    {
        if (currentText != null)
        {
            Color textFadeColor = currentTextFullColor;
            textFadeColor.a *= alpha;
            currentText.color = textFadeColor;
        }

        if (currentTalkingHeadRenderer != null)
        {
            Color headFadeColor = currentHeadFullColor;
            headFadeColor.a *= alpha;
            currentTalkingHeadRenderer.color = headFadeColor;
        }
    }

    private bool ReserveMessageId(string messageId)
    {
        return !string.IsNullOrEmpty(messageId) && shownMessageIds.Add(messageId);
    }

    private void HideTextWhenItsLifetimeEnds()
    {
        if (currentText == null)
        {
            return;
        }

        bool stateChanged = CurrentState != stateWhenShown;
        bool waveChanged = CurrentWaveIndex != waveWhenShown;
        if (IsFighting)
        {
            currentTextHasEnteredFight = true;
        }

        bool shouldHide =
            (currentLifetime == TextLifetime.UntilStateChanges && stateChanged)
            || (currentLifetime == TextLifetime.UntilWaveChanges && waveChanged)
            || (currentLifetime == TextLifetime.UntilStateOrWaveChanges && (stateChanged || waveChanged))
            || (currentLifetime == TextLifetime.ForSeconds && Time.time >= hideAtTime)
            || (currentLifetime == TextLifetime.UntilFightEnds
                && currentTextHasEnteredFight
                && IsBuilding);

        if (shouldHide)
        {
            BeginFadeOut();
        }
    }

    private void FaceTextTowardCamera()
    {
        if (!faceCamera || currentText == null)
        {
            return;
        }

        if (facingCamera == null)
        {
            facingCamera = Camera.main;
        }

        if (facingCamera == null)
        {
            return;
        }

        Vector3 direction = reverseFacingDirection
            ? currentText.transform.position - facingCamera.transform.position
            : facingCamera.transform.position - currentText.transform.position;

        if (direction.sqrMagnitude > 0.0001f)
        {
            currentText.transform.rotation = Quaternion.LookRotation(direction);
        }
    }

    private void UpdateStateTimer()
    {
        if (CurrentState == previousState)
        {
            return;
        }

        if (previousState == WaveSpawner.GameState.Wave
            && CurrentState == WaveSpawner.GameState.Building
            && CurrentWaveIndex == delayedMessageWaveIndex)
        {
            firstFightEnded = true;
        }

        previousState = CurrentState;
        stateStartedAt = Time.time;
    }

    private void ResolveReferences()
    {
        if (waveSpawner == null)
        {
            waveSpawner = FindFirstObjectByType<WaveSpawner>();
        }

        if (facingCamera == null)
        {
            facingCamera = Camera.main;
        }
    }

    private void OnDestroy()
    {
        DestroyCurrentTextImmediately();
    }
}
