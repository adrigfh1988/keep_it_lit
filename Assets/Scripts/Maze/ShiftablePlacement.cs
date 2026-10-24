using UnityEngine;

/// <summary>
/// F104 B9/B10: one decorated placement MazeGenerator recorded while dressing the maze - a set piece, or a wall or ceiling prop
/// with look-alike models. WorldShiftDirector reads these to quietly change what the player has already seen (B9: switch to the
/// next look-alike while unobserved) or to copy a set piece somewhere else (B10). Only dressing is ever recorded: stars, keys,
/// doors, lockers, fuses and pickups are never shiftable. Plain value type, so it shares nothing with the scene.
/// </summary>
public readonly struct ShiftablePlacement
{
    /// <summary>The placed clone's root.</summary>
    public readonly Transform Root;

    /// <summary>Its look-alike models, or null when it has none (a set piece without variants).</summary>
    public readonly ModelVariants Variants;

    /// <summary>The cell it stands in.</summary>
    public readonly Vector2Int Cell;

    /// <summary>World direction of the wall it is mounted on (from the cell centre outward), or zero for a ceiling prop.</summary>
    public readonly Vector3 WallDir;

    /// <summary>True for a set piece (the only kind B10 may copy).</summary>
    public readonly bool IsSetPiece;

    /// <summary>Records a placement.</summary>
    public ShiftablePlacement(Transform root, ModelVariants variants, Vector2Int cell, Vector3 wallDir, bool isSetPiece)
    {
        Root = root;
        Variants = variants;
        Cell = cell;
        WallDir = wallDir;
        IsSetPiece = isSetPiece;
    }
}
