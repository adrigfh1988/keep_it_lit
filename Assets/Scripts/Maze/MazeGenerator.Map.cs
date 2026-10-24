using System.Collections.Generic;
using UnityEngine;

// Partial of MazeGenerator: ASCII debug map, MazeMapData snapshot builder (F106/F110), and player map coordinates.
public partial class MazeGenerator
{
    /// <summary>
    /// Debug: the current maze as ASCII, north up, for debugging sessions (KEEP IT LIT &gt; Debug &gt; Print Maze Map, this
    /// component's context menu, or <c>unity command eval --code 'return UnityEngine.Object.FindAnyObjectByType&lt;MazeGenerator&gt;().DebugMap();'</c>).
    /// Draws the walls and the doors (each door's id on its wall), and marks the live stars, the keys still lying about,
    /// the buttons (b + the id of the door they open), the start, the player, the hunter and an open hatch; a legend
    /// below lists every door with its kind, state and button. Play mode only - the maze is built at runtime. Read it in
    /// a monospace font: the Console is not one, which is why the menu item also copies it to the clipboard.
    /// </summary>
    public string DebugMap()
    {
        MazeMapData data = BuildMapData();
        if (data == null) return "MazeGenerator.DebugMap: no maze yet - it is built when Play starts.";

        // Doors: their labels are on the snapshot; looked up from either side of the passage.
        Dictionary<(Vector2Int, int), char> doorGlyphs = new Dictionary<(Vector2Int, int), char>();
        foreach (MazeMapData.DoorInfo door in data.Doors)
        {
            doorGlyphs[(door.CellA, door.Dir)] = door.Id;
            doorGlyphs[(door.CellB, (door.Dir + 2) % 4)] = door.Id;
        }

        // Cell marks, most important first; a cell shows at most three characters (the legend lists everything).
        string[,] marks = new string[width, height];
        if (data.HasPlayer) marks[data.PlayerCell.x, data.PlayerCell.y] += "@";
        if (aiFollower != null && TryWorldToCell(aiFollower.transform.position, out Vector2Int hunterCell)) marks[hunterCell.x, hunterCell.y] += "H";
        foreach (Vector2Int star in data.Stars) marks[star.x, star.y] += "*";
        foreach (MazeMapData.KeyInfo key in data.Keys)
        {
            if (!key.Taken) marks[key.Cell.x, key.Cell.y] += "k";
        }
        foreach (MazeMapData.ButtonInfo button in data.Buttons) marks[button.Cell.x, button.Cell.y] += "b" + button.DoorId;
        if (data.HatchOpen) marks[data.HatchCell.x, data.HatchCell.y] += "E";
        marks[_startCell.x, _startCell.y] += "S";
        AddLayoutMarks(marks); // F105 C2/C3: r / g / q, only on a cell with no other mark
        AddLoopMark(marks); // F105 C4: L
        AddEntityMarks(marks); // F107 D: l t b d
        AddNightmareMarks(marks); // F123: m a h

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        FloorProfile.FloorShape shape = _profile != null ? _profile.Shape : FloorProfile.FloorShape.Rectangle;
        FloorProfile.MazeMorphology morph = _profile != null ? _profile.Morphology : FloorProfile.MazeMorphology.Standard;
        sb.Append($"Maze seed {UsedSeed}, floor {GameFlow.CurrentFloor}, {width}x{height} ({_playableCellCount} playable, shape {shape}, morphology {morph}), north up, start S = ({_startCell.x},{_startCell.y}).\n");

        // Column numbers over each cell's interior, row numbers beside it.
        sb.Append("    ");
        for (int x = 0; x < width; x++) sb.Append(' ').Append(x.ToString().PadLeft(2)).Append(' ');
        sb.Append('\n');

        for (int z = height - 1; z >= 0; z--)
        {
            AppendDebugBorder(sb, z, 0, doorGlyphs);
            sb.Append(z.ToString().PadLeft(2)).Append("  ");
            for (int x = 0; x < width; x++)
            {
                sb.Append(DebugWallGlyph(x, z, 3, '|', doorGlyphs));
                if (IsVoid(x, z))
                {
                    sb.Append(" · ");
                }
                else
                {
                    string m = marks[x, z] ?? string.Empty;
                    sb.Append(m.Length > 3 ? m.Substring(0, 3) : m.PadRight(3));
                }
            }
            sb.Append(DebugWallGlyph(width - 1, z, 1, '|', doorGlyphs)).Append('\n');
        }
        AppendDebugBorder(sb, 0, 2, doorGlyphs);

        sb.Append("\n* star   k key   bN button for door N   S start   @ you   H hunter   E hatch   G exit gate   digit/letter on a wall = door\n");
        AppendDebugLegend(sb, data);
        AppendMorphologyLegend(sb); // F127
        AppendLayoutLegend(sb);
        AppendLoopLegend(sb);
        AppendGateLegend(sb); // F111
        AppendEntityLegend(sb); // F107 D
        AppendNightmareLegend(sb); // F123
        return sb.ToString();
    }

