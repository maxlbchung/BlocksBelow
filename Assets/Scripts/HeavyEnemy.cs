using System.Collections;
using UnityEngine;

/// <summary>
/// A basic pursuing enemy protected by a separate, non-overflowing shield.
/// The visual is created at runtime so every pooled copy owns and resets its effect.
/// </summary>
public sealed class HeavyEnemy : Enemy
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 7f;
    [SerializeField] private float shieldSpeed = 4f;
    public int damage;

    [Header("Shield")]
    [SerializeField, Min(0f)] private float shieldHealth = 10f;
    [SerializeField] private Color shieldColor = new Color(0.12f, 0.68f, 1f, 1f);
    [SerializeField, Range(0f, 1f), Tooltip("Overall visibility of the shield dome.")]
    private float shieldOpacity = 1f;
    [SerializeField, Tooltip("Color of the solid circle around the shield radius.")]
    private Color shieldOutlineColor = new Color(0.25f, 0.9f, 1f, 1f);
    [SerializeField, Min(0.01f)] private float shieldDiameter = 3f;
    [SerializeField, Range(0f, 1f)] private float minimumShieldScale = 0.35f;
    [SerializeField] private int shieldSortingOrder = 1;

    [Header("Shield Hit Flash")]
    [SerializeField] private Color shieldFlashColor = new Color(0.65f, 0.9f, 1f, 0.9f);
    [SerializeField, Min(0f)] private float shieldFlashDuration = 0.06f;
    [SerializeField, Min(1)] private int shieldFlashCount = 2;

    [Header("Shield Shatter")]
    [SerializeField, Min(1)] private int shatterPieceCount = 18;
    [SerializeField, Min(0f)] private float shatterSpeed = 3f;
    [SerializeField, Min(0.01f)] private float shatterLifetime = 0.45f;

    private static Sprite shieldSprite;
    private static Sprite shieldOutlineSprite;
    private static Material shieldMaterial;

    private float maximumShieldHealth;
    private bool maximumShieldHealthCaptured;
    private float currentShieldHealth;
    private SpriteRenderer shieldRenderer;
    private SpriteRenderer shieldOutlineRenderer;
    private ParticleSystem shatterParticles;
    private Coroutine shieldFlashRoutine;
    

    public override int ContactDamage => damage;
    public float ShieldHealth => currentShieldHealth;
    public float MaximumShieldHealth => maximumShieldHealth;

    protected override void Awake()
    {
        CaptureMaximumShieldHealth();
        EnsureShieldVisual();
        base.Awake();
        ResetShield();
    }

    protected override Vector2 CalculateDesiredVelocity(Transform player, float elapsed)
    {
        if (player == null)
        {
            return Vector2.zero;
        }

        Vector2 direction = (Vector2)player.position - Position;
        float distanceSquared = direction.sqrMagnitude;
        return distanceSquared <= 0.000001f
            ? Vector2.zero
            : direction * (moveSpeed / Mathf.Sqrt(distanceSquared));
    }

    public override bool TryTakeDamage(float damageAmount)
    {
        if (!CanTakeDamage || IsInvincible)
        {
            return false;
        }

        if (currentShieldHealth > 0f)
        {
            OpenHitInvincibilityWindow();

            // Deliberately discard overflow: an attack that breaks the shield never
            // damages the heavy enemy's regular health.
            currentShieldHealth = Mathf.Max(0f, currentShieldHealth - damageAmount);
            UpdateShieldVisual();

            if (currentShieldHealth <= 0f)
            {
                ShatterShield();
            }
            else
            {
                FlashShield();
            }

            return true;
        }

        return base.TryTakeDamage(damageAmount);
    }

    protected override void ResetEnemyState()
    {
        base.ResetEnemyState();
        CaptureMaximumShieldHealth();
        EnsureShieldVisual();
        ResetShield();
    }

    private void CaptureMaximumShieldHealth()
    {
        if (maximumShieldHealthCaptured)
        {
            return;
        }

        maximumShieldHealth = shieldHealth;
        maximumShieldHealthCaptured = true;
    }

    private void EnsureShieldVisual()
    {
        if (shieldRenderer != null)
        {
            return;
        }

        GameObject shieldObject = new GameObject("Shield Dome");
        shieldObject.transform.SetParent(transform, false);
        shieldRenderer = shieldObject.AddComponent<SpriteRenderer>();
        shieldRenderer.sprite = GetShieldSprite();
        shieldRenderer.sharedMaterial = GetShieldMaterial();

        SpriteRenderer bodyRenderer = GetComponent<SpriteRenderer>();
        if (bodyRenderer != null)
        {
            shieldRenderer.sortingLayerID = bodyRenderer.sortingLayerID;
            shieldRenderer.sortingOrder = bodyRenderer.sortingOrder + shieldSortingOrder;
        }
        else
        {
            shieldRenderer.sortingOrder = shieldSortingOrder;
        }

        GameObject outlineObject = new GameObject("Shield Outline");
        outlineObject.transform.SetParent(transform, false);
        shieldOutlineRenderer = outlineObject.AddComponent<SpriteRenderer>();
        shieldOutlineRenderer.sprite = GetShieldOutlineSprite();
        shieldOutlineRenderer.sharedMaterial = GetShieldMaterial();
        shieldOutlineRenderer.sortingLayerID = shieldRenderer.sortingLayerID;
        shieldOutlineRenderer.sortingOrder = shieldRenderer.sortingOrder + 1;

        GameObject shatterObject = new GameObject("Shield Shatter");
        shatterObject.transform.SetParent(transform, false);
        shatterParticles = shatterObject.AddComponent<ParticleSystem>();
        ConfigureShatterParticles();
    }

    private void ResetShield()
    {
        if (shieldFlashRoutine != null)
        {
            StopCoroutine(shieldFlashRoutine);
            shieldFlashRoutine = null;
        }

        if (shatterParticles != null)
        {
            shatterParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        currentShieldHealth = maximumShieldHealth;
        UpdateShieldVisual();
    }

    private void UpdateShieldVisual()
    {
        if (shieldRenderer == null)
        {
            return;
        }

        float percentage = maximumShieldHealth > 0f
            ? Mathf.Clamp01(currentShieldHealth / maximumShieldHealth)
            : 0f;
        float scale = shieldDiameter * Mathf.Lerp(minimumShieldScale, 1f, percentage);
        shieldRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        shieldOutlineRenderer.transform.localScale = new Vector3(scale, scale, 1f);
        shieldRenderer.color = GetShieldDisplayColor();
        shieldOutlineRenderer.color = shieldOutlineColor;
        shieldRenderer.enabled = percentage > 0f;
        shieldOutlineRenderer.enabled = percentage > 0f;
    }

    private void FlashShield()
    {
        if (!isActiveAndEnabled || shieldRenderer == null)
        {
            return;
        }

        if (shieldFlashRoutine != null)
        {
            StopCoroutine(shieldFlashRoutine);
        }

        shieldFlashRoutine = StartCoroutine(FlashShieldRoutine());
    }

    private IEnumerator FlashShieldRoutine()
    {
        WaitForSeconds wait = new WaitForSeconds(shieldFlashDuration);
        for (int i = 0; i < shieldFlashCount; i++)
        {
            shieldRenderer.color = shieldFlashColor;
            shieldOutlineRenderer.color = shieldFlashColor;
            yield return wait;
            shieldRenderer.color = GetShieldDisplayColor();
            shieldOutlineRenderer.color = shieldOutlineColor;

            if (i < shieldFlashCount - 1)
            {
                yield return wait;
            }
        }

        shieldFlashRoutine = null;
    }

    private Color GetShieldDisplayColor()
    {
        Color displayColor = shieldColor;
        displayColor.a = shieldOpacity;
        return displayColor;
    }

    private void ShatterShield()
    {
        if (shieldFlashRoutine != null)
        {
            StopCoroutine(shieldFlashRoutine);
            shieldFlashRoutine = null;
        }

        shieldRenderer.enabled = false;
        shieldOutlineRenderer.enabled = false;
        if (shatterParticles != null)
        {
            shatterParticles.Emit(shatterPieceCount);
        }
    }

    private void ConfigureShatterParticles()
    {
        ParticleSystem.MainModule main = shatterParticles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.startLifetime = shatterLifetime;
        main.startSpeed = shatterSpeed;
        main.startSize = Mathf.Max(0.03f, shieldDiameter * 0.08f);
        main.startColor = shieldColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.None;

        ParticleSystem.EmissionModule emission = shatterParticles.emission;
        emission.enabled = false;

        ParticleSystem.ShapeModule shape = shatterParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = shieldDiameter * 0.5f;
        shape.radiusThickness = 0f;

        ParticleSystem.VelocityOverLifetimeModule velocity = shatterParticles.velocityOverLifetime;
        velocity.enabled = false;

        ParticleSystemRenderer renderer = shatterParticles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = shieldSortingOrder + 1;
        renderer.material = new Material(Shader.Find("Sprites/Default"));
        renderer.material.mainTexture = GetShieldSprite().texture;
    }

    private static Sprite GetShieldSprite()
    {
        if (shieldSprite != null)
        {
            return shieldSprite;
        }

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Runtime Heavy Enemy Shield",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[size * size];
        Vector2 centre = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.48f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float normalizedRadius = Vector2.Distance(new Vector2(x, y), centre) / radius;
                // Completely solid inside. Only the outermost pixels fade to
                // antialias the edge; Shield Opacity controls all transparency.
                float alpha = 1f - Mathf.SmoothStep(0.94f, 1f, normalizedRadius);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        shieldSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        shieldSprite.name = "Runtime Heavy Enemy Shield";
        shieldSprite.hideFlags = HideFlags.HideAndDontSave;
        return shieldSprite;
    }

    private static Sprite GetShieldOutlineSprite()
    {
        if (shieldOutlineSprite != null)
        {
            return shieldOutlineSprite;
        }

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Runtime Heavy Enemy Shield Outline",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[size * size];
        Vector2 centre = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.48f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float normalizedRadius = Vector2.Distance(new Vector2(x, y), centre) / radius;
                float outerEdge = 1f - Mathf.SmoothStep(0.985f, 1f, normalizedRadius);
                float innerEdge = Mathf.SmoothStep(0.91f, 0.94f, normalizedRadius);
                pixels[y * size + x] = new Color(1f, 1f, 1f, outerEdge * innerEdge);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        shieldOutlineSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        shieldOutlineSprite.name = "Runtime Heavy Enemy Shield Outline";
        shieldOutlineSprite.hideFlags = HideFlags.HideAndDontSave;
        return shieldOutlineSprite;
    }

    private static Material GetShieldMaterial()
    {
        if (shieldMaterial != null)
        {
            return shieldMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        shieldMaterial = new Material(shader)
        {
            name = "Runtime Heavy Enemy Shield Material",
            hideFlags = HideFlags.HideAndDontSave
        };
        return shieldMaterial;
    }
}
