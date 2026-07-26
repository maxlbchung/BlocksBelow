using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One-off flourish for the run's first fully-caged energy stack, in two beats.
/// <para>
/// First the tally: an arrow appears beside the energy tower pointing at it, then steps
/// down the stack a tile at a time, dropping a "+1" beside every cage it counts off.
/// </para>
/// <para>
/// Then the climb: an orb of white energy wells out of the bird in the bottom cage and
/// arcs up through the birds above it into the tower, lighting it up.
/// </para>
/// <para>
/// Everything it draws is built at runtime - generated sprites, a trail and text - so no
/// prefab or art asset has to carry it. The object deletes itself when the flourish ends.
/// </para>
/// </summary>
public class FirstCaptureCinematic : MonoBehaviour
{
    // --- Tally beat ---
    private const float ArrowFadeDuration = 0.25f;
    private const float ArrowStepDuration = 0.35f;
    private const float TallyPopupHold = 0.45f;
    private const float TallyEndHold = 0.35f;
    private const float TallyFadeDuration = 0.25f;

    // --- Climb beat ---
    private const float BirthDuration = 0.35f;
    private const float CageHopDuration = 0.9f;
    private const float TowerHopDuration = 1.25f;
    private const float AbsorbDuration = 0.28f;
    private const float LandingDuration = 0.22f;
    private const float FlashDuration = 0.45f;

    // Distances are in grid cells, measured off the tower's own sprite so the flourish
    // keeps its shape whatever the scene's cell size is.
    private const float ArrowOffset = 1f;
    private const float ArrowWidth = 0.8f;
    private const float PopupOffset = 1.05f;
    private const float ExitDistance = 4f;
    private const float ArcHeight = 3f;
    private const float CageHopArcScale = 0.7f;
    private const float OrbDiameter = 0.5f;
    private const float HaloScale = 2.6f;
    private const float FlashDiameter = 2.8f;
    private const float TrailTime = 0.3f;

    private const string SortingLayerName = "Foreground";
    private const int CoreSortingOrder = 20;
    private const int HaloSortingOrder = 19;
    private const int TrailSortingOrder = 18;
    private const int PulseSortingOrder = 5;
    private const int ArrowSortingOrder = 25;

    private static Sprite glowSprite;
    private static Sprite arrowSprite;

    /// <summary>
    /// True from the moment the flourish starts until its last frame. The energy tower
    /// holds its round payout back while this is set, so the two do not play over each
    /// other when the caged bird was the round's last enemy.
    /// </summary>
    public static bool IsPlaying { get; private set; }

    private readonly List<EnergyGainPopup> tallyPopups = new List<EnergyGainPopup>(4);
    private Transform coreTransform;
    private Transform haloTransform;
    private TrailRenderer trail;
    private Material trailMaterial;
    private float cellUnit = 1f;

    /// <summary>
    /// Plays the flourish if this capture is the one that fills the last empty cage under
    /// an energy tower, and the run has not spent its flourish yet. Safe to call on every
    /// capture; a stack with birds still to catch simply waits for them.
    /// </summary>
    public static void TryPlay(CageTower cage)
    {
        // Checked first so that every later capture in the run costs nothing: the work
        // below scans the scene, and a stack that never fills would otherwise pay for it
        // on every bird caught.
        if (cage == null || RunStats.FirstCageCaptureClaimed)
        {
            return;
        }

        // Everything is resolved before the claim is taken, so a capture that cannot put
        // on the whole show does not burn the one flourish the run gets.
        EnergyTower tower = FindPoweredTower(cage);
        if (tower == null)
        {
            return;
        }

        List<CageTower> cages = CollectFullStack(tower);
        if (cages == null || !RunStats.TryClaimFirstCageCapture())
        {
            return;
        }

        IsPlaying = true;
        GameObject cinematicObject = new GameObject("First Capture Cinematic");
        cinematicObject.transform.position = cages[0].transform.position;
        cinematicObject.AddComponent<FirstCaptureCinematic>().Launch(tower, cages);
    }

