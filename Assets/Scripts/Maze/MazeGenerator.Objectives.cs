using System.Collections.Generic;
using UnityEngine;

public partial class MazeGenerator
{
    /// <summary>
    /// F71: true if an uncollected star sits within one cell of the position (a kill-wave keeps clear of those lamps, a silhouette's dead lamp likewise).
    /// Collected stars are destroyed (Stars entries go null), so they stop counting on their own.
    /// </summary>
    public bool IsNearLiveStar(Vector3 position)
    {
        return IsNearLiveStar(position, null);
    }

    /// <summary>F105 C5: as <see cref="IsNearLiveStar(Vector3)"/>, ignoring `except` - the lure star is still alive while its own pickup event runs, and must not shield the lamps around it.</summary>
    public bool IsNearLiveStar(Vector3 position, Transform except)
    {
        float threshold = cellSize * 1.5f; // adjacent cell centres are one cellSize apart
        for (int i = 0; i < _stars.Count; i++)
        {
            Transform star = _stars[i];
            if (star != null && star != except && Vector3.Distance(position, star.position) <= threshold) return true;
        }

        return false;
    }

    /// <summary>F45: cell centres two steps from star `starIndex`, around a corner from it - never a straight line to it. Empty for an out-of-range index or a star that sits in a dead end.</summary>
    public IReadOnlyList<Vector3> AmbushCellsFor(int starIndex)
    {
        if (starIndex < 0 || starIndex >= _ambushCells.Count) return System.Array.Empty<Vector3>();
        return _ambushCells[starIndex];
    }

    // ---------------------------------------------------------------- contents

    private void DisableExistingPickups()
    {
        if (!disableExistingPickups) return;

        Pickup[] pickups = FindObjectsByType<Pickup>(FindObjectsInactive.Exclude);
        foreach (Pickup pickup in pickups)
        {
            pickup.gameObject.SetActive(false);
        }
    }

