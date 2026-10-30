using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

public partial class MazeGenerator
{
    /// <summary>
    /// Themed only: one pillar at every grid vertex where the walls meeting it are not just a straight
    /// wall passing through - i.e. corners, junctions, dead-end wall stubs, and the outer boundary's own
    /// corners. A vertex with exactly two closed segments that are collinear (both the east-west pair or
    /// both the north-south pair) is a straight run and is skipped to reduce clutter.
    /// </summary>
    private void BuildPillars()
    {
        if (_theme == null || _theme.Pillar == null) return;

        for (int vx = 0; vx <= width; vx++)
        {
            for (int vz = 0; vz <= height; vz++)
            {
                if (VertexNeedsPillar(vx, vz)) SpawnPillar(vx, vz);
            }
        }

        BuildRoomPillars(); // F105 C2
    }

    private void SpawnPillar(int vx, int vz, Transform parent = null)
    {
        Vector3 position = new Vector3(origin.x + vx * cellSize, FloorTop, origin.z + vz * cellSize);
        GameObject pillar = Instantiate(_theme.Pillar, position, Quaternion.identity, parent != null ? parent : _pillarsGroup);
        pillar.SetActive(true);
        pillar.name = $"Pillar_{vx}_{vz}";
        pillar.transform.localScale = new Vector3(1f, wallHeight / 4f, 1f);
    }

    private bool VertexNeedsPillar(int vx, int vz)
    {
        bool west = HorizontalWallClosed(vx - 1, vz);
        bool east = HorizontalWallClosed(vx, vz);
        bool south = VerticalWallClosed(vx, vz - 1);
        bool north = VerticalWallClosed(vx, vz);

        int closedCount = (west ? 1 : 0) + (east ? 1 : 0) + (south ? 1 : 0) + (north ? 1 : 0);
        if (closedCount == 0) return false; // nothing meets here at all
        if (closedCount != 2) return true;  // a corner, a T-junction or a dead-end stub

        bool collinear = (west && east) || (south && north);
        return !collinear; // two collinear segments is a straight wall passing through - skip it
    }

    /// <summary>Is the horizontal (east-west running) wall segment at grid row zBoundary, spanning cell column x to x+1, closed? Out-of-range x means no such segment.</summary>
    private bool HorizontalWallClosed(int x, int zBoundary)
    {
        if (x < 0 || x >= width) return false;
        bool southPlayable = zBoundary > 0 && zBoundary <= height && !IsVoid(x, zBoundary - 1);
        bool northPlayable = zBoundary >= 0 && zBoundary < height && !IsVoid(x, zBoundary);
        if (!southPlayable && !northPlayable) return false;
        if (northPlayable) return _wallS[x, zBoundary];
        return _wallN[x, zBoundary - 1];
    }

    /// <summary>Is the vertical (north-south running) wall segment at grid column xBoundary, spanning cell row z to z+1, closed? Out-of-range z means no such segment.</summary>
    private bool VerticalWallClosed(int xBoundary, int z)
    {
        if (z < 0 || z >= height) return false;
        bool westPlayable = xBoundary > 0 && xBoundary <= width && !IsVoid(xBoundary - 1, z);
        bool eastPlayable = xBoundary >= 0 && xBoundary < width && !IsVoid(xBoundary, z);
        if (!westPlayable && !eastPlayable) return false;
        if (eastPlayable) return _wallW[xBoundary, z];
        return _wallE[xBoundary - 1, z];
    }

    // ---------------------------------------------------------------- wall lamps