    /// <summary>A row of corners and north (dir 0) or south (dir 2) walls: "---" closed, "-N-" door N, blank open.</summary>
    private void AppendDebugBorder(System.Text.StringBuilder sb, int z, int dir, Dictionary<(Vector2Int, int), char> doorGlyphs)
    {
        sb.Append("    ");
        for (int x = 0; x < width; x++)
        {
            sb.Append('+');
            char glyph = DebugWallGlyph(x, z, dir, '-', doorGlyphs);
            sb.Append(glyph == '-' ? "---" : glyph == ' ' ? "   " : "-" + glyph + "-");
        }
        sb.Append("+\n");
    }

    /// <summary>The character for one wall of a cell: the door's id when a door sits in it, `closed` for a wall, a space for a passage.</summary>
    private char DebugWallGlyph(int x, int z, int dir, char closed, Dictionary<(Vector2Int, int), char> doorGlyphs)
    {
        if (doorGlyphs.TryGetValue((new Vector2Int(x, z), dir), out char id)) return id;
        if (IsGateOpening(x, z, dir)) return 'G'; // F111: the gate stands in this outer wall
        return IsWallClosed(x, z, dir) ? closed : ' ';
    }

    /// <summary>Doors (kind, cells, state, the button that opens them), then the stars and keys with their cells.</summary>
    private void AppendDebugLegend(System.Text.StringBuilder sb, MazeMapData data)
    {
        foreach (MazeMapData.DoorInfo door in data.Doors)
        {
            sb.Append($"door {door.Id}: {door.Kind} between {door.CellA} and {door.CellB}, {(door.Open ? "open" : "closed")}{(door.Unlocked ? "" : ", locked")}{(door.Vault ? ", vault" : "")}");
            foreach (MazeMapData.ButtonInfo button in data.Buttons)
            {
                if (button.DoorId == door.Id) sb.Append($", button at {button.Cell}");
            }
            if (door.LinkedId != '\0') sb.Append($", also opens door {door.LinkedId}");
            sb.Append('\n');
        }

        sb.Append("stars left:");
        foreach (Vector2Int star in data.Stars) sb.Append(' ').Append(star);
        sb.Append($"   (taken {data.StarsTotal - data.Stars.Count} of {data.StarsTotal})\n");

        if (data.Keys.Count > 0)
        {
            sb.Append("keys:");
            foreach (MazeMapData.KeyInfo key in data.Keys) sb.Append(' ').Append(key.Cell).Append(key.Taken ? " taken" : "");
            sb.Append('\n');
        }
    }

    // ---------------------------------------------------------------- map data (F106)

    private Transform _mapPlayer;
    private Transform _mapCamera;
    private MazeEscape _mapEscape;

