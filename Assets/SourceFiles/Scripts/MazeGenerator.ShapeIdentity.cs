using System.Collections.Generic;
using UnityEngine;

public partial class MazeGenerator
{
    // F125: dedicated RNG stream offset (+28) for post-carve shape avenues & void atmosphere.
    // Never read on FloorShape.Rectangle, so Story Mode and Level 1 DESCENT are 100% untouched.
    private const int ShapeIdentitySalt = 28;

    private readonly HashSet<(Vector2Int cell, Vector3 dir)> _voidWindowWalls = new HashSet<(Vector2Int cell, Vector3 dir)>();
    private Material _voidGrilleMaterial;

    private bool HasCustomShape => _profile != null && _profile.Shape != FloorProfile.FloorShape.Rectangle;

    // =========================================================================
    // 1. SHAPE GEOMETRY QUERIES
    // =========================================================================

    /// <summary>True when (x, z) is a playable cell that shares at least one cardinal edge with an in-bounds void cell.</summary>
    private bool TouchesInternalVoid(int x, int z)
    {
        if (!HasCustomShape || IsVoid(x, z)) return false;
        if (x > 0 && IsVoid(x - 1, z)) return true;
        if (x + 1 < width && IsVoid(x + 1, z)) return true;
        if (z > 0 && IsVoid(x, z - 1)) return true;
        if (z + 1 < height && IsVoid(x, z + 1)) return true;
        return false;
    }

    /// <summary>True when (x, z) lies inside the narrow central bridge connecting the West and East wings of a TwinWings floor.</summary>
    private bool IsInTwinWingsBridge(int x, int z)
    {
        if (_profile == null || _profile.Shape != FloorProfile.FloorShape.TwinWings || IsVoid(x, z)) return false;
        int vw = Mathf.Clamp(width - 6, 2, 4);
        int x0 = (width - vw) / 2;
        return x >= x0 && x < x0 + vw;
    }

    /// <summary>True when (x, z) lies inside the 3x3 central crossroads atrium of a Cross floor.</summary>
    private bool IsInCrossAtrium(int x, int z)
    {
        if (_profile == null || _profile.Shape != FloorProfile.FloorShape.Cross || IsVoid(x, z)) return false;
        int cx = width / 2;
        int cz = height / 2;
        return Mathf.Abs(x - cx) <= 1 && Mathf.Abs(z - cz) <= 1;
    }

    /// <summary>True when (x, z) lies along the inner concave bend of an LChasm floor.</summary>
    private bool IsInLChasmCorner(int x, int z)
    {
        if (_profile == null || _profile.Shape != FloorProfile.FloorShape.LChasm || IsVoid(x, z)) return false;
        int cw = Mathf.Clamp(width / 2 - 1, 3, 6);
        int ch = Mathf.Clamp(height / 2 - 1, 3, 5);
        int cornerX = width - cw - 1;
        int cornerZ = height - ch - 1;
        return Mathf.Abs(x - cornerX) <= 1 && Mathf.Abs(z - cornerZ) <= 1;
    }

    private Vector2Int ShapeWorldToCell(Vector3 worldPos)
    {
        int x = Mathf.Clamp(Mathf.FloorToInt((worldPos.x - origin.x) / cellSize), 0, width - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt((worldPos.z - origin.z) / cellSize), 0, height - 1);
        return new Vector2Int(x, z);
    }

    private bool IsLockerBackWall(int x, int z, int dir)
    {
        return _lockerWall.TryGetValue(new Vector2Int(x, z), out Vector3 lw) && lw == DirVector(dir);
    }

    private void OpenShapeWall(Vector2Int a, Vector2Int b, int dirFromA)
    {
        switch (dirFromA)
        {
            case 0:
                _wallN[a.x, a.y] = false;
                _wallS[b.x, b.y] = false;
                break;
            case 1:
                _wallE[a.x, a.y] = false;
                _wallW[b.x, b.y] = false;
                break;
            case 2:
                _wallS[a.x, a.y] = false;
                _wallN[b.x, b.y] = false;
                break;
            default:
                _wallW[a.x, a.y] = false;
                _wallE[b.x, b.y] = false;
                break;
        }
    }

    // =========================================================================
    // 2. SHAPE-AWARE CARVE BIAS, DEAD-END TUNING & RING/ARM AVENUES (D2)
    // =========================================================================

    private int PickShapeCarveDirection(Vector2Int current, List<int> candidates, int lastDir, System.Random rng)
    {
        if (!HasCustomShape || candidates.Count <= 1)
        {
            return candidates[rng.Next(candidates.Count)];
        }

        FloorProfile.FloorShape shape = _profile.Shape;
        float totalWeight = 0f;
        System.Span<float> weights = stackalloc float[candidates.Count];

        float cx = (width - 1) * 0.5f;
        float cz = (height - 1) * 0.5f;

        for (int i = 0; i < candidates.Count; i++)
        {
            int dir = candidates[i];
            Vector2Int next = Neighbour(current, dir);
            bool ns = dir == 0 || dir == 2;
            bool ew = dir == 1 || dir == 3;
            float w = 1f;

            switch (shape)
            {
                case FloorProfile.FloorShape.Ring:
                {
                    // Prefer carving tangentially around the central void shaft rather than cutting across the ring.
                    float dx = Mathf.Abs(current.x - cx) / Mathf.Max(1f, width * 0.5f);
                    float dz = Mathf.Abs(current.y - cz) / Mathf.Max(1f, height * 0.5f);
                    bool tangent = (dx > dz && ns) || (dz >= dx && ew);
                    if (tangent) w *= 2.8f;
                    if (TouchesInternalVoid(next.x, next.y) && tangent) w *= 1.35f;
                    break;
                }
                case FloorProfile.FloorShape.TwinWings:
                {
                    // Straight East-West arterial flow through the central bridge; North-South flow inside the two wings.
                    if (IsInTwinWingsBridge(current.x, current.y) || IsInTwinWingsBridge(next.x, next.y))
                    {
                        if (ew) w *= 3.6f;
                    }
                    else
                    {
                        if (ns) w *= 1.65f;
                    }
                    break;
                }
                case FloorProfile.FloorShape.Cross:
                {
                    // Axial corridors down each arm; straight spokes through the central crossroads.
                    int hubX = width / 2;
                    int hubZ = height / 2;
                    if (Mathf.Abs(current.x - hubX) <= 1 && Mathf.Abs(current.y - hubZ) <= 1)
                    {
                        if (dir == lastDir) w *= 2.5f;
                    }
                    else if (Mathf.Abs(current.y - hubZ) > Mathf.Abs(current.x - hubX))
                    {
                        if (ns) w *= 2.4f;
                    }
                    else
                    {
                        if (ew) w *= 2.4f;
                    }
                    break;
                }
                case FloorProfile.FloorShape.LChasm:
                {
                    // Long sightlines hugging the two perpendicular legs and the concave chasm rim.
                    if (dir == lastDir) w *= 2.3f;
                    if (TouchesInternalVoid(next.x, next.y)) w *= 1.55f;
                    break;
                }
            }

            weights[i] = w;
            totalWeight += w;
        }

        float roll = (float)(rng.NextDouble() * totalWeight);
        float accum = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            accum += weights[i];
            if (roll <= accum) return candidates[i];
        }