    /// <summary>
    /// Built after BuildCeiling, i.e. after the navmesh bake - a lamp at 2.6 m would not be carved out
    /// anyway (it sits above the 0.5 m agent), but this keeps the "after the bake" rule for anything
    /// that is not a wall.
    /// </summary>
    private void BuildWallLamps()
    {
        _lamps.Clear();
        _lampCells.Clear();
        if (!wallLamps) return;

        System.Random rng = new System.Random(UsedSeed + 2);
        HashSet<Vector2Int> lockerCellSet = new HashSet<Vector2Int>(_lockerCells);

        List<Vector2Int> cells = new List<Vector2Int>(_lockerCells);

        List<Vector2Int> others = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                Vector2Int c = new Vector2Int(x, z);
                if (lockerCellSet.Contains(c)) continue;
                others.Add(c);
            }
        }

        Shuffle(others, rng);
        PrioritiseShapeLampCells(others); // F125 D6: landmark cells first on custom shapes, no-op on Rectangle
        int extraCount = Mathf.Max(0, _playableCellCount / Mathf.Max(1, cellsPerLamp));
        for (int i = 0; i < extraCount && i < others.Count; i++)
        {
            cells.Add(others[i]);
        }

        // Unused by the themed path (each clone brings its own fixture material), so skip building it.
        Material sharedMaterial = _theme == null ? BuildLampMaterial() : null;
        foreach (Vector2Int cell in cells)
        {
            if (IsLayoutReserved(cell)) continue; // F105 C3: the inserts hide the walls, the low ceiling the rest; C4: the entrance wall is open
            BuildWallLamp(cell, lockerCellSet.Contains(cell), sharedMaterial, rng);
        }

        BuildGalleryLamp(sharedMaterial, rng); // F105 C2: after every other lamp, so the +2 stream's earlier draws are untouched
        ApplyShapeLampLandmarks(sharedMaterial, rng); // F125 D6: landmark lamp tints & Cross atrium pendant, no-op on Rectangle
        BuildMorphologyRoomLamps(sharedMaterial); // F127 D3: overhead pendant lamp in each carved room
    }

    private Material BuildLampMaterial()
    {
        Material source = FindStarMaterial();
        if (source == null) return null;

        Material material = new Material(source) { name = "Maze_LampFixture" };
        material.EnableKeyword("_EMISSION");
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", lampColor);
        if (material.HasProperty("_Color")) material.SetColor("_Color", lampColor);
        // F71: was 2.5, same as the star. Dimmer fixture glow so a lamp pool reads as a pool rather than
        // a second sun - the star keeps 2.5 so it still reads as the thing worth finding in the dark.
        material.SetColor("_EmissionColor", lampColor * 1.8f);

        _runtimeMaterials.Add(material);
        return material;
    }

    /// <summary>Direction from the cell centre toward the wall the lamp mounts on. Shared by both build paths so the RNG stream (UsedSeed + 2) stays identical whether or not a theme is in play - the set of lamp cells and their walls is unchanged by theming.</summary>
    private Vector3 ResolveLampWallDirection(Vector2Int cell, bool isLockerCell, System.Random rng)
    {
        if (isLockerCell)
        {
            // Above the locker: the same back wall the locker sits against.
            return _lockerWall[cell];
        }

        List<Vector3> options = new List<Vector3>();
        if (_wallN[cell.x, cell.y] && !IsVoidWindowWall(cell, Vector3.forward)) options.Add(Vector3.forward);
        if (_wallE[cell.x, cell.y] && !IsVoidWindowWall(cell, Vector3.right)) options.Add(Vector3.right);
        if (_wallS[cell.x, cell.y] && !IsVoidWindowWall(cell, Vector3.back)) options.Add(Vector3.back);
        if (_wallW[cell.x, cell.y] && !IsVoidWindowWall(cell, Vector3.left)) options.Add(Vector3.left);
        if (options.Count == 0) return Vector3.zero; // every real cell has at least one wall
        return options[rng.Next(options.Count)];
    }

    /// <param name="forcedWall">F105 C2: the wall to mount on (a unit direction from the cell centre); zero lets the +2 stream pick one.</param>
    /// <summary>
    /// A clone of the theme's lamp, wired and configured. The artist owns the fixture's look; the profile still owns brightness/range
    /// (stealth exposure depends on them) and colour comes from the theme, not the Inspector default. The caller registers it in whatever
    /// list it belongs to (F105 C4's corridor lamps are in none of the maze's).
    /// </summary>
    private WallLamp CreateThemedLamp(string lampName, Vector3 position, Quaternion rotation, Transform parent, bool faulty, int phase, bool isLockerCell)
    {
        WallLamp lamp = Instantiate(_theme.Lamp, position, rotation, parent);
        lamp.gameObject.SetActive(true);
        lamp.name = lampName;

        Light light = lamp.LightOrChild;
        if (light != null)
        {
            light.color = _theme.LampColor;
            light.range = lampRange;
            light.intensity = lampIntensity;
            light.shadows = LightShadows.None;
        }

        lamp.Configure(lampIntensity, faulty, phase, isLockerCell);
        return lamp;
    }

    private void BuildWallLamp(Vector2Int cell, bool isLockerCell, Material material, System.Random rng, Vector3 forcedWall = default)
    {
        int x = cell.x;
        int z = cell.y;
        Vector3 cellCenter = CellCenter(x, z);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;

        Vector3 wallDirection = forcedWall != Vector3.zero ? forcedWall : ResolveLampWallDirection(cell, isLockerCell, rng);
        if (wallDirection == Vector3.zero) return;

        float cellCeil = CeilingHeightAt(x, z);
        float mountY = cellCeil < wallHeight ? Mathf.Min(lampHeight, cellCeil - 0.30f) : lampHeight;
        Vector3 position = cellCenter + wallDirection * (backFaceDistance - 0.08f) + Vector3.up * mountY;
        Quaternion rotation = Quaternion.LookRotation(-wallDirection, Vector3.up);

        if (_theme != null && _theme.Lamp != null)
        {
            bool themedFaulty = rng.NextDouble() < _theme.FaultyLampChance;
            WallLamp themedLamp = CreateThemedLamp($"WallLamp_{x}_{z}", position, rotation, _lampsGroup, themedFaulty, rng.Next(1000), isLockerCell);
            _lamps.Add(themedLamp);
            _lampCells.Add(cell);
            return;
        }

        GameObject fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fixture.name = "WallLamp";
        fixture.layer = 0;
        fixture.transform.SetParent(_lampsGroup, false);
        fixture.transform.position = position;
        fixture.transform.rotation = rotation;
        fixture.transform.localScale = new Vector3(0.28f, 0.10f, 0.14f);

        Collider fixtureCollider = fixture.GetComponent<Collider>();
        if (fixtureCollider != null) Destroy(fixtureCollider);

        MeshRenderer fixtureRenderer = fixture.GetComponent<MeshRenderer>();
        if (material != null) fixtureRenderer.sharedMaterial = material;

        GameObject lightHolder = new GameObject("Light");
        lightHolder.transform.SetParent(fixture.transform, false);
        lightHolder.transform.localPosition = Vector3.zero;

        Light light = lightHolder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = lampColor;
        light.range = lampRange;
        light.intensity = lampIntensity;
        light.shadows = LightShadows.None;

        WallLamp lamp = fixture.AddComponent<WallLamp>();
        bool faulty = rng.NextDouble() < faultyLampChance;
        lamp.Configure(light, fixtureRenderer, lampIntensity, faulty, rng.Next(1000), isLockerCell);
        _lamps.Add(lamp);
        _lampCells.Add(cell);
    }

    // ---------------------------------------------------------------- geometry

    /// <summary>
    /// The source materials are shared with the rest of the tutorial scene, and Material_WhiteGrid is
    /// emissive white - the walls would self-illuminate no matter how dark the scene lighting got.
    /// Runtime copies keep the assets on disk untouched.
    /// </summary>
    private Material DarkCopy(Material source, Color tint, string copyName)
    {
        if (source == null) return null;

        Material copy = new Material(source) { name = copyName };
        copy.DisableKeyword("_EMISSION");
        copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", tint);
        if (copy.HasProperty("_Color")) copy.SetColor("_Color", tint);
        if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", surfaceSmoothness);
        if (copy.HasProperty("_Glossiness")) copy.SetFloat("_Glossiness", surfaceSmoothness);

        _runtimeMaterials.Add(copy);
        return copy;
    }

    private void BuildGeometry()
    {
        ResetVoidWindows();
        _mazeRoot = new GameObject("Maze").transform;
        _mazeRoot.position = origin;

        _wallsGroup = Group("Walls");
        _pillarsGroup = Group("Pillars");
        _floorGroup = Group("Floor");
        _ceilingGroup = Group("Ceiling");
        _lampsGroup = Group("Lamps");
        _lockersGroup = Group("Lockers");
        _propsGroup = Group("Props");
        _setPiecesGroup = Group("SetPieces");
        _decalsGroup = Group("Decals");

        // Always built, even when themed: cheap, and it is what PhantomDirector's silhouette falls back
        // to if the theme itself has no wall material assigned.
        _wallMat = DarkCopy(wallMaterial, wallTint, "Maze_Wall");
        _floorMat = DarkCopy(floorMaterial, floorTint, "Maze_Floor");
        _ceilingMat = DarkCopy(wallMaterial, ceilingTint, "Maze_Ceiling");

        if (_theme != null)
        {
            BuildThemedGeometry();
        }
        else
        {
            BuildPrimitiveGeometry();
        }
    }

    private Transform Group(string groupName)
    {
        Transform group = new GameObject(groupName).transform;
        group.SetParent(_mazeRoot, false);
        return group;
    }

    /// <summary>One floor tile and one wall piece clone per cell, from the theme's prefabs.</summary>
    private void BuildThemedGeometry()
    {
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                GameObject tile = Instantiate(_theme.FloorTile, CellCenter(x, z), Quaternion.identity, _floorGroup);
                tile.SetActive(true);
                tile.name = $"Floor_{x}_{z}";
                tile.transform.localScale = new Vector3(cellSize / 4.5f, 1f, cellSize / 4.5f);
            }
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;

                // Walls are shared, so only the west and south sides are built per cell; the outer
                // east and north sides (and boundaries against a void cell) are added once on the last column / row or void edge.
                if (_wallW[x, z] && !IsLoopOpening(x, z, 3) && !IsGateOpening(x, z, 3))
                {
                    Vector3 pos = new Vector3(origin.x + x * cellSize, FloorTop, origin.z + (z + 0.5f) * cellSize);
                    Quaternion rot = Quaternion.Euler(0f, 90f, 0f);
                    if (ShouldBuildVoidWindow(x, z, 3)) SpawnVoidGrilleWall($"Wall_W_{x}_{z}", x, z, 3, pos, rot);
                    else SpawnThemedWall($"Wall_W_{x}_{z}", pos, rot);
                }

                if (_wallS[x, z] && !IsLoopOpening(x, z, 2) && !IsGateOpening(x, z, 2))
                {
                    Vector3 pos = new Vector3(origin.x + (x + 0.5f) * cellSize, FloorTop, origin.z + z * cellSize);
                    if (ShouldBuildVoidWindow(x, z, 2)) SpawnVoidGrilleWall($"Wall_S_{x}_{z}", x, z, 2, pos, Quaternion.identity);
                    else SpawnThemedWall($"Wall_S_{x}_{z}", pos, Quaternion.identity);
                }

                if ((x == width - 1 || IsVoid(x + 1, z)) && _wallE[x, z] && !IsLoopOpening(x, z, 1) && !IsGateOpening(x, z, 1))
                {
                    Vector3 pos = new Vector3(origin.x + (x + 1) * cellSize, FloorTop, origin.z + (z + 0.5f) * cellSize);
                    Quaternion rot = Quaternion.Euler(0f, 90f, 0f);
                    if (ShouldBuildVoidWindow(x, z, 1)) SpawnVoidGrilleWall($"Wall_E_{x}_{z}", x, z, 1, pos, rot);
                    else SpawnThemedWall($"Wall_E_{x}_{z}", pos, rot);
                }

                if ((z == height - 1 || IsVoid(x, z + 1)) && _wallN[x, z] && !IsLoopOpening(x, z, 0) && !IsGateOpening(x, z, 0))
                {
                    Vector3 pos = new Vector3(origin.x + (x + 0.5f) * cellSize, FloorTop, origin.z + (z + 1) * cellSize);
                    if (ShouldBuildVoidWindow(x, z, 0)) SpawnVoidGrilleWall($"Wall_N_{x}_{z}", x, z, 0, pos, Quaternion.identity);
                    else SpawnThemedWall($"Wall_N_{x}_{z}", pos, Quaternion.identity);
                }
            }
        }
    }

    private void SpawnThemedWall(string wallName, Vector3 floorCenter, Quaternion rotation)
    {
        SpawnThemedWall(wallName, floorCenter, rotation, cellSize + wallThickness, wallHeight, wallThickness);
    }

    /// <summary>A themed wall piece stretched to the given world size (F105 C3 uses it for the squeeze's inserts, C4 for the corridor, under its own `parent`).</summary>
    private WallPiece SpawnThemedWall(string wallName, Vector3 floorCenter, Quaternion rotation, float length, float height, float thickness, Transform parent = null)
    {
        WallPiece wall = Instantiate(_theme.Wall, floorCenter, rotation, parent != null ? parent : _wallsGroup);
        wall.gameObject.SetActive(true);
        wall.name = wallName;
        wall.transform.localScale = wall.ScaleFor(length, height, thickness);
        return wall;
    }

    private void BuildPrimitiveGeometry()
    {
        float spanX = width * cellSize + wallThickness;
        float spanZ = height * cellSize + wallThickness;

        // Floor: 0.2 thick, top exactly at FloorTop so the walls and the player stand on it.
        CreateBox(
            "Floor",
            new Vector3(origin.x + width * cellSize * 0.5f, FloorTop - 0.1f, origin.z + height * cellSize * 0.5f),
            new Vector3(spanX, 0.2f, spanZ),
            _floorMat,
            _floorGroup);

        float wallCenterY = FloorTop + wallHeight * 0.5f;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;

                // Walls are shared, so only the west and south sides are built per cell; the outer
                // east and north sides (and boundaries against a void cell) are added once on the last column / row or void edge.
                if (_wallW[x, z] && !IsGateOpening(x, z, 3))
                {
                    if (ShouldBuildVoidWindow(x, z, 3))
                        SpawnVoidGrilleWall($"Wall_W_{x}_{z}", x, z, 3, new Vector3(origin.x + x * cellSize, FloorTop, origin.z + (z + 0.5f) * cellSize), Quaternion.Euler(0f, 90f, 0f));
                    else
                        CreateBox(
                            $"Wall_W_{x}_{z}",
                            new Vector3(origin.x + x * cellSize, wallCenterY, origin.z + (z + 0.5f) * cellSize),
                            new Vector3(wallThickness, wallHeight, cellSize + wallThickness),
                            _wallMat,
                            _wallsGroup);
                }

                if (_wallS[x, z] && !IsGateOpening(x, z, 2))
                {
                    if (ShouldBuildVoidWindow(x, z, 2))
                        SpawnVoidGrilleWall($"Wall_S_{x}_{z}", x, z, 2, new Vector3(origin.x + (x + 0.5f) * cellSize, FloorTop, origin.z + z * cellSize), Quaternion.identity);
                    else
                        CreateBox(
                            $"Wall_S_{x}_{z}",
                            new Vector3(origin.x + (x + 0.5f) * cellSize, wallCenterY, origin.z + z * cellSize),
                            new Vector3(cellSize + wallThickness, wallHeight, wallThickness),
                            _wallMat,
                            _wallsGroup);
                }

                if ((x == width - 1 || IsVoid(x + 1, z)) && _wallE[x, z] && !IsGateOpening(x, z, 1))
                {
                    if (ShouldBuildVoidWindow(x, z, 1))
                        SpawnVoidGrilleWall($"Wall_E_{x}_{z}", x, z, 1, new Vector3(origin.x + (x + 1) * cellSize, FloorTop, origin.z + (z + 0.5f) * cellSize), Quaternion.Euler(0f, 90f, 0f));
                    else
                        CreateBox(
                            $"Wall_E_{x}_{z}",
                            new Vector3(origin.x + (x + 1) * cellSize, wallCenterY, origin.z + (z + 0.5f) * cellSize),
                            new Vector3(wallThickness, wallHeight, cellSize + wallThickness),
                            _wallMat,
                            _wallsGroup);
                }

                if ((z == height - 1 || IsVoid(x, z + 1)) && _wallN[x, z] && !IsGateOpening(x, z, 0))
                {
                    if (ShouldBuildVoidWindow(x, z, 0))
                        SpawnVoidGrilleWall($"Wall_N_{x}_{z}", x, z, 0, new Vector3(origin.x + (x + 0.5f) * cellSize, FloorTop, origin.z + (z + 1) * cellSize), Quaternion.identity);
                    else
                        CreateBox(
                            $"Wall_N_{x}_{z}",
                            new Vector3(origin.x + (x + 0.5f) * cellSize, wallCenterY, origin.z + (z + 1) * cellSize),
                            new Vector3(cellSize + wallThickness, wallHeight, wallThickness),
                            _wallMat,
                            _wallsGroup);
                }
            }
        }
    }

    /// <summary>
    /// Built after the navmesh bake, so there is never a question of whether the slab reads as a
    /// walkable surface. Themed: one ceiling tile clone per cell. Primitive: a single scaled cube
    /// (rather than a quad or plane, so it has a real underside).
    /// </summary>
    private void BuildCeiling()
    {
        if (!buildCeiling) return;

        if (_theme != null)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    if (IsVoid(x, z)) continue;
                    Vector3 position = CellCenter(x, z) + Vector3.up * CeilingHeightAt(x, z); // F105 C3: lower over the squeeze
                    GameObject tile = Instantiate(_theme.CeilingTile, position, Quaternion.identity, _ceilingGroup);
                    tile.SetActive(true);
                    tile.name = $"Ceiling_{x}_{z}";
                    tile.transform.localScale = new Vector3(cellSize / 4.5f, 1f, cellSize / 4.5f);
                }
            }
            BuildShapeCeilingFascia(); // F125 D6: clerestory walls & arch lintels where ceiling height steps up/down
            return;
        }

        CreateBox(
            "Ceiling",
            new Vector3(
                origin.x + width * cellSize * 0.5f,
                FloorTop + wallHeight + 0.1f,
                origin.z + height * cellSize * 0.5f),
            new Vector3(width * cellSize + wallThickness, 0.2f, height * cellSize + wallThickness),
            _ceilingMat,
            _ceilingGroup);
    }

    private void CreateBox(string boxName, Vector3 center, Vector3 size, Material material, Transform parent)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = boxName;
        // Layer 0 is what the player's GroundLayers mask and the camera's obstacle filter look at.
        box.layer = 0;
        box.transform.SetParent(parent, false);
        box.transform.position = center;
        box.transform.localScale = size;

        if (material != null)
        {
            // sharedMaterial: one instance for the whole maze instead of one per cube
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }

    /// <summary>Combines the static themed geometry groups after the bake and after the ceiling exists. Off by default (staticBatchThemedGeometry); never run on lamps, lockers or props.</summary>
    private void CombineStaticGroups()
    {
        if (_wallsGroup != null) StaticBatchingUtility.Combine(_wallsGroup.gameObject);
        if (_pillarsGroup != null) StaticBatchingUtility.Combine(_pillarsGroup.gameObject);
        if (_floorGroup != null) StaticBatchingUtility.Combine(_floorGroup.gameObject);
        if (_ceilingGroup != null) StaticBatchingUtility.Combine(_ceilingGroup.gameObject);
    }

    // ---------------------------------------------------------------- navmesh

    /// <summary>The surface the maze bakes (F105 C3 reads its agent radius before the bake, to size the squeeze). Null when the scene has none.</summary>
    private NavMeshSurface ResolveNavMeshSurface()
    {
        if (navMeshSurface == null)
        {
            // F93: never the asylum hub's own surface (agent type "Hub"); the maze bakes with the default humanoid.
            foreach (NavMeshSurface candidate in FindObjectsByType<NavMeshSurface>())
            {
                if (candidate.GetComponentInParent<AsylumHub>(true) == null) { navMeshSurface = candidate; break; }
            }
        }

        return navMeshSurface;
    }

    private void BuildRuntimeNavMesh()
    {
        if (ResolveNavMeshSurface() == null)
        {
            Debug.LogWarning("MazeGenerator: no NavMeshSurface in the scene, the AI will not move.", this);
            return;
        }

        Vector3 center = new Vector3(
            origin.x + width * cellSize * 0.5f,
            FloorTop,
            origin.z + height * cellSize * 0.5f);

        // The surface bakes around its own transform, so move it onto the maze first.
        navMeshSurface.transform.position = center;
        navMeshSurface.transform.rotation = Quaternion.identity;
        navMeshSurface.collectObjects = CollectObjects.Volume;
        navMeshSurface.center = new Vector3(0f, 1.5f, 0f);
        navMeshSurface.size = new Vector3(
            width * cellSize + 2f * wallThickness + 2f,
            Mathf.Max(6f, wallHeight + 3f),
            height * cellSize + 2f * wallThickness + 2f);

        // Synchronous: RemoveData discards the stale baked asset, so no navmesh survives under the old scene.
        navMeshSurface.BuildNavMesh();
    }
}
