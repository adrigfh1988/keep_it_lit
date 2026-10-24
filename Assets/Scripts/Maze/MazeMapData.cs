using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F106: a snapshot of the floor for anything that draws it - the text map (MazeGenerator.DebugMap) and the floor map the loop puzzle
/// rewards (FloorMapUi). Filled by MazeGenerator.BuildMapData, the one place that collects it. Everything is in maze cells (x east, z north,
/// the start cell is (0, 0)); the hunter is deliberately not in it. A snapshot can be refilled in place, so a screen redrawing it twice a second
/// allocates nothing. The wall arrays are the maze's own, shared not copied: they never change once the maze is built.
/// </summary>
public sealed class MazeMapData
{
    /// <summary>A door sitting in the passage on side `Dir` (0 north, 1 east, 2 south, 3 west) of `CellA`.</summary>
    public struct DoorInfo
    {
        /// <summary>The door's label: 1-9, then A-Z, in the order the maze built them.</summary>
        public char Id;
        public Vector2Int CellA;
        public Vector2Int CellB;
        public int Dir;
        public MazeDoor.Kind Kind;
        public bool Open;
        public bool Unlocked;
        public bool Vault;
        /// <summary>The label of the door this one also opens, or '\0'.</summary>
        public char LinkedId;
    }

    /// <summary>A wall button and the label of the door it opens ('?' when it opens none the map knows).</summary>
    public struct ButtonInfo
    {
        public Vector2Int Cell;
        public char DoorId;
    }

    /// <summary>A key lying in a cell; a taken one stays listed so the text legend can say so.</summary>
    public struct KeyInfo
    {
        public Vector2Int Cell;
        public bool Taken;
    }

    private bool[,] _wallN, _wallE, _wallS, _wallW;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Seed { get; private set; }
    public int Floor { get; private set; }

    public readonly List<DoorInfo> Doors = new List<DoorInfo>();
    public readonly List<ButtonInfo> Buttons = new List<ButtonInfo>();
    public readonly List<KeyInfo> Keys = new List<KeyInfo>();
    /// <summary>The cells of the stars still on the floor.</summary>
    public readonly List<Vector2Int> Stars = new List<Vector2Int>();
    public int StarsTotal { get; set; }

    /// <summary>True while the escape hatch or lift is open, at `HatchCell`.</summary>
    public bool HatchOpen { get; set; }
    public Vector2Int HatchCell { get; set; }

    /// <summary>False when the player is not inside the maze (in the shop, the loop corridor...).</summary>
    public bool HasPlayer { get; set; }
    public Vector2Int PlayerCell { get; set; }
    /// <summary>The player in cell units: (3.5, 2.5) is the centre of cell (3, 2).</summary>
    public Vector2 PlayerPosition { get; set; }
    /// <summary>Which way they face, degrees clockwise from north (the +Z world axis).</summary>
    public float PlayerYaw { get; set; }

    /// <summary>True when the wall on side `dir` of cell (x, z) is closed. A door's passage is open here; the door is in <see cref="Doors"/>.</summary>
    public bool IsWallClosed(int x, int z, int dir)
    {
        switch (dir)
        {
            case 0: return _wallN[x, z];
            case 1: return _wallE[x, z];
            case 2: return _wallS[x, z];
            default: return _wallW[x, z];
        }
    }

    /// <summary>Empties the lists and points the snapshot at this maze's walls, ready to be filled.</summary>
    public void Begin(int width, int height, int seed, int floor, bool[,] wallN, bool[,] wallE, bool[,] wallS, bool[,] wallW)
    {
        Width = width;
        Height = height;
        Seed = seed;
        Floor = floor;
        _wallN = wallN;
        _wallE = wallE;
        _wallS = wallS;
        _wallW = wallW;
        Doors.Clear();
        Buttons.Clear();
        Keys.Clear();
        Stars.Clear();
        StarsTotal = 0;
        HatchOpen = false;
        HasPlayer = false;
    }
}
