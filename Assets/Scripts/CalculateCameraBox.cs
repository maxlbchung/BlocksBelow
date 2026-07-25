using UnityEngine;

/// <summary>
/// Tracks the camera's viewport size in world units.
/// <para>
/// This used to publish the size by writing it into a <see cref="BoxCollider2D"/> every frame.
/// The camera carries no <see cref="Rigidbody2D"/>, so that collider was static, and rewriting
/// a static collider's shape - while <see cref="CameraFollow"/> also moved it every frame -
/// made 2D physics rebuild its broadphase entry on every step for a value that only changes
/// when the window is resized. The size now lives in <see cref="Size"/>, and the collider is
/// only touched if one happens to be attached (other scenes use it as a camera trigger volume).
/// </para>
/// </summary>
[RequireComponent(typeof(Camera))]
public class CalculateCameraBox : MonoBehaviour
{
    private Camera cam;
    private BoxCollider2D camBox;
    private Vector2 size;
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private float lastOrthographicSize = -1f;

    /// <summary>The camera's visible area in world units.</summary>
    public Vector2 Size
    {
        get
        {
            Refresh();
            return size;
        }
    }

    private void Awake()
    {
        cam = GetComponent<Camera>();
        camBox = GetComponent<BoxCollider2D>();
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    /// <summary>Recomputes the size, but only when something it depends on actually moved.</summary>
    public void UpdateCameraBox()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (cam == null)
        {
            cam = GetComponent<Camera>();
            if (cam == null)
            {
                return;
            }
        }

        int screenWidth = Screen.width;
        int screenHeight = Screen.height;
        float orthographicSize = cam.orthographicSize;

        if (screenWidth == lastScreenWidth
            && screenHeight == lastScreenHeight
            && orthographicSize == lastOrthographicSize)
        {
            return;
        }

        lastScreenWidth = screenWidth;
        lastScreenHeight = screenHeight;
        lastOrthographicSize = orthographicSize;

        float sizeY = orthographicSize * 2f;
        float ratio = screenHeight > 0 ? screenWidth / (float)screenHeight : 1f;
        size = new Vector2(sizeY * ratio, sizeY);

        if (camBox != null)
        {
            camBox.size = size;
        }
    }
}
