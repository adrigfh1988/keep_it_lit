using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// F127: Dynamic Maze Morphologies &amp; Unobserved Wall Shifts (+29 stream).
/// Gives Floor 2+ (Story) and Level 2+ (DESCENT) four non-repeating spatial morphologies:
///  - Standard: classic braided 1-cell corridor maze.
///  - CompactWarren: smaller grid, tighter 4.0 m pitch and 3.3 m ceiling, winding anti-straight corridors,
///    high unbraided dead-end density (72%), zero open rooms, extra bait dead-end star.
///  - CavernousHalls: expanded grid, wide 5.0 m+ pitch and tall walls, 3-5 multi-cell halls with raised
///    cathedral ceilings, internal L/island cover baffles, overhead pendant lamps, and ~42% of remaining
///    interior corridor walls knocked open into pillared plazas and double-wide boulevards.
///  - ChambersAndChokepoints: 2-4 sector chambers with raised ceilings and internal cover baffles connected
///    by low-braided single-corridor chokepoints where security and manual doors prioritise chamber entrances.
/// Also builds post-bake linked shifting shutter pairs (BuildShiftingPassages) that WorldShiftDirector swaps
/// out of sight mid-run while preserving 100% cell and objective reachability.
/// </summary>
public partial class MazeGenerator
{
    private const int MorphologySalt = 29;
    private const int MorphologyRollSalt = 0x4D4F5250; // "MORP"

    public enum DebugMazeMorphology
    {
        Auto = -1,
        Standard = 0,
        CompactWarren = 1,
        CavernousHalls = 2,
        ChambersAndChokepoints = 3
    }

    private struct ShiftingPassagePair
    {
        public MazeDoor DoorA;
        public MazeDoor DoorB;
        public Passage PassageA;
        public Passage PassageB;
        public bool Shifted;
    }

    private readonly List<RectInt> _carvedRooms = new List<RectInt>();
    private readonly List<ShiftingPassagePair> _shiftPairs = new List<ShiftingPassagePair>();
    private AudioClip _wallShiftRumbleClip;

    public int CarvedRoomCount => _carvedRooms.Count;
    public int ShiftingPairCount => _shiftPairs.Count;

    // =========================================================================
    // 1. MORPHOLOGY ROLL & PROFILE SCALING (D1 & D2)
    // =========================================================================

    /// <summary>
    /// Deterministically rolls the floor's <see cref="FloorProfile.MazeMorphology"/> for Story Mode or DESCENT,
    /// keeping Floor 1 / Depth 1 as <see cref="FloorProfile.MazeMorphology.Standard"/> and preventing two consecutive
    /// floors from sharing the same morphology.
    /// </summary>
    public static FloorProfile.MazeMorphology RollMorphologyForFloor(int floorOrDepth, int usedSeed, bool isDescent)
    {
        if (floorOrDepth <= 1)
        {
            if (isDescent)
            {
                DescentRun.PreviousMorphology = FloorProfile.MazeMorphology.Standard;
            }
            else if (floorOrDepth >= 0 && floorOrDepth < GameFlow.RolledMorphologies.Length)
            {
                GameFlow.RolledMorphologies[floorOrDepth] = (int)FloorProfile.MazeMorphology.Standard;
            }
            return FloorProfile.MazeMorphology.Standard;
        }

        FloorProfile.MazeMorphology prev = isDescent
            ? DescentRun.PreviousMorphology
            : (floorOrDepth - 1 >= 1 && floorOrDepth - 1 < GameFlow.RolledMorphologies.Length
                ? (FloorProfile.MazeMorphology)GameFlow.RolledMorphologies[floorOrDepth - 1]
                : FloorProfile.MazeMorphology.Standard);

        // Pool weights non-Standard morphologies on Floor 2+ while keeping Standard in the rotation from Floor 3+.
        FloorProfile.MazeMorphology[] pool = floorOrDepth == 2
            ? new[]
            {
                FloorProfile.MazeMorphology.CompactWarren,
                FloorProfile.MazeMorphology.CavernousHalls,
                FloorProfile.MazeMorphology.ChambersAndChokepoints
            }
            : new[]
            {
                FloorProfile.MazeMorphology.Standard,
                FloorProfile.MazeMorphology.CompactWarren,
                FloorProfile.MazeMorphology.CavernousHalls,
                FloorProfile.MazeMorphology.ChambersAndChokepoints
            };

        int hash = RunRules.HashSeedFloor(usedSeed ^ MorphologyRollSalt, floorOrDepth * 53 + (isDescent ? 17 : 0)) & 0x7FFFFFFF;
        int pick = hash % pool.Length;
        FloorProfile.MazeMorphology chosen = pool[pick];
        if (chosen == prev)
        {
            chosen = pool[(pick + 1 + (hash / pool.Length) % (pool.Length - 1)) % pool.Length];
        }

        if (isDescent)
        {
            DescentRun.PreviousMorphology = chosen;
        }
        else if (floorOrDepth >= 0 && floorOrDepth < GameFlow.RolledMorphologies.Length)
        {
            GameFlow.RolledMorphologies[floorOrDepth] = (int)chosen;
        }

        return chosen;
    }

