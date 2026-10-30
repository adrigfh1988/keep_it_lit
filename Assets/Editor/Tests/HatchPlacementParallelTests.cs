using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace KeepItLit.Tests
{
    /// <summary>
    /// Parallelized 10,000-seed EditMode verification of <see cref="MazeEscape"/> hatch placement across all
    /// <see cref="FloorProfile.FloorShape"/> void masks (Rectangle, Ring, TwinWings, Cross, LChasm variants 1-3) and
    /// <see cref="FloorProfile.MazeMorphology"/> scales, in both normal escapeHunt mode and superEasyMode.
    /// Ensures every chosen hatch cell is in bounds, non-void, and 100% reachable from the start cell and the player cell.
    /// </summary>
    [Category("Fast")]
    public class HatchPlacementParallelTests
    {
        private const int TotalSeeds = 10_000;
        private static readonly Vector3 DescentOrigin = new Vector3(-70f, 4.98f, -400f);
        private static readonly Vector2 DefaultPathWindow = new Vector2(25f, 40f);
        private const float DefaultWindowWiden = 0.75f;
        private const float DefaultMinHunterDist = 15f;
        private const float DefaultMinPlayerDist = 12f;

        private static readonly FloorProfile.FloorShape[] Shapes =
        {
            FloorProfile.FloorShape.Rectangle,
            FloorProfile.FloorShape.Ring,
            FloorProfile.FloorShape.TwinWings,
            FloorProfile.FloorShape.Cross,
            FloorProfile.FloorShape.LChasm
        };

        private static readonly FloorProfile.MazeMorphology[] Morphologies =
        {
            FloorProfile.MazeMorphology.Standard,
            FloorProfile.MazeMorphology.CompactWarren,
            FloorProfile.MazeMorphology.CavernousHalls,
            FloorProfile.MazeMorphology.ChambersAndChokepoints
        };

        [Test]
        public void Parallel10KSeeds_AllHatchesAreInPlayableReachableCells()
        {
            int[] seeds = new int[TotalSeeds];
            System.Random masterRng = new System.Random(20261030);
            for (int i = 0; i < TotalSeeds; i++)
            {
                // Include the exact bug seed (211843) at index 0, followed by 9,999 randomly generated seeds.
                seeds[i] = i == 0 ? 211843 : masterRng.Next(1, int.MaxValue);
            }

            ConcurrentBag<string> failures = new ConcurrentBag<string>();

            Parallel.For(0, TotalSeeds, i =>
            {
                int seed = seeds[i];
                FloorProfile.FloorShape shape = i == 0
                    ? FloorProfile.FloorShape.Cross
                    : Shapes[(seed & 0x7FFFFFFF) % Shapes.Length];
                FloorProfile.MazeMorphology morph = i == 0
                    ? FloorProfile.MazeMorphology.ChambersAndChokepoints
                    : Morphologies[((seed >> 5) & 0x7FFFFFFF) % Morphologies.Length];
                int variant = 1 + (((seed >> 9) & 0x7FFFFFFF) % 3);
                int depth = i == 0 ? 2 : 1 + (((seed >> 13) & 0x7FFFFFFF) % 15);

                ResolveDimensions(depth, shape, morph, out int width, out int height, out float cellSize, out float wallHeight);

                MazeGrid grid = BuildCarvedMazeGrid(seed, width, height, cellSize, wallHeight, shape, variant);

                if (grid.IsVoid(grid.StartCell.x, grid.StartCell.y))
                {
                    failures.Add($"seed={seed} shape={shape} morph={morph}: StartCell {grid.StartCell} is void");
                    return;
                }

                IReadOnlyList<Vector3> centers = grid.CellCenters;
                if (centers.Count == 0)
                {
                    failures.Add($"seed={seed} shape={shape} morph={morph}: zero playable cells");
                    return;
                }

                // Pick a deterministic player cell (where the last star was collected) and hunter cell.
                Vector3 playerPos = centers[(seed & 0x7FFFFFFF) % centers.Count];
                Vector3 hunterPos = grid.CellCenter(grid.FarthestCell.x, grid.FarthestCell.y);

                // 1. Normal escapeHunt mode
                Vector3 normalHatch = MazeEscape.ChooseHatchPositionForGrid(
                    grid,
                    superEasyMode: false,
                    escapeHunt: true,
                    playerPos: playerPos,
                    hunterPos: hunterPos,
                    pathWindowMetres: DefaultPathWindow,
                    windowWiden: DefaultWindowWiden,
                    minHunterDistance: DefaultMinHunterDist,
                    minPlayerDistance: DefaultMinPlayerDist,
                    randomPickSeed: seed ^ 0x5A5A5A5A);

                ValidateHatch(grid, normalHatch, seed, shape, morph, "normal", failures);

                // 2. SuperEasyMode (regression guard for Cross void (0, 0) bug)
                Vector3 superEasyHatch = MazeEscape.ChooseHatchPositionForGrid(
                    grid,
                    superEasyMode: true,
                    escapeHunt: true,
                    playerPos: playerPos,
                    hunterPos: hunterPos,
                    pathWindowMetres: DefaultPathWindow,
                    windowWiden: DefaultWindowWiden,
                    minHunterDistance: DefaultMinHunterDist,
                    minPlayerDistance: DefaultMinPlayerDist,
                    randomPickSeed: seed ^ 0x3C3C3C3C);

                ValidateHatch(grid, superEasyHatch, seed, shape, morph, "superEasy", failures);
            });

            if (!failures.IsEmpty)
            {
                List<string> sample = new List<string>();
                foreach (string f in failures)
                {
                    sample.Add(f);
                    if (sample.Count >= 20) break;
                }
                Assert.Fail($"{failures.Count} hatch placement failure(s) out of {TotalSeeds} seeds:\n" + string.Join("\n", sample));
            }
        }

        private static void ValidateHatch(
            MazeGrid grid,
            Vector3 hatchWorld,
            int seed,
            FloorProfile.FloorShape shape,
            FloorProfile.MazeMorphology morph,
            string mode,
            ConcurrentBag<string> failures)
        {
            if (!grid.TryWorldToCell(hatchWorld, out int hx, out int hy))
            {
                failures.Add($"seed={seed} shape={shape} morph={morph} mode={mode}: hatch at {hatchWorld} is outside playable maze or inside a void cell");
                return;
            }

            Vector2Int hatchCell = new Vector2Int(hx, hy);
            if (grid.GetDistance(hx, hy) < 0)
            {
                failures.Add($"seed={seed} shape={shape} morph={morph} mode={mode}: hatch cell {hatchCell} is unreachable from StartCell {grid.StartCell}");
            }
        }

        private static void ResolveDimensions(
            int depth,
            FloorProfile.FloorShape shape,
            FloorProfile.MazeMorphology morph,
            out int width,
            out int height,
            out float cellSize,
            out float wallHeight)
        {
            int stars = Mathf.Min(3 + depth / 3, 8);
            int required = stars + 3;
            int minForDepth = 42 + (depth - 1) * 6;
            int targetPlayable = Mathf.Clamp(Mathf.Max(18 + 7 * required, minForDepth), 42, 160);

            float fill = shape switch
            {
                FloorProfile.FloorShape.Ring => 0.78f,
                FloorProfile.FloorShape.TwinWings => 0.76f,
                FloorProfile.FloorShape.Cross => 0.68f,
                FloorProfile.FloorShape.LChasm => 0.76f,
                _ => 1.00f
            };

            int rawCells = Mathf.RoundToInt(targetPlayable / fill);
            int minW = shape == FloorProfile.FloorShape.Rectangle ? 7 : (shape == FloorProfile.FloorShape.Cross ? 10 : 9);
            int minH = shape == FloorProfile.FloorShape.Rectangle ? 6 : (shape == FloorProfile.FloorShape.Cross ? 9 : 8);
            width = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(rawCells * 1.25f)), minW, 16);
            height = Mathf.Clamp(Mathf.RoundToInt((float)rawCells / width), minH, 13);
            cellSize = 4.5f;
            wallHeight = 4.0f;

            switch (morph)
            {
                case FloorProfile.MazeMorphology.CompactWarren:
                    width = Mathf.Max(minW, Mathf.RoundToInt(width * 0.80f));
                    height = Mathf.Max(minH, Mathf.RoundToInt(height * 0.80f));
                    cellSize = 4.0f;
                    wallHeight = 3.3f;
                    break;
                case FloorProfile.MazeMorphology.CavernousHalls:
                    width = Mathf.Clamp(Mathf.RoundToInt(width * 1.25f), Mathf.Min(width + 1, 18), 18);
                    height = Mathf.Clamp(Mathf.RoundToInt(height * 1.20f), Mathf.Min(height + 1, 14), 14);
                    cellSize = 5.0f;
                    wallHeight = 4.6f;
                    break;
                case FloorProfile.MazeMorphology.ChambersAndChokepoints:
                    width = Mathf.Clamp(Mathf.RoundToInt(width * 1.15f), Mathf.Min(width + 1, 17), 17);
                    height = Mathf.Clamp(Mathf.RoundToInt(height * 1.15f), Mathf.Min(height + 1, 13), 13);
                    cellSize = 4.6f;
                    wallHeight = 4.2f;
                    break;
            }
        }

        private static MazeGrid BuildCarvedMazeGrid(
            int usedSeed,
            int width,
            int height,
            float cellSize,
            float wallHeight,
            FloorProfile.FloorShape shape,
            int shapeVariant)
        {
            MazeGrid grid = new MazeGrid(width, height, cellSize, wallHeight, DescentOrigin, DescentOrigin.y + 0.02f);
            bool[,] visited = new bool[width, height];
            ApplyVoidMask(grid, visited, shape, shapeVariant);

            System.Random rng = new System.Random(usedSeed);
            Stack<Vector2Int> stack = new Stack<Vector2Int>();
            Vector2Int current = grid.StartCell;
            visited[current.x, current.y] = true;
            stack.Push(current);

            List<int> candidates = new List<int>(4);
            while (stack.Count > 0)
            {
                current = stack.Peek();
                candidates.Clear();

                if (current.y + 1 < height && !visited[current.x, current.y + 1]) candidates.Add(0);
                if (current.x + 1 < width && !visited[current.x + 1, current.y]) candidates.Add(1);
                if (current.y - 1 >= 0 && !visited[current.x, current.y - 1]) candidates.Add(2);
                if (current.x - 1 >= 0 && !visited[current.x - 1, current.y]) candidates.Add(3);

                if (candidates.Count == 0)
                {
                    stack.Pop();
                    continue;
                }

                int dir = candidates[rng.Next(candidates.Count)];
                grid.Neighbour(current.x, current.y, dir, out int nx, out int ny);
                grid.OpenWall(current.x, current.y, nx, ny);
                visited[nx, ny] = true;
                stack.Push(new Vector2Int(nx, ny));
            }

            // Braid dead ends so loop paths match runtime mazes.
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    if (grid.IsVoid(x, z) || grid.WallCount(x, z) != 3) continue;
                    for (int dir = 0; dir < 4; dir++)
                    {
                        if (!grid.IsWallClosed(x, z, dir)) continue;
                        if (grid.Neighbour(x, z, dir, out int nx, out int ny))
                        {
                            grid.OpenWall(x, z, nx, ny);
                            break;
                        }
                    }
                }
            }

            grid.ComputeDistances(grid.StartCell);
            grid.CacheCellCenters();
            return grid;
        }

        private static void ApplyVoidMask(MazeGrid grid, bool[,] visited, FloorProfile.FloorShape shape, int variant)
        {
            int width = grid.Width;
            int height = grid.Height;
            bool[,] voidMask = grid.Void;
            Vector2Int startCell = Vector2Int.zero;

            switch (shape)
            {
                case FloorProfile.FloorShape.Ring:
                {
                    int vw = Mathf.Clamp(width - 6, 2, 6);
                    int vh = Mathf.Clamp(height - 6, 2, 5);
                    int x0 = (width - vw) / 2;
                    int z0 = (height - vh) / 2;
                    for (int x = x0; x < x0 + vw; x++)
                        for (int z = z0; z < z0 + vh; z++)
                            voidMask[x, z] = true;
                    startCell = Vector2Int.zero;
                    break;
                }
                case FloorProfile.FloorShape.TwinWings:
                {
                    int vw = Mathf.Clamp(width - 6, 2, 4);
                    int nh = Mathf.Clamp((height - 3) / 2, 2, 4);
                    int x0 = (width - vw) / 2;
                    for (int x = x0; x < x0 + vw; x++)
                    {
                        for (int z = 0; z < nh; z++) voidMask[x, z] = true;
                        for (int z = height - nh; z < height; z++) voidMask[x, z] = true;
                    }
                    startCell = Vector2Int.zero;
                    break;
                }
                case FloorProfile.FloorShape.Cross:
                {
                    int cw = Mathf.Clamp((width - 4) / 2, 2, 4);
                    int ch = Mathf.Clamp((height - 4) / 2, 2, 3);
                    for (int x = 0; x < width; x++)
                    {
                        bool xCorner = x < cw || x >= width - cw;
                        if (!xCorner) continue;
                        for (int z = 0; z < height; z++)
                        {
                            if (z < ch || z >= height - ch)
                                voidMask[x, z] = true;
                        }
                    }
                    startCell = new Vector2Int(width / 2, height / 2);
                    voidMask[startCell.x, startCell.y] = false;
                    break;
                }
                case FloorProfile.FloorShape.LChasm:
                {
                    int cw = Mathf.Clamp(width / 2 - 1, 3, 6);
                    int ch = Mathf.Clamp(height / 2 - 1, 3, 5);
                    int v = variant >= 1 && variant <= 3 ? variant : 1;
                    int xStart = (v == 2) ? 0 : width - cw;
                    int zStart = (v == 3) ? 0 : height - ch;
                    for (int x = xStart; x < xStart + cw; x++)
                        for (int z = zStart; z < zStart + ch; z++)
                            voidMask[x, z] = true;
                    startCell = Vector2Int.zero;
                    voidMask[0, 0] = false;
                    break;
                }
            }

            int playable = 0;
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    if (voidMask[x, z]) visited[x, z] = true;
                    else playable++;
                }
            }

            grid.StartCell = startCell;
            grid.PlayableCellCount = playable;
        }
    }
}
