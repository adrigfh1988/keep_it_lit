using UnityEngine;

/// <summary>
/// F105 C5, the maze side of the lure star: which star it is, and the dried blood under it. What taking it does lives in
/// DreadDirector (HandleLureTaken) and AIFollower.StartLureChase.
/// </summary>
public partial class MazeGenerator
{
    // Salts the lure pick apart from PickBaseMusic's ("MUSI") and the objective roll's, so each is its own draw from the seed.
    private const int LureSeedSalt = 0x4C555245; // "LURE"
    private const string BloodPoolName = "BloodPool";
    private const int LureDecalSalt = 7;

    [Header("F105 C5 - lure star")]
    [Tooltip("The first floor with a lure star: one star on it is a trap (taking it brings the hunter and kills the lamps around it)")]
    [SerializeField, Min(1)] private int lureFromFloor = 3;
    [Tooltip("A star within this horizontal distance of a pickup position counts as that pickup (the star bobs, so the match is tolerant) (m)")]
    [SerializeField, Min(0.1f)] private float lureMatchRadius = 0.6f;

    /// <summary>F105 C5: this floor's lure star (a dried-blood decal lies under it), or null - floors before lureFromFloor, the flag off, or a maze with fewer than two stars. Becomes a destroyed object once taken.</summary>
    public Transform LureStar { get; private set; }

    /// <summary>F105 C5: true if a pickup at `at` (Pickup.OnCollectedAt) is the lure star being taken.</summary>
    public bool IsLure(Vector3 at)
    {
        if (LureStar == null) return false;

        Vector3 flat = LureStar.position - at;
        flat.y = 0f;
        return flat.sqrMagnitude <= lureMatchRadius * lureMatchRadius;
    }

    /// <summary>
    /// Picks the lure from the stars SpawnStars just placed: the first A10 bait dead-end star if there is one (the bet already on offer
    /// becomes a trap), otherwise one chosen by a hash of (seed, floor). No rng draw, so every stream after SpawnStars is untouched.
    /// </summary>
    private void ChooseLureStar()
    {
        LureStar = null;
        if (!terror.lureStars || _profile == null || _profile.Floor < lureFromFloor || _stars.Count < 2) return;

        int index = -1;
        foreach (Vector2Int bait in _baitDeadEnds)
        {
            index = _starCells.IndexOf(bait);
            if (index >= 0) break;
        }

        bool onBait = index >= 0;
        if (!onBait)
        {
            int hash = RunRules.HashSeedFloor(UsedSeed ^ LureSeedSalt, GameFlow.CurrentFloor);
            index = ((hash % _stars.Count) + _stars.Count) % _stars.Count;
        }

        LureStar = _stars[index];
        BuildLureDecal(_starCells[index]);
        terror.Log($"Lure star at {_starCells[index]}{(onBait ? " (bait dead end)" : "")}", this);
    }

    /// <summary>The dried-blood pool under the lure: the theme's own BloodPool floor decal at its largest size, so a second look reads it. No decal in the theme means no decal (logged).</summary>
    private void BuildLureDecal(Vector2Int cell)
    {
        if (PlaceBloodPool(CellCenter(cell.x, cell.y), cell, LureSeedSalt, _decalsGroup, $"Decal_LureBlood_{cell.x}_{cell.y}") == null)
            terror.Log($"The lure star at {cell} has no blood decal: this theme lists no {BloodPoolName} floor decal", this);
    }

    /// <summary>
    /// The theme's BloodPool floor decal at its largest size, flat on the floor at `floorPosition`. Its yaw and look-alike variant come from a hash
    /// of `key` and `salt` (no rng draw), so the same place always shows the same stain. Null when the theme lists none.
    /// </summary>
    private PropPiece PlaceBloodPool(Vector3 floorPosition, Vector2Int key, int salt, Transform parent, string decalName)
    {
        PropPiece pool = FindThemeDecal(PropPiece.MountKind.FloorDecal, BloodPoolName);
        if (pool == null) return null;

        int hash = (key.x * 73856093) ^ (key.y * 19349663) ^ salt;
        Quaternion yaw = Quaternion.Euler(0f, (hash & 0xFF) * (360f / 256f), 0f);
        PropPiece decal = Instantiate(pool, floorPosition + Vector3.up * 0.008f, yaw, parent);
        decal.gameObject.SetActive(true);
        decal.transform.localScale = Vector3.one * pool.DecalSizeRange.y;
        decal.name = decalName;
        ApplyDecalVariant(decal, key.x, key.y, LureDecalSalt, 1);
        return decal;
    }

    /// <summary>The theme's enabled decal of this mount kind whose name ends in `suffix` (BloodPool, BloodSmear...), or null. F106 shares it with the loop corridor's handprint.</summary>
    private PropPiece FindThemeDecal(PropPiece.MountKind kind, string suffix)
    {
        if (_dressing.Props == null) return null;

        foreach (PropPiece prop in _dressing.Props)
        {
            if (prop != null && prop.Mount == kind && prop.EnabledInMaze && prop.name.EndsWith(suffix)) return prop;
        }

        return null;
    }
}