    /// <summary>
    /// Applies spatial scale, corridor pitch, wall height, dead-end retention, door counts, and Hunter/timer tuning
    /// for <paramref name="morphology"/> onto <paramref name="p"/>.
    /// </summary>
    public static void ApplyMorphologyToProfile(FloorProfile p, FloorProfile.MazeMorphology morphology, bool isDescent)
    {
        if (p == null) return;
        p.Morphology = morphology;

        switch (morphology)
        {
            case FloorProfile.MazeMorphology.Standard:
                break;

            case FloorProfile.MazeMorphology.CompactWarren:
            {
                int minSide = (p.Floor >= 3 || isDescent) ? 7 : 6;
                int minW = p.Shape == FloorProfile.FloorShape.Rectangle ? minSide : (p.Shape == FloorProfile.FloorShape.Cross ? 10 : 9);
                int minH = p.Shape == FloorProfile.FloorShape.Rectangle ? minSide : (p.Shape == FloorProfile.FloorShape.Cross ? 9 : 8);

                p.Width = Mathf.Max(minW, Mathf.RoundToInt(p.Width * 0.80f));
                p.Height = Mathf.Max(minH, Mathf.RoundToInt(p.Height * 0.80f));
                p.CellSize = 4.0f;
                p.WallHeight = 3.3f;
                p.DeadEndKeep = Mathf.Max(p.DeadEndKeep, 0.72f);
                p.DeadEndBaitStars = Mathf.Min(Mathf.Max(0, p.StarCount - 1), p.DeadEndBaitStars + 1);
                p.HearingScale *= 1.15f;
                p.AmbushBias = Mathf.Min(0.85f, p.AmbushBias + 0.15f);
                p.LampRange *= 0.88f;
                break;
            }

            case FloorProfile.MazeMorphology.CavernousHalls:
            {
                // Keep Story height * cellSize <= 55 m so the maze stays clear of the FloorThemes gallery at z = -6.
                int maxW = isDescent ? 18 : 14;
                int maxH = isDescent ? 14 : 11;

                p.Width = Mathf.Clamp(Mathf.RoundToInt(p.Width * 1.25f), Mathf.Min(p.Width + 1, maxW), maxW);
                p.Height = Mathf.Clamp(Mathf.RoundToInt(p.Height * 1.20f), Mathf.Min(p.Height + 1, maxH), maxH);
                p.CellSize = Mathf.Max(p.CellSize, 5.0f);
                p.WallHeight = Mathf.Max(p.WallHeight, 4.6f);
                p.DeadEndKeep *= 0.40f;
                p.SprintSeconds *= 1.20f;
                p.EscapeSeconds *= 1.20f;
                p.GateEscapeSeconds *= 1.15f;
                p.LampRange = Mathf.Max(p.LampRange * 1.20f, 7.5f);
                p.LampIntensity = Mathf.Max(p.LampIntensity, 1.5f);
                p.ViewDistance *= 1.15f;
                p.LockerCount = Mathf.Max(p.LockerCount, Mathf.RoundToInt(p.LockerCount * 1.20f));
                break;
            }

            case FloorProfile.MazeMorphology.ChambersAndChokepoints:
            {
                int maxW = isDescent ? 17 : 13;
                int maxH = isDescent ? 13 : 10;

                p.Width = Mathf.Clamp(Mathf.RoundToInt(p.Width * 1.15f), Mathf.Min(p.Width + 1, maxW), maxW);
                p.Height = Mathf.Clamp(Mathf.RoundToInt(p.Height * 1.15f), Mathf.Min(p.Height + 1, maxH), maxH);
                p.CellSize = Mathf.Max(p.CellSize, 4.6f);
                p.WallHeight = Mathf.Max(p.WallHeight, 4.2f);
                p.DeadEndKeep = Mathf.Max(p.DeadEndKeep, 0.50f);
                p.ManualDoors += 1;
                p.SprintSeconds *= 1.10f;
                p.EscapeSeconds *= 1.10f;
                p.AmbushBias = Mathf.Min(0.85f, p.AmbushBias + 0.10f);
                break;
            }
        }
    }

