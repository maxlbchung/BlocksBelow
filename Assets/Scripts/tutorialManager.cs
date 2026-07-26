using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

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
    [SerializeField, Tooltip("Sorting layer used by the tutorial text so it draws over the scene.")]
    private string textSortingLayer = "Foreground";
    [SerializeField, Tooltip("Sorting order used by the tutorial text within its sorting layer.")]
    private int textSortingOrder = 100;

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

    [Header("Opening Lore Card")]
    [SerializeField, Tooltip("Open on a lore card. The first round is held back until the player clicks through it.")]
    private bool enableOpeningMessage = true;
    // Named apart from the fields this replaced on purpose. Unity restores a serialized
    // field's live value across a recompile, so an edit to the initializer alone never
    // reaches a component that already exists in an open scene; a new name is a new field.
    [SerializeField, TextArea(4, 12)] private string openingCardMessage =
        "The monsters took everything.\n"
        + "This island is the final unconquered land,\n"
        + "the last stand...\n\n"
        + "Our towers are dead metal without power,\n"
        + "and the only power left in this world\n"
        + "is carried on the wings of the birds.";
    [SerializeField] private string openingButtonLabel = "Next";
    [SerializeField, Min(0f), Tooltip("Seconds after the scene loads before the card appears.")]
    private float openingMessageDelay = 0.5f;
    [SerializeField, Tooltip("Where the card appears. Falls back to the Wave 0 message location, then to this object.")]
    private Transform openingMessageLocation;
    [SerializeField, Tooltip("Extra nudge on top of the card's placement. Moves the card and its "
        + "talking head together, since the head is parented to the text.")]
    private Vector3 openingCardOffset;
    [SerializeField, Min(0f), Tooltip("Zero uses the global Talking Head Size.")]
    private float openingTalkingHeadSize;
    [SerializeField, Min(0f), Tooltip("Zero uses the global/default text Character Size.")]
    private float openingTextSize;
    [SerializeField, Tooltip("Fine adjustment added only to this message's talking-head position.")]
    private Vector3 openingTalkingHeadOffset = new Vector3(-3f, 0f, 0f);
    [SerializeField, Min(0f), Tooltip("Seconds message one is on screen before the round that fields the birds is released, so the player is told what to do before there is anything doing it.")]
    private float birdSpawnLeadSeconds = 2.5f;

    [Header("Wave 0 - Delayed Help")]
    [SerializeField] private bool enableWaveZeroMessage = true;
    [SerializeField, Min(0)] private int delayedMessageWaveIndex = 0;
    [SerializeField, Min(0f), Tooltip("Seconds before message one appears, measured from the scene "
        + "starting - or from the opening card being clicked through, when one is used.")]
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
    [SerializeField, Min(0f), Tooltip("Seconds the first message remains before the ghost-tower message begins. Ignored while the Continue button is used.")]
    private float buildingIntroMessageSeconds = 4f;
    [SerializeField, Min(0f), Tooltip("Extra pause after the first message has completely faded out before showing the ghost message.")]
    private float buildingGhostMessageDelay = 0.15f;
    [SerializeField, Tooltip("Hold this message back while the first-capture flourish (arrow tally and energy climb) is still playing.")]
    private bool waitForFirstCaptureCinematic = true;
    [SerializeField, Min(0f), Tooltip("Extra pause after that flourish has finished before this message appears.")]
    private float buildingIntroCinematicDelay = 0.5f;
    [SerializeField, Min(0f), Tooltip("Zero uses the global Talking Head Size.")]
    private float buildingIntroTalkingHeadSize;
    [SerializeField, Min(0f), Tooltip("Zero uses the global/default text Character Size.")]
    private float buildingIntroTextSize;
    [SerializeField, Tooltip("Fine adjustment added only to this message's talking-head position.")]
    private Vector3 buildingIntroTalkingHeadOffset = new Vector3(0f, -0.05f, 0f);

    [Header("Building Before Wave 1 - Continue Button")]
    [SerializeField, Tooltip("Hold the first message until the player clicks Continue instead of dismissing it on a timer.")]
    private bool useContinueButton = true;
    [SerializeField] private string continueButtonLabel = "Continue";
    [SerializeField, Tooltip("Button size in 1920x1080 reference pixels.")]
    private Vector2 continueButtonSize = new Vector2(240f, 62f);
    [SerializeField, Tooltip("Reference-pixel offset from the bottom centre of the message.")]
    private Vector2 continueButtonOffset = new Vector2(0f, -28f);
    [SerializeField, Min(1)] private int continueButtonFontSize = 28;
    [SerializeField] private Color continueButtonOutlineColor = new Color(0.78f, 0.88f, 1f, 0.85f);
    [SerializeField] private Color continueButtonTextColor = new Color(0.9f, 0.94f, 1f, 1f);
    [SerializeField, Min(0.5f)] private float continueButtonOutlineThickness = 3f;

    [Header("Building Before Wave 1 - Shop Arrows")]
    [SerializeField, Tooltip("Point at the Round tab, then at Start Round, once enough towers are down.")]
    private bool enableShopArrows = true;
    [SerializeField, Min(1), Tooltip("Towers the player must place before the first arrow appears.")]
    private int arrowsAfterTowersPlaced = 3;
    [SerializeField, Min(0), Tooltip("Upcoming zero-based wave index the arrows are shown in.")]
    private int arrowsUpcomingWaveIndex = 1;
    [SerializeField, Min(1f), Tooltip("Arrow width in 1920x1080 reference pixels.")]
    private float arrowWidth = 90f;
    [SerializeField, Min(0f), Tooltip("Gap between the button's right edge and the arrow's tip, at the near end of its travel.")]
    private float arrowGap = 14f;
    [SerializeField, Min(0f), Tooltip("How far the arrow slides back and forth, in reference pixels.")]
    private float arrowTravel = 22f;
    [SerializeField, Min(0f), Tooltip("Back-and-forth cycles per second.")]
    private float arrowBobSpeed = 0.9f;
    [SerializeField] private Color arrowColor = new Color(0.55f, 1f, 0.65f, 1f);

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

    /// <summary>
    /// How far left of the one-line messages' anchor the opening card sits when it has no
    /// location of its own. It is a wide block of prose where they are a single sentence, so
    /// sharing their anchor pushes it off the right of the screen.
    /// </summary>
    private const float OpeningCardFallbackShift = 2.5f;

    /// <summary>What clicking the shared Continue button should move on to.</summary>
    private enum ContinueAction
    {
        None,
        OpeningLore,
        BuildingIntro
    }

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
    private float buildingIntroBlockedUntil;
    private Canvas continueCanvas;
    private Canvas arrowCanvas;
    private CanvasGroup continueCanvasGroup;
    private RectTransform continueButtonRect;
    private Text continueButtonText;
    private bool continueButtonActive;
    private ContinueAction continueAction;
    private bool openingMessagePending;
    private float delayedMessageReadyAt;
    private bool firstWaveReleasePending;
    private float firstWaveReleaseAt;
    private RectTransform arrowRect;
    private Image arrowImage;
    private TowerShopUI towerShop;
    private RectTransform arrowTarget;
    private float arrowShownAt;
    private readonly Vector3[] arrowTargetCorners = new Vector3[4];

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
        delayedMessageReadyAt = Time.time + delayedMessageSeconds;

        // Taken in Awake because the spawner starts its opening round from Start, and Unity
        // runs every Awake before the first of those.
        if (enableOpeningMessage)
        {
            openingMessagePending = true;
            if (waveSpawner != null)
            {
                waveSpawner.HoldFirstWave();
            }
        }
    }

    private void Update()
    {
        ResolveReferences();
        UpdateStateTimer();
        UpdateCinematicGate();
        ReleaseFirstWaveWhenDue();
        HideTextWhenItsLifetimeEnds();
        UpdateMessageFade();
        RunTutorial();
    }

    private void LateUpdate()
    {
        // Placed after the shop's own layout has settled for the frame, and run whether or
        // not a message is up - the arrows outlast the tutorial text.
        UpdateShopArrow();

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
        UpdateContinueButtonPosition();
    }

    private void RunTutorial()
    {
        // If the first fight is still running after the configured delay, display
        // help at the location assigned in the Inspector.

        

        // The card that opens the run. Nothing else can be reached until it is clicked
        // through: the round it releases is what the message after it is about.
        if (enableOpeningMessage
            && openingMessagePending
            && currentText == null
            && Time.time - gameStartedAt >= openingMessageDelay)
        {
            // The shift is applied here rather than left to the serialized offset because
            // Unity keeps a component's existing field values across a recompile: an edit to
            // a default never reaches a scene object that already has one. A location set in
            // the Inspector is taken as deliberate and moves nothing.
            Vector3 openingPosition;
            if (openingMessageLocation != null)
            {
                openingPosition = openingMessageLocation.position;
            }
            else
            {
                Transform fallback = delayedMessageLocation != null
                    ? delayedMessageLocation
                    : transform;
                openingPosition = fallback.position + Vector3.left * OpeningCardFallbackShift;
            }

            if (ShowTextOnce(
                "opening-lore",
                openingCardMessage,
                openingPosition + openingCardOffset,
                TextLifetime.Manual,
                0f,
                openingTalkingHeadSize,
                openingTextSize,
                openingTalkingHeadOffset))
            {
                ShowContinueButton(openingButtonLabel, ContinueAction.OpeningLore);
            }
        }

        if (enableWaveZeroMessage
            && !openingMessagePending
            && !firstFightEnded
            && CurrentWaveIndex <= delayedMessageWaveIndex
            && Time.time >= delayedMessageReadyAt
            && delayedMessageLocation != null)
        {
            bool helpWasShown = ShowTextOnce(
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

            // The instruction is up, so the birds it is about are now free to fly in behind
            // it - timed from here rather than from the click, which is what puts the words
            // on screen first.
            if (helpWasShown && firstWaveReleasePending)
            {
                firstWaveReleaseAt = Time.time + birdSpawnLeadSeconds;
            }
        }

        // The build tutorial is a two-message sequence. The first message stays at
        // a scene transform for a configured time. Only after it finishes does the
        // ghost tower appear with the second message attached to it.
        // The gate only holds the opening message back. Once the sequence has begun the
        // flourish is long over, so the ghost message is never delayed by it.
        if (enableBuildingMessage
            && IsBuilding
            && NextWaveIndex == buildingMessageUpcomingWaveIndex
            && buildingIntroMessageLocation != null
            && (buildingIntroStarted || Time.time >= buildingIntroBlockedUntil))
        {
            if (!buildingIntroStarted)
            {
                bool introWasShown = ShowTextOnce(
                    "building-intro-before-wave-" + buildingMessageUpcomingWaveIndex,
                    buildingIntroMessage,
                    buildingIntroMessageLocation.position + buildingIntroMessageOffset,
                    // Read at the player's own pace when the button is up, so nothing
                    // clears it but the click or the round starting.
                    useContinueButton ? TextLifetime.UntilStateChanges : TextLifetime.ForSeconds,
                    buildingIntroMessageSeconds,
                    buildingIntroTalkingHeadSize,
                    buildingIntroTextSize,
                    buildingIntroTalkingHeadOffset);

                if (introWasShown)
                {
                    buildingIntroStarted = true;

                    if (useContinueButton)
                    {
                        // The click sets the real time; until then the ghost message has
                        // no schedule to run to.
                        buildingIntroEndsAt = float.PositiveInfinity;
                        ShowContinueButton(continueButtonLabel, ContinueAction.BuildingIntro);
                    }
                    else
                    {
                        buildingIntroEndsAt =
                            Time.time
                            + buildingIntroMessageSeconds
                            + messageFadeSeconds
                            + buildingGhostMessageDelay;
                    }
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
        // The button belongs to a message, so it never outlives one - including a message
        // cut short by the round starting rather than by the click.
        HideContinueButton();

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
        buildingIntroBlockedUntil = 0f;
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
        ApplyTextSortingLayer();
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

    /// <summary>
    /// A TextMesh renders through a MeshRenderer, which sits on the Default sorting
    /// layer at order 0 unless it is told otherwise, so scene sprites cover it.
    /// </summary>
    private void ApplyTextSortingLayer()
    {
        if (currentText == null || string.IsNullOrEmpty(textSortingLayer))
        {
            return;
        }

        MeshRenderer textRenderer = currentText.GetComponent<MeshRenderer>();
        if (textRenderer == null)
        {
            return;
        }

        textRenderer.sortingLayerName = textSortingLayer;
        textRenderer.sortingOrder = textSortingOrder;
    }

    /// <summary>
    /// Puts the button under the message and arms it. The canvas is built on the first
    /// message that asks for one and then kept, hidden, for any later message.
    /// </summary>
    private void ShowContinueButton(string label, ContinueAction action)
    {
        EnsureContinueCanvas();
        if (continueButtonRect == null)
        {
            return;
        }

        continueAction = action;
        continueButtonText.text = label;
        continueButtonActive = true;
        continueCanvas.gameObject.SetActive(true);

        // Starts closed and opens on the message's own fade curve, so the two arrive
        // together - but it takes clicks from the first frame. Gating the raycast on the
        // fade as well only creates a window where the button looks ready and is not.
        continueCanvasGroup.alpha = messageFadeSeconds > 0f ? 0f : 1f;
        continueCanvasGroup.blocksRaycasts = true;
        continueCanvasGroup.interactable = true;
        UpdateContinueButtonPosition();
    }

    private void HideContinueButton()
    {
        continueButtonActive = false;
        continueAction = ContinueAction.None;

        if (continueCanvas != null)
        {
            continueCanvasGroup.blocksRaycasts = false;
            continueCanvasGroup.interactable = false;
            continueCanvas.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Dismisses the message the button belongs to and starts whatever that message was
    /// holding up, timed from the click rather than from a clock the player cannot see.
    /// </summary>
    private void OnContinueClicked()
    {
        if (!continueButtonActive || currentText == null || fadingOut)
        {
            return;
        }

        switch (continueAction)
        {
            case ContinueAction.OpeningLore:
                openingMessagePending = false;

                // Cleared rather than re-timed, so the message about the birds goes up the
                // moment the card has faded out - ShowTextOnce will not run until it has.
                delayedMessageReadyAt = Time.time;

                // The round the card was holding back is what fields the birds, and it is
                // armed rather than released: the instruction has to be on screen before
                // there is anything on screen to follow it. This is the backstop time, in
                // case that message never appears; showing it moves the release in.
                firstWaveReleasePending = true;
                firstWaveReleaseAt = Time.time + messageFadeSeconds * 2f + birdSpawnLeadSeconds;
                break;

            case ContinueAction.BuildingIntro:
                buildingIntroEndsAt = Time.time + messageFadeSeconds + buildingGhostMessageDelay;
                break;
        }

        continueAction = ContinueAction.None;
        HideText();
    }

    /// <summary>
    /// The message is a world-space TextMesh, so the button is placed by projecting the
    /// bottom of the text onto the canvas each frame. Reading the renderer's bounds rather
    /// than the transform keeps the gap even however many lines the message runs to.
    /// </summary>
    private void UpdateContinueButtonPosition()
    {
        if (!continueButtonActive || continueButtonRect == null || currentText == null)
        {
            return;
        }

        Camera camera = facingCamera != null ? facingCamera : Camera.main;
        if (camera == null)
        {
            return;
        }

        Renderer textRenderer = currentText.GetComponent<Renderer>();
        Vector3 anchorPoint = textRenderer != null
            ? new Vector3(
                textRenderer.bounds.center.x,
                textRenderer.bounds.min.y,
                currentText.transform.position.z)
            : currentText.transform.position;

        Vector3 screenPoint = camera.WorldToScreenPoint(anchorPoint);
        continueButtonRect.anchoredPosition =
            ToCanvasPoint(continueCanvas, screenPoint) + continueButtonOffset;
    }

    /// <summary>
    /// Screen pixels to a position on <paramref name="canvas"/>. Everything the tutorial
    /// places is anchored to its canvas's bottom-left corner, so a projected point can be
    /// used as the position directly once the scaler's factor is taken back out.
    /// </summary>
    private static Vector2 ToCanvasPoint(Canvas canvas, Vector3 screenPoint)
    {
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        return new Vector2(screenPoint.x / scale, screenPoint.y / scale);
    }

    private static Canvas CreateOverlayCanvas(string objectName, Transform parent, bool interactive)
    {
        GameObject canvasObject = new GameObject(
            objectName,
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(CanvasGroup));
        canvasObject.transform.SetParent(parent, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the tower shop at 100, below the give-up prompt at 400.
        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;

        // Only the button takes clicks. The arrow canvas is left without a raycaster at all
        // so it cannot intercept one meant for the shop it is pointing at.
        if (interactive)
        {
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        return canvas;
    }

    /// <summary>
    /// The button's own canvas, kept apart from the arrows'. They are shown at different
    /// times and for different reasons, and a fault building one must not be able to leave
    /// the other half-built.
    /// </summary>
    private void EnsureContinueCanvas()
    {
        if (continueCanvas != null)
        {
            return;
        }

        EnsureEventSystem();
        continueCanvas = CreateOverlayCanvas("Tutorial Continue Canvas", transform, true);
        continueCanvasGroup = continueCanvas.GetComponent<CanvasGroup>();
        continueCanvasGroup.alpha = 0f;
        continueCanvasGroup.blocksRaycasts = false;
        continueCanvasGroup.interactable = false;

        BuildContinueButton(continueCanvas.transform);
        continueCanvas.gameObject.SetActive(false);
    }

    /// <summary>
    /// A button with no fill of its own - a wireframe box and a label, washing faintly under
    /// the pointer - which is how the shop and the menus draw theirs.
    /// </summary>
    private void BuildContinueButton(Transform parent)
    {
        GameObject buttonObject = new GameObject("Continue Button", typeof(RectTransform));
        buttonObject.transform.SetParent(parent, false);

        continueButtonRect = buttonObject.GetComponent<RectTransform>();
        // Anchored to the canvas's bottom-left corner so a projected screen point can be
        // used as the position directly. The top-centre pivot hangs it below the message.
        continueButtonRect.anchorMin = Vector2.zero;
        continueButtonRect.anchorMax = Vector2.zero;
        continueButtonRect.pivot = new Vector2(0.5f, 1f);
        continueButtonRect.sizeDelta = continueButtonSize;

        Image background = buttonObject.AddComponent<Image>();
        background.color = Color.white;

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(OnContinueClicked);

        ColorBlock colors = ColorBlock.defaultColorBlock;
        colors.normalColor = new Color(1f, 1f, 1f, 0f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.22f);
        colors.selectedColor = new Color(1f, 1f, 1f, 0f);
        colors.disabledColor = new Color(1f, 1f, 1f, 0f);
        button.colors = colors;

        GameObject outlineObject = new GameObject("Outline", typeof(RectTransform));
        outlineObject.transform.SetParent(buttonObject.transform, false);
        RectTransform outlineRect = outlineObject.GetComponent<RectTransform>();
        outlineRect.anchorMin = Vector2.zero;
        outlineRect.anchorMax = Vector2.one;
        outlineRect.offsetMin = Vector2.zero;
        outlineRect.offsetMax = Vector2.zero;

        UIWireframeBox outline = outlineObject.AddComponent<UIWireframeBox>();
        outline.Color = continueButtonOutlineColor;
        outline.Thickness = continueButtonOutlineThickness;

        GameObject labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(buttonObject.transform, false);

        continueButtonText = labelObject.AddComponent<Text>();
        Text label = continueButtonText;
        label.font = MenuFont.Regular;
        label.fontSize = continueButtonFontSize;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = continueButtonTextColor;
        label.fontStyle = FontStyle.Bold;
        label.raycastTarget = false;
        // Truncate, the default, drops a line whole rather than clipping it, so a label a
        // pixel too tall for its box vanishes outright - and the menu face is tall per point.
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.text = continueButtonLabel;

        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
    }

    private void EnsureArrowCanvas()
    {
        if (arrowCanvas != null)
        {
            return;
        }

        arrowCanvas = CreateOverlayCanvas("Tutorial Arrow Canvas", transform, false);

        GameObject arrowObject = new GameObject("Shop Arrow", typeof(RectTransform));
        arrowObject.transform.SetParent(arrowCanvas.transform, false);

        arrowRect = arrowObject.GetComponent<RectTransform>();
        // Anchored bottom-left with a left-middle pivot, so the position it is given is
        // where the arrow's tip lands - the sprite's point sits on its own left edge.
        arrowRect.anchorMin = Vector2.zero;
        arrowRect.anchorMax = Vector2.zero;
        arrowRect.pivot = new Vector2(0f, 0.5f);

        arrowImage = arrowObject.AddComponent<Image>();
        arrowImage.sprite = FirstCaptureCinematic.GetArrowSprite();
        arrowImage.color = arrowColor;
        // The arrow lies over the shop it is pointing at, so it must not eat the click it
        // is asking for.
        arrowImage.raycastTarget = false;

        arrowObject.SetActive(false);
    }

    /// <summary>
    /// Drives the pair of arrows that see the player out of their first build phase: one at
    /// the Round tab, and once that page is open, one at Start Round. Which is wanted is
    /// worked out from the shop's own state every frame rather than tracked as a step, so
    /// leaving the round page puts the first arrow back rather than stranding the player.
    /// </summary>
    private void UpdateShopArrow()
    {
        RectTransform target = ResolveArrowTarget();
        if (target == null)
        {
            if (arrowRect != null && arrowRect.gameObject.activeSelf)
            {
                arrowRect.gameObject.SetActive(false);
            }

            arrowTarget = null;
            return;
        }

        EnsureArrowCanvas();
        if (arrowRect == null)
        {
            return;
        }

        if (arrowTarget != target)
        {
            // The two buttons are a long way apart, so the arrow fades in at the new one
            // rather than appearing to slide up the shop between them.
            arrowTarget = target;
            arrowShownAt = Time.unscaledTime;
            arrowRect.gameObject.SetActive(true);
        }

        Sprite sprite = arrowImage.sprite;
        float aspect = sprite != null && sprite.rect.width > 0f
            ? sprite.rect.height / sprite.rect.width
            : 0.625f;
        arrowRect.sizeDelta = new Vector2(arrowWidth, arrowWidth * aspect);

        // Overlay canvases put their rects in screen pixels, so a corner is already a
        // screen point and needs no camera to project it.
        target.GetWorldCorners(arrowTargetCorners);
        Vector3 rightEdgeMiddle = (arrowTargetCorners[2] + arrowTargetCorners[3]) * 0.5f;

        // A full sine, so the arrow eases at both ends of its travel instead of snapping
        // back at them.
        float bob = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * arrowBobSpeed * 2f * Mathf.PI);
        arrowRect.anchoredPosition =
            ToCanvasPoint(arrowCanvas, rightEdgeMiddle)
            + new Vector2(arrowGap + bob * arrowTravel, 0f);

        float fade = messageFadeSeconds > 0f
            ? Mathf.Clamp01((Time.unscaledTime - arrowShownAt) / messageFadeSeconds)
            : 1f;
        arrowImage.color = new Color(arrowColor.r, arrowColor.g, arrowColor.b, arrowColor.a * fade);
    }

    /// <summary>
    /// The button the arrow should be pointing at, or null when neither is wanted. The round
    /// page is switched off while the build tab is open, so whether Start Round is in the
    /// hierarchy at all is what says which of the two steps the player is on.
    /// </summary>
    private RectTransform ResolveArrowTarget()
    {
        if (!enableShopArrows
            || !IsBuilding
            || NextWaveIndex != arrowsUpcomingWaveIndex
            || RunStats.TowersPlaced < arrowsAfterTowersPlaced)
        {
            return null;
        }

        if (towerShop == null)
        {
            towerShop = FindFirstObjectByType<TowerShopUI>();
            if (towerShop == null)
            {
                return null;
            }
        }

        Button startRound = towerShop.StartRoundButton;
        if (startRound != null && startRound.gameObject.activeInHierarchy)
        {
            return startRound.GetComponent<RectTransform>();
        }

        Button roundTab = towerShop.RoundTabButton;
        return roundTab != null && roundTab.gameObject.activeInHierarchy
            ? roundTab.GetComponent<RectTransform>()
            : null;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
        {
            return;
        }

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
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

        if (continueButtonActive && continueCanvasGroup != null)
        {
            continueCanvasGroup.alpha = alpha;

            // Dropped only once the message is on its way out, so a click cannot land on a
            // button that is already leaving. Fading in does not gate it.
            continueCanvasGroup.blocksRaycasts = !fadingOut;
            continueCanvasGroup.interactable = !fadingOut;
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

    /// <summary>
    /// The first-capture flourish is launched by a capture during the fight but usually runs
    /// on into the build phase, so the message explaining what it just showed would otherwise
    /// appear over the top of it. Pushing the release time forward on every frame the flourish
    /// is playing covers both the flourish itself and the pause after it, without needing to
    /// know when it started or how long it will run.
    /// </summary>
    /// <summary>
    /// Lets the opening round go once the message about the birds has had its head start.
    /// Armed by the lore card's button and never left armed, so a tutorial that holds the
    /// round back cannot fail to hand it over.
    /// </summary>
    private void ReleaseFirstWaveWhenDue()
    {
        if (!firstWaveReleasePending || Time.time < firstWaveReleaseAt)
        {
            return;
        }

        firstWaveReleasePending = false;
        if (waveSpawner != null)
        {
            waveSpawner.ReleaseFirstWave();
        }
    }

    private void UpdateCinematicGate()
    {
        if (waitForFirstCaptureCinematic && FirstCaptureCinematic.IsPlaying)
        {
            buildingIntroBlockedUntil = Time.time + buildingIntroCinematicDelay;
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
