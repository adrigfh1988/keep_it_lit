using System.Collections.Generic;

/// <summary>
/// F118: the result of <see cref="MazeGenerator.CheckReachability"/>. Plain data (no Unity types), so tests can read it and the
/// debug map can format it.
/// </summary>
public sealed class ReachabilityReport
{
    /// <summary>Stars + untaken keys + fuse boxes + buttons (+ gate cells on a gate floor) that were checked.</summary>
    public int Targets;
    public int CellsBeforeButtons;
    public int CellsReached;
    public int GateCount;

    // Coverage, for the test's summary line.
    public int Stars, Keys, FuseBoxes, Buttons, Doors;

    /// <summary>Targets that failed a leg, as "kind (x, y)" for the grid leg and "kind (x, y) [navmesh]" for the NavMesh leg.</summary>
    public readonly List<string> Missing = new List<string>();

    /// <summary>NavMesh-only failures whose grid path crosses the squeeze (the NavMesh is baked for the hunter, the squeeze is for the player). Informational, not a failure.</summary>
    public readonly List<string> NavMissing = new List<string>();
}