    private void ResolveAndApplyMorphology(int usedSeed)
    {
        if (_profile == null) return;

        bool descent = GameFlow.IsDescent;
        int floorOrDepth = descent ? DescentRun.Depth : _profile.Floor;

        FloorProfile.MazeMorphology morphology;
        if (Application.isEditor && debugMorphology != DebugMazeMorphology.Auto)
        {
            morphology = (FloorProfile.MazeMorphology)debugMorphology;
        }
        else if (terror == null || !terror.dynamicMorphologies)
        {
            morphology = FloorProfile.MazeMorphology.Standard;
        }
        else
        {
            morphology = RollMorphologyForFloor(floorOrDepth, usedSeed, descent);
        }

        ApplyMorphologyToProfile(_profile, morphology, descent);
    }

    // =========================================================================
    // 2. CARVE BIAS, MULTI-ROOM CARVING, BAFFLES & WALL THINNING (D3)
    // =========================================================================

    private int PickMorphologyCarveDirection(List<int> candidates, int lastDir, System.Random rng)
    {
        if (candidates.Count <= 1 || lastDir < 0 || _profile == null)
        {
            return candidates[rng.Next(candidates.Count)];
        }

        FloorProfile.MazeMorphology morph = _profile.Morphology;
        if (morph != FloorProfile.MazeMorphology.CompactWarren && morph != FloorProfile.MazeMorphology.CavernousHalls)
        {
            return candidates[rng.Next(candidates.Count)];
        }

        float turnWeight = morph == FloorProfile.MazeMorphology.CompactWarren ? 2.4f : 1.0f;
        float straightWeight = morph == FloorProfile.MazeMorphology.CavernousHalls ? 1.8f : 1.0f;

        float total = 0f;
        System.Span<float> weights = stackalloc float[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            float w = candidates[i] == lastDir ? straightWeight : turnWeight;
            weights[i] = w;
            total += w;
        }

        float roll = (float)(rng.NextDouble() * total);
        float accum = 0f;
        for (int i = 0; i < candidates.Count; i++)
        {
            accum += weights[i];
            if (roll <= accum) return candidates[i];
        }

        return candidates[candidates.Count - 1];
    }

    /// <summary>
    /// F127 D3: Handles multi-room carving, internal L/island cover baffles, and cavernous wall thinning
    /// for non-Standard morphologies on stream <c>usedSeed + 29</c>. Returns true when handled.
    /// </summary>
    private bool BuildMorphologyRooms(int usedSeed)
    {
        _carvedRooms.Clear();
        if (_profile == null || !terror.dynamicMorphologies || _profile.Morphology == FloorProfile.MazeMorphology.Standard)
        {
            return false;
        }

        FloorProfile.MazeMorphology morph = _profile.Morphology;
        if (morph == FloorProfile.MazeMorphology.CompactWarren)
        {
            // CompactWarren has zero open rooms and zero galleries: pure claustrophobic warren.
            terror.Log("Morphology CompactWarren: 0 rooms, 0 galleries", this);
            return true;
        }

        System.Random rng = new System.Random(usedSeed + MorphologySalt);

        if (morph == FloorProfile.MazeMorphology.CavernousHalls)
        {
            int wantRooms = (width * height >= 115) ? 5 : (width * height >= 80 ? 4 : 3);
            Vector2Int[] sizes =
            {
                new Vector2Int(4, 3),
                new Vector2Int(3, 4),
                new Vector2Int(3, 3),
                new Vector2Int(3, 2),
                new Vector2Int(2, 3),
                new Vector2Int(2, 2)
            };

            CarveMultipleRooms(wantRooms, sizes, 1, false, rng);
            OpenExtraCavernousPassages(rng);
            RebuildRoomPillarVertices();
            if (_profile.Floor >= galleryFromFloor) FindOrCarveGallery(rng);
            return true;
        }

        if (morph == FloorProfile.MazeMorphology.ChambersAndChokepoints)
        {
            int wantRooms = (width * height >= 95) ? 4 : 3;
            Vector2Int[] sizes =
            {
                new Vector2Int(3, 3),
                new Vector2Int(3, 2),
                new Vector2Int(2, 3),
                new Vector2Int(2, 2)
            };

            CarveMultipleRooms(wantRooms, sizes, 2, true, rng);
            RebuildRoomPillarVertices();
            if (_profile.Floor >= galleryFromFloor) FindOrCarveGallery(rng);
            return true;
        }

        return false;
    }

