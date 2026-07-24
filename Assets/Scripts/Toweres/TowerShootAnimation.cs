using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plays a one-shot sprite flipbook on a tower's renderer each time it fires and
/// restores the idle sprite when the last frame ends. Frames are individual sprite
/// assets, one PNG per frame. Only the sprite changes, so the tower's collider and
/// grid position stay exactly where placement expects them. The component disables
/// itself between shots, so idle towers cost nothing to update.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class TowerShootAnimation : MonoBehaviour
{
    [Tooltip("One PNG per frame. Drop the whole set in at once. Leave empty to disable the animation.")]
    [SerializeField] private List<Sprite> frames = new List<Sprite>();
    [SerializeField, Min(0.001f), Tooltip("Seconds each frame stays on screen.")]
    private float frameDuration = 0.05f;
    [Tooltip("Plays the frames in file-name order, counting trailing numbers properly "
        + "(fire_2 before fire_10). Turn off to play them in exactly the order listed.")]
    [SerializeField] private bool sortFramesByName = true;

    private SpriteRenderer spriteRenderer;
    private Sprite idleSprite;
    private float frameTimer;
    private int frameIndex;
    private bool isPlaying;

    public bool HasFrames => frames.Count > 0;
    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        ApplyFrameOrder();
        CacheIdleSprite();
        // Nothing to update until the tower fires.
        enabled = false;
    }

    public void Configure(IReadOnlyList<Sprite> newFrames, float newFrameDuration, bool sortByName = true)
    {
        sortFramesByName = sortByName;
        frames.Clear();
        if (newFrames != null)
        {
            for (int i = 0; i < newFrames.Count; i++)
            {
                // A blank entry left in the inspector list would flash the tower invisible.
                if (newFrames[i] != null)
                {
                    frames.Add(newFrames[i]);
                }
            }
        }

        frameDuration = Mathf.Max(0.001f, newFrameDuration);
        ApplyFrameOrder();
        CacheIdleSprite();
    }

    /// <summary>
    /// Puts separately imported frames into playback order. Dropping several PNGs onto
    /// the list at once does not guarantee the order they land in, so the file name decides.
    /// </summary>
    private void ApplyFrameOrder()
    {
        if (sortFramesByName && frames.Count > 1)
        {
            frames.Sort(CompareFrameNames);
        }
    }

    /// <summary>
    /// Orders names the way a frame set reads, treating digit runs as numbers so
    /// fire_2 comes before fire_10 instead of after it.
    /// </summary>
    private static int CompareFrameNames(Sprite left, Sprite right)
    {
        if (left == null || right == null)
        {
            return left == right ? 0 : (left == null ? 1 : -1);
        }

        return CompareNaturalNames(left.name, right.name);
    }

    private static int CompareNaturalNames(string a, string b)
    {
        int i = 0;
        int j = 0;

        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int digitsStartA = i;
                int digitsStartB = j;
                while (i < a.Length && char.IsDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsDigit(b[j]))
                {
                    j++;
                }

                // Leading zeros do not change the value, so 007 and 7 compare equal.
                string numberA = a.Substring(digitsStartA, i - digitsStartA).TrimStart('0');
                string numberB = b.Substring(digitsStartB, j - digitsStartB).TrimStart('0');
                if (numberA.Length != numberB.Length)
                {
                    return numberA.Length - numberB.Length;
                }

                int digitComparison = string.CompareOrdinal(numberA, numberB);
                if (digitComparison != 0)
                {
                    return digitComparison;
                }

                continue;
            }

            int comparison = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
            if (comparison != 0)
            {
                return comparison;
            }

            i++;
            j++;
        }

        // Whichever name still has characters left is the longer, later one.
        return (a.Length - i) - (b.Length - j);
    }

    /// <summary>Starts the flipbook, restarting it when another shot lands mid-animation.</summary>
    public void Play()
    {
        if (!HasFrames || spriteRenderer == null)
        {
            return;
        }

        CacheIdleSprite();
        isPlaying = true;
        frameIndex = 0;
        frameTimer = 0f;
        spriteRenderer.sprite = frames[0];
        enabled = true;
    }

    /// <summary>Stops the flipbook and puts the idle sprite back.</summary>
    public void Stop()
    {
        isPlaying = false;
        frameIndex = 0;
        frameTimer = 0f;
        enabled = false;
        RestoreIdleSprite();
    }

    private void Update()
    {
        frameTimer += Time.deltaTime;
        if (frameTimer < frameDuration)
        {
            return;
        }

        // A long frame time or a hitch can cover several animation frames in one update.
        int step = (int)(frameTimer / frameDuration);
        frameTimer -= step * frameDuration;
        frameIndex += step;

        if (frameIndex >= frames.Count)
        {
            Stop();
            return;
        }

        spriteRenderer.sprite = frames[frameIndex];
    }

    private void OnDisable()
    {
        // Covers the tower being disabled or destroyed part-way through a shot.
        if (isPlaying)
        {
            isPlaying = false;
            RestoreIdleSprite();
        }
    }

    /// <summary>Remembers the tower's resting sprite, ignoring frames it is mid-way through.</summary>
    private void CacheIdleSprite()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (spriteRenderer != null && !isPlaying)
        {
            idleSprite = spriteRenderer.sprite;
        }
    }

    private void RestoreIdleSprite()
    {
        if (spriteRenderer != null && idleSprite != null)
        {
            spriteRenderer.sprite = idleSprite;
        }
    }
}
