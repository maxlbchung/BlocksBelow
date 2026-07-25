using System.Net;
using Unity.VisualScripting;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    private Transform player;
    private CalculateCameraBox cameraBox;
    private Camera cam;
    private GameObject[] boundaries;
    private Bounds[] allBounds;
    private Bounds targetBounds;

    public float speed;
    private float waitForSeconds = 0.5f;

    /// <summary>
    /// The camera's visible area in world units. This used to be read off a BoxCollider2D on
    /// the camera; that collider was static and being moved and resized every frame, so the
    /// size is now published by <see cref="CalculateCameraBox"/> instead.
    /// </summary>
    private Vector2 ViewSize
    {
        get
        {
            if (cameraBox != null)
            {
                return cameraBox.Size;
            }

            if (cam == null)
            {
                cam = GetComponent<Camera>();
            }

            if (cam == null)
            {
                return Vector2.zero;
            }

            float height = cam.orthographicSize * 2f;
            float ratio = Screen.height > 0 ? Screen.width / (float)Screen.height : 1f;
            return new Vector2(height * ratio, height);
        }
    }

    void Start()
    {
        if (GameObject.Find("Player") != null)
            player = GameObject.Find("Player").GetComponent<Transform>();
        cameraBox = GetComponent<CalculateCameraBox>();
        cam = GetComponent<Camera>();
        FindLimits();
    }

    void LateUpdate()
    {
        if (player == null)
        {
            if (GameObject.Find("Player") != null)
                player = GameObject.Find("Player").GetComponent<Transform>();
            return;
        }
        if (waitForSeconds > 0)
        {
            waitForSeconds -= Time.deltaTime;
        }
        else
        {
            SetOneLimit();
            FollowPlayer();
        }
    }

    void FindLimits()
    {//Finds all limits of the stage environment.
        boundaries = GameObject.FindGameObjectsWithTag("Boundary");
        allBounds = new Bounds[boundaries.Length];
        for (int i = 0; i < boundaries.Length; i++)
        {
            allBounds[i] = boundaries[i].gameObject.GetComponent<BoxCollider2D>().bounds;
        }
    }

    void SetOneLimit()
    {//Sets limits on the camera based on which boundary the player is located in.
        for (int i = 0; i < allBounds.Length; i++)
        {
            if (player.position.x > allBounds[i].min.x && player.position.x < allBounds[i].max.x && player.position.y > allBounds[i].min.y && player.position.y < allBounds[i].max.y)
            {
                targetBounds = allBounds[i];
                return;
            }
        }
    }

    void FollowPlayer()
    {
        Vector2 viewSize = ViewSize;

        float xTarget = viewSize.x < targetBounds.size.x ? Mathf.Clamp(player.position.x, targetBounds.min.x + viewSize.x / 2, targetBounds.max.x - viewSize.x / 2) : (targetBounds.min.x + targetBounds.max.x) / 2;
        float yTarget = viewSize.y < targetBounds.size.y ? Mathf.Clamp(player.position.y, targetBounds.min.y + viewSize.y / 2, targetBounds.max.y - viewSize.y / 2) : (targetBounds.min.y + targetBounds.max.y) / 2;
        Vector3 target = new Vector3(xTarget, yTarget, transform.position.z);

        transform.position = Vector3.Lerp(transform.position, target, speed * Time.deltaTime);
    }
}