    private void SpawnStars(int usedSeed)
    {
        if (starPrefab == null)
        {
            Debug.LogWarning("MazeGenerator: no star prefab assigned, the maze has nothing to collect.", this);
            return;
        }

        Vector2Int aiCell = FarthestCell();

        // Prefer cells at least two steps from the start so no star is visible from the spawn point.
        List<Vector2Int> preferred = new List<Vector2Int>();
        List<Vector2Int> fallback = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                if (x == _startCell.x && z == _startCell.y) continue;
                if (x == aiCell.x && z == aiCell.y) continue;
                if (_lockerCells.Contains(new Vector2Int(x, z))) continue;
                if (IsLayoutReserved(new Vector2Int(x, z))) continue; // F105 C3: nothing is ever worth the squeeze; C4: nor the loop entrance

                // On a bigger grid, two steps from the start is still in plain sight of it
                if (_distance[x, z] >= Mathf.Max(2, width / 2)) preferred.Add(new Vector2Int(x, z));
                else fallback.Add(new Vector2Int(x, z));
            }
        }

        System.Random rng = new System.Random(usedSeed + 1);
        Shuffle(preferred, rng);
        Shuffle(fallback, rng);
        preferred.AddRange(fallback);
        PrioritiseShapeStarCandidates(preferred); // F125 D4: round-robin across wings/arms/quadrants on custom shapes, no-op on Rectangle
        PrioritiseBaitCells(preferred);
        // Super easy mode: the nearest cells to the start, side by side (after the shuffle, so the +1 stream is unchanged).
        if (superEasyMode) SortByStartDistance(preferred);

        // Keep the stars apart, or a shuffle can drop two of them in neighbouring cells
        int minSeparation = superEasyMode ? 0 : Mathf.Max(2, (width + height) / 4);
        List<Vector2Int> chosen = new List<Vector2Int>();

        for (int pass = 0; pass < 2 && chosen.Count < starCount; pass++)
        {
            // Second pass drops the separation constraint rather than spawning fewer stars
            foreach (Vector2Int candidate in preferred)
            {
                if (chosen.Count >= starCount) break;
                if (chosen.Contains(candidate)) continue;

                if (pass == 0 && !IsFarEnough(candidate, chosen, minSeparation)) continue;
                // F107 A: after the shuffle and the separation test, so the +1 stream is unchanged. A bait star on such a cell falls through to the next candidate.
                if (HasCeilingProp(candidate))
                {
                    terror.Log($"Star skipped ceiling-prop cell {candidate}", this);
                    continue;
                }
                chosen.Add(candidate);
            }
        }

        foreach (Vector2Int cell in chosen)
        {
            // One call with the final position: Pickup.Start captures it as the anchor of the bobbing motion.
            GameObject star = Instantiate(
                starPrefab,
                CellCenter(cell.x, cell.y) + Vector3.up * starHeight,
                Quaternion.identity,
                _mazeRoot);

            _stars.Add(star.transform);
            if (starLights) AttachStarLight(star);
        }

        LogBaitStars(chosen);

        _starCells.Clear();
        _starCells.AddRange(chosen);

        if (chosen.Count < starCount)
        {
            Debug.LogWarning($"MazeGenerator: only {chosen.Count} of {starCount} stars fit in the maze.", this);
        }

        PrecomputeAmbushCells();
    }

    /// <summary>
    /// F103 A10: a stable partition that moves the bait dead ends to the front of the candidate list, in order. No rng draw, so the +1
    /// star stream is unchanged. The first candidate is always accepted; the rest still respect the star separation, so a second
    /// bait may be skipped if it is too close to the first.
    /// </summary>
    private void PrioritiseBaitCells(List<Vector2Int> candidates)
    {
        int front = 0;
        foreach (Vector2Int bait in _baitDeadEnds)
        {
            int index = candidates.IndexOf(bait);
            if (index < front) continue;

            candidates.RemoveAt(index);
            candidates.Insert(front++, bait);
        }
    }

    private void LogBaitStars(List<Vector2Int> chosen)
    {
        if (_baitDeadEnds.Count == 0) return;

        List<Vector2Int> baited = chosen.FindAll(cell => _baitDeadEnds.Contains(cell));
        terror.Log($"Bait stars: {baited.Count} at ({string.Join(", ", baited)})", this);
    }

    /// <summary>
    /// F45: for every star, every cell that sits two open-passage steps away with a turn in between -
    /// i.e. around a corner from it, never a straight line down the same corridor. Costs nothing at
    /// runtime since it is computed once here, right after the stars that define it exist.
    /// </summary>
    private void PrecomputeAmbushCells()
    {
        _ambushCells.Clear();

        foreach (Vector2Int star in _starCells)
        {
            List<Vector3> cells = new List<Vector3>();

            // A dead end (one open wall, three closed) has only one way in - never camp the only door.
            if (WallCount(star.x, star.y) != 3)
            {
                for (int dir = 0; dir < 4; dir++)
                {
                    if (IsWallClosed(star.x, star.y, dir)) continue;
                    Vector2Int n = Neighbour(star, dir);
                    if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;

                    for (int dir2 = 0; dir2 < 4; dir2++)
                    {
                        // Same direction twice is a straight line through n, not a corner.
                        if (dir2 == dir) continue;
                        if (IsWallClosed(n.x, n.y, dir2)) continue;

                        Vector2Int a = Neighbour(n, dir2);
                        if (a.x < 0 || a.x >= width || a.y < 0 || a.y >= height) continue;
                        // The reverse direction leads straight back to the star cell itself.
                        if (a == star) continue;

                        Vector3 position = CellCenter(a.x, a.y);
                        if (!cells.Contains(position)) cells.Add(position);
                    }
                }
            }

            _ambushCells.Add(cells);
        }
    }

    /// <summary>
    /// Glowing shards hidden off the direct path (F31). Anti-farm rule (decision 4a): a retried floor
    /// spawns only ShardCount minus however many were already taken on this floor this campaign, so a
    /// retry after a partial sweep has fewer lying about instead of a free refill.
    /// </summary>
    private void SpawnShards(int usedSeed)
    {
        int wanted = _profile != null ? _profile.ShardCount : shardCount;
        wanted -= PlayerWallet.MazeShardsAlreadyTakenOnFloor(GameFlow.CurrentFloor);
        if (wanted <= 0) return;

        Material shardMaterial = BuildShardMaterial();
        AudioClip chime = BuildShardChimeClip();
        System.Random rng = new System.Random(usedSeed + 5);

        // F72: a vault forces up to 2 of the floor's shards inside it instead of adding to the total -
        // decision "shard count and caps unchanged, just relocated". Both shards land in the same cell,
        // so the second call excludes the first shard's wall.
        int forced = Mathf.Min(_vaultForcedShardCells.Count, wanted);
        HashSet<Vector3> excludeWalls = new HashSet<Vector3>();
        Vector2Int previousVaultCell = default;
        for (int i = 0; i < forced; i++)
        {
            Vector2Int vaultCell = _vaultForcedShardCells[i];
            if (i == 0 || vaultCell != previousVaultCell) excludeWalls.Clear();
            Vector3 usedWall = BuildShard(vaultCell, rng, shardMaterial, chime, excludeWalls);
            excludeWalls.Add(usedWall);
            previousVaultCell = vaultCell;
        }
        wanted -= forced;
        if (wanted <= 0) return;

        Vector2Int aiCell = FarthestCell();
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);
        HashSet<Vector2Int> vaultCellSet = new HashSet<Vector2Int>(_vaultForcedShardCells);

        // Preferred: nooks (two or more real walls). Fallback: anything else off the direct path.
        List<Vector2Int> preferred = new List<Vector2Int>();
        List<Vector2Int> fallback = new List<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == _startCell) continue;
                if (cell == aiCell) continue;
                if (starCellSet.Contains(cell)) continue;
                if (lockerCellSet.Contains(cell)) continue;
                if (vaultCellSet.Contains(cell)) continue;
                if (_distance[x, z] < 2) continue;

                bool farFromStars = true;
                foreach (Vector2Int starCell in _starCells)
                {
                    int manhattan = Mathf.Abs(cell.x - starCell.x) + Mathf.Abs(cell.y - starCell.y);
                    if (manhattan < 2) { farFromStars = false; break; }
                }
                if (!farFromStars) continue;

                if (WallCount(x, z) >= 2) preferred.Add(cell);
                else fallback.Add(cell);
            }
        }

        Shuffle(preferred, rng);
        Shuffle(fallback, rng);
        List<Vector2Int> pool = new List<Vector2Int>(preferred);
        pool.AddRange(fallback);

        int minSeparation = Mathf.Max(2, (width + height) / 5);
        List<Vector2Int> chosen = new List<Vector2Int>();

        for (int pass = 0; pass < 2 && chosen.Count < wanted; pass++)
        {
            foreach (Vector2Int candidate in pool)
            {
                if (chosen.Count >= wanted) break;
                if (chosen.Contains(candidate)) continue;
                if (pass == 0 && !IsFarEnough(candidate, chosen, minSeparation)) continue;
                chosen.Add(candidate);
            }
        }

        if (chosen.Count == 0) return;

        foreach (Vector2Int cell in chosen)
        {
            BuildShard(cell, rng, shardMaterial, chime, null);
        }
    }

    private Material BuildShardMaterial()
    {
        Material source = FindStarMaterial();
        if (source == null) return null;

        Material material = new Material(source) { name = "Maze_Shard" };
        material.EnableKeyword("_EMISSION");
        Color baseColor = new Color(0.55f, 0.9f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", baseColor);
        material.SetColor("_EmissionColor", baseColor * 2.5f);

        _runtimeMaterials.Add(material);
        return material;
    }

    private Material _shardModelMaterial;

    /// <summary>F90: the lumpy pack model keeps its form under the torch only if its glow is gentler than the old cube's flat 2.5x emission (which blows a whole lump out to a white silhouette). Shared by every shard of the floor.</summary>
    private Material ModelShardMaterial(Material glow)
    {
        if (_shardModelMaterial != null) return _shardModelMaterial;
        _shardModelMaterial = new Material(glow) { name = "Maze_ShardModel" };
        Color baseColor = new Color(0.55f, 0.9f, 1f);
        _shardModelMaterial.SetColor("_EmissionColor", baseColor * 0.9f);
        _runtimeMaterials.Add(_shardModelMaterial);
        return _shardModelMaterial;
    }

    /// <summary>
    /// A shard root sits unscaled at the wall (so its SphereCollider radius means what it says); the
    /// glassy cube it is built from is a scaled child instead of the root itself. Returns the wall
    /// direction it used, so a vault's second forced shard (F72) can avoid landing on the same wall as
    /// the first. excludeWalls may be null (the normal, non-forced path).
    /// </summary>
    private Vector3 BuildShard(Vector2Int cell, System.Random rng, Material material, AudioClip chime, HashSet<Vector3> excludeWalls)
    {
        int x = cell.x;
        int z = cell.y;
        Vector3 cellCenter = CellCenter(x, z);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;

        List<Vector3> options = new List<Vector3>();
        if (_wallN[x, z]) options.Add(Vector3.forward);
        if (_wallE[x, z]) options.Add(Vector3.right);
        if (_wallS[x, z]) options.Add(Vector3.back);
        if (_wallW[x, z]) options.Add(Vector3.left);
        if (options.Count == 0) return Vector3.zero; // every real cell has at least one wall

        // F72: a vault's second forced shard skips whichever wall the first one already took, so the
        // two don't overlap in the same tiny dead-end cell. Falls back to the full list if that would
        // empty it (a one-walled cell forced to hold two shards).
        if (excludeWalls != null && excludeWalls.Count > 0)
        {
            List<Vector3> notExcluded = new List<Vector3>(options.Count);
            foreach (Vector3 dir in options)
            {
                if (!excludeWalls.Contains(dir)) notExcluded.Add(dir);
            }
            if (notExcluded.Count > 0) options = notExcluded;
        }

        // Avoid a wall already dressed with a prop (a shard clipping a prop is ugly); if that would
        // empty the list, a shard on the same wall as a prop is harmless, so fall back to the full list.
        if (_propWalls.TryGetValue(cell, out List<Vector3> takenWalls) && takenWalls.Count > 0)
        {
            List<Vector3> avoiding = new List<Vector3>(options.Count);
            foreach (Vector3 dir in options)
            {
                if (!takenWalls.Contains(dir)) avoiding.Add(dir);
            }
            if (avoiding.Count > 0) options = avoiding;
        }

        Vector3 wallDirection = options[rng.Next(options.Count)];

        Vector3 position = cellCenter + wallDirection * (backFaceDistance * 0.55f) + Vector3.up * 0.85f;

        GameObject root = new GameObject($"Shard_{x}_{z}");
        root.transform.SetParent(_mazeRoot, false);
        root.transform.position = position;

        // F90: the body is the kit's pack model (InteractableKit.shardModel) when there is one, the old cube otherwise.
        // Either way exactly one rng.Next(360) is drawn here, so the +5 shard stream is the same as before.
        GameObject body;
        float yaw = rng.Next(360);
        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        GameObject model = interactableKit != null ? interactableKit.ShardModel : null;
        if (model != null)
        {
            body = Instantiate(model, root.transform);
            body.name = "Body";
            body.SetActive(true);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.Euler(35f, yaw, 25f);
            foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) Destroy(c);
            if (material != null)
            {
                foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = ModelShardMaterial(material);
            }
        }
        else
        {
            body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.layer = 0;
            body.transform.SetParent(root.transform, false);
            body.transform.localRotation = Quaternion.Euler(35f, yaw, 25f);
            body.transform.localScale = new Vector3(0.16f, 0.5f, 0.16f);

            Collider bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null) Destroy(bodyCollider);
            if (material != null) body.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        SphereCollider trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.7f;

        GameObject lightHolder = new GameObject("Light");
        lightHolder.transform.SetParent(root.transform, false);

        Light light = lightHolder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.6f, 0.9f, 1f);
        light.range = 2.5f;
        light.intensity = 0.7f;
        light.shadows = LightShadows.None;

        ShardPickup shard = root.AddComponent<ShardPickup>();
        shard.Configure(chime, (float)rng.NextDouble() * 10f);

        return wallDirection;
    }

    /// <summary>Two-partial glass chime, same construction technique as MazeEscape.BuildPingClip.</summary>
    private static AudioClip BuildShardChimeClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.22f;
        const float decay = 0.07f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t / decay);
            envelope *= Mathf.Clamp01(t / 0.003f); // 3 ms fade-in kills the attack click

            float value = 0.6f * Mathf.Sin(2f * Mathf.PI * 2100f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 3150f * t);
            samples[i] = value * envelope;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.8f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("ShardChime", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>Super easy mode: orders `cells` nearest-to-the-start first (BFS steps), keeping the shuffled order among equals.</summary>
    private void SortByStartDistance(List<Vector2Int> cells)
    {
        List<Vector2Int> ordered = new List<Vector2Int>(cells.Count);
        int maxDistance = 0;
        foreach (Vector2Int c in cells) maxDistance = Mathf.Max(maxDistance, _distance[c.x, c.y]);
        for (int d = 0; d <= maxDistance; d++)
        {
            foreach (Vector2Int c in cells) if (_distance[c.x, c.y] == d) ordered.Add(c);
        }
        foreach (Vector2Int c in cells) if (_distance[c.x, c.y] < 0) ordered.Add(c); // unreachable, last
        cells.Clear();
        cells.AddRange(ordered);
    }

    private bool IsFarEnough(Vector2Int candidate, List<Vector2Int> chosen, int minSeparation)
    {
        foreach (Vector2Int other in chosen)
        {
            int manhattan = Mathf.Abs(candidate.x - other.x) + Mathf.Abs(candidate.y - other.y);
            if (manhattan < minSeparation) return false;
        }

        return true;
    }

    // ---------------------------------------------------------------- fuses (F73)

    /// <summary>
    /// A Blackout floor's fuse boxes: FuseCount cells, spread out, each owning a sector of the maze
    /// found by a multi-source BFS from every fuse cell at once (every cell/lamp belongs to whichever
    /// fuse reached it in the fewest steps). Post-bake, after BuildDoors - a fuse box needs a free wall
    /// the same way a button does, so it has to see _propWalls/lamp walls and every door/button/vault
    /// cell already claimed (_doorUsedCells). On a non-Blackout floor (FuseCount 0) this is a no-op.
    /// </summary>
    private void BuildFuses(System.Random rng)
    {
        _fuseBoxes.Clear();
        if (_profile == null || _profile.FuseCount <= 0) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasFuseBox)
        {
            Debug.LogError("MazeGenerator: no InteractableKit fuse box in the scene - skipping this Blackout floor's fuses. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        if (aiFollower == null) aiFollower = FindAnyObjectByType<AIFollower>();

        Vector2Int start = _startCell;
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);

        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == start || (!superEasyMode && starCellSet.Contains(cell)) || _lockerCells.Contains(cell)) continue;
                if (_setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell)) continue;
                if (HasFreeWall(cell)) candidates.Add(cell);
            }
        }
        Shuffle(candidates, rng);
        if (superEasyMode) SortByStartDistance(candidates);

        List<Vector2Int> chosenCells = new List<Vector2Int>();
        int minSeparation = superEasyMode ? 1 : Mathf.Max(3, (width + height) / 5);
        while (minSeparation >= 1 && chosenCells.Count < _profile.FuseCount)
        {
            foreach (Vector2Int cell in candidates)
            {
                if (chosenCells.Count >= _profile.FuseCount) break;
                if (chosenCells.Contains(cell) || !IsFarEnough(cell, chosenCells, minSeparation)) continue;
                chosenCells.Add(cell);
            }
            minSeparation--;
        }

        if (chosenCells.Count < _profile.FuseCount)
        {
            Debug.LogWarning($"MazeGenerator: only {chosenCells.Count} of {_profile.FuseCount} fuse boxes fit in this maze.", this);
        }

        // Multi-source BFS: every cell/lamp belongs to whichever fuse cell reached it in the fewest
        // steps, with door passages counting as open exactly like every other open passage (the wall
        // arrays a MazeDoor sits on were never touched - see MazeDoor's own doc comment).
        Dictionary<Vector2Int, int> sectorOf = new Dictionary<Vector2Int, int>();
        Dictionary<Vector2Int, int> stepOf = new Dictionary<Vector2Int, int>();
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        for (int i = 0; i < chosenCells.Count; i++)
        {
            sectorOf[chosenCells[i]] = i;
            stepOf[chosenCells[i]] = 0;
            queue.Enqueue(chosenCells[i]);
        }
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int nextStep = stepOf[cell] + 1;
            int sector = sectorOf[cell];
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (sectorOf.ContainsKey(n)) continue;
                sectorOf[n] = sector;
                stepOf[n] = nextStep;
                queue.Enqueue(n);
            }
        }

        if (_profile.Has(FloorProfile.Objective.Blackout))
        {
            for (int i = 0; i < _lamps.Count; i++)
            {
                _lamps[i]?.SetPowered(false);
            }

            BuildEmergencyLight(start);
        }

        for (int i = 0; i < chosenCells.Count; i++)
        {
            BuildFuseBox(chosenCells[i], i, sectorOf, stepOf, rng);
        }
    }

    /// <summary>True if `cell` has at least one closed wall not already claimed by a wall prop, a button, or a lamp - the same free-wall test PlaceButtonFor uses.</summary>
    private bool HasFreeWall(Vector2Int cell)
    {
        for (int dir = 0; dir < 4; dir++)
        {
            if (IsFreeWall(cell, dir)) return true;
        }
        return false;
    }

    /// <summary>
    /// The free-wall test shared by every wall-mounted pass: `dir` (0 N, 1 E, 2 S, 3 W) is a closed wall of `cell` that no wall prop, button
    /// or set piece has claimed (_propWalls) and no lamp hangs on.
    /// </summary>
    public bool IsFreeWall(Vector2Int cell, int dir)
    {
        if (!IsWallClosed(cell.x, cell.y, dir)) return false;

        Vector3 dirVec = DirVector(dir);
        if (_propWalls.TryGetValue(cell, out List<Vector3> taken) && taken.Contains(dirVec)) return false;
        return !IsLampWall(cell, dirVec);
    }

    /// <summary>
    /// F104 B10: copies a seen set piece onto a free wall of another cell, so a landmark appears a second time. Visual only - the colliders and
    /// flicker lights are removed, because the NavMesh was baked without it. Refuses (false) for anything but a set piece, and for the start,
    /// star, locker, set-piece and door cells or a wall that is not free. Draws nothing from any rng. The twin keeps the source's active
    /// look-alike model (it is a copy), and its wall is reserved in _propWalls so nothing else lands on it.
    /// </summary>
    public bool TryPlaceTwin(ShiftablePlacement source, Vector2Int cell, int dir, out Transform twin)
    {
        twin = null;
        if (!source.IsSetPiece || source.Root == null || dir < 0 || dir > 3) return false;
        if (IsVoid(cell) || cell == _startCell || _starCells.Contains(cell) || _lockerCells.Contains(cell) || _setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell)) return false;
        if (!IsFreeWall(cell, dir)) return false;

        Vector3 dirVec = DirVector(dir);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * backFaceDistance;
        twin = Instantiate(source.Root.gameObject, pos, Quaternion.LookRotation(-dirVec), _setPiecesGroup).transform;
        twin.name = "Twin_" + source.Root.name;

        foreach (Collider collider in twin.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        foreach (FlickerLight flicker in twin.GetComponentsInChildren<FlickerLight>(true)) Destroy(flicker);

        if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
        {
            taken = new List<Vector3>();
            _propWalls[cell] = taken;
        }
        taken.Add(dirVec);
        return true;
    }

    /// <summary>Picks (and does not yet reserve) one of `cell`'s free walls, seeded so a same-seed retry mounts the fuse box on the same wall.</summary>
    private bool TryChooseFreeWall(Vector2Int cell, System.Random rng, out int dir)
    {
        List<int> options = new List<int>(4);
        for (int d = 0; d < 4; d++)
        {
            if (IsFreeWall(cell, d)) options.Add(d);
        }

        if (options.Count == 0)
        {
            dir = -1;
            return false;
        }

        dir = options[rng.Next(options.Count)];
        return true;
    }

    /// <summary>Clones InteractableKit.fuseBox onto a free wall of `cell`, and hands it every lamp in its sector plus each one's BFS step distance (the ripple delay).</summary>
    private void BuildFuseBox(Vector2Int cell, int sectorIndex, Dictionary<Vector2Int, int> sectorOf, Dictionary<Vector2Int, int> stepOf, System.Random rng)
    {
        if (!TryChooseFreeWall(cell, rng, out int dir))
        {
            Debug.LogWarning($"MazeGenerator: fuse box at {cell} has no free wall; skipping it.", this);
            return;
        }

        Vector3 dirVec = DirVector(dir);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        // Chest height, same reasoning as CreateButtonInstance (the cabinet used to sit half in the floor).
        Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * backFaceDistance + Vector3.up * 1.25f;

        FuseBox box = Instantiate(interactableKit.FuseBox, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
        box.gameObject.SetActive(true);
        box.name = $"FuseBox_{cell.x}_{cell.y}";

        List<WallLamp> sectorLamps = new List<WallLamp>();
        List<int> sectorSteps = new List<int>();
        for (int i = 0; i < _lamps.Count; i++)
        {
            Vector2Int lampCell = _lampCells[i];
            if (!sectorOf.TryGetValue(lampCell, out int owner) || owner != sectorIndex) continue;

            sectorLamps.Add(_lamps[i]);
            sectorSteps.Add(stepOf.TryGetValue(lampCell, out int steps) ? steps : 0);
        }

        box.Configure(sectorLamps, sectorSteps, aiFollower);

        if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
        {
            taken = new List<Vector3>();
            _propWalls[cell] = taken;
        }
        taken.Add(dirVec);

        _fuseBoxes.Add(box);
    }

    /// <summary>Blackout floors start dark - one faint red emergency light at the start cell so the first seconds aren't pure black.</summary>
    private void BuildEmergencyLight(Vector2Int cell)
    {
        GameObject holder = new GameObject("EmergencyLight");
        holder.transform.SetParent(_mazeRoot, false);
        holder.transform.position = CellCenter(cell.x, cell.y) + Vector3.up * 1.6f;

        Light light = holder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.9f, 0.15f, 0.1f);
        light.range = 3f;
        light.intensity = 0.4f;
        light.shadows = LightShadows.None;

        LightPool.Register(holder.transform, light.range, () => 1f);
    }

    // ---------------------------------------------------------------- keys (F77 Key Hunt)

    /// <summary>
    /// A KeyHunt floor's brass keys: FloorProfile.KeyCount cells, spread out by the same decreasing-
    /// minSeparation loop BuildFuses uses, each mounted on a free wall. Post-bake, after BuildFuses (so a
    /// key never lands on a reserved fuse-box wall) and before the +13 throwable/notes passes (so they in
    /// turn never land on a key's wall). Gates on what was actually placed, not FloorProfile.KeyCount
    /// (decision 11): KeyRing.Configure (SetUpAtmosphere) sets Total from _keys.Count, so a maze that only
    /// fits 1 of 3 keys locks the hatch behind 1 key rather than one that can never unlock.
    /// </summary>
    private void BuildKeys(System.Random rng)
    {
        _keys.Clear();
        if (_profile == null || _profile.KeyCount <= 0) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasKey)
        {
            Debug.LogError("MazeGenerator: no InteractableKit key in the scene - skipping this Key Hunt floor's keys (the hatch will not be locked). Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        Vector2Int start = _startCell;
        HashSet<Vector2Int> starCellSet = new HashSet<Vector2Int>(_starCells);
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);

        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == start || (!superEasyMode && starCellSet.Contains(cell)) || lockerCellSet.Contains(cell)) continue;
                if (_setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell)) continue;
                if (!superEasyMode && _distance[x, z] < 3) continue;
                if (HasFreeWall(cell)) candidates.Add(cell);
            }
        }
        Shuffle(candidates, rng);
        if (superEasyMode) SortByStartDistance(candidates);

        List<Vector2Int> chosenCells = new List<Vector2Int>();
        int minSeparation = superEasyMode ? 1 : Mathf.Max(3, (width + height) / 5);
        while (minSeparation >= 1 && chosenCells.Count < _profile.KeyCount)
        {
            foreach (Vector2Int cell in candidates)
            {
                if (chosenCells.Count >= _profile.KeyCount) break;
                if (chosenCells.Contains(cell) || !IsFarEnough(cell, chosenCells, minSeparation)) continue;
                chosenCells.Add(cell);
            }
            minSeparation--;
        }

        for (int i = 0; i < chosenCells.Count; i++)
        {
            Vector2Int cell = chosenCells[i];
            if (!TryChooseFreeWall(cell, rng, out int dir))
            {
                Debug.LogWarning($"MazeGenerator: key at {cell} has no free wall; skipping it.", this);
                continue;
            }

            Vector3 dirVec = DirVector(dir);
            float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
            Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * (backFaceDistance - 0.03f) + Vector3.up * 1.3f;

            KeyPickup key = Instantiate(interactableKit.KeyPickup, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
            key.gameObject.SetActive(true);
            key.name = $"Key_{cell.x}_{cell.y}";

            if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
            {
                taken = new List<Vector3>();
                _propWalls[cell] = taken;
            }
            taken.Add(dirVec);

            _keys.Add(key);
        }

        if (_keys.Count < _profile.KeyCount)
        {
            Debug.LogWarning($"MazeGenerator: only {_keys.Count} of {_profile.KeyCount} keys fit in this maze.", this);
        }
    }

    /// <summary>
    /// Warm amber point light parented to the star, so it dies when the star is collected. On
    /// its own a star is invisible until you are already on top of it, and the faint glow bleeding
    /// around a corner is what makes a pitch-black maze navigable rather than infuriating.
    /// </summary>
    private void AttachStarLight(GameObject star)
    {
        GameObject holder = new GameObject("StarLight");
        holder.transform.SetParent(star.transform, false);
        holder.transform.localPosition = Vector3.zero;

        Light light = holder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.64f, 0.19f);
        light.range = starLightRange;
        light.intensity = starLightIntensity;
        // The flashlight is the only shadow caster in the maze: a shadowed point light costs six
        // atlas faces, and a few of them would subdivide the atlas and wreck the flashlight.
        light.shadows = LightShadows.None;

        // F71: a gentle pulse so a star reads as something alive rather than a static bulb, now that
        // everything else in the maze has gone much darker around it.
        holder.AddComponent<StarPulse>().Configure(light);
    }

    private Material FindStarMaterial()
    {
        if (starPrefab == null) return null;

        Renderer renderer = starPrefab.GetComponentInChildren<Renderer>();
        return renderer != null ? renderer.sharedMaterial : null;
    }
}
