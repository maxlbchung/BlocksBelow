using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Entity : MonoBehaviour
{
    public float health;

    [Header("Hit Feedback")]
    [SerializeField, AudioClipDropdown] private AudioClip hitSfx;
    [SerializeField] private Color hitFlashColor = Color.white;
    [SerializeField, Min(0f)] private float hitFlashDuration = 0.06f;
    [SerializeField, Min(1)] private int hitFlashCount = 2;
    [SerializeField, Min(0)] private int hitParticleCount = 8;
    [SerializeField, Min(0f), Tooltip("Seconds after a hit during which this entity ignores further hits.")]
    private float invincibilityTime = 0.1f;

    // One material per flash color, shared by every entity flashing that color.
    // A flash material never changes after creation, so entities flashing at
    // the same time cannot fight over it.
    private static readonly Dictionary<Color, Material> flashMaterials = new();
    private static bool flashShaderMissingLogged;

    private SpriteRenderer[] flashRenderers;
    private Material[] originalMaterials;
    private Coroutine flashRoutine;
    private WaitForSeconds flashWait;
    private float lastHitTime = float.NegativeInfinity;

    /// <summary>
    /// True while the invincibility window opened by the last hit is running.
    /// </summary>
    public bool IsInvincible => Time.time - lastHitTime < invincibilityTime;

    protected virtual void Awake()
    {
        // Cached before Start so visuals built there (the player's health bar)
        // never get caught in the flash.
        flashRenderers = GetComponentsInChildren<SpriteRenderer>();
        originalMaterials = new Material[flashRenderers.Length];
        for (int i = 0; i < flashRenderers.Length; i++)
        {
            originalMaterials[i] = flashRenderers[i].sharedMaterial;
        }

        flashWait = new WaitForSeconds(hitFlashDuration);
    }

    /// <summary>
    /// Flashes the sprite, bursts white particles, plays the hit sound, and
    /// opens the invincibility window. Call once per hit that actually lands.
    /// </summary>
    protected void PlayHitFeedback(Vector2 hitPosition)
    {
        lastHitTime = Time.time;

        if (hitSfx != null)
        {
            AudioController.Play(hitSfx);
        }

        HitParticles.Emit(hitPosition, hitParticleCount);

        if (flashRenderers == null || flashRenderers.Length == 0 || !isActiveAndEnabled)
        {
            return;
        }

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            RestoreMaterials();
        }

        flashRoutine = StartCoroutine(FlashRoutine());
    }

    /// <summary>
    /// Stops any running flash, restores the original materials, and closes the
    /// invincibility window. Pooled entities call this around despawn so a
    /// mid-flash release cannot leak the flash material into the next life.
    /// </summary>
    protected void ResetHitFeedback()
    {
        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        RestoreMaterials();
        lastHitTime = float.NegativeInfinity;
    }

    private IEnumerator FlashRoutine()
    {
        Material flashMaterial = GetFlashMaterial(hitFlashColor);
        if (flashMaterial == null)
        {
            flashRoutine = null;
            yield break;
        }

        for (int flash = 0; flash < hitFlashCount; flash++)
        {
            ApplyFlashMaterial(flashMaterial);
            yield return flashWait;
            RestoreMaterials();

            if (flash < hitFlashCount - 1)
            {
                yield return flashWait;
            }
        }

        flashRoutine = null;
    }

    private void ApplyFlashMaterial(Material flashMaterial)
    {
        for (int i = 0; i < flashRenderers.Length; i++)
        {
            if (flashRenderers[i] != null)
            {
                flashRenderers[i].sharedMaterial = flashMaterial;
            }
        }
    }

    private void RestoreMaterials()
    {
        if (flashRenderers == null)
        {
            return;
        }

        for (int i = 0; i < flashRenderers.Length; i++)
        {
            if (flashRenderers[i] != null)
            {
                flashRenderers[i].sharedMaterial = originalMaterials[i];
            }
        }
    }

    private static Material GetFlashMaterial(Color color)
    {
        if (flashMaterials.TryGetValue(color, out Material material) && material != null)
        {
            return material;
        }

        Shader flashShader = Shader.Find("TowerDefense/SpriteFlash");
        if (flashShader == null)
        {
            if (!flashShaderMissingLogged)
            {
                Debug.LogWarning("The TowerDefense/SpriteFlash shader could not be found.");
                flashShaderMissingLogged = true;
            }

            return null;
        }

        material = new Material(flashShader)
        {
            name = "Shared Hit Flash Material",
            hideFlags = HideFlags.HideAndDontSave
        };
        material.SetColor("_FlashColor", color);
        flashMaterials[color] = material;
        return material;
    }
}
