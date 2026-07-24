using UnityEngine;
using UnityEngine.UIElements;

public class CalculateCameraBox : MonoBehaviour
{
    private Camera cam;
    private BoxCollider2D camBox;
    private float sizeX, sizeY, ratio;
    void Start()
    {
        cam = GetComponent<Camera>();
        camBox = GetComponent<BoxCollider2D>();
        sizeY = cam.orthographicSize * 2f;
        ratio = (float)Screen.width / (float)Screen.height;
        sizeX = sizeY * ratio;

        camBox.size = new Vector2(sizeX, sizeY);
    }

    // Update is called once per frame
    void Update()
    {
        UpdateCameraBox();
    }

    public void UpdateCameraBox()
    {
        sizeY = cam.orthographicSize * 2f;
        ratio = (float)Screen.width / (float)Screen.height;
        sizeX = sizeY * ratio;
        camBox.size = new Vector2(sizeX, sizeY);
    }
}