    /// <summary>
    /// F106: the floor as one snapshot for DebugMap's text and FloorMapUi's picture - the walls, the doors with their state, the live stars,
    /// the keys, the buttons, the open hatch, and the player's cell and heading (the hunter is not in it). Pass the previous snapshot
    /// as `into` to refill it in place instead of allocating a new one. Null before the maze exists (Play starts it).
    /// </summary>
    public MazeMapData BuildMapData(MazeMapData into = null)
    {
        if (_wallN == null) return null;

        MazeMapData data = into ?? new MazeMapData();
        data.Begin(width, height, UsedSeed, GameFlow.CurrentFloor, _wallN, _wallE, _wallS, _wallW);

        for (int i = 0; i < _doors.Count; i++)
        {
            MazeDoor door = _doors[i];
            if (door == null || !_doorPassages.TryGetValue(door, out Passage p)) continue;

            int linked = door.LinkedDoor != null ? _doors.IndexOf(door.LinkedDoor) : -1;
            data.Doors.Add(new MazeMapData.DoorInfo
            {
                Id = DoorLabel(i),
                CellA = p.CellA,
                CellB = p.CellB,
                Dir = p.Dir,
                Kind = door.DoorKind,
                Open = door.IsOpen,
                Unlocked = door.IsUnlocked,
                Vault = door.IsVaultDoor,
                LinkedId = linked >= 0 ? DoorLabel(linked) : '\0'
            });
        }

        foreach (WallButton button in _buttons)
        {
            if (button == null || !TryWorldToCell(button.transform.position, out Vector2Int cell)) continue;

            int doorIndex = button.Door != null ? _doors.IndexOf(button.Door) : -1;
            data.Buttons.Add(new MazeMapData.ButtonInfo { Cell = cell, DoorId = doorIndex >= 0 ? DoorLabel(doorIndex) : '?' });
        }

        data.StarsTotal = _stars.Count;
        foreach (Transform star in _stars)
        {
            if (star != null && TryWorldToCell(star.position, out Vector2Int cell)) data.Stars.Add(cell);
        }

        foreach (KeyPickup key in _keys)
        {
            if (key != null && TryWorldToCell(key.transform.position, out Vector2Int cell)) data.Keys.Add(new MazeMapData.KeyInfo { Cell = cell, Taken = key.IsTaken });
        }

        if (_mapEscape == null) _mapEscape = FindAnyObjectByType<MazeEscape>();
        if (!HasExitGates && _mapEscape != null && _mapEscape.HatchOpen && TryWorldToCell(_mapEscape.HatchPosition, out Vector2Int hatch))
        {
            data.HatchOpen = true;
            data.HatchCell = hatch;
        }

        EnsureMapPlayer();

        if (_mapPlayer != null && TryWorldToCell(_mapPlayer.position, out Vector2Int playerCell))
        {
            data.HasPlayer = true;
            data.PlayerCell = playerCell;
            data.PlayerPosition = new Vector2((_mapPlayer.position.x - origin.x) / cellSize, (_mapPlayer.position.z - origin.z) / cellSize);
            Vector3 forward = _mapCamera.forward;
            data.PlayerYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        return data;
    }

    private void EnsureMapPlayer()
    {
        if (_mapPlayer != null) return;

        _mapPlayer = FindPlayer();
        FirstPersonRig rig = _mapPlayer != null ? _mapPlayer.GetComponent<FirstPersonRig>() : null;
        _mapCamera = rig != null && rig.PlayerCamera != null ? rig.PlayerCamera.transform : _mapPlayer;
    }

    /// <summary>
    /// F110: the player in map units (cell units from the start cell's corner, so (3.5, 2.5) is the centre of cell (3, 2)) and the way the body faces,
    /// degrees clockwise from north. The first-person controller locks the body's yaw to the camera's, so the yaw is steady looking straight up or down.
    /// False when the player is not inside the maze.
    /// </summary>
    public bool TryGetPlayerOnMap(out Vector2 cellUnits, out float yaw)
    {
        cellUnits = default;
        yaw = 0f;
        EnsureMapPlayer();
        if (_mapPlayer == null || !TryWorldToCell(_mapPlayer.position, out _)) return false;

        cellUnits = new Vector2((_mapPlayer.position.x - origin.x) / cellSize, (_mapPlayer.position.z - origin.z) / cellSize);
        yaw = _mapPlayer.eulerAngles.y;
        return true;
    }

    /// <summary>The label of the `index`-th door on the maps: 1-9, then A-Z.</summary>
    private static char DoorLabel(int index)
    {
        return index < 9 ? (char)('1' + index) : (char)('A' + index - 9);
    }

    /// <summary>Right-click the component in Play: logs DebugMap and copies it to the clipboard (paste into anything monospace).</summary>
    [ContextMenu("Debug: Print Maze Map")]
    private void PrintDebugMap()
    {
        string map = DebugMap();
        GUIUtility.systemCopyBuffer = map;
        Debug.Log(map, this);
    }
}
