using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    private Vector2 currentPosition, lastPosition, positionDifference;
    private bool parallaxNow = true;
    public float speed;
    //background layers must be positve
    //foreground layers must be negative
    void Start()
    {
        
    }

    // Update is called once per frame
    void LateUpdate()
    {
        if (parallaxNow)
        {
            DetectCameraMovement();
            transform.Translate(new Vector3(positionDifference.x, positionDifference.y, 0) * speed * Time.deltaTime, Space.World);
        }
    }

    void DetectCameraMovement()
    {
        currentPosition = new Vector2(Camera.main.transform.position.x, Camera.main.transform.position.y);

        if (currentPosition == lastPosition)
        {
            positionDifference = Vector2.zero;
        }
        else
        {
            positionDifference = currentPosition - lastPosition;
        }

        lastPosition = currentPosition;
    }

    void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.CompareTag("MainCamera"))
        {
            lastPosition = currentPosition = new Vector2(Camera.main.transform.position.x, Camera.main.transform.position.y);
            parallaxNow = true;
        }
    }

    private void OnTriggerStay2D(Collider2D collision)
    {
        if (collision.CompareTag("MainCamera"))
        {
            parallaxNow = true;
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (collision.CompareTag("MainCamera"))
        {
            positionDifference = Vector2.zero;
            parallaxNow = false;
        }
    }
}