    private void CarveMultipleRooms(int wantRooms, Vector2Int[] sizeTemplates, int minGapCells, bool distributeSectors, System.Random rng)
    {
        HashSet<Vector2Int> avoid = DeadEndCells();

        for (int r = 0; r < wantRooms; r++)
        {
            bool carvedOne = false;
            int targetSector = distributeSectors ? (r % 4) : -1;

            for (int s = 0; s < sizeTemplates.Length && !carvedOne; s++)
            {
                int rw = sizeTemplates[s].x;
                int rh = sizeTemplates[s].y;
                if (rw + 2 > width || rh + 2 > height) continue;

                List<Vector2Int> anchors = new List<Vector2Int>();
                for (int x = 0; x + rw <= width; x++)
                {
                    for (int z = 0; z + rh <= height; z++)
                    {
                        RectInt candidate = new RectInt(x, z, rw, rh);
                        if (!RectRoomFits(candidate, minGapCells, avoid)) continue;
                        anchors.Add(new Vector2Int(x, z));
                    }
                }

                if (anchors.Count == 0) continue;
                Shuffle(anchors, rng);

                if (targetSector >= 0)
                {
                    int midX = width / 2;
                    int midZ = height / 2;
                    List<Vector2Int> inSector = new List<Vector2Int>();
                    List<Vector2Int> others = new List<Vector2Int>();
                    foreach (Vector2Int a in anchors)
                    {
                        int cx = a.x + rw / 2;
                        int cz = a.y + rh / 2;
                        int sec = (cx >= midX ? 1 : 0) + (cz >= midZ ? 2 : 0);
                        if (sec == targetSector) inSector.Add(a);
                        else others.Add(a);
                    }
                    anchors.Clear();
                    anchors.AddRange(inSector);
                    anchors.AddRange(others);
                }

                WallSnapshot before = TakeWallSnapshot();
                foreach (Vector2Int anchor in anchors)
                {
                    RectInt rect = new RectInt(anchor.x, anchor.y, rw, rh);
                    OpenRectBlock(rect);
                    if (rw >= 3 && rh >= 3)
                    {
                        AddRoomInteriorBaffles(rect, rng);
                    }

                    ComputeDistances();
                    Vector2Int farthest = FarthestCell();
                    if (!InRect(farthest, rect) && !_roomCells.Contains(farthest) && ReachableCells(_startCell, null).Count == _playableCellCount)
                    {
                        _carvedRooms.Add(rect);
                        for (int x = rect.xMin; x < rect.xMax; x++)
                        {
                            for (int z = rect.yMin; z < rect.yMax; z++)
                            {
                                _roomCells.Add(new Vector2Int(x, z));
                            }
                        }
                        terror.Log($"Morphology room {rw}x{rh} at {anchor}", this);
                        carvedOne = true;
                        break;
                    }

                    RestoreWalls(before);
                }
            }
        }

        ComputeDistances();
    }

    private bool RectRoomFits(RectInt rect, int minGapCells, HashSet<Vector2Int> avoid)
    {
        for (int x = rect.xMin; x < rect.xMax; x++)
        {
            for (int z = rect.yMin; z < rect.yMax; z++)
            {
                if (IsVoid(x, z)) return false;
                if (Mathf.Max(Mathf.Abs(x - _startCell.x), Mathf.Abs(z - _startCell.y)) < 2) return false;
                Vector2Int c = new Vector2Int(x, z);
                if (avoid.Contains(c) || _roomCells.Contains(c)) return false;
            }
        }

        foreach (RectInt existing in _carvedRooms)
        {
            if (rect.xMax + minGapCells > existing.xMin && rect.xMin - minGapCells < existing.xMax &&
                rect.yMax + minGapCells > existing.yMin && rect.yMin - minGapCells < existing.yMax)
            {
                return false;
            }
        }

        return true;
    }

    private static bool InRect(Vector2Int cell, RectInt rect)
    {
        return cell.x >= rect.xMin && cell.x < rect.xMax && cell.y >= rect.yMin && cell.y < rect.yMax;
    }

    private void OpenRectBlock(RectInt rect)
    {
        for (int x = rect.xMin; x < rect.xMax; x++)
        {
            for (int z = rect.yMin; z < rect.yMax; z++)
            {
                if (x + 1 < rect.xMax) OpenWall(new Vector2Int(x, z), 1);
                if (z + 1 < rect.yMax) OpenWall(new Vector2Int(x, z), 0);
            }
        }
    }

    private void CloseWall(Vector2Int cell, int direction)
    {
        Vector2Int other = Neighbour(cell, direction);
        switch (direction)
        {
            case 0: _wallN[cell.x, cell.y] = true; _wallS[other.x, other.y] = true; break;
            case 1: _wallE[cell.x, cell.y] = true; _wallW[other.x, other.y] = true; break;
            case 2: _wallS[cell.x, cell.y] = true; _wallN[other.x, other.y] = true; break;
            default: _wallW[cell.x, cell.y] = true; _wallE[other.x, other.y] = true; break;
        }
    }