    /// <summary>
    /// The energy tower this cage feeds is the one whose stack it stands in. A cage that is
    /// in no stack falls back to the nearest tower, so the flourish still has somewhere to go.
    /// </summary>
    private static EnergyTower FindPoweredTower(CageTower cage)
    {
        EnergyTower[] towers = FindObjectsByType<EnergyTower>(FindObjectsSortMode.None);
        EnergyTower nearest = null;
        float nearestDistanceSquared = float.PositiveInfinity;

        for (int i = 0; i < towers.Length; i++)
        {
            EnergyTower tower = towers[i];
            TowerCageStack stack = tower.GetComponent<TowerCageStack>();
            if (stack != null)
            {
                // Re-scanned here because a tower only maps its stack when it is placed,
                // which may have happened before the cages under it existed.
                stack.FindContinuousCagesBelow();

                IReadOnlyList<CageTower> cagesBelow = stack.CagesBelow;
                for (int j = 0; j < cagesBelow.Count; j++)
                {
                    if (cagesBelow[j] == cage)
                    {
                        return tower;
                    }
                }
            }

            float distanceSquared =
                (tower.transform.position - cage.transform.position).sqrMagnitude;
            if (distanceSquared < nearestDistanceSquared)
            {
                nearestDistanceSquared = distanceSquared;
                nearest = tower;
            }
        }

        return nearest;
    }

    /// <summary>
    /// The tower's cages ordered bottom-first, or null while any of them is still empty.
    /// The orb is one continuous climb, so it only sets off once the whole stack can be
    /// carried in a single run.
    /// </summary>
    private static List<CageTower> CollectFullStack(EnergyTower tower)
    {
        TowerCageStack stack = tower.GetComponent<TowerCageStack>();
        IReadOnlyList<CageTower> cagesBelow = stack != null ? stack.CagesBelow : null;
        if (cagesBelow == null || cagesBelow.Count == 0)
        {
            return null;
        }

        // The stack runs downward from the tower, so reversing it puts the bottom bird -
        // where the energy starts - first.
        List<CageTower> bottomFirst = new List<CageTower>(cagesBelow.Count);
        for (int i = cagesBelow.Count - 1; i >= 0; i--)
        {
            CageTower cage = cagesBelow[i];
            if (cage == null || cage.State != CageTower.CageState.Full)
            {
                return null;
            }

            bottomFirst.Add(cage);
        }

        return bottomFirst;
    }

    private void Launch(EnergyTower tower, List<CageTower> cages)
    {
        cellUnit = GetCellUnit(tower);
        BuildOrbVisual();
        SetOrbScale(0f);

        // The tower would otherwise light up the instant the last cage filled, before the
        // flourish has even started. It is held at the level it was on for both beats; the
        // margin covers the frames the loops overshoot by, and landing clears it outright.
        float tallySeconds = ArrowFadeDuration
            + cages.Count * (ArrowStepDuration + TallyPopupHold)
            + TallyEndHold
            + TallyFadeDuration;
        float climbSeconds = BirthDuration
            + (cages.Count - 1) * (CageHopDuration + AbsorbDuration)
            + TowerHopDuration;
        tower.HoldPowerLevelBack(tallySeconds + climbSeconds + 0.5f, cages.Count);

        StartCoroutine(PlayRoutine(tower, cages));
    }

    private IEnumerator PlayRoutine(EnergyTower tower, List<CageTower> cages)
    {
        yield return TallyRoutine(tower, cages);

        if (tower == null)
        {
            Destroy(gameObject);
            yield break;
        }

        yield return ClimbRoutine(tower, cages);
        Destroy(gameObject);
    }

    /// <summary>
    /// The arrow beat: it fades in beside the tower pointing at it, then walks down the
    /// stack a tile at a time, leaving a "+1" beside each cage it counts.
    /// </summary>
    private IEnumerator TallyRoutine(EnergyTower tower, List<CageTower> cages)
    {
        float arrowX = tower.transform.position.x + cellUnit * ArrowOffset;
        Vector3 arrowPosition = new Vector3(arrowX, tower.transform.position.y, 0f);
        SpriteRenderer arrow = CreateArrow(arrowPosition);

        for (float elapsed = 0f; elapsed < ArrowFadeDuration; elapsed += Time.deltaTime)
        {
            arrow.color = new Color(1f, 1f, 1f, elapsed / ArrowFadeDuration);
            yield return null;
        }

        arrow.color = Color.white;

        // Counted from the tower downward, which is the opposite of the order the orb
        // then carries them back up in.
        for (int i = cages.Count - 1; i >= 0; i--)
        {
            if (cages[i] == null)
            {
                continue;
            }

            Vector3 stepTo = new Vector3(arrowX, cages[i].transform.position.y, 0f);
            for (float elapsed = 0f; elapsed < ArrowStepDuration; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / ArrowStepDuration);
                arrow.transform.position = Vector3.Lerp(arrowPosition, stepTo, t);
                yield return null;
            }

            arrow.transform.position = stepTo;
            arrowPosition = stepTo;

            tallyPopups.Add(EnergyGainPopup.Show(
                stepTo + Vector3.right * (cellUnit * PopupOffset),
                "+1",
                cellUnit));

            yield return new WaitForSeconds(TallyPopupHold);
        }

