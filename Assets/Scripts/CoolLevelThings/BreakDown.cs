using System.Collections.Generic;
using UnityEngine;

public class BreakDown : MonoBehaviour
{
    [SerializeField, Min(0)] private int numberOfBlocksToBreak;
    [SerializeField] private int blocksAbove;

    private readonly HashSet<GameObject> countedBlocks = new();
    private bool hasBroken;

    public int BlocksAbove => blocksAbove;

    private void Update()
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(transform.position, Vector2.up);
        int wallLayer = LayerMask.NameToLayer("Wall");

        countedBlocks.Clear();

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (wallLayer >= 0 && hit.collider.gameObject.layer == wallLayer)
            {
                break;
            }

            if (hit.collider.gameObject.layer != wallLayer
                && hit.collider.CompareTag("block"))
            {
                countedBlocks.Add(hit.collider.gameObject);
            }
        }

        blocksAbove = countedBlocks.Count;

        if (!hasBroken && blocksAbove >= numberOfBlocksToBreak)
        {
            hasBroken = true;
            Break();
        }
    }

    private void Break()
    {
        Debug.Log("BREAK!");
    }
}
