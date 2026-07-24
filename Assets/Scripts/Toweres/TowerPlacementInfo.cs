using UnityEngine;

/// <summary>
/// Placement traits a tower carries on its own prefab, so the shop and the grid
/// placer do not need to know which behaviour script is attached.
/// </summary>
public class TowerPlacementInfo : MonoBehaviour
{
    [Tooltip("Can be turned with R, and shows an aim indicator while being placed.")]
    [SerializeField] private bool rotatable;

    [Tooltip("The direction this tower fires or pushes toward before any rotation.")]
    [SerializeField] private Vector2 aimDirection = Vector2.left;

    [Tooltip("A support piece may be placed without a cage directly beneath it, the way cages and scaffolding are.")]
    [SerializeField] private bool supportPiece;

    [Tooltip("The player can stand inside this piece, so its cell stays placeable while the player is in it.")]
    [SerializeField] private bool walkThrough;

    public bool Rotatable => rotatable;
    public Vector2 AimDirection => aimDirection;
    public bool SupportPiece => supportPiece;
    public bool WalkThrough => walkThrough;
}
