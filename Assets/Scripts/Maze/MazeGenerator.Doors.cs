using System.Collections.Generic;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public partial class MazeGenerator
{
    // ---------------------------------------------------------------- doors, buttons & vaults (F72)

    /// <summary>One open passage between two adjacent cells, as ReachableCells/CreateDoor key off it.</summary>
    private struct Passage
    {
        public Vector2Int CellA;
        public int Dir;
        public Vector2Int CellB;
    }

    /// <summary>
    /// Shutter doors, their buttons, and vault rooms. Post-bake (a door needs no NavMeshObstacle - see
    /// MazeDoor) and run between SpawnStars and SpawnShards: security placement tests whether blocking
    /// a passage actually cuts a star cell off (needs _starCells), and a vault's forced shards must be
    /// known before SpawnShards decides where the rest of the floor's shards go.
    /// </summary>
    private void BuildDoors(System.Random rng)
    {
        _vaultForcedShardCells.Clear();
        _doors.Clear();
        _doorPassages.Clear();
        _buttons.Clear();
        _batteryPickups.Clear();

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasDoorsAndButtons)
        {
            Debug.LogError("MazeGenerator: no InteractableKit (doors/buttons/battery cell) in the scene - skipping doors, buttons and vaults. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        // Super easy mode: nothing may shut a star away, so no doors at all (a Lockdown floor has no lockdown).
        if (superEasyMode) return;

        if (aiFollower == null) aiFollower = FindAnyObjectByType<AIFollower>();
        Transform player = FindPlayer();

        int wantSecurity = _profile != null ? _profile.SecurityDoors : 0;
        int wantManual = _profile != null ? _profile.ManualDoors : 1;
        int wantVaults = _profile != null ? _profile.Vaults : 0;
        bool wantTimed = _profile != null && _profile.HasTimedDoor;

        // Kept in a field (not a local), so BuildFuses (+12, after this) can avoid every cell a
        // door/button/vault already used - see the field doc comment.
        _doorUsedCells.Clear();
        HashSet<Vector2Int> usedCells = _doorUsedCells;
        usedCells.UnionWith(_squeezeCells); // F105 C3: no shutter, button or vault in the squeeze
        if (_loopActive) usedCells.Add(_loopEntrance); // F105 C4: nor at the loop entrance

        List<Passage> placedDoorPassages = new List<Passage>();

        List<(MazeDoor door, Vector2Int startCell)> securityDoors =
            PlaceSecurityDoors(rng, wantSecurity, player, placedDoorPassages, usedCells);

        if (wantTimed && securityDoors.Count > 0)
        {
            MazeDoor farthest = null;
            int best = -1;
            foreach ((MazeDoor door, Vector2Int startCell) entry in securityDoors)
            {
                int dist = _distance[entry.startCell.x, entry.startCell.y];
                if (dist <= best) continue;
                best = dist;
                farthest = entry.door;
            }
            // A sealed star room is a LinkedDoor chain: convert every shutter in it, so the button's
            // Unlock starts the same 25 s timer on each and they all roll down together.
            for (MazeDoor d = farthest; d != null; d = d.LinkedDoor) d.ConvertToTimed();
        }

        // Decision D15: vault loot (the Second Wind roll) gets its own +15 stream, separate from +11's
        // door/button placement - so changing how many security doors a floor has never reshuffles
        // whether a vault happens to grant a Second Wind.
        System.Random lootRng = new System.Random(UsedSeed + 15);
        PlaceVaults(rng, lootRng, wantVaults, player, placedDoorPassages, usedCells);
        PlaceManualDoors(rng, wantManual, player, placedDoorPassages, usedCells);
    }

    /// <summary>Every open interior passage, each appearing exactly once (dir 0/North and 1/East from every cell covers every edge - the mirror image from the other cell's dir 2/South or 3/West would just be the same edge again).</summary>
    private List<Passage> AllOpenPassages()
    {
        List<Passage> list = new List<Passage>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                if (z + 1 < height && !IsVoid(x, z + 1) && !_wallN[x, z]) list.Add(new Passage { CellA = new Vector2Int(x, z), Dir = 0, CellB = new Vector2Int(x, z + 1) });
                if (x + 1 < width && !IsVoid(x + 1, z) && !_wallE[x, z]) list.Add(new Passage { CellA = new Vector2Int(x, z), Dir = 1, CellB = new Vector2Int(x + 1, z) });
            }
        }
        return list;
    }

    private static bool IsSamePassage(Vector2Int cell, int dir, Passage p)
    {
        Vector2Int neighbour = Neighbour(cell, dir);
        return (cell == p.CellA && neighbour == p.CellB) || (cell == p.CellB && neighbour == p.CellA);
    }

    /// <summary>World-space centre and facing for the shared wall line between a cell and its dir neighbour - the same line CreateBox would use for a real wall there.</summary>
    private (Vector3 center, Quaternion rotation) WallLine(Vector2Int cell, int dir)
    {
        switch (dir)
        {
            case 0: return (new Vector3(origin.x + (cell.x + 0.5f) * cellSize, FloorTop, origin.z + (cell.y + 1) * cellSize), Quaternion.identity);
            case 1: return (new Vector3(origin.x + (cell.x + 1) * cellSize, FloorTop, origin.z + (cell.y + 0.5f) * cellSize), Quaternion.Euler(0f, 90f, 0f));
            case 2: return (new Vector3(origin.x + (cell.x + 0.5f) * cellSize, FloorTop, origin.z + cell.y * cellSize), Quaternion.identity);
            default: return (new Vector3(origin.x + cell.x * cellSize, FloorTop, origin.z + (cell.y + 0.5f) * cellSize), Quaternion.Euler(0f, 90f, 0f));
        }
    }

    /// <summary>Approximate "is a wall lamp already here" test for button placement - lamps aren't indexed by cell/direction, so this just checks proximity to the candidate wall position.</summary>
    private bool IsLampWall(Vector2Int cell, Vector3 dirVec)
    {
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        Vector3 wallPos = CellCenter(cell.x, cell.y) + dirVec * backFaceDistance;
        float threshold = cellSize * 0.4f;

        foreach (WallLamp lamp in _lamps)
        {
            if (lamp != null && (lamp.transform.position - wallPos).sqrMagnitude < threshold * threshold) return true;
        }
        return false;
    }

    /// <summary>
    /// Security-door candidates are bridge passages (the maze is braided - RemoveDeadEnds adds loops -
    /// so most passages are not bridges) whose removal strands at least one star on the start's side.
    /// Prefers a cut-off region that is 3-40% of the maze; falls back to any qualifying candidate if too
    /// few of those exist. Each door gets a button in the start-side component (decision D6/F72 slice).
    /// </summary>
    private List<(MazeDoor door, Vector2Int startCell)> PlaceSecurityDoors(System.Random rng, int want, Transform player,
        List<Passage> placedDoorPassages, HashSet<Vector2Int> usedCells)
    {
        List<(MazeDoor door, Vector2Int startCell)> result = new List<(MazeDoor, Vector2Int)>();
        if (want <= 0) return result;

        Vector2Int start = _startCell;
        int totalCells = _playableCellCount;

        List<(Passage passage, HashSet<Vector2Int> startSide, float cutFraction)> candidates =
            new List<(Passage, HashSet<Vector2Int>, float)>();

        foreach (Passage p in AllOpenPassages())
        {
            if (p.CellA == start || p.CellB == start) continue;

            HashSet<Vector2Int> reachable = ReachableCells(start, p);
            bool losesStar = false;
            foreach (Vector2Int star in _starCells)
            {
                if (!reachable.Contains(star)) { losesStar = true; break; }
            }
            if (!losesStar) continue;

            float cutFraction = 1f - reachable.Count / (float)totalCells;
            candidates.Add((p, reachable, cutFraction));
        }

        List<(Passage, HashSet<Vector2Int>, float)> preferred = candidates.FindAll(c => c.Item3 >= 0.03f && c.Item3 <= 0.40f);
        List<(Passage, HashSet<Vector2Int>, float)> pool = preferred.Count >= want ? preferred : candidates;

        List<int> order = new List<int>(pool.Count);
        for (int i = 0; i < pool.Count; i++) order.Add(i);
        ShuffleInts(order, rng);
        PrioritiseTwinWingsBridgeOrder(order, pool); // F125 D4: prefer gating the TwinWings central bridge, no-op otherwise
        PrioritiseMorphologySecurityOrder(order, pool); // F127 D4: prefer gating chamber thresholds on ChambersAndChokepoints

        int minSeparation = Mathf.Max(2, (width + height) / 6);
        foreach (int idx in order)
        {
            if (result.Count >= want) break;

            (Passage passage, HashSet<Vector2Int> startSide, float cutFraction) candidate = pool[idx];
            if (usedCells.Contains(candidate.passage.CellA) || usedCells.Contains(candidate.passage.CellB)) continue;
            if (TooCloseToPlacedDoors(candidate.passage.CellA, placedDoorPassages, minSeparation)) continue;

            Vector2Int startCell = candidate.startSide.Contains(candidate.passage.CellA) ? candidate.passage.CellA : candidate.passage.CellB;
            Vector2Int insideCell = startCell == candidate.passage.CellA ? candidate.passage.CellB : candidate.passage.CellA;

            MazeDoor door = CreateDoor(MazeDoor.Kind.Security, candidate.passage, startCell, insideCell, player);
            WallButton button = PlaceButtonFor(door, candidate.startSide, startCell, rng, usedCells);
            if (button == null)
            {
                Debug.LogWarning($"MazeGenerator: security door at {candidate.passage.CellA}/{candidate.passage.CellB} has no room for a button; skipping it.", this);
                DiscardDoor(door);
                continue;
            }
            if (!DoorsStayCompletable())
            {
                // Soft-lock guard: this door set plus the ones already placed must leave every cell reachable (a button behind its own door chain would never open).
                RollbackDoorSet(new[] { door }, button, usedCells);
                continue;
            }

            placedDoorPassages.Add(candidate.passage);
            usedCells.Add(candidate.passage.CellA);
            usedCells.Add(candidate.passage.CellB);
            result.Add((door, startCell));
        }

        // RemoveDeadEnds braids the maze, so a single-passage cut rarely strands a star and the loop above
        // often places nothing. Fallback: seal the star's own cell - a shutter on every opening, chained
        // through LinkedDoor to one button - provided the rest of the maze stays connected without it.
        if (result.Count < want)
        {
            List<Vector2Int> starCells = new List<Vector2Int>(_starCells);
            Shuffle(starCells, rng);

            foreach (Vector2Int starCell in starCells)
            {
                if (result.Count >= want) break;
                if (Mathf.Max(Mathf.Abs(starCell.x - start.x), Mathf.Abs(starCell.y - start.y)) <= 1) continue;
                if (usedCells.Contains(starCell)) continue;
                if (TooCloseToPlacedDoors(starCell, placedDoorPassages, minSeparation)) continue;

                List<int> openDirs = new List<int>(4);
                bool neighbourTaken = false;
                for (int dir = 0; dir < 4; dir++)
                {
                    if (IsWallClosed(starCell.x, starCell.y, dir)) continue;
                    openDirs.Add(dir);
                    if (usedCells.Contains(Neighbour(starCell, dir))) neighbourTaken = true;
                }
                if (openDirs.Count == 0 || neighbourTaken) continue;

                HashSet<Vector2Int> startSide = ReachableCellsAvoiding(start, starCell);
                if (startSide.Count != totalCells - 1) continue; // sealing it would cut the maze

                List<MazeDoor> doors = new List<MazeDoor>(openDirs.Count);
                List<Passage> passages = new List<Passage>(openDirs.Count);
                foreach (int dir in openDirs)
                {
                    Vector2Int outside = Neighbour(starCell, dir);
                    Passage p = new Passage { CellA = starCell, Dir = dir, CellB = outside };
                    MazeDoor door = CreateDoor(MazeDoor.Kind.Security, p, outside, starCell, player);
                    if (doors.Count > 0) doors[doors.Count - 1].LinkedDoor = door;
                    doors.Add(door);
                    passages.Add(p);
                }

                Vector2Int firstOutside = Neighbour(starCell, openDirs[0]);
                WallButton button = PlaceButtonFor(doors[0], startSide, firstOutside, rng, usedCells);
                if (button == null)
                {
                    foreach (MazeDoor door in doors) DiscardDoor(door);
                    continue;
                }
                if (!DoorsStayCompletable())
                {
                    RollbackDoorSet(doors, button, usedCells);
                    continue;
                }

                placedDoorPassages.AddRange(passages);
                usedCells.Add(starCell);
                foreach (int dir in openDirs) usedCells.Add(Neighbour(starCell, dir));
                result.Add((doors[0], firstOutside));
            }
        }

        if (result.Count < want)
        {
            Debug.LogWarning($"MazeGenerator: only {result.Count} of {want} security doors fit in this maze.", this);
        }
        return result;
    }

    /// <summary>A dead-end cell (WallCount == 3, one open side) turned into a locked room with a battery cell, a shot at a Second Wind, and 2 of the floor's own shards moved inside.</summary>
    private void PlaceVaults(System.Random rng, System.Random lootRng, int want, Transform player, List<Passage> placedDoorPassages, HashSet<Vector2Int> usedCells)
    {
        if (want <= 0) return;

        Vector2Int start = _startCell;
        // The hunter spawns in the farthest cell (PlaceAI), which is usually a dead end - and since F103 A10 keeps some dead ends,
        // a vault kept landing on it: the hunter started the floor locked in, bashed its way out and (before the TickAmbush fix)
        // froze for good on that stall (seed 26249140, floor 5, 10 Oct 2026). Never vault the spawn cell.
        Vector2Int hunterCell = FarthestCell();
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);

        List<Vector2Int> deadEnds = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == start || cell == hunterCell || WallCount(x, z) != 3) continue;
                if (starCellSet.Contains(cell) || lockerCellSet.Contains(cell) || _setPieceCells.Contains(cell)) continue;
                if (usedCells.Contains(cell)) continue;
                deadEnds.Add(cell);
            }
        }
        Shuffle(deadEnds, rng);

        int placed = 0;
        foreach (Vector2Int vaultCell in deadEnds)
        {
            if (placed >= want) break;

            int openDir = -1;
            for (int dir = 0; dir < 4; dir++)
            {
                if (!IsWallClosed(vaultCell.x, vaultCell.y, dir)) { openDir = dir; break; }
            }
            if (openDir < 0) continue;

            Vector2Int outside = Neighbour(vaultCell, openDir);
            Passage p = new Passage { CellA = vaultCell, Dir = openDir, CellB = outside };

            HashSet<Vector2Int> reachableFromStart = ReachableCells(start, p);
            if (reachableFromStart.Contains(vaultCell)) continue; // defensive - a true dead end never loops back

            MazeDoor door = CreateDoor(MazeDoor.Kind.Security, p, outside, vaultCell, player);
            door.IsVaultDoor = true; // F75 Fuse Map beacon
            WallButton button = PlaceButtonFor(door, reachableFromStart, outside, rng, usedCells);
            if (button == null)
            {
                Debug.LogWarning($"MazeGenerator: vault at {vaultCell} has no room for a button; skipping it.", this);
                DiscardDoor(door);
                continue;
            }
            if (!DoorsStayCompletable())
            {
                RollbackDoorSet(new[] { door }, button, usedCells);
                continue;
            }

            placedDoorPassages.Add(p);
            usedCells.Add(vaultCell);
            usedCells.Add(outside);
            BuildVaultLoot(vaultCell, lootRng);
            placed++;
        }

        // RemoveDeadEnds braids the maze, so normally there are no dead ends at all. Fall back to a
        // two-opening cell whose removal leaves every other cell reachable from the start: a shutter
        // on each opening, one button, the first door's Unlock chaining to the second.
        if (placed < want)
        {
            List<Vector2Int> corridors = new List<Vector2Int>();
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    if (IsVoid(x, z)) continue;
                    Vector2Int cell = new Vector2Int(x, z);
                    if (cell == start || cell == hunterCell || WallCount(x, z) != 2) continue;
                    if (Mathf.Max(Mathf.Abs(x - start.x), Mathf.Abs(z - start.y)) <= 1) continue; // not beside the start
                    if (starCellSet.Contains(cell) || lockerCellSet.Contains(cell) || _setPieceCells.Contains(cell)) continue;
                    if (usedCells.Contains(cell)) continue;
                    corridors.Add(cell);
                }
            }
            Shuffle(corridors, rng);

            foreach (Vector2Int vaultCell in corridors)
            {
                if (placed >= want) break;

                List<int> openDirs = new List<int>(2);
                for (int dir = 0; dir < 4; dir++)
                {
                    if (!IsWallClosed(vaultCell.x, vaultCell.y, dir)) openDirs.Add(dir);
                }
                if (openDirs.Count != 2) continue;

                Vector2Int outsideA = Neighbour(vaultCell, openDirs[0]);
                Vector2Int outsideB = Neighbour(vaultCell, openDirs[1]);
                if (usedCells.Contains(outsideA) || usedCells.Contains(outsideB)) continue;

                HashSet<Vector2Int> startSide = ReachableCellsAvoiding(start, vaultCell);
                if (startSide.Count != _playableCellCount - 1) continue; // sealing it would cut the maze

                Passage pA = new Passage { CellA = vaultCell, Dir = openDirs[0], CellB = outsideA };
                Passage pB = new Passage { CellA = vaultCell, Dir = openDirs[1], CellB = outsideB };

                MazeDoor doorA = CreateDoor(MazeDoor.Kind.Security, pA, outsideA, vaultCell, player);
                MazeDoor doorB = CreateDoor(MazeDoor.Kind.Security, pB, outsideB, vaultCell, player);
                doorA.LinkedDoor = doorB;
                doorA.IsVaultDoor = true; // F75 Fuse Map beacon
                doorB.IsVaultDoor = true;

                WallButton button = PlaceButtonFor(doorA, startSide, outsideA, rng, usedCells);
                if (button == null)
                {
                    DiscardDoor(doorA);
                    DiscardDoor(doorB);
                    continue;
                }
                if (!DoorsStayCompletable())
                {
                    RollbackDoorSet(new[] { doorA, doorB }, button, usedCells);
                    continue;
                }

                placedDoorPassages.Add(pA);
                placedDoorPassages.Add(pB);
                usedCells.Add(vaultCell);
                usedCells.Add(outsideA);
                usedCells.Add(outsideB);
                BuildVaultLoot(vaultCell, lootRng);
                placed++;
            }
        }

        if (placed < want)
        {
            Debug.LogWarning($"MazeGenerator: only {placed} of {want} vaults fit in this maze.", this);
        }
    }

    /// <summary>The cell PlaceButtonFor put its last button on, so RollbackDoorSet can take it back.</summary>
    private Vector2Int _lastButtonCell;

    /// <summary>Removes a door that never made it (no button / failed the completability check): out of the registries, its lintel and the object itself.</summary>
    private void DiscardDoor(MazeDoor door)
    {
        _doors.Remove(door);
        _doorPassages.Remove(door);
        Transform lintel = _mazeRoot != null ? _mazeRoot.Find("Lintel_" + door.name) : null;
        if (lintel != null) Destroy(lintel.gameObject);
        Destroy(door.gameObject);
    }

    /// <summary>Undoes a door set and its just-placed button (PlaceButtonFor's last pick): registries, prop wall, used cell, objects.</summary>
    private void RollbackDoorSet(IEnumerable<MazeDoor> doors, WallButton button, HashSet<Vector2Int> usedCells)
    {
        if (button != null)
        {
            _buttons.Remove(button);
            if (_propWalls.TryGetValue(_lastButtonCell, out List<Vector3> taken) && taken.Count > 0)
            {
                taken.RemoveAt(taken.Count - 1);
                if (taken.Count == 0) _propWalls.Remove(_lastButtonCell);
            }
            usedCells.Remove(_lastButtonCell);
            Destroy(button.gameObject);
        }
        foreach (MazeDoor door in doors) DiscardDoor(door);
    }

    /// <summary>Any open passage, away from the start and from a star's own doorway, spaced out from every other door - a free hiding/escape tool with nothing to gate (decision D6).</summary>
    private void PlaceManualDoors(System.Random rng, int want, Transform player, List<Passage> placedDoorPassages, HashSet<Vector2Int> usedCells)
    {
        if (want <= 0) return;

        Vector2Int start = _startCell;
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);

        List<Passage> candidates = new List<Passage>();
        foreach (Passage p in AllOpenPassages())
        {
            if (p.CellA == start || p.CellB == start) continue;
            if (starCellSet.Contains(p.CellA) || starCellSet.Contains(p.CellB)) continue;
            candidates.Add(p);
        }

        List<int> order = new List<int>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++) order.Add(i);
        ShuffleInts(order, rng);
        PrioritiseTwinWingsBridgeManualOrder(order, candidates); // F125 D4: prefer placing a manual door on the TwinWings bridge
        PrioritiseMorphologyManualOrder(order, candidates); // F127 D4: prefer gating chamber thresholds on ChambersAndChokepoints

        int minSeparation = Mathf.Max(2, (width + height) / 6);
        int placed = 0;
        foreach (int idx in order)
        {
            if (placed >= want) break;

            Passage p = candidates[idx];
            if (TooCloseToPlacedDoors(p.CellA, placedDoorPassages, minSeparation)) continue;

            CreateDoor(MazeDoor.Kind.Manual, p, p.CellA, p.CellB, player);
            placedDoorPassages.Add(p);
            placed++;
        }

        if (placed < want)
        {
            Debug.LogWarning($"MazeGenerator: only {placed} of {want} manual doors fit in this maze.", this);
        }
    }

    private bool TooCloseToPlacedDoors(Vector2Int cell, List<Passage> placedDoorPassages, int minSeparation)
    {
        foreach (Passage other in placedDoorPassages)
        {
            int manhattan = Mathf.Abs(cell.x - other.CellA.x) + Mathf.Abs(cell.y - other.CellA.y);
            if (manhattan < minSeparation) return true;
        }
        return false;
    }

    /// <summary>Clones InteractableKit.shutterDoor into the opening between p.CellA and p.CellB, sized to the corridor, and fills the lintel above it with a plain wall-coloured box.</summary>
    private MazeDoor CreateDoor(MazeDoor.Kind kind, Passage p, Vector2Int startCell, Vector2Int insideCell, Transform player)
    {
        (Vector3 center, Quaternion rotation) = WallLine(p.CellA, p.Dir);
        float doorHeight = Mathf.Min(wallHeight, 3.0f);
        float doorWidth = cellSize - wallThickness;

        MazeDoor door = Instantiate(interactableKit.ShutterDoor, center, rotation, _mazeRoot);
        door.gameObject.SetActive(true);
        door.name = $"Door_{p.CellA.x}_{p.CellA.y}_{p.Dir}_{kind}";

        Vector3 insideWorld = CellCenter(insideCell.x, insideCell.y);
        Vector3 insideDirection = insideWorld - center;
        insideDirection.y = 0f;

        door.Configure(kind, doorWidth, doorHeight, center, insideDirection, aiFollower, player);

        float lintelHeight = wallHeight - doorHeight;
        if (lintelHeight > 0.05f)
        {
            Material lintelMat = _theme != null && _theme.WallMaterial != null ? _theme.WallMaterial : _wallMat;
            bool alongX = p.Dir == 0 || p.Dir == 2; // N/S passage: the wall (and this lintel) spans X, thin along Z
            Vector3 size = alongX
                ? new Vector3(doorWidth + wallThickness, lintelHeight, wallThickness)
                : new Vector3(wallThickness, lintelHeight, doorWidth + wallThickness);
            Vector3 lintelCenter = center + Vector3.up * (doorHeight + lintelHeight * 0.5f);
            CreateBox($"Lintel_{door.name}", lintelCenter, size, lintelMat, _mazeRoot);
        }

        _doors.Add(door);
        _doorPassages[door] = p;
        return door;
    }

    /// <summary>
    /// A button for `door` on a free closed wall of `startSide`, registered in _propWalls so nothing else dresses over it.
    /// 10 Oct 2026: it goes where the player can see it from the door - a cell in a straight open line from `doorStartCell`
    /// (the cell in front of the door), at most buttonSightCells away, preferring one that is not the door's own cell. The old
    /// rule (anywhere on the start side, at least 3 BFS steps away) put a Lockdown door's button in a pocket out of sight
    /// (seed 26249140, floor 5) and turned the floor into a hunt for a switch; it is now only the last resort.
    /// One rng draw either way, so the rest of the +11 stream draws the same samples.
    /// </summary>
    private WallButton PlaceButtonFor(MazeDoor door, HashSet<Vector2Int> startSide, Vector2Int doorStartCell, System.Random rng, HashSet<Vector2Int> usedCells)
    {
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);

        List<(Vector2Int cell, int dir)> options = new List<(Vector2Int, int)>();
        AddButtonWalls(SightLineCells(doorStartCell, startSide), starCellSet, lockerCellSet, usedCells, options);
        if (options.Count == 0) AddButtonWalls(new[] { doorStartCell }, starCellSet, lockerCellSet, usedCells, options);
        if (options.Count == 0)
        {
            List<Vector2Int> farCells = new List<Vector2Int>();
            foreach (KeyValuePair<Vector2Int, int> kv in BfsWithin(doorStartCell, startSide))
            {
                if (kv.Value >= 3) farCells.Add(kv.Key);
            }
            AddButtonWalls(farCells, starCellSet, lockerCellSet, usedCells, options);
            if (options.Count > 0) Debug.LogWarning($"MazeGenerator: no free wall in sight of the door at {doorStartCell}; its button goes out of sight.", this);
        }
        if (options.Count == 0) return null;

        (Vector2Int cell, int dir) chosen = options[rng.Next(options.Count)];
        WallButton button = CreateButtonInstance(chosen.cell, chosen.dir, door);

        Vector3 chosenDir = DirVector(chosen.dir);
        if (!_propWalls.TryGetValue(chosen.cell, out List<Vector3> takenList))
        {
            takenList = new List<Vector3>();
            _propWalls[chosen.cell] = takenList;
        }
        takenList.Add(chosenDir);
        usedCells.Add(chosen.cell);
        _lastButtonCell = chosen.cell;

        return button;
    }

    /// <summary>
    /// The cells seen from in front of a door: walking straight from `origin` in each direction through open passages, staying
    /// inside `region`, up to buttonSightCells steps (the origin itself is not included). The locked side is not in `region`, so the
    /// walk never looks through the door.
    /// </summary>
    private List<Vector2Int> SightLineCells(Vector2Int origin, HashSet<Vector2Int> region)
    {
        List<Vector2Int> cells = new List<Vector2Int>();
        for (int dir = 0; dir < 4; dir++)
        {
            Vector2Int current = origin;
            for (int step = 0; step < buttonSightCells; step++)
            {
                if (IsWallClosed(current.x, current.y, dir)) break;
                Vector2Int next = Neighbour(current, dir);
                if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height || !region.Contains(next)) break;

                cells.Add(next);
                current = next;
            }
        }
        return cells;
    }

    /// <summary>Every free closed wall (not claimed in _propWalls, no lamp) of the given cells that may hold a button: not a star, locker, set-piece or already-used cell.</summary>
    private void AddButtonWalls(IEnumerable<Vector2Int> cells, HashSet<Vector2Int> starCells, HashSet<Vector2Int> lockerCells, HashSet<Vector2Int> usedCells, List<(Vector2Int cell, int dir)> options)
    {
        foreach (Vector2Int cell in cells)
        {
            if (starCells.Contains(cell) || lockerCells.Contains(cell) || _setPieceCells.Contains(cell) || usedCells.Contains(cell)) continue;

            for (int dir = 0; dir < 4; dir++)
            {
                if (IsFreeWall(cell, dir)) options.Add((cell, dir));
            }
        }
    }

    private WallButton CreateButtonInstance(Vector2Int cell, int dir, MazeDoor door)
    {
        Vector3 dirVec = DirVector(dir);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        // Hand height: the template's geometry is centred on its root, and CellCenter is floor level
        // (buttons used to spawn with their plate half in the floor - fixed 4 Oct 2026).
        Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * backFaceDistance + Vector3.up * 1.2f;

        WallButton button = Instantiate(interactableKit.WallButton, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
        button.gameObject.SetActive(true);
        button.name = $"Button_{cell.x}_{cell.y}";
        button.Configure(door, aiFollower);

        _buttons.Add(button);
        return button;
    }

    /// <summary>A battery cell (plus a seeded shot at a Second Wind) inside the vault; the 2 forced shards themselves are placed later by SpawnShards, from _vaultForcedShardCells.</summary>
    private void BuildVaultLoot(Vector2Int vaultCell, System.Random rng)
    {
        if (interactableKit.BatteryCell != null)
        {
            Vector3 pos = CellCenter(vaultCell.x, vaultCell.y) + Vector3.up * 0.4f;
            BatteryCellPickup pickup = Instantiate(interactableKit.BatteryCell, pos, Quaternion.identity, _mazeRoot);
            pickup.gameObject.SetActive(true);
            pickup.name = $"BatteryCell_{vaultCell.x}_{vaultCell.y}";

            bool grantsSecondWind = rng.NextDouble() < 0.2 && PlayerInventory.CanHoldMore(ShopItem.SecondWind); // F122: the draw first, as always; NO SECOND CHANCES makes CanHoldMore false
            pickup.SetGrantsSecondWind(grantsSecondWind);
            _batteryPickups.Add(pickup);
        }

        _vaultForcedShardCells.Add(vaultCell);
        _vaultForcedShardCells.Add(vaultCell);
    }

    /// <summary>The agent type the maze NavMesh was baked for (not the hub's "Hub" type living in the same scene).</summary>
    public int MazeAgentTypeId
    {
        get { NavMeshSurface surface = ResolveNavMeshSurface(); return surface != null ? surface.agentTypeID : 0; }
    }

    /// <summary>
    /// A completability check for the map: a flood fill from the start with every locked door a wall; any reached button then unlocks its door
    /// (and the chain behind it) and the fill runs again, until nothing changes. Reports every star, key, fuse box and button, and an open
    /// hatch, still out of reach - the squeeze counts as open, since the player fits it.
    /// </summary>
    private void AppendReachability(StringBuilder sb)
    {
        ReachabilityReport report = CheckReachability();
        sb.Append($"reachability (locked doors are walls until their button is reached): {report.CellsBeforeButtons} of {_playableCellCount} cells before any button, {report.CellsReached} after; ");
        sb.Append(report.Missing.Count == 0 ? $"all {report.Targets} targets reachable\n" : $"UNREACHABLE: {string.Join(", ", report.Missing)}\n");
    }

    /// <summary>
    /// F118: two legs per target. GRID: the flood described above (locked doors are walls until their button is reached; the squeeze is open).
    /// NAVMESH: a path from the start cell to the target cell on the maze's own NavMesh, which catches props and lockers carving a corridor shut
    /// (doors are not in the NavMesh, so this leg ignores locks by design). A NavMesh-only failure whose grid path must cross the squeeze is
    /// recorded in <see cref="ReachabilityReport.NavMissing"/> instead of failing. The hatch is not a target: it only exists after the last star.
    /// </summary>
    public ReachabilityReport CheckReachability()
    {
        ReachabilityReport report = new ReachabilityReport { Doors = _doors.Count };

        HashSet<Vector2Int> reached = FloodWithButtons(out int cellsBeforeButtons);
        report.CellsBeforeButtons = cellsBeforeButtons;
        report.CellsReached = reached.Count;

        // NavMesh leg setup.
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = MazeAgentTypeId, areaMask = NavMesh.AllAreas };
        float snap = cellSize * 0.45f;
        bool haveStart = NavMesh.SamplePosition(CellCenter(_startCell.x, _startCell.y), out NavMeshHit startHit, snap, filter);
        Dictionary<Vector2Int, Vector2Int> parents = null;

        void Check(Vector3 world, string label)
        {
            report.Targets++;
            if (!TryWorldToCell(world, out Vector2Int cell) || !reached.Contains(cell))
            {
                report.Missing.Add($"{label} {cell}");
                return;
            }

            // NavMesh leg.
            Vector3 center = CellCenter(cell.x, cell.y);
            bool ok = false;
            if (haveStart && NavMesh.SamplePosition(center, out NavMeshHit endHit, snap, filter))
            {
                NavMeshPath path = new NavMeshPath();
                if (NavMesh.CalculatePath(startHit.position, endHit.position, filter, path) && path.status == NavMeshPathStatus.PathComplete && path.corners.Length > 0)
                {
                    Vector3 last = path.corners[path.corners.Length - 1];
                    ok = Vector3.Distance(last, endHit.position) <= cellSize * 0.5f;
                }
            }

            if (ok) return;

            if (_squeezeCells.Count > 0)
            {
                if (parents == null) parents = BfsParents(_startCell);
                for (Vector2Int c = cell; parents.TryGetValue(c, out Vector2Int prev); c = prev)
                {
                    if (_squeezeCells.Contains(c))
                    {
                        report.NavMissing.Add($"{label} {cell} (through squeeze)");
                        return;
                    }
                }
            }

            report.Missing.Add($"{label} {cell} [navmesh]");
        }

        foreach (Transform star in _stars) if (star != null) { report.Stars++; Check(star.position, "star"); }
        foreach (KeyPickup key in _keys) if (key != null && !key.IsTaken) { report.Keys++; Check(key.transform.position, "key"); }
        foreach (FuseBox box in _fuseBoxes) if (box != null) { report.FuseBoxes++; Check(box.transform.position, "fuse box"); }
        foreach (WallButton button in _buttons) if (button != null) { report.Buttons++; Check(button.transform.position, "button"); }
        if (HasExitGates)
        {
            foreach (Vector2Int gateCell in GateCells) { report.GateCount++; Check(CellCenter(gateCell.x, gateCell.y), "exit gate"); } // F111: always built, so always checked
        }
        else
        {
            MazeEscape escape = FindAnyObjectByType<MazeEscape>();
            if (escape != null && escape.HatchOpen) Check(escape.HatchPosition, "hatch");
        }

        return report;
    }

    /// <summary>
    /// The grid flood of <see cref="CheckReachability"/>: from the start with every locked door a wall; any reached button unlocks its door (and the
    /// chain behind it) and the fill runs again, until nothing changes. <paramref name="cellsBeforeButtons"/> is the first pass.
    /// </summary>
    private HashSet<Vector2Int> FloodWithButtons(out int cellsBeforeButtons)
    {
        HashSet<(Vector2Int, int)> locked = new HashSet<(Vector2Int, int)>();
        foreach (MazeDoor door in _doors)
        {
            if (door != null && !door.IsUnlocked) LockPassage(locked, door, true);
        }

        HashSet<Vector2Int> reached = FloodFrom(_startCell, locked);
        cellsBeforeButtons = reached.Count;
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (WallButton button in _buttons)
            {
                if (button == null || button.Door == null || !TryWorldToCell(button.transform.position, out Vector2Int cell) || !reached.Contains(cell)) continue;

                int hops = 0;
                for (MazeDoor door = button.Door; door != null && hops++ <= _doors.Count; door = door.LinkedDoor) changed |= LockPassage(locked, door, false);
            }

            if (changed) reached = FloodFrom(_startCell, locked);
        }

        return reached;
    }

    /// <summary>
    /// Soft-lock guard for door placement: with every door placed so far (each one locked until its button is reached), is every cell of the maze
    /// still reachable from the start? Each locked door set is validated alone by its placer, so two sets that are fine apart can still lock each
    /// other away from their buttons (floor 5, seed 505); the placers call this right after a set button lands and roll the set back on false.
    /// </summary>
    private bool DoorsStayCompletable() => FloodWithButtons(out _).Count == _playableCellCount;

    /// <summary>Open-passage BFS tree from <paramref name="from"/> (child cell to parent cell), every door treated as open.</summary>
    private Dictionary<Vector2Int, Vector2Int> BfsParents(Vector2Int from)
    {
        Dictionary<Vector2Int, Vector2Int> parents = new Dictionary<Vector2Int, Vector2Int>();
        HashSet<Vector2Int> seen = new HashSet<Vector2Int> { from };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int next = Neighbour(cell, dir);
                if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height || !seen.Add(next)) continue;
                parents[next] = cell;
                queue.Enqueue(next);
            }
        }

        return parents;
    }

    /// <summary>F118 (benchmark route): the centre of the cell farthest from the start cell by open-passage distance.</summary>
    public Vector3 FarthestCellCenter()
    {
        Dictionary<Vector2Int, int> dist = new Dictionary<Vector2Int, int> { { _startCell, 0 } };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(_startCell);
        Vector2Int best = _startCell;
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            if (dist[cell] > dist[best]) best = cell;
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int next = Neighbour(cell, dir);
                if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height || dist.ContainsKey(next)) continue;
                dist[next] = dist[cell] + 1;
                queue.Enqueue(next);
            }
        }

        return CellCenter(best.x, best.y);
    }

    /// <summary>Adds (lock) or removes (unlock) both directions of a door's passage. Returns true if the set changed.</summary>
    private bool LockPassage(HashSet<(Vector2Int, int)> locked, MazeDoor door, bool lockIt)
    {
        if (!_doorPassages.TryGetValue(door, out Passage p)) return false;

        bool changed;
        if (lockIt)
        {
            changed = locked.Add((p.CellA, p.Dir));
            changed |= locked.Add((p.CellB, (p.Dir + 2) % 4));
        }
        else
        {
            changed = locked.Remove((p.CellA, p.Dir));
            changed |= locked.Remove((p.CellB, (p.Dir + 2) % 4));
        }

        return changed;
    }

    private HashSet<Vector2Int> FloodFrom(Vector2Int from, HashSet<(Vector2Int, int)> locked)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { from };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir) || locked.Contains((cell, dir))) continue;

                Vector2Int next = Neighbour(cell, dir);
                if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height || !visited.Add(next)) continue;

                queue.Enqueue(next);
            }
        }

        return visited;
    }
}