    /// <summary>
    /// F127 D3: Places 1 or 2 interior partition wall segments inside a 3x3+ room to form an L-shaped or island cover baffle,
    /// breaking sightlines and creating real architectural cover inside great halls.
    /// </summary>
    private void AddRoomInteriorBaffles(RectInt rect, System.Random rng)
    {
        int cx = rect.xMin + (rect.width - 1) / 2;
        int cz = rect.yMin + (rect.height - 1) / 2;
        Vector2Int center = new Vector2Int(cx, cz);

        int pattern = rng.Next(4);
        switch (pattern)
        {
            case 0:
                if (cx + 1 < rect.xMax) CloseWall(center, 1);
                if (cz + 1 < rect.yMax) CloseWall(center, 0);
                break;
            case 1:
                if (cx > rect.xMin) CloseWall(center, 3);
                if (cz + 1 < rect.yMax) CloseWall(center, 0);
                break;
            case 2:
                if (cx + 1 < rect.xMax) CloseWall(center, 1);
                if (cz > rect.yMin) CloseWall(center, 2);
                break;
            default:
                if (cx > rect.xMin) CloseWall(center, 3);
                if (cz > rect.yMin) CloseWall(center, 2);
                break;
        }
    }

    /// <summary>
    /// F127 D3: On CavernousHalls, knocks down ~42% of remaining interior corridor walls between non-dead-end cells,
    /// turning narrow maze passages into wide pillared boulevards and open plazas.
    /// </summary>
    private void OpenExtraCavernousPassages(System.Random rng)
    {
        HashSet<Vector2Int> avoid = DeadEndCells();
        avoid.Add(_startCell);

        List<(Vector2Int cell, int dir)> closedWalls = new List<(Vector2Int, int)>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                Vector2Int a = new Vector2Int(x, z);
                if (avoid.Contains(a)) continue;

                if (z + 1 < height && !IsVoid(x, z + 1) && _wallN[x, z])
                {
                    Vector2Int b = new Vector2Int(x, z + 1);
                    if (!avoid.Contains(b) && !(_roomCells.Contains(a) && _roomCells.Contains(b)))
                    {
                        closedWalls.Add((a, 0));
                    }
                }
                if (x + 1 < width && !IsVoid(x + 1, z) && _wallE[x, z])
                {
                    Vector2Int b = new Vector2Int(x + 1, z);
                    if (!avoid.Contains(b) && !(_roomCells.Contains(a) && _roomCells.Contains(b)))
                    {
                        closedWalls.Add((a, 1));
                    }
                }
            }
        }

        for (int i = closedWalls.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (closedWalls[i], closedWalls[j]) = (closedWalls[j], closedWalls[i]);
        }

        int targetOpen = Mathf.RoundToInt(closedWalls.Count * 0.42f);
        int opened = 0;
        for (int i = 0; i < closedWalls.Count && opened < targetOpen; i++)
        {
            (Vector2Int cell, int dir) cand = closedWalls[i];
            if (!IsWallClosed(cand.cell.x, cand.cell.y, cand.dir)) continue;

            OpenWall(cand.cell, cand.dir);
            ComputeDistances();
            Vector2Int farthest = FarthestCell();
            if (_roomCells.Contains(farthest) || farthest == _startCell)
            {
                CloseWall(cand.cell, cand.dir);
                ComputeDistances();
                continue;
            }
            opened++;
        }

        if (opened > 0)
        {
            terror.Log($"CavernousHalls opened {opened} extra interior walls", this);
        }
    }

    private void RebuildRoomPillarVertices()
    {
        _roomPillarVertices.Clear();
        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
        foreach (RectInt rect in _carvedRooms)
        {
            for (int vx = rect.xMin + 1; vx < rect.xMax; vx++)
            {
                for (int vz = rect.yMin + 1; vz < rect.yMax; vz++)
                {
                    Vector2Int v = new Vector2Int(vx, vz);
                    if (seen.Add(v)) _roomPillarVertices.Add(v);
                }
            }
        }
    }

    /// <summary>
    /// F127 D3: Raised cathedral ceilings over carved rooms (+1.2 m on CavernousHalls, +1.0 m on ChambersAndChokepoints, +0.8 m on Standard).
    /// </summary>
    private float MorphologyRoomCeilingHeightAt(int x, int z, float baseHeight)
    {
        if (terror == null || !terror.dynamicMorphologies || _profile == null) return baseHeight;
        if (!_roomCells.Contains(new Vector2Int(x, z))) return baseHeight;

        float raise = _profile.Morphology switch
        {
            FloorProfile.MazeMorphology.CavernousHalls => 1.2f,
            FloorProfile.MazeMorphology.ChambersAndChokepoints => 1.0f,
            _ => 0.8f
        };
        return Mathf.Max(baseHeight, wallHeight + raise);
    }

    /// <summary>
    /// F127 D3: Suspends an overhead pendant lamp from the raised ceiling at the centre of each carved room.
    /// Uses its own RNG offset so the +2 wall-lamp stream is untouched on Standard floors.
    /// </summary>
    private void BuildMorphologyRoomLamps(Material sharedMaterial)
    {
        if (terror == null || !terror.dynamicMorphologies || _carvedRooms.Count == 0 || !wallLamps) return;

        System.Random rng = new System.Random(UsedSeed + MorphologySalt + 100);
        Material cordMat = _wallMat;

        for (int i = 0; i < _carvedRooms.Count; i++)
        {
            RectInt rect = _carvedRooms[i];
            Vector2Int center = new Vector2Int(rect.xMin + rect.width / 2, rect.yMin + rect.height / 2);
            if (IsVoid(center) || _lampCells.Contains(center)) continue;

            float ceilY = CeilingHeightAt(center.x, center.y);
            float mountHeight = Mathf.Max(lampHeight + 0.35f, ceilY - 0.45f);
            Vector3 pos = CellCenter(center.x, center.y) + Vector3.up * mountHeight;
            Quaternion rot = Quaternion.Euler(90f, 0f, 0f);

            // Hang a slender vertical conduit rod from the raised ceiling down to the pendant fixture.
            float rodLen = (FloorTop + ceilY) - pos.y;
            if (rodLen > 0.08f)
            {
                CreateBox(
                    $"RoomPendantRod_{center.x}_{center.y}",
                    pos + Vector3.up * (rodLen * 0.5f),
                    new Vector3(0.06f, rodLen, 0.06f),
                    cordMat,
                    _lampsGroup);
            }

            WallLamp roomLamp;
            if (_theme != null && _theme.Lamp != null)
            {
                roomLamp = CreateThemedLamp($"RoomPendantLamp_{center.x}_{center.y}", pos, rot, _lampsGroup, false, rng.Next(1000), false);
            }
            else
            {
                GameObject fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fixture.name = $"RoomPendantLamp_{center.x}_{center.y}";
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

                roomLamp = fixture.AddComponent<WallLamp>();
                roomLamp.Configure(light, mr, lampIntensity, false, rng.Next(1000), false);
            }

            _lamps.Add(roomLamp);
            _lampCells.Add(center);
        }
    }

    // =========================================================================
    // 3. CHOKEPOINT DOOR PRIORITISATION & ROOM-TO-ROOM PATROL (D4)
    // =========================================================================

    private bool IsChamberThresholdPassage(Passage p)
    {
        if (_roomCells.Count == 0) return false;
        return _roomCells.Contains(p.CellA) != _roomCells.Contains(p.CellB);
    }

    private void PrioritiseMorphologySecurityOrder(List<int> order, List<(Passage passage, HashSet<Vector2Int> startSide, float cutFraction)> pool)
    {
        if (_profile == null || _profile.Morphology != FloorProfile.MazeMorphology.ChambersAndChokepoints || order.Count <= 1 || _roomCells.Count == 0)
        {
            return;
        }

        List<int> thresholds = new List<int>();
        List<int> rest = new List<int>(order.Count);
        foreach (int idx in order)
        {
            if (IsChamberThresholdPassage(pool[idx].passage)) thresholds.Add(idx);
            else rest.Add(idx);
        }

        if (thresholds.Count == 0 || rest.Count == 0) return;
        order.Clear();
        order.AddRange(thresholds);
        order.AddRange(rest);
    }

    private void PrioritiseMorphologyManualOrder(List<int> order, List<Passage> candidates)
    {
        if (_profile == null || _profile.Morphology != FloorProfile.MazeMorphology.ChambersAndChokepoints || order.Count <= 1 || _roomCells.Count == 0)
        {
            return;
        }

        List<int> thresholds = new List<int>();
        List<int> rest = new List<int>(order.Count);
        foreach (int idx in order)
        {
            if (IsChamberThresholdPassage(candidates[idx])) thresholds.Add(idx);
            else rest.Add(idx);
        }

        if (thresholds.Count == 0 || rest.Count == 0) return;
        order.Clear();
        order.AddRange(thresholds);
        order.AddRange(rest);
    }

    /// <summary>
    /// F127 D4: On multi-room morphologies (CavernousHalls, ChambersAndChokepoints), gives the Hunter a 50% chance
    /// to pick a wander target inside a different carved chamber than the one it currently occupies.
    /// </summary>
    private int PickMorphologyWanderCellIndex(Vector3 hunterPos, int currentIndex, IReadOnlyList<Vector3> wanderPoints)
    {
        if (_carvedRooms.Count < 2 || wanderPoints == null || wanderPoints.Count <= 1) return -1;

        System.Random rng = new System.Random(System.Environment.TickCount ^ (currentIndex * 397) ^ (int)(Time.time * 100f));
        if (rng.NextDouble() >= 0.50) return -1;

        Vector2Int hunterCell = ShapeWorldToCell(hunterPos);
        int currentRoom = -1;
        for (int i = 0; i < _carvedRooms.Count; i++)
        {
            if (InRect(hunterCell, _carvedRooms[i]))
            {
                currentRoom = i;
                break;
            }
        }

        List<int> otherRooms = new List<int>();
        for (int i = 0; i < _carvedRooms.Count; i++)
        {
            if (i != currentRoom) otherRooms.Add(i);
        }
        if (otherRooms.Count == 0) return -1;

        RectInt targetRoom = _carvedRooms[otherRooms[rng.Next(otherRooms.Count)]];
        Vector2Int targetCell = new Vector2Int(
            targetRoom.xMin + rng.Next(targetRoom.width),
            targetRoom.yMin + rng.Next(targetRoom.height));
        Vector3 worldTarget = CellCenter(targetCell.x, targetCell.y);

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

    // =========================================================================
    // 4. MID-RUN UNOBSERVED WALL / SHUTTER SHIFTS (D5)
    // =========================================================================

    /// <summary>
    /// F127 D5: Post-bake setup of up to 2 linked open/closed shutter pairs on loop passages.
    /// Runs after all other placement passes on stream <c>usedSeed + 29 + 200</c> so no earlier stream is touched.
    /// Closing either (or both) passages in a pair is verified to leave 100% of playable cells reachable.
    /// </summary>
    private void BuildShiftingPassages(int usedSeed)
    {
        _shiftPairs.Clear();
        if (terror == null || !terror.wallShifts || superEasyMode || _profile == null) return;

        bool descent = GameFlow.IsDescent;
        int floorOrDepth = descent ? DescentRun.Depth : _profile.Floor;
        if (floorOrDepth < 2) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || interactableKit.ShutterDoor == null) return;

        if (aiFollower == null) aiFollower = FindAnyObjectByType<AIFollower>();
        Transform player = FindPlayer();

        int maxPairs = (floorOrDepth >= 4 || (descent && floorOrDepth >= 5)) ? 2 : 1;
        System.Random rng = new System.Random(usedSeed + MorphologySalt + 200);

        HashSet<Vector2Int> busyCells = new HashSet<Vector2Int>(_starCells);
        busyCells.UnionWith(_lockerCells);
        busyCells.UnionWith(_setPieceCells);
        busyCells.UnionWith(_squeezeCells);
        busyCells.UnionWith(_doorUsedCells);
        if (_loopActive) busyCells.Add(_loopEntrance);
        if (HasExitGates)
        {
            foreach (Vector2Int gateCell in GateCells) busyCells.Add(gateCell);
        }
        busyCells.Add(_startCell);
        busyCells.Add(FarthestCell());

        List<Passage> existingDoors = new List<Passage>(_doorPassages.Values);
        List<Passage> loopPassages = new List<Passage>();

        foreach (Passage p in AllOpenPassages())
        {
            if (busyCells.Contains(p.CellA) || busyCells.Contains(p.CellB)) continue;
            if (Mathf.Abs(p.CellA.x - _startCell.x) + Mathf.Abs(p.CellA.y - _startCell.y) < 3) continue;
            if (Mathf.Abs(p.CellB.x - _startCell.x) + Mathf.Abs(p.CellB.y - _startCell.y) < 3) continue;
            if (_roomCells.Contains(p.CellA) && _roomCells.Contains(p.CellB)) continue;
            if (TooCloseToPlacedDoors(p.CellA, existingDoors, 2)) continue;

            // Must be a non-bridge passage: blocking it alone leaves 100% of playable cells reachable.
            if (ReachableCells(_startCell, p).Count != _playableCellCount) continue;
            loopPassages.Add(p);
        }

        if (loopPassages.Count < 2) return;

        List<int> order = new List<int>(loopPassages.Count);
        for (int i = 0; i < loopPassages.Count; i++) order.Add(i);
        ShuffleInts(order, rng);

        HashSet<int> usedIndices = new HashSet<int>();
        for (int i = 0; i < order.Count && _shiftPairs.Count < maxPairs; i++)
        {
            int idxA = order[i];
            if (usedIndices.Contains(idxA)) continue;
            Passage pA = loopPassages[idxA];
            if (TooCloseToPlacedDoors(pA.CellA, existingDoors, 2)) continue;

            for (int j = i + 1; j < order.Count; j++)
            {
                int idxB = order[j];
                if (usedIndices.Contains(idxB)) continue;
                Passage pB = loopPassages[idxB];

                int dist = Mathf.Abs(pA.CellA.x - pB.CellA.x) + Mathf.Abs(pA.CellA.y - pB.CellA.y);
                if (dist < 2 || dist > 7) continue;
                if (TooCloseToPlacedDoors(pB.CellA, existingDoors, 2)) continue;

                // Blocking both passages simultaneously (and respecting all locked security doors) must still reach 100% of cells.
                if (ReachableCellsBlockingBoth(_startCell, pA, pB).Count != _playableCellCount) continue;

                MazeDoor doorA = CreateDoor(MazeDoor.Kind.Manual, pA, pA.CellA, pA.CellB, player);
                MazeDoor doorB = CreateDoor(MazeDoor.Kind.Manual, pB, pB.CellA, pB.CellB, player);
                doorA.name = $"ShiftDoor_{pA.CellA.x}_{pA.CellA.y}_{pA.Dir}_Open";
                doorB.name = $"ShiftDoor_{pB.CellA.x}_{pB.CellA.y}_{pB.Dir}_Closed";
                doorA.IsShiftDoor = true;
                doorB.IsShiftDoor = true;
                doorA.SetOpenImmediate();

                if (!DoorsStayCompletable())
                {
                    DiscardDoor(doorA);
                    DiscardDoor(doorB);
                    continue;
                }

                existingDoors.Add(pA);
                existingDoors.Add(pB);
                usedIndices.Add(idxA);
                usedIndices.Add(idxB);

                _shiftPairs.Add(new ShiftingPassagePair
                {
                    DoorA = doorA,
                    DoorB = doorB,
                    PassageA = pA,
                    PassageB = pB,
                    Shifted = false
                });

                terror.Log($"ShiftPair #{_shiftPairs.Count}: open ({pA.CellA}->{pA.CellB}) / closed ({pB.CellA}->{pB.CellB})", this);
                break;
            }
        }
    }

    private HashSet<Vector2Int> ReachableCellsBlockingBoth(Vector2Int from, Passage blockedA, Passage blockedB)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { from };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                if (IsSamePassage(cell, dir, blockedA) || IsSamePassage(cell, dir, blockedB)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height || IsVoid(n)) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }

        return visited;
    }

    /// <summary>
    /// F127 D5: Called by <see cref="WorldShiftDirector"/> to swap an unobserved shifting passage pair
    /// once the player has visited the area.
    /// </summary>
    public bool TryTriggerWallShift(WorldShiftDirector shiftDirector, Vector3 playerPos)
    {
        if (shiftDirector == null || _shiftPairs.Count == 0) return false;

        for (int i = 0; i < _shiftPairs.Count; i++)
        {
            ShiftingPassagePair pair = _shiftPairs[i];
            if (pair.Shifted || pair.DoorA == null || pair.DoorB == null) continue;

            // Require the player to have visited at least one cell adjacent to the pair so the shift alters a route they know.
            bool seenArea = shiftDirector.TryGetVisitedAt(pair.PassageA.CellA, out _)
                || shiftDirector.TryGetVisitedAt(pair.PassageA.CellB, out _)
                || shiftDirector.TryGetVisitedAt(pair.PassageB.CellA, out _)
                || shiftDirector.TryGetVisitedAt(pair.PassageB.CellB, out _);
            if (!seenArea) continue;

            if (!shiftDirector.IsUnobserved(pair.DoorA.transform.position, pair.DoorA.transform)) continue;
            if (!shiftDirector.IsUnobserved(pair.DoorB.transform.position, pair.DoorB.transform)) continue;

            // Flip whichever is open to closed and whichever is closed to open.
            bool aWasOpen = pair.DoorA.IsOpen;
            pair.DoorA.ShiftToggle(!aWasOpen);
            pair.DoorB.ShiftToggle(aWasOpen);

            pair.Shifted = true;
            _shiftPairs[i] = pair;

            Vector3 midpoint = (pair.DoorA.transform.position + pair.DoorB.transform.position) * 0.5f;
            if (!GameAudio.PlayAt(GameAudio.Library.shutterRoll, midpoint, 0.55f))
            {
                if (_wallShiftRumbleClip == null) _wallShiftRumbleClip = DoorAudio.BuildClunkClip(0.55f, 48f, 0.65f);
                GameAudio.ClipAtPoint(_wallShiftRumbleClip, midpoint, 0.55f);
            }

            terror?.Log($"WallShift: flipped ({pair.PassageA.CellA}<->{pair.PassageA.CellB}) and ({pair.PassageB.CellA}<->{pair.PassageB.CellB})", this);
            return true;
        }

        return false;
    }

    private void AppendMorphologyLegend(StringBuilder sb)
    {
        if (_profile == null) return;
        sb.Append($"morphology: {_profile.Morphology}, carved rooms: {_carvedRooms.Count}, shift pairs: {_shiftPairs.Count}\n");
    }
}
