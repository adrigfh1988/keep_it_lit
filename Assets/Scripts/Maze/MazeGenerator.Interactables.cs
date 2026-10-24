using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Partial of MazeGenerator: lockers, throwables, noisy surfaces, lanterns, lore notes, patient files, and theme interactables (F74).
public partial class MazeGenerator
{
    /// <summary>
    /// Picks lockers that stand against the SIDE wall of a hall rather than at the end of one. The
    /// maze has no dead ends any more, and a locker in a straight stretch is a choice the player
    /// makes on the move instead of a trap at the end of a corridor. Straight cells (walls on two
    /// opposite sides only) are used first; corners and junctions only if the maze runs out of them.
    /// Excludes the start and the AI spawn. Only needs wall flags, so it runs before any geometry.
    /// </summary>
    private void ChooseLockerCells(System.Random rng)
    {
        _lockerCells.Clear();
        _lockerWall.Clear();

        Vector2Int aiCell = FarthestCell();
        List<Vector2Int> straight = new List<Vector2Int>();
        List<Vector2Int> other = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z) || (x == _startCell.x && z == _startCell.y)) continue;
                if (x == aiCell.x && z == aiCell.y) continue;
                if (_baitDeadEnds.Contains(new Vector2Int(x, z))) continue; // F103 A10: a bait dead end never hosts a locker, its mouth does
                if (IsLayoutReserved(new Vector2Int(x, z))) continue; // F105 C3 (squeeze), C4 (loop entrance)

                bool northSouth = _wallN[x, z] && _wallS[x, z] && !_wallE[x, z] && !_wallW[x, z];
                bool eastWest = _wallE[x, z] && _wallW[x, z] && !_wallN[x, z] && !_wallS[x, z];
                if (northSouth || eastWest) straight.Add(new Vector2Int(x, z));
                else if (WallCount(x, z) >= 1) other.Add(new Vector2Int(x, z));
            }
        }

        Shuffle(straight, rng);
        Shuffle(other, rng);

        // Same greedy-then-relaxed pass as SpawnStars, so lockers do not cluster in one corner
        int minSeparation = Mathf.Max(2, (width + height) / 7);
        List<Vector2Int> chosen = new List<Vector2Int>();

        for (int pass = 0; pass < 2 && chosen.Count < lockerCount; pass++)
        {
            for (int pool = 0; pool < 2; pool++)
            {
                foreach (Vector2Int candidate in pool == 0 ? straight : other)
                {
                    if (chosen.Count >= lockerCount) break;
                    if (chosen.Contains(candidate)) continue;
                    if (pass == 0 && !IsFarEnough(candidate, chosen, minSeparation)) continue;
                    chosen.Add(candidate);
                }
            }
        }

        EnsureLockersAtBaitMouths(chosen);

        _lockerCells.AddRange(chosen);

        // Which wall each locker stands against: one of the cell's real walls, picked from the seed
        List<Vector3> walls = new List<Vector3>(4);
        foreach (Vector2Int cell in chosen)
        {
            walls.Clear();
            if (_wallN[cell.x, cell.y]) walls.Add(Vector3.forward);
            if (_wallE[cell.x, cell.y]) walls.Add(Vector3.right);
            if (_wallS[cell.x, cell.y]) walls.Add(Vector3.back);
            if (_wallW[cell.x, cell.y]) walls.Add(Vector3.left);
            _lockerWall[cell] = walls[rng.Next(walls.Count)];
        }

        if (chosen.Count < lockerCount)
        {
            Debug.LogWarning($"MazeGenerator: only {chosen.Count} of {lockerCount} lockers fit in this maze.", this);
        }
    }

    /// <summary>
    /// F103 A10 safety valve: every bait dead end gets a locker at its mouth, so the player always has somewhere to go on the way
    /// out of the bet. A mouth missing from the greedy pick replaces the last entry that is not itself a bait mouth (or is appended
    /// if lockers are still owed). This shifts the +3 locker stream's picks only on floors that have bait dead ends, where the layout already differs.
    /// </summary>
    private void EnsureLockersAtBaitMouths(List<Vector2Int> chosen)
    {
        foreach (Vector2Int bait in _baitDeadEnds)
        {
            Vector2Int mouth = MouthOf(bait);
            if (chosen.Contains(mouth)) continue;

            if (chosen.Count < lockerCount)
            {
                chosen.Add(mouth);
                continue;
            }

            for (int i = chosen.Count - 1; i >= 0; i--)
            {
                if (_baitDeadEnds.Exists(b => MouthOf(b) == chosen[i])) continue;
                chosen[i] = mouth;
                break;
            }
        }
    }

    /// <summary>
    /// Built between BuildGeometry and BuildRuntimeNavMesh so every locker body is carved out of the
    /// navmesh and the hunter paths around it, not through it.
    /// </summary>
    private void BuildLockers()
    {
        _lockers.Clear();

        foreach (Vector2Int cell in _lockerCells)
        {
            BuildLocker(cell);
        }
    }

    private void BuildLocker(Vector2Int cell)
    {
        if (_theme != null && _theme.Locker != null)
        {
            BuildThemedLocker(cell);
        }
        else
        {
            BuildPrimitiveLocker(cell);
        }
    }

    /// <summary>
    /// Clones the theme's locker prefab and hands MazeGenerator's own computed positions to Configure -
    /// the prefab's own insideAnchor/frontAnchor win when the artist set them, otherwise the same offsets
    /// the primitive locker uses are computed from its transform. The prefab's own trigger collider
    /// replaces the runtime-added one BuildPrimitiveLocker builds by hand.
    /// </summary>
    private void BuildThemedLocker(Vector2Int cell)
    {
        int x = cell.x;
        int z = cell.y;
        Vector3 cellCenter = CellCenter(x, z);
        Vector3 openDir = -_lockerWall[cell];
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        Vector3 wallFace = cellCenter - openDir * backFaceDistance;

        Locker locker = Instantiate(_theme.Locker, wallFace, Quaternion.LookRotation(openDir, Vector3.up), _lockersGroup);
        locker.gameObject.SetActive(true);
        locker.name = $"Locker_{x}_{z}";

        Vector3 inside = locker.InsideAnchor != null
            ? locker.InsideAnchor.position
            : locker.transform.TransformPoint(new Vector3(0f, 0f, lockerDepth * 0.5f));
        Vector3 front = locker.FrontAnchor != null
            ? locker.FrontAnchor.position
            : locker.transform.TransformPoint(new Vector3(0f, 0f, lockerDepth + 1.0f));

        locker.Configure(inside, front, locker.transform.eulerAngles.y, locker.Door);
        _lockers.Add(locker);
    }

    private void BuildPrimitiveLocker(Vector2Int cell)
    {
        int x = cell.x;
        int z = cell.y;
        Vector3 cellCenter = CellCenter(x, z);

        // The locker stands against the wall chosen in ChooseLockerCells and its door faces the
        // middle of the hall, i.e. away from that wall.
        Vector3 openDir = -_lockerWall[cell];

        GameObject root = new GameObject($"Locker_{x}_{z}");
        root.transform.SetParent(_lockersGroup, false);
        root.transform.position = cellCenter;
        root.transform.rotation = Quaternion.LookRotation(openDir, Vector3.up);

        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        float frontFaceZ = -backFaceDistance + lockerDepth;

        Material lockerMat = DarkCopy(wallMaterial, lockerTint, "Maze_Locker");

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.layer = 0;
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, lockerHeight * 0.5f, -backFaceDistance + lockerDepth * 0.5f);
        body.transform.localScale = new Vector3(lockerWidth, lockerHeight, lockerDepth);
        if (lockerMat != null) body.GetComponent<MeshRenderer>().sharedMaterial = lockerMat;

        // Door: three horizontal slats on a hinge at the left edge, proud of the front face by 0.02 m.
        // Their colliders are what stops the hunter's LOS raycast seeing in - visibility is 0 while
        // hidden anyway, so the gaps between them are purely for looks.
        GameObject doorPivot = new GameObject("Door");
        doorPivot.transform.SetParent(root.transform, false);
        doorPivot.transform.localPosition = new Vector3(-lockerWidth * 0.5f, 0f, frontFaceZ + 0.02f);
        doorPivot.transform.localRotation = Quaternion.identity;

        BuildDoorSlats(doorPivot.transform, lockerWidth - 0.04f, lockerHeight - 0.04f, 0.04f, lockerMat);

        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(1.4f, 2.0f, 1.2f);
        trigger.center = new Vector3(0f, 1.0f, frontFaceZ + 0.9f);

        Vector3 insidePosition = root.transform.TransformPoint(new Vector3(0f, 0f, -backFaceDistance + lockerDepth * 0.5f));
        Vector3 frontPosition = root.transform.TransformPoint(new Vector3(0f, 0f, frontFaceZ + 1.0f));
        float facingYaw = root.transform.eulerAngles.y;

        Locker locker = root.AddComponent<Locker>();
        locker.Configure(insidePosition, frontPosition, facingYaw, doorPivot.transform);
        _lockers.Add(locker);
    }

    /// <summary>Three slats with two 0.06 m gaps around eye height (1.5-1.7 m), spanning the door width.</summary>
    private void BuildDoorSlats(Transform doorPivot, float doorWidth, float doorHeight, float depth, Material material)
    {
        const float gap = 0.06f;

        float bottomHeight = Mathf.Clamp(1.5f, 0.1f, doorHeight);
        float midHeight = 0.08f;
        float midStart = bottomHeight + gap;
        float midEnd = midStart + midHeight;
        float topStart = midEnd + gap;
        float topHeight = Mathf.Max(0.1f, doorHeight - topStart);

        CreateDoorSlat(doorPivot, "Slat_Bottom", doorWidth, bottomHeight, depth, bottomHeight * 0.5f, material);
        CreateDoorSlat(doorPivot, "Slat_Middle", doorWidth, midHeight, depth, midStart + midHeight * 0.5f, material);
        CreateDoorSlat(doorPivot, "Slat_Top", doorWidth, topHeight, depth, topStart + topHeight * 0.5f, material);
    }

    private void CreateDoorSlat(Transform parent, string slatName, float width, float height, float depth, float centerY, Material material)
    {
        GameObject slat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slat.name = slatName;
        slat.layer = 0;
        slat.transform.SetParent(parent, false);
        slat.transform.localPosition = new Vector3(width * 0.5f, centerY, 0f);
        slat.transform.localScale = new Vector3(width, height, depth);
        if (material != null) slat.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    // ---------------------------------------------------------------- throwables, noisy floors, lantern, lore, theme interactables (F74)

    /// <summary>A free-wall cell not already claimed by a star/locker/set-piece/door/button/vault or a prior call this pass - the same free-wall test PlaceButtonFor/BuildFuseBox use, shared by every F74 wall-mounted interactable.</summary>
    private bool TryPickFreeWallCell(System.Random rng, HashSet<Vector2Int> localUsed, out Vector2Int cell, out int dir)
    {
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                Vector2Int c = new Vector2Int(x, z);
                if (c == _startCell) continue;
                if (_starCells.Contains(c) || lockerCellSet.Contains(c) || _setPieceCells.Contains(c)) continue;
                if (_doorUsedCells.Contains(c) || (localUsed != null && localUsed.Contains(c))) continue;
                if (HasFreeWall(c)) candidates.Add(c);
            }
        }

        if (candidates.Count == 0)
        {
            cell = default;
            dir = -1;
            return false;
        }

        cell = candidates[rng.Next(candidates.Count)];
        return TryChooseFreeWall(cell, rng, out dir);
    }

    /// <summary>TryPickFreeWallCell, plus reserving the chosen wall in _propWalls and the chosen cell in `localUsed` so a second call this same pass never lands on top of the first.</summary>
    private bool TryPlaceOnFreeWall(System.Random rng, HashSet<Vector2Int> localUsed, out Vector2Int cell, out int dir)
    {
        if (!TryPickFreeWallCell(rng, localUsed, out cell, out dir)) return false;

        Vector3 dirVec = DirVector(dir);
        if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
        {
            taken = new List<Vector3>();
            _propWalls[cell] = taken;
        }
        taken.Add(dirVec);
        localUsed?.Add(cell);
        return true;
    }

    /// <summary>Bottles and cans (Blend(3,5) by floor progress) on plain floor cells, post-bake. A trigger collider at rest - see ThrowableItem.Awake.</summary>
    private void BuildThrowables(System.Random rng)
    {
        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasThrowables)
        {
            Debug.LogError("MazeGenerator: no InteractableKit throwables in the scene - skipping bottles/cans. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        float t = _profile != null ? _profile.Progress : 0f;
        int want = Mathf.RoundToInt(Mathf.Lerp(3, 5, t));

        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == _startCell) continue;
                if (_starCells.Contains(cell) || lockerCellSet.Contains(cell) || _setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell)) continue;
                candidates.Add(cell);
            }
        }
        Shuffle(candidates, rng);
        candidates.RemoveAll(HasCeilingProp); // F107 A: after the shuffle, so the +13 stream is unchanged; a bottle lies near the cell centre

        for (int i = 0; i < want && i < candidates.Count; i++)
        {
            Vector2Int cell = candidates[i];
            bool bottle = rng.NextDouble() < 0.6;
            ThrowableItem template = bottle ? interactableKit.ThrowableBottle : interactableKit.ThrowableCan;
            // F79: optional bottle look-alikes, picked by a hash of the cell so no extra rng draw is made.
            ThrowableItem[] variants = interactableKit.BottleVariants;
            if (bottle && variants != null && variants.Length > 0)
            {
                ThrowableItem variant = variants[(((cell.x * 73856093) ^ (cell.y * 19349663)) & int.MaxValue) % variants.Length];
                if (variant != null) template = variant;
            }

            Vector3 lateral = new Vector3((float)(rng.NextDouble() * 2.0 - 1.0), 0f, (float)(rng.NextDouble() * 2.0 - 1.0)) * (cellSize * 0.25f);
            Vector3 pos = CellCenter(cell.x, cell.y) + lateral + Vector3.up * (bottle ? 0.15f : 0.08f);
            float yaw = (float)(rng.NextDouble() * 360.0);

            ThrowableItem clone = Instantiate(template, pos, Quaternion.Euler(0f, yaw, 0f), _mazeRoot);
            clone.gameObject.SetActive(true);
            clone.name = $"Throwable_{template.name}_{cell.x}_{cell.y}";
        }
    }

    /// <summary>Glass/puddle patches (Blend(2,6) by floor progress, x RunRules.NoisyPatchMultiplier) centred in ordinary cells, post-bake.</summary>
    private void BuildNoisySurfaces(System.Random rng)
    {
        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasNoisySurfaces)
        {
            Debug.LogError("MazeGenerator: no InteractableKit noisy floor patches in the scene - skipping glass/puddles. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        float t = _profile != null ? _profile.Progress : 0f;
        int want = Mathf.RoundToInt(Mathf.Lerp(2, 6, t) * RunRules.NoisyPatchMultiplier);

        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == _startCell) continue;
                if (_starCells.Contains(cell) || lockerCellSet.Contains(cell)) continue;
                candidates.Add(cell);
            }
        }
        Shuffle(candidates, rng);

        for (int i = 0; i < want && i < candidates.Count; i++)
        {
            Vector2Int cell = candidates[i];
            bool glass = rng.NextDouble() < 0.5;
            NoisySurface template = glass ? interactableKit.GlassPatch : interactableKit.Puddle;
            float yaw = (float)(rng.NextDouble() * 360.0);

            NoisySurface clone = Instantiate(template, CellCenter(cell.x, cell.y), Quaternion.Euler(0f, yaw, 0f), _mazeRoot);
            clone.gameObject.SetActive(true);
            clone.name = $"NoisySurface_{template.name}_{cell.x}_{cell.y}";
            clone.Configure(glass ? NoisySurface.Kind.Glass : NoisySurface.Kind.Puddle);
        }
    }

    /// <summary>One lantern per floor from floor 2 on - near the start (&lt;=2 BFS steps) on a Blackout floor, anywhere valid otherwise.</summary>
    private void BuildLantern(System.Random rng)
    {
        _lanterns.Clear();
        if (_profile == null || _profile.Floor < 2) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasLantern)
        {
            Debug.LogError("MazeGenerator: no InteractableKit lantern in the scene - skipping it. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        bool blackout = _profile.Has(FloorProfile.Objective.Blackout);
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        List<Vector2Int> candidates = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == _startCell) continue;
                if (_starCells.Contains(cell) || lockerCellSet.Contains(cell) || _setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell)) continue;
                if (blackout && _distance[x, z] > 2) continue;
                candidates.Add(cell);
            }
        }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("MazeGenerator: no room for this floor's lantern.", this);
            return;
        }

        int pick = rng.Next(candidates.Count);
        // F107 A: the draw is made first; a pick under a hanging body walks on to the next candidate that is clear.
        for (int step = 0; step < candidates.Count && HasCeilingProp(candidates[pick]); step++) pick = (pick + 1) % candidates.Count;
        Vector2Int chosen = candidates[pick];
        Vector3 pos = CellCenter(chosen.x, chosen.y) + Vector3.up * 0.35f;

        Lantern lantern = Instantiate(interactableKit.Lantern, pos, Quaternion.identity, _mazeRoot);
        lantern.gameObject.SetActive(true);
        lantern.name = $"Lantern_{chosen.x}_{chosen.y}";
        lantern.Configure(rng);
        _lanterns.Add(lantern);
    }

    /// <summary>2 lore notes per floor, ids (floor-1)*2 + {0,1} (F74 slice spec's verbatim text table).</summary>
    private void BuildLoreNotes(System.Random rng)
    {
        _loreNotes.Clear();
        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasLoreNote)
        {
            Debug.LogError("MazeGenerator: no InteractableKit lore note in the scene - skipping this floor's notes. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        int floor = _profile != null ? _profile.Floor : 1;
        HashSet<Vector2Int> localUsed = new HashSet<Vector2Int>();

        for (int i = 0; i < 2; i++)
        {
            if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
            {
                Debug.LogWarning($"MazeGenerator: only {i} of 2 lore notes fit on floor {floor}.", this);
                break;
            }

            int noteId = (floor - 1) * 2 + i;
            Vector3 dirVec = DirVector(dir);
            float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
            Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * (backFaceDistance - 0.03f) + Vector3.up * 1.4f;

            LoreNote note = Instantiate(interactableKit.LoreNote, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
            note.gameObject.SetActive(true);
            note.name = $"LoreNote_{noteId}";
            note.Configure(noteId, LoreNoteText(noteId), LoreNoteKey(noteId));
            _loreNotes.Add(note);
        }
    }

    /// <summary>
    /// F93 (E6): one case file per maze, id 99 + floor (100..104), text from PatientFiles. It IS a LoreNote (cloned from
    /// InteractableKit.patientFile), so reading, the first-read bonus and the save all work through LoreArchive. Own RNG (+19). Prefers
    /// a wall at least 4 BFS steps from the start; falls back to any free wall. Added to _loreNotes so SetUpAtmosphere binds it.
    /// </summary>
    private void BuildPatientFile(System.Random rng)
    {
        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasPatientFile)
        {
            Debug.LogError("MazeGenerator: no InteractableKit patient file in the scene - skipping this floor's case file. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        int floor = _profile != null ? _profile.Floor : 1;
        int fileId = PatientFiles.IdForFloor(Mathf.Clamp(floor, 1, PatientFiles.MazeFileCount));

        // Cells within 3 steps of the start are pre-claimed so the first pick lands further in; if nothing fits, try again without.
        HashSet<Vector2Int> everything = new HashSet<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (!IsVoid(x, z)) everything.Add(new Vector2Int(x, z));
            }
        }
        HashSet<Vector2Int> localUsed = new HashSet<Vector2Int>();
        // Super easy mode turns that around: the far cells are claimed, so the file lands within 3 steps of the start.
        foreach (KeyValuePair<Vector2Int, int> kv in BfsWithin(_startCell, everything))
        {
            if (superEasyMode ? kv.Value >= 4 : kv.Value < 4) localUsed.Add(kv.Key);
        }

        if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
        {
            localUsed.Clear();
            if (!TryPlaceOnFreeWall(rng, localUsed, out cell, out dir))
            {
                Debug.LogWarning($"MazeGenerator: the case file did not fit on floor {floor}.", this);
                return;
            }
        }

        Vector3 dirVec = DirVector(dir);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * (backFaceDistance - 0.03f) + Vector3.up * 1.4f;

        LoreNote file = Instantiate(interactableKit.PatientFile, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
        file.gameObject.SetActive(true);
        file.name = $"PatientFile_{fileId}";
        file.Configure(fileId, PatientFiles.Text(fileId), "lore.file." + fileId + ".text");
        _loreNotes.Add(file);
        Debug.Log($"MazeGenerator: patient file {fileId} placed in cell {cell} (wall {dir}).", this);
    }

    /// <summary>S9: the 10 lore texts live in the Lore table (lore.note.0..9, verbatim from plannings/darkness-and-interactivity-spec.md F74 item 4).</summary>
    private static string LoreNoteKey(int id) => id >= 0 && id <= 9 ? "lore.note." + id : null;

    private static string LoreNoteText(int id)
    {
        string key = LoreNoteKey(id);
        return key != null ? L10n.T(key) : "";
    }

    /// <summary>Ward -> bell, Boiler -> valve, Crypt -> candles x2, Lab -> terminal. Never on the primitive fallback (no theme to key off - see decision D11's graceful-degradation rule).</summary>
    private void BuildThemeInteractables(System.Random rng)
    {
        _themeInteractables.Clear();
        _pendingTerminals.Clear();
        if (_theme == null || _profile == null) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasThemeInteractables)
        {
            Debug.LogError("MazeGenerator: no InteractableKit theme interactables in the scene - skipping this floor's. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        if (aiFollower == null) aiFollower = FindAnyObjectByType<AIFollower>();
        HashSet<Vector2Int> localUsed = new HashSet<Vector2Int>();

        switch (_profile.ThemeIndex)
        {
            case FloorProfile.Theme.Ward:
                BuildCallBellInstance(rng, localUsed);
                break;
            case FloorProfile.Theme.BoilerDeck:
                BuildSteamValveInstance(rng, localUsed);
                break;
            case FloorProfile.Theme.Crypt:
                BuildCandlesInstance(rng, localUsed);
                BuildCandlesInstance(rng, localUsed);
                break;
            case FloorProfile.Theme.Lab:
                BuildTerminalInstance(rng, localUsed);
                break;
        }
    }

    /// <summary>`mountHeight` is how far above the floor the template's root goes (every template's geometry is centred on its root): wall-mounted kinds sit at hand height, floor-standing ones at half their own height. Added 4 Oct 2026 - until then every theme interactable was spawned at floor level.</summary>
    private ThemeInteractable InstantiateThemeInteractable(ThemeInteractable template, Vector2Int cell, int dir, string label, float mountHeight)
    {
        Vector3 dirVec = DirVector(dir);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * backFaceDistance + Vector3.up * mountHeight;

        ThemeInteractable clone = Instantiate(template, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
        clone.gameObject.SetActive(true);
        clone.name = $"{label}_{cell.x}_{cell.y}";
        return clone;
    }

    private void BuildCallBellInstance(System.Random rng, HashSet<Vector2Int> localUsed)
    {
        if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
        {
            Debug.LogWarning("MazeGenerator: no room for the call bell.", this);
            return;
        }

        ThemeInteractable bell = InstantiateThemeInteractable(interactableKit.CallBell, cell, dir, "CallBell", 1.3f);
        Vector2Int remote = FindCellAtLeastSteps(cell, 5) ?? FarthestCell();
        bell.ConfigureCallBell(CellCenter(remote.x, remote.y), aiFollower);
        _themeInteractables.Add(bell);
    }

    private void BuildSteamValveInstance(System.Random rng, HashSet<Vector2Int> localUsed)
    {
        if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
        {
            Debug.LogWarning("MazeGenerator: no room for the steam valve.", this);
            return;
        }

        Vector2Int adjacent = cell;
        for (int d = 0; d < 4; d++)
        {
            if (IsWallClosed(cell.x, cell.y, d)) continue;
            adjacent = Neighbour(cell, d);
            break;
        }

        GameObject anchor = new GameObject("SteamAnchor");
        anchor.transform.SetParent(_mazeRoot, false);
        anchor.transform.position = CellCenter(adjacent.x, adjacent.y);

        NavMeshObstacle obstacle = anchor.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = new Vector3(cellSize - wallThickness, 2f, cellSize - wallThickness);
        obstacle.carving = true;
        obstacle.enabled = false;

        ParticleSystem steam = BuildSteamParticles(anchor.transform);

        ThemeInteractable valve = InstantiateThemeInteractable(interactableKit.SteamValve, cell, dir, "SteamValve", 1.0f);
        valve.ConfigureSteamValve(obstacle, steam, aiFollower);
        _themeInteractables.Add(valve);
    }

    private ParticleSystem BuildSteamParticles(Transform parent)
    {
        GameObject go = new GameObject("Steam");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.up * 1.4f;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = ps.main;
        main.loop = true;
        main.startLifetime = 1.5f;
        main.startSpeed = 0.6f;
        main.startSize = 0.6f;
        main.startColor = new Color(1f, 1f, 1f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 20f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.15f;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = GetSteamMaterial();

        ps.Stop();
        return ps;
    }

    private static Material s_steamMaterial;

    private static Material GetSteamMaterial()
    {
        if (s_steamMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
            s_steamMaterial = new Material(shader) { name = "SteamParticleMat" };
            if (s_steamMaterial.HasProperty("_BaseColor")) s_steamMaterial.SetColor("_BaseColor", Color.white);
        }
        return s_steamMaterial;
    }

    private void BuildCandlesInstance(System.Random rng, HashSet<Vector2Int> localUsed)
    {
        if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
        {
            Debug.LogWarning("MazeGenerator: no room for candles.", this);
            return;
        }

        ThemeInteractable candles = InstantiateThemeInteractable(interactableKit.Candles, cell, dir, "Candles", 0f); // floor candles
        candles.ConfigureCandles(aiFollower);
        _themeInteractables.Add(candles);
    }

    private void BuildTerminalInstance(System.Random rng, HashSet<Vector2Int> localUsed)
    {
        if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
        {
            Debug.LogWarning("MazeGenerator: no room for the terminal.", this);
            return;
        }

        // ConfigureTerminal needs the player camera, which does not exist yet this early in Awake -
        // deferred to SetUpAtmosphere via _pendingTerminals.
        ThemeInteractable terminal = InstantiateThemeInteractable(interactableKit.Terminal, cell, dir, "Terminal", 0.35f); // floor console, 0.7 m tall, centred on its root
        _themeInteractables.Add(terminal);
        _pendingTerminals.Add(terminal);
    }
}