        yield return new WaitForSeconds(TallyEndHold);

        // The whole tally clears together, so the count reads as one total before the
        // energy sets off to deliver it.
        FadeTallyPopups();
        for (float elapsed = 0f; elapsed < TallyFadeDuration; elapsed += Time.deltaTime)
        {
            arrow.color = new Color(1f, 1f, 1f, 1f - elapsed / TallyFadeDuration);
            yield return null;
        }

        Destroy(arrow.gameObject);
    }

    /// <summary>
    /// The orb beat: it wells out of the bottom bird and arcs up through the stack, taking
    /// one cage's power with it each leg, into the tower.
    /// </summary>
    private IEnumerator ClimbRoutine(EnergyTower tower, List<CageTower> cages)
    {
        Vector3 start = transform.position;
        for (float elapsed = 0f; elapsed < BirthDuration; elapsed += Time.deltaTime)
        {
            float t = elapsed / BirthDuration;
            SetOrbScale(Mathf.SmoothStep(0f, 1f, t));
            transform.position = start + Vector3.up * (cellUnit * 0.15f * t);
            yield return null;
        }

        SetOrbScale(1f);
        trail.Clear();
        trail.emitting = true;

        for (int i = 0; i < cages.Count; i++)
        {
            bool intoTower = i == cages.Count - 1;
            Transform destination = intoTower
                ? tower.transform
                : cages[i + 1] != null ? cages[i + 1].transform : null;
            if (destination == null)
            {
                break;
            }

            yield return Fly(
                destination.position,
                intoTower ? TowerHopDuration : CageHopDuration,
                intoTower ? 1f : CageHopArcScale);

            if (!intoTower)
            {
                yield return AbsorbAtCage();
            }
        }

        trail.emitting = false;

        if (tower != null)
        {
            tower.ReceiveFirstCaptureEnergy();
        }

        // Parented to this object, which no longer moves, so the flash stays on the tower
        // and goes away with everything else.
        SpriteRenderer flash = CreateGlow("Power Up Flash", Color.white, PulseSortingOrder);

        for (float elapsed = 0f; elapsed < FlashDuration; elapsed += Time.deltaTime)
        {
            float eased = Mathf.SmoothStep(0f, 1f, elapsed / FlashDuration);

            // The orb collapses into the tower over the first slice of the flash.
            float collapse = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / LandingDuration));
            SetOrbScale(Mathf.Lerp(1.45f, 0f, collapse));

            float diameter = cellUnit * Mathf.Lerp(FlashDiameter * 0.3f, FlashDiameter, eased);
            flash.transform.localScale = Vector3.one * diameter;
            flash.color = new Color(1f, 1f, 1f, 0.9f * (1f - eased));
            yield return null;
        }
    }

    /// <summary>
    /// Swings the orb from where it is out to the left and back around into
    /// <paramref name="destination"/>, so each leg reads as one wide arc rather than a
    /// there-and-back.
    /// </summary>
    private IEnumerator Fly(Vector3 destination, float duration, float arcScale)
    {
        Vector3 origin = transform.position;

        // Leaving along the first handle sends the orb straight out to the left; arriving
        // along the second brings it back in from high on that same side.
        Vector3 exitHandle = origin
            + Vector3.left * (cellUnit * ExitDistance * arcScale * 0.85f);
        Vector3 approachHandle = destination
            + Vector3.left * (cellUnit * ExitDistance * arcScale)
            + Vector3.up * (cellUnit * ArcHeight * arcScale);

        for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            transform.position = CubicBezier(origin, exitHandle, approachHandle, destination, t);
            SetOrbScale(1f + 0.12f * Mathf.Sin(t * Mathf.PI * 3f));
            yield return null;
        }

        transform.position = destination;
    }

    /// <summary>The beat where the orb takes on the cage it has just arrived at.</summary>
    private IEnumerator AbsorbAtCage()
    {
        SpriteRenderer pulse = CreateGlow("Cage Pulse", Color.white, PulseSortingOrder);

        for (float elapsed = 0f; elapsed < AbsorbDuration; elapsed += Time.deltaTime)
        {
            float t = elapsed / AbsorbDuration;

            // Dips as it takes the power on, swells past its old size, and settles back
            // where the next leg picks it up - so the beat leaves no jump in size.
            SetOrbScale(1f - 0.3f * Mathf.Sin(t * Mathf.PI * 2f));

            float diameter = cellUnit * Mathf.Lerp(FlashDiameter * 0.25f, FlashDiameter * 0.7f, t);
            pulse.transform.localScale = Vector3.one * diameter;
            pulse.color = new Color(1f, 1f, 1f, 0.7f * (1f - t));
            yield return null;
        }

        Destroy(pulse.gameObject);
        SetOrbScale(1f);
    }

    private void BuildOrbVisual()
    {
        haloTransform = CreateGlow(
            "Halo",
            new Color(0.78f, 0.92f, 1f, 0.4f),
            HaloSortingOrder).transform;
        coreTransform = CreateGlow("Core", Color.white, CoreSortingOrder).transform;

        trailMaterial = new Material(Shader.Find("Sprites/Default"))
        {
            name = "Energy Orb Trail Material"
        };

        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(0.78f, 0.92f, 1f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(0.85f, 0f),
                new GradientAlphaKey(0f, 1f)
            });

        trail = gameObject.AddComponent<TrailRenderer>();
        trail.sharedMaterial = trailMaterial;
        trail.time = TrailTime;
        trail.minVertexDistance = 0.02f;
        trail.autodestruct = false;
        trail.emitting = false;
        trail.numCapVertices = 4;
        trail.alignment = LineAlignment.View;
        trail.textureMode = LineTextureMode.Stretch;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        // The curve is set first: assigning a width shape after the multiplier is the
        // order that keeps both.
        trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        trail.widthMultiplier = cellUnit * OrbDiameter * 0.6f;
        trail.colorGradient = gradient;
        trail.sortingLayerName = SortingLayerName;
        trail.sortingOrder = TrailSortingOrder;
    }

    /// <summary>
    /// The tally arrow. Parented to this object like everything else so it cannot outlive
    /// the flourish, but placed in world space - this object holds still until the arrow
    /// is gone.
    /// </summary>
    private SpriteRenderer CreateArrow(Vector3 worldPosition)
    {
        GameObject arrowObject = new GameObject("Tally Arrow");
        arrowObject.transform.SetParent(transform, false);
        arrowObject.transform.position = worldPosition;
        arrowObject.transform.localScale = Vector3.one * (cellUnit * ArrowWidth);

        SpriteRenderer renderer = arrowObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetArrowSprite();
        renderer.color = new Color(1f, 1f, 1f, 0f);
        renderer.sortingLayerName = SortingLayerName;
        renderer.sortingOrder = ArrowSortingOrder;
        return renderer;
    }

    private SpriteRenderer CreateGlow(string name, Color color, int sortingOrder)
    {
        GameObject glowObject = new GameObject(name);
        glowObject.transform.SetParent(transform, false);

        SpriteRenderer renderer = glowObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetGlowSprite();
        renderer.color = color;
        renderer.sortingLayerName = SortingLayerName;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    private void SetOrbScale(float multiplier)
    {
        float diameter = cellUnit * OrbDiameter * Mathf.Max(0f, multiplier);
        coreTransform.localScale = Vector3.one * diameter;
        haloTransform.localScale = Vector3.one * (diameter * HaloScale);
    }

    private void FadeTallyPopups()
    {
        for (int i = 0; i < tallyPopups.Count; i++)
        {
            if (tallyPopups[i] != null)
            {
                tallyPopups[i].FadeOut(TallyFadeDuration);
            }
        }

        tallyPopups.Clear();
    }

    /// <summary>
    /// One grid cell in world units. The tower fills its cell, so its sprite measures the
    /// grid without this having to be handed the placement settings.
    /// </summary>
    private static float GetCellUnit(EnergyTower tower)
    {
        SpriteRenderer renderer = tower.GetComponent<SpriteRenderer>();
        float width = renderer != null ? renderer.bounds.size.x : 0f;
        return width > 0.05f ? width : 1f;
    }

    private static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float inverse = 1f - t;
        return inverse * inverse * inverse * p0
            + 3f * inverse * inverse * t * p1
            + 3f * inverse * t * t * p2
            + t * t * t * p3;
    }

    /// <summary>A soft white disc, one world unit across, shared by every glow this draws.</summary>
    private static Sprite GetGlowSprite()
    {
        if (glowSprite != null)
        {
            return glowSprite;
        }

        const int Resolution = 64;
        Texture2D texture = CreateTexture("Energy Orb Glow", Resolution, Resolution);
        Color32[] pixels = new Color32[Resolution * Resolution];
        float centre = (Resolution - 1) * 0.5f;

        for (int y = 0; y < Resolution; y++)
        {
            for (int x = 0; x < Resolution; x++)
            {
                float distance = new Vector2(x - centre, y - centre).magnitude / centre;
                // Squaring the falloff keeps a bright core while the rim reaches zero
                // cleanly, so the disc reads as a glow rather than a circle.
                float alpha = Mathf.Clamp01(1f - distance);
                pixels[y * Resolution + x] = new Color(1f, 1f, 1f, alpha * alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        glowSprite = CreateSprite(texture, "Energy Orb Glow");
        return glowSprite;
    }

    /// <summary>
    /// A solid left-pointing arrow, one world unit wide. Sampled several times per pixel
    /// so the diagonals of the head do not come out as stair steps at this size.
    /// <para>
    /// Public so the tutorial's shop arrows are the same shape as the one the player was
    /// first shown here, rather than a second arrow drawn slightly differently.
    /// </para>
    /// </summary>
    public static Sprite GetArrowSprite()
    {
        if (arrowSprite != null)
        {
            return arrowSprite;
        }

        const int Width = 96;
        const int Height = 60;
        const int SamplesPerAxis = 3;
        const float HeadLength = 0.5f;
        const float ShaftHalfHeight = 0.26f;

        Texture2D texture = CreateTexture("Tally Arrow", Width, Height);
        Color32[] pixels = new Color32[Width * Height];
        float sampleStep = 1f / SamplesPerAxis;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int inside = 0;

                for (int sampleY = 0; sampleY < SamplesPerAxis; sampleY++)
                {
                    for (int sampleX = 0; sampleX < SamplesPerAxis; sampleX++)
                    {
                        // u runs 0 at the tip to 1 at the tail; v is 0 on the centre line
                        // and 1 at the top or bottom edge.
                        float u = (x + (sampleX + 0.5f) * sampleStep) / Width;
                        float v = Mathf.Abs((y + (sampleY + 0.5f) * sampleStep) / Height * 2f - 1f);

                        bool inHead = u <= HeadLength && v <= u / HeadLength;
                        bool inShaft = u >= HeadLength * 0.85f && v <= ShaftHalfHeight;
                        if (inHead || inShaft)
                        {
                            inside++;
                        }
                    }
                }

                float coverage = inside / (float)(SamplesPerAxis * SamplesPerAxis);
                pixels[y * Width + x] = new Color(1f, 1f, 1f, coverage);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        arrowSprite = CreateSprite(texture, "Tally Arrow");
        return arrowSprite;
    }

    private static Texture2D CreateTexture(string name, int width, int height)
    {
        return new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    /// <summary>Wraps a generated texture as a sprite exactly one world unit wide.</summary>
    private static Sprite CreateSprite(Texture2D texture, string name)
    {
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            texture.width);
        sprite.name = name;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private void OnDestroy()
    {
        // Cleared here rather than at the end of the routine so that a flourish cut short -
        // by the tower going away, or by the scene being reloaded part-way - cannot leave
        // a payout waiting on something that will never finish.
        IsPlaying = false;

        // The popups are their own objects so they can fade on their own clock, which also
        // means they have to be cleared when the flourish is cut short. Deleted outright
        // rather than faded: there is nothing left running to fade them on.
        for (int i = 0; i < tallyPopups.Count; i++)
        {
            if (tallyPopups[i] != null)
            {
                Destroy(tallyPopups[i].gameObject);
            }
        }

        tallyPopups.Clear();

        if (trailMaterial != null)
        {
            Destroy(trailMaterial);
        }
    }
}
