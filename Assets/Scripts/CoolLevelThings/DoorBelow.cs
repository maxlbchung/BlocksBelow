using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public class DoorBelow : MonoBehaviour
{
    [SerializeField, Min(0)] private int numberOfBlocksToOpen;
    [SerializeField] private int blocksBelow;

    private BoxCollider2D doorCollider;
    private bool doorOpened;

    public int BlocksBelow => blocksBelow;

    private void Awake()
    {
        doorCollider = GetComponent<BoxCollider2D>();
    }

    private void Update()
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(transform.position, Vector2.down);

        blocksBelow = 0;

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider != null && hit.collider.CompareTag("block"))
            {
                blocksBelow++;
            }
        }

        if (!doorOpened && blocksBelow >= numberOfBlocksToOpen)
        {
            doorOpened = true;
            OpenDoor();
        }
        else if (doorOpened && blocksBelow < numberOfBlocksToOpen)
        {
            doorOpened = false;
            CloseDoor();
        }
    }

    private void OpenDoor()
    {
        doorCollider.enabled = false;
    }

    private void CloseDoor()
    {
        doorCollider.enabled = true;
    }
}