        return candidates[candidates.Count - 1];
    }

    private float AdjustShapeDeadEndKeep(float baseKeep)
    {
        if (!HasCustomShape || baseKeep <= 0f) return baseKeep;

        switch (_profile.Shape)
        {
            case FloorProfile.FloorShape.Ring:
                return baseKeep * 0.55f; // Fewer cul-de-sacs: emphasise circular loop flow
            case FloorProfile.FloorShape.Cross:
                return Mathf.Min(0.85f, baseKeep * 1.30f); // More deep pockets at the ends of the 4 arms
            case FloorProfile.FloorShape.TwinWings:
                return baseKeep * 0.85f;
            case FloorProfile.FloorShape.LChasm:
                return baseKeep * 0.75f;
            default:
                return baseKeep;
        }
    }

    /// <summary>
    /// F125 D2: Post-dead-end-braiding pass using its own RNG stream (usedSeed + 28) so existing streams are untouched.
    /// Carves shape-defining avenues: inner gallery loop on Ring, clean bridge crossings on TwinWings,
    /// axial spokes into the 3x3 atrium on Cross, and the cliffside rim avenue on LChasm.
    /// </summary>
    private void CarveShapeAvenues(int usedSeed)
    {
        if (!HasCustomShape) return;

        System.Random rng = new System.Random(usedSeed + ShapeIdentitySalt);
        switch (_profile.Shape)
        {
            case FloorProfile.FloorShape.Ring:
                CarveRingInnerGallery(rng);
                break;
            case FloorProfile.FloorShape.TwinWings:
                CarveTwinWingsBridge(rng);
                break;
            case FloorProfile.FloorShape.Cross:
                CarveCrossAtriumAndSpokes(rng);
                break;
            case FloorProfile.FloorShape.LChasm:
                CarveLChasmRimAvenue(rng);
                break;
        }
    }

    private void CarveRingInnerGallery(System.Random rng)
    {
        int vw = Mathf.Clamp(width - 6, 2, 6);
        int vh = Mathf.Clamp(height - 6, 2, 5);
        int x0 = (width - vw) / 2;
        int z0 = (height - vh) / 2;

        int ringLeft = Mathf.Max(0, x0 - 1);
        int ringRight = Mathf.Min(width - 1, x0 + vw);
        int ringSouth = Mathf.Max(0, z0 - 1);
        int ringNorth = Mathf.Min(height - 1, z0 + vh);

        // Open ~70% of closed segments along the inner perimeter loop immediately hugging the central shaft.
        for (int x = ringLeft; x < ringRight; x++)
        {
            TryOpenAvenueEdge(new Vector2Int(x, ringSouth), 1, rng, 0.70);
            TryOpenAvenueEdge(new Vector2Int(x, ringNorth), 1, rng, 0.70);
        }
        for (int z = ringSouth; z < ringNorth; z++)
        {
            TryOpenAvenueEdge(new Vector2Int(ringLeft, z), 0, rng, 0.70);
            TryOpenAvenueEdge(new Vector2Int(ringRight, z), 0, rng, 0.70);
        }
    }

    private void CarveTwinWingsBridge(System.Random rng)
    {
        int vw = Mathf.Clamp(width - 6, 2, 4);
        int nh = Mathf.Clamp((height - 3) / 2, 2, 4);
        int x0 = (width - vw) / 2;
        int zMin = nh;
        int zMax = height - nh - 1;

        // Guarantee straight East-West avenues across the bridge span connecting West Wing (x0 - 1) to East Wing (x0 + vw).
        for (int z = zMin; z <= zMax; z++)
        {
            double chance = (z == zMin || z == zMax) ? 0.90 : 0.65;
            for (int x = Mathf.Max(0, x0 - 1); x < Mathf.Min(width - 1, x0 + vw); x++)
            {
                TryOpenAvenueEdge(new Vector2Int(x, z), 1, rng, chance);
            }
        }

        // Cross-link the middle of the bridge vertically so the bridge can be switched mid-crossing.
        int midX = x0 + vw / 2;
        for (int z = zMin; z < zMax; z++)
        {
            TryOpenAvenueEdge(new Vector2Int(midX, z), 0, rng, 0.75);
        }
    }

    private void CarveCrossAtriumAndSpokes(System.Random rng)
    {
        int cx = width / 2;
        int cz = height / 2;

        // Open radial spokes 2 cells out from (cx, cz) in all 4 cardinal directions.
        for (int dir = 0; dir < 4; dir++)
        {
            Vector2Int cell = new Vector2Int(cx, cz);
            for (int step = 0; step < 2; step++)
            {
                Vector2Int next = Neighbour(cell, dir);
                if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height || IsVoid(next.x, next.y)) break;
                OpenShapeWall(cell, next, dir);
                cell = next;
            }
        }

        // Open ~65% of internal walls inside the 3x3 central atrium so it feels like a genuine rotunda.
        for (int x = cx - 1; x <= cx + 1; x++)
        {
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                if (x < 0 || x >= width || z < 0 || z >= height || IsVoid(x, z)) continue;
                if (x + 1 <= cx + 1 && x + 1 < width && !IsVoid(x + 1, z))
                    TryOpenAvenueEdge(new Vector2Int(x, z), 1, rng, 0.65);
                if (z + 1 <= cz + 1 && z + 1 < height && !IsVoid(x, z + 1))
                    TryOpenAvenueEdge(new Vector2Int(x, z), 0, rng, 0.65);
            }
        }
    }

    private void CarveLChasmRimAvenue(System.Random rng)
    {
        int cw = Mathf.Clamp(width / 2 - 1, 3, 6);
        int ch = Mathf.Clamp(height / 2 - 1, 3, 5);
        int rimX = Mathf.Clamp(width - cw - 1, 0, width - 1);
        int rimZ = Mathf.Clamp(height - ch - 1, 0, height - 1);

        // North-South cliffside avenue along x = rimX (bordering the NE chasm's west wall).
        for (int z = Mathf.Max(0, rimZ - 1); z < height - 1; z++)
        {
            TryOpenAvenueEdge(new Vector2Int(rimX, z), 0, rng, 0.72);
        }

        // West-East cliffside avenue along z = rimZ (bordering the NE chasm's south wall).
        for (int x = Mathf.Max(0, rimX - 1); x < width - 1; x++)
        {
            TryOpenAvenueEdge(new Vector2Int(x, rimZ), 1, rng, 0.72);
        }
    }

    private void TryOpenAvenueEdge(Vector2Int cell, int dir, System.Random rng, double chance)
    {
        if (cell.x < 0 || cell.x >= width || cell.y < 0 || cell.y >= height || IsVoid(cell.x, cell.y)) return;
        Vector2Int next = Neighbour(cell, dir);
        if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height || IsVoid(next.x, next.y)) return;
        if (!IsWallClosed(cell.x, cell.y, dir)) return;
        if (rng.NextDouble() < chance)
        {
            OpenShapeWall(cell, next, dir);
        }
    }

    // =========================================================================
    // 3. VOID WINDOWS (BARRED IRON GRILLES) & ABYSSAL ATMOSPHERE (D3)
    // =========================================================================

    private void ResetVoidWindows()
    {
        _voidWindowWalls.Clear();
    }

    /// <summary>
    /// Called immediately after _propWalls.Clear() in BuildSetPieces so that barred void-window walls stay reserved
    /// across all subsequent prop, decal, lamp, button, fuse, key, and note placement passes.
    /// </summary>
    private void RestoreVoidWindowPropWalls()
    {
        if (_voidWindowWalls.Count == 0) return;

        foreach ((Vector2Int cell, Vector3 dir) entry in _voidWindowWalls)
        {
            if (!_propWalls.TryGetValue(entry.cell, out List<Vector3> taken))
            {
                taken = new List<Vector3>();
                _propWalls[entry.cell] = taken;
            }
            if (!taken.Contains(entry.dir))
            {
                taken.Add(entry.dir);
            }
        }
    }

    private bool IsVoidWindowWall(Vector2Int cell, Vector3 dir)
    {
        return _voidWindowWalls.Contains((cell, dir));
    }

    /// <summary>
    /// True when the closed wall at (x, z, dir) separates a playable cell from an IN-BOUNDS void cell
    /// and should be built as a barred iron-grille window rather than an opaque wall slab.
    /// </summary>
    private bool ShouldBuildVoidWindow(int x, int z, int dir)
    {
        if (!HasCustomShape || IsVoid(x, z)) return false;

        // Every second cell along the void perimeter gets a barred window so masonry piers alternate with iron grilles.
        if (((x + z) & 1) != 0) return false;
        if (!IsWallClosed(x, z, dir)) return false;
        if (IsLoopOpening(x, z, dir) || IsGateOpening(x, z, dir) || IsLockerBackWall(x, z, dir)) return false;

        Vector2Int next = Neighbour(new Vector2Int(x, z), dir);
        // Must be an IN-BOUNDS void cell (never the outer boundary of the grid).
        if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height) return false;
        if (!IsVoid(next.x, next.y)) return false;

        // Keep at least one solid closed wall in the cell for lamps/buttons/props.
        if (WallCount(x, z) < 2) return false;
        Vector2Int cell = new Vector2Int(x, z);
        if (_voidWindowWalls.Contains((cell, Vector3.forward)) ||
            _voidWindowWalls.Contains((cell, Vector3.right)) ||
            _voidWindowWalls.Contains((cell, Vector3.back)) ||
            _voidWindowWalls.Contains((cell, Vector3.left)))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Spawns a waist-high dado wall + top lintel + side jambs + vertical iron bars instead of a solid wall slab.
    /// The 1.0 m dado collider carves the NavMesh and stops CharacterController movement, while the barred opening
    /// (y = 1.05 m .. wallHeight - 0.55 m) has no solid slab collider so torch beams and Hunter/Stalker LOS raycasts
    /// pass straight across the void chasm.
    /// </summary>
    private void SpawnVoidGrilleWall(string wallName, int x, int z, int dir, Vector3 floorCenter, Quaternion rotation)
    {
        Vector2Int cell = new Vector2Int(x, z);
        Vector3 dirVec = DirVector(dir);
        _voidWindowWalls.Add((cell, dirVec));

        if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
        {
            taken = new List<Vector3>();
            _propWalls[cell] = taken;
        }
        if (!taken.Contains(dirVec)) taken.Add(dirVec);

        GameObject root = new GameObject(wallName);
        root.layer = 0;
        root.transform.SetParent(_wallsGroup, false);
        root.transform.position = floorCenter;
        root.transform.rotation = rotation;

        float span = cellSize + (_theme == null ? wallThickness : 0f);
        float dadoH = Mathf.Min(1.05f, wallHeight * 0.36f);
        float lintelH = Mathf.Min(0.55f, wallHeight * 0.20f);
        float windowH = Mathf.Max(0.8f, wallHeight - dadoH - lintelH);
        float jambW = Mathf.Min(0.36f, span * 0.10f);
        float openingW = Mathf.Max(0.8f, span - 2f * jambW);

        Material wallMat = ResolveVoidMasonryMaterial();
        Material ironMat = ResolveVoidIronMaterial();

        // 1. Lower dado wall (carves NavMesh, blocks movement)
        CreateGrilleBox(root.transform, "Dado",
            new Vector3(0f, dadoH * 0.5f, 0f),
            new Vector3(span, dadoH, wallThickness),
            wallMat, keepCollider: true);

        // 2. Upper lintel beam
        CreateGrilleBox(root.transform, "Lintel",
            new Vector3(0f, wallHeight - lintelH * 0.5f, 0f),
            new Vector3(span, lintelH, wallThickness),
            wallMat, keepCollider: true);

        // 3. Left & right masonry jambs
        float jambX = (span - jambW) * 0.5f;
        float windowMidY = dadoH + windowH * 0.5f;
        CreateGrilleBox(root.transform, "Jamb_L",
            new Vector3(-jambX, windowMidY, 0f),
            new Vector3(jambW, windowH, wallThickness),
            wallMat, keepCollider: true);
        CreateGrilleBox(root.transform, "Jamb_R",
            new Vector3(jambX, windowMidY, 0f),
            new Vector3(jambW, windowH, wallThickness),
            wallMat, keepCollider: true);

        // 4. Iron sill & horizontal transom rail (no collider so eye-level LOS rays never snag)
        CreateGrilleBox(root.transform, "IronSill",
            new Vector3(0f, dadoH + 0.025f, 0f),
            new Vector3(openingW, 0.05f, wallThickness + 0.04f),
            ironMat, keepCollider: false);
        CreateGrilleBox(root.transform, "CrossRail",
            new Vector3(0f, dadoH + windowH * 0.52f, 0f),
            new Vector3(openingW, 0.045f, 0.055f),
            ironMat, keepCollider: false);

        // 5. Vertical iron bars spaced ~0.48 m apart across the window opening (visual only, no collider)
        int barCount = Mathf.Clamp(Mathf.RoundToInt(openingW / 0.48f) - 1, 3, 7);
        float step = openingW / (barCount + 1);
        float leftEdge = -openingW * 0.5f;
        for (int i = 1; i <= barCount; i++)
        {
            float bx = leftEdge + i * step;
            CreateGrilleBox(root.transform, $"Bar_{i}",
                new Vector3(bx, windowMidY, 0f),
                new Vector3(0.042f, windowH, 0.042f),
                ironMat, keepCollider: false);
        }
    }

    private static void CreateGrilleBox(Transform parent, string name, Vector3 localPos, Vector3 localScale, Material mat, bool keepCollider)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.layer = 0;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = localPos;
        box.transform.localRotation = Quaternion.identity;
        box.transform.localScale = localScale;

        if (!keepCollider && box.TryGetComponent(out Collider col))
        {
            Destroy(col);
        }
        if (mat != null && box.TryGetComponent(out MeshRenderer mr))
        {
            mr.sharedMaterial = mat;
        }
    }

    private Material ResolveVoidMasonryMaterial()
    {
        if (_wallMat != null) return _wallMat;
        if (_theme != null && _theme.Wall != null)
        {
            MeshRenderer mr = _theme.Wall.GetComponentInChildren<MeshRenderer>();
            if (mr != null && mr.sharedMaterial != null) return mr.sharedMaterial;
        }
        return wallMaterial;
    }

    private Material ResolveVoidIronMaterial()
    {
        if (_voidGrilleMaterial != null) return _voidGrilleMaterial;

        Material baseMat = ResolveVoidMasonryMaterial();
        if (baseMat != null)
        {
            _voidGrilleMaterial = DarkCopy(baseMat, new Color(0.07f, 0.075f, 0.085f), "Maze_VoidGrilleIron");
            return _voidGrilleMaterial;
        }
        return null;
    }

    /// <summary>
    /// F125 D3: Post-bake abyssal shaft walls, deep pit floor, rising mist and dim chasm uplight for internal void regions.
    /// Makes looking through the barred void windows reveal a deep architectural shaft rather than raw skybox.
    /// </summary>
    private void BuildVoidAtmosphere()
    {
        if (!HasCustomShape) return;

        Transform voidGroup = new GameObject("VoidShafts").transform;
        voidGroup.SetParent(_mazeRoot, false);

        const float shaftDepth = 5.0f;
        Material shaftMat = ResolveVoidIronMaterial() ?? ResolveVoidMasonryMaterial();

        // 1. Build descending shaft walls along every edge where a playable cell meets an in-bounds void cell.
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (!IsVoid(x, z)) continue;

                // Pit floor tile at the bottom of the void cell
                CreateBox(
                    $"VoidPitFloor_{x}_{z}",
                    CellCenter(x, z) + Vector3.down * (shaftDepth + 0.1f),
                    new Vector3(cellSize + 0.05f, 0.2f, cellSize + 0.05f),
                    shaftMat,
                    voidGroup);

                // Skirt walls descending from FloorTop down to FloorTop - shaftDepth on edges bordering playable cells
                for (int dir = 0; dir < 4; dir++)
                {
                    Vector2Int nb = Neighbour(new Vector2Int(x, z), dir);
                    bool bordersPlayable = nb.x >= 0 && nb.x < width && nb.y >= 0 && nb.y < height && !IsVoid(nb.x, nb.y);
                    bool bordersOuterEdge = nb.x < 0 || nb.x >= width || nb.y < 0 || nb.y >= height;
                    if (!bordersPlayable && !bordersOuterEdge) continue;

                    Vector3 d = DirVector(dir);
                    Vector3 edgeCenter = CellCenter(x, z) + d * (cellSize * 0.5f);
                    float y = bordersPlayable ? (FloorTop - shaftDepth * 0.5f) : (FloorTop + (wallHeight - shaftDepth) * 0.5f);
                    float h = bordersPlayable ? shaftDepth : (wallHeight + shaftDepth);
                    Vector3 size = (dir == 0 || dir == 2)
                        ? new Vector3(cellSize + wallThickness, h, wallThickness)
                        : new Vector3(wallThickness, h, cellSize + wallThickness);

                    GameObject skirt = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    skirt.name = $"VoidShaftWall_{x}_{z}_{dir}";
                    skirt.layer = 0;
                    skirt.transform.SetParent(voidGroup, false);
                    skirt.transform.position = new Vector3(edgeCenter.x, y, edgeCenter.z);
                    skirt.transform.localScale = size;
                    if (skirt.TryGetComponent(out Collider col)) Destroy(col);
                    if (shaftMat != null && skirt.TryGetComponent(out MeshRenderer mr)) mr.sharedMaterial = shaftMat;
                }
            }
        }

        // 2. Find connected internal void regions and place one eerie uplight + rising mist bank in each.
        bool[,] seen = new bool[width, height];
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        ParticleSystem fogTemplate = interactableKit != null ? interactableKit.FogBank : null;
        int regionIndex = 0;

        for (int sx = 0; sx < width; sx++)
        {
            for (int sz = 0; sz < height; sz++)
            {
                if (!IsVoid(sx, sz) || seen[sx, sz]) continue;

                int minX = sx, maxX = sx, minZ = sz, maxZ = sz;
                int count = 0;
                seen[sx, sz] = true;
                queue.Enqueue(new Vector2Int(sx, sz));

                while (queue.Count > 0)
                {
                    Vector2Int c = queue.Dequeue();
                    count++;
                    if (c.x < minX) minX = c.x;
                    if (c.x > maxX) maxX = c.x;
                    if (c.y < minZ) minZ = c.y;
                    if (c.y > maxZ) maxZ = c.y;

                    for (int dir = 0; dir < 4; dir++)
                    {
                        Vector2Int n = Neighbour(c, dir);
                        if (n.x >= 0 && n.x < width && n.y >= 0 && n.y < height && IsVoid(n.x, n.y) && !seen[n.x, n.y])
                        {
                            seen[n.x, n.y] = true;
                            queue.Enqueue(n);
                        }
                    }
                }

                if (count == 0) continue;
                regionIndex++;

                float spanX = (maxX - minX + 1) * cellSize;
                float spanZ = (maxZ - minZ + 1) * cellSize;
                Vector3 regionCenter = new Vector3(
                    origin.x + (minX + maxX + 1) * 0.5f * cellSize,
                    FloorTop,
                    origin.z + (minZ + maxZ + 1) * 0.5f * cellSize);

                // Subtle abyssal uplight deep in the pit so iron bars cast stark silhouettes against the mist.
                GameObject lightObj = new GameObject($"VoidShaftLight_{regionIndex}");
                lightObj.transform.SetParent(voidGroup, false);
                lightObj.transform.position = regionCenter + Vector3.down * 1.4f;
                Light pitLight = lightObj.AddComponent<Light>();
                pitLight.type = LightType.Point;
                pitLight.color = _profile.Shape == FloorProfile.FloorShape.TwinWings
                    ? new Color(0.85f, 0.28f, 0.16f)
                    : new Color(0.28f, 0.48f, 0.62f);
                pitLight.range = Mathf.Max(spanX, spanZ) * 0.85f + 4f;
                pitLight.intensity = 0.55f;
                pitLight.shadows = LightShadows.None;

                if (fogTemplate != null && fogDensity > 0f && fogMaxParticles > 0)
                {
                    ParticleSystem mist = Instantiate(fogTemplate, regionCenter + Vector3.down * 0.6f, Quaternion.identity, voidGroup);
                    mist.name = $"VoidMist_{regionIndex}";
                    mist.gameObject.SetActive(true);
                    mist.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ParticleSystem.MainModule main = mist.main;
                    int alive = Mathf.Clamp(Mathf.RoundToInt(spanX * spanZ * 0.25f), 4, 24);
                    main.maxParticles = alive + 2;
                    ParticleSystem.EmissionModule emission = mist.emission;
                    float life = (main.startLifetime.constantMin + main.startLifetime.constantMax) * 0.5f;
                    emission.rateOverTime = alive / Mathf.Max(1f, life);
                    ParticleSystem.ShapeModule shape = mist.shape;
                    shape.scale = new Vector3(spanX, 1.2f, spanZ);
                    mist.Simulate(life, true, true);
                    mist.gameObject.AddComponent<FogScale>();
                    mist.Play();
                }
            }
        }
    }

    // =========================================================================
    // 4. TOPOLOGY-DRIVEN PLACEMENT OF STARS & DOORS (D4)
    // =========================================================================

    /// <summary>
    /// F125 D4: Reorders the shuffled star candidate list so that round-robin zone interleaving places one star
    /// in each wing/arm/quadrant before doubling up. Preserves relative shuffled order within each zone.
    /// No-op on Rectangle.
    /// </summary>
    private void PrioritiseShapeStarCandidates(List<Vector2Int> candidates)
    {
        if (!HasCustomShape || candidates.Count <= 2) return;

        int zoneCount = _profile.Shape == FloorProfile.FloorShape.Cross ? 4
            : _profile.Shape == FloorProfile.FloorShape.Ring ? 4
            : 3;

        List<Vector2Int>[] buckets = new List<Vector2Int>[zoneCount];
        for (int i = 0; i < zoneCount; i++) buckets[i] = new List<Vector2Int>();

        foreach (Vector2Int cell in candidates)
        {
            int zone = Mathf.Clamp(ResolveShapeZone(cell), 0, zoneCount - 1);
            buckets[zone].Add(cell);
        }

        candidates.Clear();
        int index = 0;
        bool addedAny = true;
        while (addedAny)
        {
            addedAny = false;
            for (int z = 0; z < zoneCount; z++)
            {
                if (index < buckets[z].Count)
                {
                    candidates.Add(buckets[z][index]);
                    addedAny = true;
                }
            }
            index++;
        }
    }

    private int ResolveShapeZone(Vector2Int cell)
    {
        int cx = width / 2;
        int cz = height / 2;

        switch (_profile.Shape)
        {
            case FloorProfile.FloorShape.Ring:
                // 4 compass quadrants around the central void shaft
                return (cell.x >= cx ? 1 : 0) + (cell.y >= cz ? 2 : 0);

            case FloorProfile.FloorShape.TwinWings:
            {
                // 0 = East Wing (far from start), 1 = West Wing, 2 = Central Bridge
                int vw = Mathf.Clamp(width - 6, 2, 4);
                int x0 = (width - vw) / 2;
                if (cell.x >= x0 + vw) return 0;
                if (cell.x < x0) return 1;
                return 2;
            }

            case FloorProfile.FloorShape.Cross:
            {
                // 4 cardinal arms (North, East, South, West) by dominant offset from (cx, cz)
                int dx = cell.x - cx;
                int dz = cell.y - cz;
                if (Mathf.Abs(dz) >= Mathf.Abs(dx))
                    return dz >= 0 ? 0 : 2;
                return dx >= 0 ? 1 : 3;
            }

            case FloorProfile.FloorShape.LChasm:
            {
                // 0 = North Wing tip (high Z, low X), 1 = East Wing tip (high X, low Z), 2 = Inner corner bend
                if (cell.y >= cz && cell.x < cx) return 0;
                if (cell.x >= cx && cell.y < cz) return 1;
                return 2;
            }

            default:
                return 0;
        }
    }

    /// <summary>
    /// F125 D4: On TwinWings, moves security-door candidates entering or inside the central bridge to the front of
    /// the shuffled index order so a security door naturally gates the bridge bottleneck when topological rules allow.
    /// </summary>
    private void PrioritiseTwinWingsBridgeOrder(List<int> order, List<(Passage passage, HashSet<Vector2Int> startSide, float cutFraction)> pool)
    {
        if (_profile == null || _profile.Shape != FloorProfile.FloorShape.TwinWings || order.Count <= 1) return;

        List<int> bridge = new List<int>();
        List<int> rest = new List<int>(order.Count);
        foreach (int idx in order)
        {
            Passage p = pool[idx].passage;
            if (IsInTwinWingsBridge(p.CellA.x, p.CellA.y) || IsInTwinWingsBridge(p.CellB.x, p.CellB.y))
                bridge.Add(idx);
            else
                rest.Add(idx);
        }

        if (bridge.Count == 0 || rest.Count == 0) return;
        order.Clear();
        order.AddRange(bridge);
        order.AddRange(rest);
    }

    private void PrioritiseTwinWingsBridgeManualOrder(List<int> order, List<Passage> candidates)
    {
        if (_profile == null || _profile.Shape != FloorProfile.FloorShape.TwinWings || order.Count <= 1) return;

        List<int> bridge = new List<int>();
        List<int> rest = new List<int>(order.Count);
        foreach (int idx in order)
        {
            Passage p = candidates[idx];
            if (IsInTwinWingsBridge(p.CellA.x, p.CellA.y) || IsInTwinWingsBridge(p.CellB.x, p.CellB.y))
                bridge.Add(idx);
            else
                rest.Add(idx);
        }

        if (bridge.Count == 0 || rest.Count == 0) return;
        order.Clear();
        order.AddRange(bridge);
        order.AddRange(rest);
    }

    // =========================================================================
    // 5. SHAPE-AWARE HUNTER PATROL & CHASM AMBUSH (D5)
    // =========================================================================

    /// <summary>
    /// F125 D5: Shape-specific patrol index selection for AIFollower.PickNewWanderTarget.
    /// Returns -1 on FloorShape.Rectangle (or when the roll defers to standard random patrol).
    /// </summary>
    public int PickShapeWanderCellIndex(Vector3 hunterPos, Vector3 playerPos, int currentIndex, IReadOnlyList<Vector3> wanderPoints)
    {
        if (!HasCustomShape || wanderPoints == null || wanderPoints.Count <= 1) return -1;

        System.Random rng = new System.Random(System.Environment.TickCount ^ currentIndex ^ (int)(Time.time * 100f));
        if (!TryPickShapeWanderPoint(hunterPos, playerPos, cellSize * 2.2f, rng, out Vector3 worldTarget)) return -1;

        int bestIdx = -1;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < wanderPoints.Count; i++)
        {
            if (i == currentIndex) continue;
            float d2 = (wanderPoints[i] - worldTarget).sqrMagnitude;
            if (d2 < bestDistSq)
            {
                bestDistSq = d2;
                bestIdx = i;
            }
        }
        return bestIdx;
    }

    public bool TryPickShapeWanderPoint(Vector3 hunterPos, Vector3 playerPos, float minDistanceFromHunter, System.Random rng, out Vector3 target)
    {
        target = Vector3.zero;
        if (!HasCustomShape || _cellCenters == null || _cellCenters.Count == 0) return false;

        Vector2Int hunterCell = ShapeWorldToCell(hunterPos);
        Vector2Int playerCell = ShapeWorldToCell(playerPos);

        switch (_profile.Shape)
        {
            case FloorProfile.FloorShape.Ring:
            {
                // 55% chance to orbit along the inner ring perimeter so the Hunter stalks past the barred void windows.
                if (rng.NextDouble() >= 0.55) return false;

                float cx = (width - 1) * 0.5f;
                float cz = (height - 1) * 0.5f;
                float hunterAngle = Mathf.Atan2(hunterCell.y - cz, hunterCell.x - cx);
                // Advance ~90..150 degrees around the ring
                float targetAngle = hunterAngle + (float)(1.4 + rng.NextDouble() * 1.2);

                Vector2Int best = hunterCell;
                float bestScore = float.MaxValue;
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < height; z++)
                    {
                        if (!TouchesInternalVoid(x, z)) continue;
                        Vector3 world = CellCenter(x, z);
                        if (Vector3.Distance(hunterPos, world) < minDistanceFromHunter) continue;
                        float ang = Mathf.Atan2(z - cz, x - cx);
                        float diff = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, targetAngle * Mathf.Rad2Deg));
                        if (diff < bestScore)
                        {
                            bestScore = diff;
                            best = new Vector2Int(x, z);
                        }
                    }
                }
                if (best != hunterCell)
                {
                    target = CellCenter(best.x, best.y);
                    return true;
                }
                return false;
            }

            case FloorProfile.FloorShape.TwinWings:
            {
                // 50% chance to sweep across the bridge into the opposite wing (especially if the player is on the other side).
                if (rng.NextDouble() >= 0.50) return false;

                int midX = width / 2;
                bool hunterWest = hunterCell.x < midX;
                bool playerOpposite = (playerCell.x < midX) != hunterWest;
                bool pickWest = playerOpposite ? (playerCell.x < midX) : !hunterWest;

                List<Vector2Int> pool = new List<Vector2Int>();
                for (int x = 0; x < width; x++)
                {
                    if ((x < midX) != pickWest && !IsInTwinWingsBridge(x, height / 2)) continue;
                    for (int z = 0; z < height; z++)
                    {
                        if (IsVoid(x, z)) continue;
                        if (Vector3.Distance(hunterPos, CellCenter(x, z)) >= minDistanceFromHunter)
                            pool.Add(new Vector2Int(x, z));
                    }
                }
                if (pool.Count > 0)
                {
                    Vector2Int chosen = pool[rng.Next(pool.Count)];
                    target = CellCenter(chosen.x, chosen.y);
                    return true;
                }
                return false;
            }

            case FloorProfile.FloorShape.Cross:
            {
                // 45% chance to sweep through the central 3x3 atrium or down an unvisited arm tip.
                if (rng.NextDouble() >= 0.45) return false;

                List<Vector2Int> pool = new List<Vector2Int>();
                bool hunterInAtrium = IsInCrossAtrium(hunterCell.x, hunterCell.y);
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < height; z++)
                    {
                        if (IsVoid(x, z)) continue;
                        bool inAtrium = IsInCrossAtrium(x, z);
                        if (hunterInAtrium == inAtrium) continue;
                        if (Vector3.Distance(hunterPos, CellCenter(x, z)) >= minDistanceFromHunter)
                            pool.Add(new Vector2Int(x, z));
                    }
                }
                if (pool.Count > 0)
                {
                    Vector2Int chosen = pool[rng.Next(pool.Count)];
                    target = CellCenter(chosen.x, chosen.y);
                    return true;
                }
                return false;
            }

            case FloorProfile.FloorShape.LChasm:
            {
                // 50% chance to patrol through the inner concave bend or along the cliffside void rim.
                if (rng.NextDouble() >= 0.50) return false;

                List<Vector2Int> pool = new List<Vector2Int>();
                for (int x = 0; x < width; x++)
                {
                    for (int z = 0; z < height; z++)
                    {
                        if (!TouchesInternalVoid(x, z) && !IsInLChasmCorner(x, z)) continue;
                        if (Vector3.Distance(hunterPos, CellCenter(x, z)) >= minDistanceFromHunter)
                            pool.Add(new Vector2Int(x, z));
                    }
                }
                if (pool.Count > 0)
                {
                    Vector2Int chosen = pool[rng.Next(pool.Count)];
                    target = CellCenter(chosen.x, chosen.y);
                    return true;
                }
                return false;
            }

            default:
                return false;
        }
    }

    // =========================================================================
    // 6. ARCHITECTURAL & LIGHTING LANDMARKS PER SHAPE (D6)
    // =========================================================================

    /// <summary>
    /// F125 D6: Shape-specific ceiling height override:
    /// - Cross central 3x3 atrium raises the ceiling by +1.4 m into a tall clerestory rotunda.
    /// - TwinWings central bridge lowers the ceiling to a claustrophobic bulkhead (max(2.35 m, wallHeight - 0.65 m)).
    /// - Everything else uses wallHeight.
    /// </summary>
    private float ShapeCeilingHeightAt(int x, int z)
    {
        if (!HasCustomShape) return wallHeight;

        if (IsInCrossAtrium(x, z))
        {
            return wallHeight + 1.4f;
        }
        if (IsInTwinWingsBridge(x, z))
        {
            return Mathf.Max(2.35f, wallHeight - 0.65f);
        }
        return wallHeight;
    }

    /// <summary>
    /// F125 D6: Seals vertical ceiling height transitions (Cross raised atrium clerestory walls & arch lintels,
    /// TwinWings lowered bridge bulkheads) so no skybox gaps are visible where ceiling heights step up or down.
    /// </summary>
    private void BuildShapeCeilingFascia()
    {
        if (!HasCustomShape || !buildCeiling) return;

        Material fasciaMat = ResolveVoidMasonryMaterial();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                float hHere = CeilingHeightAt(x, z);

                for (int dir = 0; dir < 4; dir++)
                {
                    bool closed = IsWallClosed(x, z, dir);
                    if (closed)
                    {
                        // Closed wall is built up to wallHeight; if this cell's ceiling is higher (Cross atrium),
                        // extend the upper clerestory wall from wallHeight to hHere.
                        if (hHere > wallHeight + 0.01f)
                        {
                            SpawnCeilingFasciaSegment(x, z, dir, wallHeight, hHere, fasciaMat);
                        }
                    }
                    else
                    {
                        // Open passage: if this cell's ceiling is higher than the neighbour's ceiling,
                        // hang a lintel fascia from hThere up to hHere across the cell boundary.
                        Vector2Int nb = Neighbour(new Vector2Int(x, z), dir);
                        if (nb.x < 0 || nb.x >= width || nb.y < 0 || nb.y >= height || IsVoid(nb.x, nb.y)) continue;
                        float hThere = CeilingHeightAt(nb.x, nb.y);
                        if (hHere > hThere + 0.01f)
                        {
                            SpawnCeilingFasciaSegment(x, z, dir, hThere, hHere, fasciaMat);
                        }
                    }
                }
            }
        }
    }

    private void SpawnCeilingFasciaSegment(int x, int z, int dir, float yBottom, float yTop, Material mat)
    {
        float h = yTop - yBottom;
        if (h <= 0.01f) return;

        Vector3 d = DirVector(dir);
        Vector3 edgeCenter = CellCenter(x, z) + d * (cellSize * 0.5f) + Vector3.up * (yBottom + h * 0.5f);
        Vector3 size = (dir == 0 || dir == 2)
            ? new Vector3(cellSize + wallThickness, h, wallThickness)
            : new Vector3(wallThickness, h, cellSize + wallThickness);

        GameObject fascia = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fascia.name = $"CeilingFascia_{x}_{z}_{dir}";
        fascia.layer = 0;
        fascia.transform.SetParent(_ceilingGroup, false);
        fascia.transform.position = edgeCenter;
        fascia.transform.localScale = size;
        if (fascia.TryGetComponent(out Collider col)) Destroy(col);
        if (mat != null && fascia.TryGetComponent(out MeshRenderer mr)) mr.sharedMaterial = mat;
    }

    /// <summary>
    /// F125 D6: Moves shape landmark cells (TwinWings bridge, Cross atrium, Ring/LChasm void perimeter) toward the front
    /// of the shuffled lamp candidate list so the floor's lamp quota reliably illuminates its architectural landmarks.
    /// </summary>
    private void PrioritiseShapeLampCells(List<Vector2Int> others)
    {
        if (!HasCustomShape || others.Count <= 1) return;

        List<Vector2Int> priority = new List<Vector2Int>();
        List<Vector2Int> rest = new List<Vector2Int>(others.Count);

        foreach (Vector2Int cell in others)
        {
            bool landmark = IsInTwinWingsBridge(cell.x, cell.y)
                || IsInCrossAtrium(cell.x, cell.y)
                || TouchesInternalVoid(cell.x, cell.y);

            if (landmark && (priority.Count == 0 || IsFarEnough(cell, priority, 2)))
                priority.Add(cell);
            else
                rest.Add(cell);
        }

        if (priority.Count == 0) return;
        others.Clear();
        others.AddRange(priority);
        others.AddRange(rest);
    }

    /// <summary>
    /// F125 D6: Tints landmark lamps by zone (industrial red-amber on the TwinWings bridge, cold surgical cyan in the
    /// Cross 3x3 atrium, warm sodium-gold along the Ring/LChasm void rim) and ensures the Cross central atrium always
    /// has a central overhead surgical lamp even when its 4 walls are open passages.
    /// </summary>
    private void ApplyShapeLampLandmarks(Material sharedMaterial, System.Random rng)
    {
        if (!HasCustomShape) return;

        Color bridgeRed = new Color(1.0f, 0.30f, 0.18f);
        Color atriumCyan = new Color(0.55f, 0.85f, 1.0f);
        Color rimAmber = new Color(1.0f, 0.68f, 0.32f);

        bool crossHasAtriumLamp = false;
        int count = Mathf.Min(_lamps.Count, _lampCells.Count);
        for (int i = 0; i < count; i++)
        {
            WallLamp lamp = _lamps[i];
            Vector2Int cell = _lampCells[i];
            if (lamp == null) continue;

            if (IsInTwinWingsBridge(cell.x, cell.y))
            {
                lamp.SetTint(bridgeRed);
            }
            else if (IsInCrossAtrium(cell.x, cell.y))
            {
                lamp.SetTint(atriumCyan);
                crossHasAtriumLamp = true;
            }
            else if (TouchesInternalVoid(cell.x, cell.y))
            {
                lamp.SetTint(rimAmber);
            }
        }

        // Cross central cell (cx, cz) often has 0 closed walls because all 4 spokes meet there;
        // guarantee a high overhead surgical lamp in the raised atrium so the crossroads is always a landmark.
        if (_profile.Shape == FloorProfile.FloorShape.Cross && !crossHasAtriumLamp)
        {
            Vector2Int center = new Vector2Int(width / 2, height / 2);
            if (!IsVoid(center.x, center.y))
            {
                Vector3 pos = CellCenter(center.x, center.y) + Vector3.up * (CeilingHeightAt(center.x, center.y) - 0.30f);
                Quaternion rot = Quaternion.Euler(90f, 0f, 0f);
                WallLamp atriumLamp;
                if (_theme != null && _theme.Lamp != null)
                {
                    atriumLamp = CreateThemedLamp($"AtriumLamp_{center.x}_{center.y}", pos, rot, _lampsGroup, false, rng.Next(1000), false);
                }
                else
                {
                    GameObject fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    fixture.name = $"AtriumLamp_{center.x}_{center.y}";
                    fixture.layer = 0;
                    fixture.transform.SetParent(_lampsGroup, false);
                    fixture.transform.position = pos;
                    fixture.transform.localScale = new Vector3(0.32f, 0.10f, 0.32f);
                    if (fixture.TryGetComponent(out Collider col)) Destroy(col);
                    MeshRenderer mr = fixture.GetComponent<MeshRenderer>();
                    if (sharedMaterial != null) mr.sharedMaterial = sharedMaterial;

                    GameObject lightHolder = new GameObject("Light");
                    lightHolder.transform.SetParent(fixture.transform, false);
                    lightHolder.transform.localPosition = Vector3.zero;
                    Light light = lightHolder.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = lampColor;
                    light.range = lampRange * 1.25f;
                    light.intensity = lampIntensity;
                    light.shadows = LightShadows.None;

                    atriumLamp = fixture.AddComponent<WallLamp>();
                    atriumLamp.Configure(light, mr, lampIntensity, false, rng.Next(1000), false);
                }
                atriumLamp.SetTint(atriumCyan);
                _lamps.Add(atriumLamp);
                _lampCells.Add(center);
            }
        }
    }
}
