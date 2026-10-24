using System.Collections.Generic;
using UnityEngine;

public partial class MazeGenerator
{
    // ---------------------------------------------------------------- props

    /// <summary>
    /// Built before the navmesh bake so a prop's body collider carves the navmesh like a locker does.
    /// One prop per eligible cell at most. Skips the start cell, the AI cell and every locker cell
    /// (decision 6). Themed floor via _dressing (F70 decision 6/16) - reads _dressing instead
    /// of _theme directly, but draws from propRng in exactly the order it always has (chance, wall pick,
    /// prop pick), so an existing seed's props land on the same walls as before this feature. The new
    /// _setPieceCells skip is checked only after all of those draws (F70 decision 16).
    ///
    /// _propWalls itself is no longer cleared here - BuildSetPieces runs first and needs to leave its
    /// reservations in place for this pass to skip, so the Clear moved to the top of BuildSetPieces.
    /// </summary>
    private void BuildWallProps(System.Random rng)
    {
        if (_dressing.Props == null || _dressing.Props.Length == 0) return;

        Vector2Int aiCell = FarthestCell();
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        List<Vector3> walls = new List<Vector3>(4);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z) || (x == _startCell.x && z == _startCell.y)) continue;
                if (x == aiCell.x && z == aiCell.y) continue;

                Vector2Int cell = new Vector2Int(x, z);
                if (lockerCellSet.Contains(cell)) continue;
                if (rng.NextDouble() >= _dressing.WallPropChance) continue;

                walls.Clear();
                if (_wallN[x, z]) walls.Add(Vector3.forward);
                if (_wallE[x, z]) walls.Add(Vector3.right);
                if (_wallS[x, z]) walls.Add(Vector3.back);
                if (_wallW[x, z]) walls.Add(Vector3.left);
                if (walls.Count == 0) continue;

                Vector3 dir = walls[rng.Next(walls.Count)];
                PropPiece prop = PickWeightedProp(_dressing.Props, PropPiece.MountKind.Wall, rng);
                if (prop == null) return; // no wall props in this theme, nothing more to try

                // F70: a set piece already claimed this cell. Checked after every RNG draw the cell
                // makes (chance, wall pick, prop pick), same rule as the low-ceiling guard below, so an
                // old scene (no set pieces) draws exactly as it always has.
                if (_setPieceCells.Contains(cell)) continue;
                if (IsVoidWindowWall(cell, dir)) continue; // F125 D3: never mount a wall prop across an iron-barred void window

                Vector3 pos = CellCenter(x, z) + dir * backFaceDistance;
                PropPiece clone = Instantiate(prop, pos, Quaternion.LookRotation(-dir), _propsGroup);
                clone.gameObject.SetActive(true);
                clone.name = $"Prop_{prop.name}_{x}_{z}";
                ApplyModelVariants(clone, x, z, 0x3A11);
                RecordShiftable(clone, cell, dir, false);

                if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
                {
                    taken = new List<Vector3>();
                    _propWalls[cell] = taken;
                }
                taken.Add(dir);
            }
        }
    }

    /// <summary>Built after the navmesh bake. Never shares a cell with a wall prop (decision 6, "keeps clutter readable") or a set piece.</summary>
    private void BuildCeilingProps(System.Random rng)
    {
        _ceilingPropCells.Clear(); // F107 A
        if (_dressing.Props == null || _dressing.Props.Length == 0) return;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z) || (x == _startCell.x && z == _startCell.y)) continue;

                Vector2Int cell = new Vector2Int(x, z);
                if (_lockerCells.Contains(cell)) continue;
                if (_propWalls.ContainsKey(cell)) continue;
                if (rng.NextDouble() >= _dressing.CeilingPropChance) continue;

                PropPiece prop = PickWeightedProp(_dressing.Props, PropPiece.MountKind.Ceiling, rng);
                if (prop == null) return; // no ceiling props in this theme, nothing more to try

                float cellCeil = CeilingHeightAt(x, z);
                Vector3 pos = CellCenter(x, z) + Vector3.up * cellCeil;
                Quaternion rotation = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);

                // F40 slice A decision 6: a low ceiling (floor 3) can't fit every prop without brushing
                // the player. Checked after every RNG draw this cell makes (chance, pick, rotation) so
                // the seed stream is unchanged - a taller floor still gets exactly the props, in exactly
                // the same rotations, it would have before this guard existed.
                if (!prop.AllowLowHang && prop.Depth > cellCeil - 2.2f) continue;

                // F70: same rule as above - checked after every draw, so it never perturbs the stream.
                if (_setPieceCells.Contains(cell)) continue;

                PropPiece clone = Instantiate(prop, pos, rotation, _propsGroup);
                clone.gameObject.SetActive(true);
                clone.name = $"Prop_{prop.name}_{x}_{z}";
                _ceilingPropCells.Add(cell); // F107 A: after every draw, so no stream moves
                ApplyModelVariants(clone, x, z, 0x4B22);
                RecordShiftable(clone, cell, Vector3.zero, false);

                foreach (HangingBodyFit fit in clone.GetComponentsInChildren<HangingBodyFit>(true)) fit.Fit(cellCeil); // F85: rope cut to this floor's ceiling
            }
        }
    }

    /// <summary>F107 A: true if a ceiling prop (a hanging body, lamp, ...) hangs in this cell, so nothing meant for the cell centre should go there.</summary>
    public bool HasCeilingProp(Vector2Int cell) => _ceilingPropCells.Contains(cell);

    /// <summary>Weighted pick among a theme's props of one mount kind, honouring EnabledInMaze. Null if the theme has none of that kind (or none currently enabled).</summary>
    /// <param name="variantWeight">Multiplies the weight of a piece carrying DecalVariants (the blood pack decals). 1 = unchanged.</param>
    private static PropPiece PickWeightedProp(PropPiece[] props, PropPiece.MountKind mount, System.Random rng, float variantWeight = 1f)
    {
        float totalWeight = 0f;
        foreach (PropPiece candidate in props)
        {
            if (candidate == null || candidate.Mount != mount || !candidate.EnabledInMaze) continue;
            totalWeight += PickWeight(candidate, variantWeight);
        }
        if (totalWeight <= 0f) return null;

        float roll = (float)(rng.NextDouble() * totalWeight);
        float cumulative = 0f;
        foreach (PropPiece candidate in props)
        {
            if (candidate == null || candidate.Mount != mount || !candidate.EnabledInMaze) continue;
            cumulative += PickWeight(candidate, variantWeight);
            if (roll <= cumulative) return candidate;
        }

        return null; // floating point edge case only
    }

    private static float PickWeight(PropPiece candidate, float variantWeight)
    {
        float weight = Mathf.Max(0.0001f, candidate.Weight);
        return variantWeight != 1f && candidate.GetComponent<DecalVariants>() != null ? weight * variantWeight : weight;
    }

    /// <summary>
    /// F70. Small vignettes built before the navmesh bake so their body colliders carve it like a locker
    /// does. Reserves a cell each: skipped afterwards by BuildWallProps, BuildCeilingProps, BuildFloorProps
    /// and BuildDecals (decision 9). Runs before BuildWallProps, so this is also where _propWalls is
    /// cleared (decision: BuildWallProps no longer clears it itself, since it needs to see the
    /// reservations this pass writes).
    ///
    /// Candidates are cells whose _distance is between 25% and 75% of the maze's max distance - far
    /// enough from the start to not spoil the opening seconds, near enough that a set piece is not
    /// stranded past where most runs reach. _starCells does not exist yet at this point in Awake (SpawnStars
    /// runs later), which is why the distance band substitutes for a "not on a star" rule.
    /// </summary>
    private void BuildSetPieces(System.Random rng)
    {
        _setPieceCells.Clear();
        _propWalls.Clear();
        RestoreVoidWindowPropWalls(); // F125 D3: keep barred void-window walls reserved in _propWalls
        _shiftables.Clear();
        if (_dressing.SetPieces == null || _dressing.SetPieces.Length == 0 || _dressing.SetPieceCount <= 0) return;

        Vector2Int aiCell = FarthestCell();
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);

        int maxDistance = 0;
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (_distance[x, z] > maxDistance) maxDistance = _distance[x, z];
            }
        }

        int minDistance = Mathf.RoundToInt(maxDistance * 0.25f);
        int maxAllowedDistance = Mathf.RoundToInt(maxDistance * 0.75f);

        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (IsVoid(x, z) || (x == _startCell.x && z == _startCell.y)) continue;
                if (cell == aiCell) continue;
                if (lockerCellSet.Contains(cell)) continue;
                if (_distance[x, z] < minDistance || _distance[x, z] > maxAllowedDistance) continue;
                if (WallCount(x, z) == 0) continue; // needs at least one closed wall to mount on
                candidates.Add(cell);
            }
        }

        Shuffle(candidates, rng);

        List<Vector2Int> chosenCells = new List<Vector2Int>();
        List<string> chosenNames = new List<string>();
        List<Vector3> walls = new List<Vector3>(4);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        float maxWidth = cellSize - 1.4f;

        foreach (Vector2Int cell in candidates)
        {
            if (chosenCells.Count >= _dressing.SetPieceCount) break;
            if (IsLayoutReserved(cell)) continue; // F105 C3, C4: checked here, not when the candidates are listed, so the +8 shuffle is unchanged
            if (!ChebyshevFarEnough(cell, chosenCells, 3)) continue;

            walls.Clear();
            if (_wallN[cell.x, cell.y] && !IsVoidWindowWall(cell, Vector3.forward)) walls.Add(Vector3.forward);
            if (_wallE[cell.x, cell.y] && !IsVoidWindowWall(cell, Vector3.right)) walls.Add(Vector3.right);
            if (_wallS[cell.x, cell.y] && !IsVoidWindowWall(cell, Vector3.back)) walls.Add(Vector3.back);
            if (_wallW[cell.x, cell.y] && !IsVoidWindowWall(cell, Vector3.left)) walls.Add(Vector3.left);
            if (walls.Count == 0) continue;

            Vector3 dir = walls[rng.Next(walls.Count)];
            SetPiece piece = PickWeightedSetPiece(_dressing.SetPieces, rng, maxWidth);
            if (piece == null) continue; // nothing fits this floor's cell size

            Vector3 pos = CellCenter(cell.x, cell.y) + dir * backFaceDistance;
            SetPiece clone = Instantiate(piece, pos, Quaternion.LookRotation(-dir), _setPiecesGroup);
            clone.gameObject.SetActive(true);
            clone.name = $"SetPiece_{piece.name}_{cell.x}_{cell.y}";
            // Look-alike models (the FallenRunner's corpses), picked by a hash of the cell - no rng draw.
            if (clone.TryGetComponent(out ModelVariants models)) models.Apply((cell.x * 73856093) ^ (cell.y * 19349663) ^ 0x5EED);
            RecordShiftable(clone, cell, dir, true);

            foreach (FlickerLight flicker in clone.GetComponentsInChildren<FlickerLight>(true))
            {
                flicker.Configure(rng.Next());
            }

            chosenCells.Add(cell);
            chosenNames.Add(piece.name);
            _setPieceCells.Add(cell);

            if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
            {
                taken = new List<Vector3>();
                _propWalls[cell] = taken;
            }
            taken.Add(dir);
        }

        if (chosenNames.Count > 0)
        {
            Debug.Log($"MazeGenerator: {chosenNames.Count} set pieces: {string.Join(", ", chosenNames)}", this);
        }
    }

    /// <summary>Weighted pick among a set of pieces, honouring EnabledInMaze and Width &lt;= maxWidth (a floor with a small cellSize can't fit every set piece). Null if nothing fits or is enabled.</summary>
    private static SetPiece PickWeightedSetPiece(SetPiece[] pieces, System.Random rng, float maxWidth)
    {
        float totalWeight = 0f;
        foreach (SetPiece candidate in pieces)
        {
            if (candidate == null || !candidate.EnabledInMaze || candidate.Width > maxWidth) continue;
            totalWeight += Mathf.Max(0.0001f, candidate.Weight);
        }
        if (totalWeight <= 0f) return null;

        float roll = (float)(rng.NextDouble() * totalWeight);
        float cumulative = 0f;
        foreach (SetPiece candidate in pieces)
        {
            if (candidate == null || !candidate.EnabledInMaze || candidate.Width > maxWidth) continue;
            cumulative += Mathf.Max(0.0001f, candidate.Weight);
            if (roll <= cumulative) return candidate;
        }

        return null; // floating point edge case only
    }

    /// <summary>Chebyshev (grid/king-move) separation check, used by set pieces so they do not cluster in one corner. Manhattan (IsFarEnough) is what stars/lockers/shards use; Chebyshev is what the F70 plan specifies for these two.</summary>
    private static bool ChebyshevFarEnough(Vector2Int candidate, List<Vector2Int> chosen, int minSeparation)
    {
        foreach (Vector2Int other in chosen)
        {
            int chebyshev = Mathf.Max(Mathf.Abs(candidate.x - other.x), Mathf.Abs(candidate.y - other.y));
            if (chebyshev < minSeparation) return false;
        }

        return true;
    }

    /// <summary>
    /// F70, built after the navmesh bake: low clutter against the wall base, no collider (decision 4 -
    /// the player walks through it, the hunter never needs to path round it). New RNG stream (+6), never
    /// read by anything that existed before this feature.
    /// </summary>
    private void BuildFloorProps(System.Random rng)
    {
        if (_dressing.Props == null || _dressing.Props.Length == 0) return;

        Vector2Int aiCell = FarthestCell();
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        List<Vector3> walls = new List<Vector3>(4);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z) || (x == _startCell.x && z == _startCell.y)) continue;
                if (x == aiCell.x && z == aiCell.y) continue;

                Vector2Int cell = new Vector2Int(x, z);
                if (lockerCellSet.Contains(cell)) continue;
                if (_setPieceCells.Contains(cell)) continue;
                if (rng.NextDouble() >= _dressing.FloorPropChance) continue;

                walls.Clear();
                if (_wallN[x, z]) walls.Add(Vector3.forward);
                if (_wallE[x, z]) walls.Add(Vector3.right);
                if (_wallS[x, z]) walls.Add(Vector3.back);
                if (_wallW[x, z]) walls.Add(Vector3.left);

                if (_propWalls.TryGetValue(cell, out List<Vector3> taken) && taken.Count > 0)
                {
                    walls.RemoveAll(dir => taken.Contains(dir));
                }
                if (walls.Count == 0) continue;

                Vector3 dir = walls[rng.Next(walls.Count)];
                PropPiece prop = PickWeightedProp(_dressing.Props, PropPiece.MountKind.Floor, rng);
                if (prop == null) return; // no floor props in this set, nothing more to try

                Vector3 tangent = Vector3.Cross(Vector3.up, dir);
                float lateral = (float)(rng.NextDouble() * 2.0 - 1.0) * (cellSize * 0.5f - 1.0f);
                Vector3 pos = CellCenter(x, z) + dir * backFaceDistance + tangent * lateral;
                float yaw = (float)(rng.NextDouble() * 2.0 - 1.0) * 25f;
                Quaternion rotation = Quaternion.LookRotation(-dir) * Quaternion.Euler(0f, yaw, 0f);

                PropPiece clone = Instantiate(prop, pos, rotation, _propsGroup);
                clone.gameObject.SetActive(true);
                clone.name = $"Prop_{prop.name}_{x}_{z}";
            }
        }
    }

    /// <summary>
    /// F70, built after the navmesh bake: alpha-clipped decal quads, analytic placement only (no
    /// raycasts - decision 4.5: colliders built earlier this same Awake are not reliably queryable before
    /// Physics.SyncTransforms, and a locker trigger would catch a ray anyway). New RNG stream (+7).
    /// </summary>
    private void BuildDecals(System.Random rng)
    {
        if (_dressing.Props == null || _dressing.Props.Length == 0) return;
        if (_dressing.MaxDecalsPerCell <= 0) return;

        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        List<Vector3> walls = new List<Vector3>(4);

        // decalDensity / bloodWeight: only this pass's own +7 stream changes, nothing after it reads it.
        float wallChance = Mathf.Min(0.9f, _dressing.WallDecalChance * decalDensity);
        float floorChance = Mathf.Min(0.9f, _dressing.FloorDecalChance * decalDensity);
        int maxPerCell = _dressing.MaxDecalsPerCell + (decalDensity > 1f ? 1 : 0);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z) || (x == _startCell.x && z == _startCell.y)) continue;

                Vector2Int cell = new Vector2Int(x, z);
                if (_setPieceCells.Contains(cell)) continue;

                Vector3 lockerWallDir = _lockerWall.TryGetValue(cell, out Vector3 lw) ? lw : Vector3.zero;
                _propWalls.TryGetValue(cell, out List<Vector3> takenWalls);

                for (int i = 0; i < maxPerCell; i++)
                {
                    float threshold = wallChance * (i == 0 ? 1f : 0.4f);
                    if (rng.NextDouble() >= threshold) break;

                    walls.Clear();
                    if (_wallN[x, z] && lockerWallDir != Vector3.forward) walls.Add(Vector3.forward);
                    if (_wallE[x, z] && lockerWallDir != Vector3.right) walls.Add(Vector3.right);
                    if (_wallS[x, z] && lockerWallDir != Vector3.back) walls.Add(Vector3.back);
                    if (_wallW[x, z] && lockerWallDir != Vector3.left) walls.Add(Vector3.left);
                    if (takenWalls != null) walls.RemoveAll(dir => takenWalls.Contains(dir));
                    if (walls.Count == 0) continue;

                    Vector3 dir = walls[rng.Next(walls.Count)];
                    PropPiece decal = PickWeightedProp(_dressing.Props, PropPiece.MountKind.WallDecal, rng, bloodWeight);
                    if (decal == null) continue;

                    float lateral = (float)(rng.NextDouble() * 2.0 - 1.0) * (cellSize * 0.5f - 0.8f);
                    float scale = Mathf.Lerp(decal.DecalSizeRange.x, decal.DecalSizeRange.y, (float)rng.NextDouble());
                    float y = 0.3f + (float)rng.NextDouble() * 1.9f;
                    y = scale > 1.9f ? 1.25f : Mathf.Clamp(y, scale * 0.5f + 0.3f, 2.2f - scale * 0.5f);
                    float roll = decal.RandomRoll ? (float)rng.NextDouble() * 360f : 0f;

                    Vector3 tangent = Vector3.Cross(Vector3.up, dir);
                    Vector3 pos = CellCenter(x, z) + dir * (backFaceDistance - 0.04f) + tangent * lateral + Vector3.up * y;
                    Quaternion rotation = Quaternion.LookRotation(-dir) * Quaternion.Euler(0f, 0f, roll);

                    PropPiece clone = Instantiate(decal, pos, rotation, _decalsGroup);
                    clone.gameObject.SetActive(true);
                    clone.transform.localScale = Vector3.one * scale;
                    clone.name = $"Decal_{decal.name}_{x}_{z}_{i}";
                    ApplyDecalVariant(clone, x, z, i, 0);
                }

                for (int i = 0; i < maxPerCell; i++)
                {
                    float threshold = floorChance * (i == 0 ? 1f : 0.4f);
                    if (rng.NextDouble() >= threshold) break;

                    PropPiece decal = PickWeightedProp(_dressing.Props, PropPiece.MountKind.FloorDecal, rng, bloodWeight);
                    if (decal == null) continue;

                    float rawX = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float rawZ = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float scale = Mathf.Lerp(decal.DecalSizeRange.x, decal.DecalSizeRange.y, (float)rng.NextDouble());
                    float yaw = decal.RandomRoll ? (float)rng.NextDouble() * 360f : 0f;

                    // F79: a big decal (a rug) keeps nearer the cell centre, or its far end slides out
                    // under the wall into the next corridor. Same draws in the same order as before, and
                    // nothing changes for a decal up to 1.2 m.
                    float reach = Mathf.Max(0f, cellSize * 0.5f - 0.8f - Mathf.Max(0f, scale - 1.2f) * 0.5f);
                    float offsetX = rawX * reach;
                    float offsetZ = rawZ * reach;

                    Vector3 pos = CellCenter(x, z) + new Vector3(offsetX, 0f, offsetZ) + Vector3.up * (0.004f + 0.002f * i);
                    Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

                    PropPiece clone = Instantiate(decal, pos, rotation, _decalsGroup);
                    clone.gameObject.SetActive(true);
                    clone.transform.localScale = Vector3.one * scale;
                    clone.name = $"Decal_{decal.name}_{x}_{z}_{i}";
                    ApplyDecalVariant(clone, x, z, i, 1);
                }
            }
        }
    }

    /// <summary>
    /// A decal with DecalVariants (the blood pack splats/smears/pools) gets one of its look-alikes, picked by a
    /// hash of where it landed - like the bottle variants, no rng draw, so the +7 stream is unchanged.
    /// </summary>
    private static void ApplyDecalVariant(PropPiece clone, int x, int z, int i, int floor)
    {
        if (clone.TryGetComponent(out DecalVariants variants))
        {
            variants.Apply((x * 73856093) ^ (z * 19349663) ^ (i * 83492791) ^ (floor * 2654435));
        }
    }

    /// <summary>F70 decision 12: one camera-attached dust ParticleSystem per run. Not parented to the maze - DustMotes re-parents itself to the player camera lazily, once FirstPersonRig exists.</summary>
    private void BuildDust()
    {
        if (_dressing.Dust == null) return;

        DustMotes clone = Instantiate(_dressing.Dust);
        clone.gameObject.SetActive(true);
        clone.MarkRuntimeClone();
        clone.name = "DustMotes";
    }

    /// <summary>
    /// Ground fog: the kit's fogBank (the hub fog's look) cloned once per chunk of fogChunkCells x fogChunkCells cells, its flat box sized
    /// to the chunk at FloorTop and its count to the chunk's area. Every cell of the grid is corridor, so the box needs no walkable mask;
    /// a bank half inside a wall fades out against it (soft particles). Off-screen chunks pause. Post-bake, collider-less, no rng draw.
    /// </summary>
    private bool _fogBuilt;

    private void BuildFog()
    {
        if (fogDensity <= 0f || fogMaxParticles <= 0) return;
        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        ParticleSystem template = interactableKit != null ? interactableKit.FogBank : null;
        if (template == null)
        {
            Debug.LogError("MazeGenerator: InteractableKit has no fogBank, so this maze has no ground fog. Run KEEP IT LIT > Build > Interactables.", this);
            return;
        }

        float density = Mathf.Min(fogDensity * 0.5f, fogMaxParticles / Mathf.Max(1f, width * cellSize * height * cellSize));
        _fogBuilt = true;
        Transform group = new GameObject("Fog").transform;
        group.SetParent(_mazeRoot, false);
        for (int cz = 0; cz < height; cz += fogChunkCells)
        {
            for (int cx = 0; cx < width; cx += fogChunkCells)
            {
                int nx = Mathf.Min(fogChunkCells, width - cx), nz = Mathf.Min(fogChunkCells, height - cz);
                Vector3 size = new Vector3(nx * cellSize, 0f, nz * cellSize);
                Vector3 centre = new Vector3(origin.x + (cx + nx * 0.5f) * cellSize, FloorTop, origin.z + (cz + nz * 0.5f) * cellSize);
                int alive = Mathf.Max(2, Mathf.RoundToInt(size.x * size.z * density * FogScaleForChunk(cx, cz, nx, nz))); // F105 C2: thinner over the gallery

                ParticleSystem fog = Instantiate(template, centre, Quaternion.identity, group);
                fog.name = $"Fog_{cx}_{cz}";
                fog.gameObject.SetActive(true);
                fog.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = fog.main;
                main.maxParticles = alive + 2;
                ParticleSystem.EmissionModule emission = fog.emission;
                float life = (main.startLifetime.constantMin + main.startLifetime.constantMax) * 0.5f;
                emission.rateOverTime = alive / Mathf.Max(1f, life);
                ParticleSystem.ShapeModule shape = fog.shape;
                shape.scale = new Vector3(size.x, 0f, size.z);
                fog.Simulate(life, true, true); // settled when the floor starts, not filling in
                fog.gameObject.AddComponent<FogScale>(); // S2: the Fog setting scales / switches this chunk
                fog.Play();
            }
        }
    }
}
