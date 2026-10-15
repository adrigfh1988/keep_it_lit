using System.Collections.Generic;
using System.Text;
using StarterAssets;
using TMPro;
using UnityEngine;

/// <summary>
/// F105 C4, the maze side of the looping corridor (the runtime half is <see cref="LoopCorridor"/>). From floor 3, once per campaign, an
/// opening is cut in the maze's outer wall and an L-shaped corridor is built outside it. The entrance is chosen in Generate (its own +23
/// stream) so every placement pass sees it reserved; the corridor itself is built after the NavMesh bake and outside the bake volume, so the
/// hunter can never path into it. The opening is only missing geometry: the wall stays closed in the wall arrays, so no pass, path or distance
/// ever sees an exit from the maze. With the flag off, on floors before loopFromFloor, once a loop has been cleared, or on a floor without a
/// theme, nothing here draws from +23 and the maze is the one it was.
/// </summary>
public partial class MazeGenerator
{
    // The corridor is two legs of this many cells joined by a corner cell, each leg followed by a cell that looks the same: the corner,
    // and a cell with a stub that makes the end of the second leg look like the corner at the end of the first. Before the first leg is a
    // vestibule behind the opening, with its own stub, so once the opening is walled up the start of the first leg looks like the corner too
    // (seen from the second leg) and the corridor can be walked back seamlessly.
    private const int LoopLegCells = 3;
    private const int LoopSalt = 23;
    // Chain order: the vestibule's stub, the vestibule, the first leg, the corner, the second leg, the cell at its end, and the stub beyond that cell.
    private const int LoopVestibuleIndex = 1;
    private const int LoopFirstLegIndex = LoopVestibuleIndex + 1;
    private const int LoopSecondLegIndex = LoopFirstLegIndex + LoopLegCells + 1;
    // The blood on the floor lies in the second leg's first cell: round the corner from the landing point, so it is never seen to appear.
    private const int LoopBloodIndex = LoopSecondLegIndex;
    // Lamp positions along a leg (in cells): the rear one is on the left wall, the front one on the right, both before the corner so both walls are closed.
    // The robot at the corner stands at the mouth of the corner cell, a metre or so past the front lamp, so the lamp's light is what shows it.
    private const float LoopRearLampCells = 0.5f;
    private const float LoopFrontLampCells = 2.8f;
    private const float LoopFigureCells = 3f;
    // F106: along the second leg (in cells): the open locker on the right wall, the handprint on the left, both behind the cut so they are never seen to appear.
    private const float LoopLockerCells = 1.2f;
    private const float LoopHandprintCells = 1.6f;
    private const float LoopHandprintHeight = 1.3f;
    private const float LoopLockerDoorDegrees = 100f;
    private const float LoopDecalWallGap = 0.04f;
    private const string HandprintDecalName = "BloodSmear";
    private const float LoopLampWallGap = 0.08f;
    private const float LoopFootprintHeight = 8f;
    private const float LoopSealPointHeight = 1.5f;
    private const float LoopProbeFloorGap = 0.1f;

    [Header("F105 C4 - the looping corridor")]
    [Tooltip("The first floor that can have the looping corridor")]
    [SerializeField, Min(1)] private int loopFromFloor = 3;
    [Tooltip("Chance per eligible floor. The loop appears on at most one floor of a campaign (set when that floor is cleared); 1 forces it for testing")]
    [SerializeField, Range(0f, 1f)] private float loopChance = 0.6f;
    [Tooltip("Testing: build the loop on any themed floor, skipping the first-floor rule, the once-per-campaign rule and the roll (the roll is still drawn, so the maze is unchanged). KEEP IT LIT > Debug > Go To Loop Entrance takes you there in Play")]
    [SerializeField] private bool debugForceLoop = false;
    [Tooltip("The entrance cell is at least this many steps (x + z) from the start cell, so the corridor is found deeper in the floor")]
    [SerializeField, Min(0)] private int loopMinStartDistance = 4;
    [Tooltip("How far along the second leg the player is carried back to the same place in the first, in cells from the start of the leg. The return line (walking back down the first leg) mirrors it: as far from the vestibule as this is from the end cell")]
    [SerializeField, Range(0.5f, 2.9f)] private float loopTriggerCells = 2f;
    [Tooltip("The maze's music and ambience level while the player is inside the corridor")]
    [SerializeField, Range(0f, 1f)] private float loopDuckGain = 0.3f;
    [Tooltip("Seconds the music and ambience take to go down or come back as the player enters or leaves")]
    [SerializeField, Min(0f)] private float loopDuckSeconds = 1.5f;
    [Tooltip("Volume of the buzz each of the corridor's lamps makes (it follows the lamp's light, so a dead lamp is silent)")]
    [SerializeField, Range(0f, 1f)] private float loopBuzzVolume = 0.3f;
    [Tooltip("The buzz is heard up to this far from its lamp (m)")]
    [SerializeField, Min(2f)] private float loopBuzzDistance = 9f;
    [Tooltip("The robot at the corner (anomaly 5): seconds after the cut before it is standing there")]
    [SerializeField, Min(0f)] private float loopFigureDelay = 0.8f;
    [Tooltip("The robot at the corner: it is gone when the player is this close to it (m)")]
    [SerializeField, Min(0f)] private float loopFigureVanishDistance = 1.5f;
    [Tooltip("The robot at the corner: height of the point on it the torch beam is tested against (m)")]
    [SerializeField, Min(0f)] private float loopFigureTestHeight = 1.2f;
    [Tooltip("The robot at the corner: the beam reaches it within this fraction of its range")]
    [SerializeField, Range(0.1f, 1f)] private float loopFigureBeamRange = 1f;
    [Tooltip("The robot at the corner: the beam reaches it within this fraction of its cone")]
    [SerializeField, Range(0.1f, 1f)] private float loopFigureBeamAngle = 0.6f;
    [Tooltip("Every lamp flickering (anomaly 7): seconds between bursts of blinks (min, max)")]
    [SerializeField] private Vector2 loopFlickerGap = new Vector2(0.8f, 2.5f);
    [Tooltip("The opening is not walled up while the player is looking at it from closer than this (m)")]
    [SerializeField, Min(0f)] private float loopSealHideDistance = 14f;

    [Header("F106 - the loop puzzle (spot the anomaly)")]
    [Tooltip("Right decisions in a row that free the player (the chalk numeral counts up to this)")]
    [SerializeField, Min(1)] private int loopTargetCount = 3;
    [Tooltip("What solving the loop gives besides the whole floor map: each item the player has room for (a full hand skips it). SpareBattery, StarCompass and SecondWind go to the inventory; BottlePack and the modifiers as a DESCENT cache gives them")]
    [SerializeField] private ShopItem[] loopRewardItems = { ShopItem.SpareBattery, ShopItem.StarCompass, ShopItem.BottlePack, ShopItem.SecondWind };
    [Tooltip("Solving the loop also recharges the torch to full")]
    [SerializeField] private bool loopRewardRefillsTorch = true;
    [Tooltip("Chance that a lap after the trap closes has an anomaly (otherwise it is normal)")]
    [SerializeField, Range(0f, 1f)] private float loopAnomalyChance = 0.5f;
    [Tooltip("With fewer anomalies than this available (a theme without a locker or blood decal, no robot template), the old fixed passes run instead of the puzzle")]
    [SerializeField, Min(1)] private int loopMinAnomalies = 3;
    [Tooltip("Seconds the rule text stays up when the trap closes")]
    [SerializeField, Min(1f)] private float loopRuleSeconds = 4f;
    [Tooltip("Volume of the soft chime for a right decision")]
    [SerializeField, Range(0f, 1f)] private float loopChimeVolume = 0.35f;
    [Tooltip("Volume of the thud for the trap closing and for a wrong decision")]
    [SerializeField, Range(0f, 1f)] private float loopThudVolume = 0.8f;
    [Tooltip("The colour of the lamp in the 'one lamp is red' anomaly")]
    [SerializeField] private Color loopRedLampColor = new Color(1f, 0.08f, 0.05f);
    [Tooltip("The chalk numeral's distance from the opening along the first leg, in cells")]
    [SerializeField, Range(0.5f, 2.8f)] private float loopCounterCells = 1.5f;
    [Tooltip("The chalk numeral's height above the floor (m)")]
    [SerializeField, Min(0.5f)] private float loopCounterHeight = 1.5f;
    [Tooltip("The chalk numeral's font size (TextMeshPro world text: about a tenth of a metre per point)")]
    [SerializeField, Min(1f)] private float loopCounterFontSize = 6f;
    [Tooltip("The chalk count's font and its lit text material (Reenie Beanie from F109). Empty = the UI font, unlit")]
    [SerializeField] private TMP_FontAsset loopCounterFont;
    [SerializeField] private Material loopCounterTextMaterial;
    [Tooltip("The rules notice taped to the first leg's left wall: its paper (Sign_Paper), font and lit text material (Barlow from F109). Empty paper = text on the wall")]
    [SerializeField] private Material loopNoticePaper;
    [SerializeField] private TMP_FontAsset loopNoticeFont;
    [SerializeField] private Material loopNoticeTextMaterial;
    [Tooltip("The rules notice's distance from the opening along the first leg, in cells, and its centre height (m)")]
    [SerializeField, Range(0.3f, 2.8f)] private float loopNoticeCells = 0.8f;
    [SerializeField, Min(0.5f)] private float loopNoticeHeight = 1.45f;
    [Tooltip("The hanging light-box sign over each leg that answers every decision (verdict, count, the rule): its glowing panel (Sign_LightPanel) and the metal of its frame and chains (Sign_Steel). Empty panel = no sign")]
    [SerializeField] private Material loopSignPanel;
    [SerializeField] private Material loopSignFrame;
    [Tooltip("The hanging sign's distance from the start of each leg, in cells (the player lands at loopTriggerCells after a cut and faces it)")]
    [SerializeField, Range(0.5f, 2.9f)] private float loopSignCells = 2.6f;
    [Tooltip("Footsteps-behind anomaly: how far behind the player the steps are (m)")]
    [SerializeField, Min(1f)] private float loopFootstepsBehind = 3f;
    [Tooltip("Footsteps-behind anomaly: how long they follow (s)")]
    [SerializeField, Min(1f)] private float loopFootstepsSeconds = 4f;
    [Tooltip("Footsteps-behind anomaly: the distance walked per step (m)")]
    [SerializeField, Min(0.5f)] private float loopFootstepsStride = 1.4f;
    [Tooltip("Footsteps-behind anomaly: the player's own step clips are pitched down to this range, like the hunter's")]
    [SerializeField] private Vector2 loopFootstepsPitch = new Vector2(0.55f, 0.7f);
    [Tooltip("Footsteps-behind anomaly: volume of each step")]
    [SerializeField, Range(0f, 1f)] private float loopFootstepsVolume = 0.7f;
    [Tooltip("The Visitor starts this far behind the player along the corridor path when the trap closes (m)")]
    [SerializeField, Min(2f)] private float visitorStartDistance = 14f;
    [Tooltip("How fast the Visitor closes in while nobody is looking at it (m/s). A lap is about 27 m, so 0.3 loses about 4 m a lap to a player who never looks back")]
    [SerializeField, Min(0f)] private float visitorCreepSpeed = 0.3f;
    [Tooltip("A wrong decision brings the Visitor this much closer (m)")]
    [SerializeField, Min(0f)] private float visitorMistakeStep = 4f;
    [Tooltip("A right decision pushes the Visitor this much back (m), never past its start distance")]
    [SerializeField, Min(0f)] private float visitorReliefStep = 1f;
    [Tooltip("At this distance the Visitor has the player (m)")]
    [SerializeField, Min(0.5f)] private float visitorCatchDistance = 1.2f;
    [Tooltip("Height of the point on the Visitor that is tested for being in view (m)")]
    [SerializeField, Min(0.1f)] private float visitorChestHeight = 1.2f;
    [Tooltip("Height of the Visitor's head, which the camera eases onto at the catch (m)")]
    [SerializeField, Min(0.5f)] private float visitorHeadHeight = 1.7f;
    [Tooltip("The Visitor counts as seen this far outside the edge of the view, as a fraction of the screen")]
    [SerializeField, Range(0f, 0.3f)] private float visitorViewMargin = 0.05f;
    [Tooltip("Within this distance behind the player the Visitor's creeping can be heard (m)")]
    [SerializeField, Min(1f)] private float visitorStepRange = 6f;
    [Tooltip("One faint step per this much creeping (m)")]
    [SerializeField, Min(0.1f)] private float visitorStepStride = 1.5f;
    [Tooltip("Volume of the Visitor's steps")]
    [SerializeField, Range(0f, 1f)] private float visitorStepVolume = 0.35f;
    [Tooltip("Pitch of the Visitor's steps")]
    [SerializeField, Range(0.3f, 1.5f)] private float visitorStepPitch = 0.6f;

    private bool _loopActive;
    private Vector2Int _loopEntrance;
    private int _loopOut;
    private int _loopTurn;
    private System.Random _loopRng;
    private LoopCorridor _loop;

    /// <summary>True when this floor has the looping corridor.</summary>
    public bool HasLoopCorridor => _loopActive;

    /// <summary>F105 C4: the looping corridor's runtime half, or null.</summary>
    public LoopCorridor Loop => _loop;

    /// <summary>True when the outer wall of cell (x, z) on side `dir` is the one the loop's opening replaces, so the wall is not built there.</summary>
    private bool IsLoopOpening(int x, int z, int dir)
    {
        return _loopActive && _loopEntrance.x == x && _loopEntrance.y == z && _loopOut == dir;
    }

    /// <summary>A cell the placement passes keep clear: the squeeze (F105 C3) and the loop's entrance (C4).</summary>
    private bool IsLayoutReserved(Vector2Int cell)
    {
        return _squeezeCells.Contains(cell) || (_loopActive && cell == _loopEntrance) || IsGateCell(cell);
    }

    // ---------------------------------------------------------------- choosing the entrance (+23)

    /// <summary>
    /// F105 C4, called from Generate after the layout is final. Rolls loopChance, then walks the maze's edge cells in a seeded order for one that
    /// is not the start, the hunter's spawn, a room, a gallery, a squeeze or a bait dead end (a kept dead end must keep its three walls), and
    /// whose corridor has room outside the maze - nothing of the scene's in the way. Stars are placed later, so the cell is reserved instead of
    /// tested: every pass that places something skips it.
    /// </summary>
    private void ChooseLoop(int usedSeed)
    {
        _loopActive = false;
        _loopRng = null;
        if (!terror.loopCorridor || _theme == null || _profile == null || (!debugForceLoop && !LoopAllowedHere())) return; // F107: a descent has its own spacing rule

        System.Random rng = new System.Random(usedSeed + LoopSalt);
        if (rng.NextDouble() >= loopChance && !debugForceLoop)
        {
            terror.Log("No loop corridor on this floor (the roll)", this);
            return;
        }

        HashSet<Vector2Int> avoid = DeadEndCells();
        avoid.UnionWith(_roomCells);
        avoid.UnionWith(_galleryCells);
        avoid.UnionWith(_squeezeCells);
        avoid.Add(Vector2Int.zero);
        avoid.Add(FarthestCell());

        List<(Vector2Int cell, int dir)> edges = new List<(Vector2Int, int)>();
        for (int x = 0; x < width; x++)
        {
            edges.Add((new Vector2Int(x, 0), 2));
            edges.Add((new Vector2Int(x, height - 1), 0));
        }

        for (int z = 0; z < height; z++)
        {
            edges.Add((new Vector2Int(0, z), 3));
            edges.Add((new Vector2Int(width - 1, z), 1));
        }

        for (int i = edges.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (edges[i], edges[j]) = (edges[j], edges[i]);
        }

        foreach ((Vector2Int cell, int dir) edge in edges)
        {
            if (avoid.Contains(edge.cell) || !IsWallClosed(edge.cell.x, edge.cell.y, edge.dir)) continue;
            // Never beside the start: the corridor is a discovery deeper in the floor, not the first thing in view.
            if (edge.cell.x + edge.cell.y < loopMinStartDistance) continue;

            int firstTurn = rng.Next(2) == 0 ? 1 : 3; // a right or a left turn first; the other if there is no room
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int turn = attempt == 0 ? firstTurn : 4 - firstTurn;
                int turnDir = (edge.dir + turn) % 4;
                if (!LoopHasRoom(edge.cell, edge.dir, turnDir)) continue;

                _loopActive = true;
                _loopEntrance = edge.cell;
                _loopOut = edge.dir;
                _loopTurn = turnDir;
                _loopRng = rng;
                terror.Log($"Loop corridor entrance at {edge.cell}, outward {edge.dir}, turning {turnDir}", this);
                return;
            }
        }

        terror.Log("No loop corridor: no edge cell has room for it", this);
    }

    /// <summary>
    /// The corridor's cells in order: the vestibule's stub, the vestibule (behind the opening), the first leg, the corner, the second leg, the
    /// cell at its end, and the stub beyond that cell. Both stubs turn the way that makes their cell read as the corner: the end cell seen
    /// walking the second leg forward, the vestibule seen walking the first leg back.
    /// </summary>
    private static List<Vector2Int> LoopChain(Vector2Int entrance, int outDir, int turnDir)
    {
        Vector2Int outward = Neighbour(Vector2Int.zero, outDir);
        Vector2Int turn = Neighbour(Vector2Int.zero, turnDir);
        Vector2Int vestibule = entrance + outward;
        Vector2Int corner = vestibule + outward * (LoopLegCells + 1);
        Vector2Int end = corner + turn * (LoopLegCells + 1);

        List<Vector2Int> chain = new List<Vector2Int>(2 * LoopLegCells + 5);
        chain.Add(vestibule + turn);
        chain.Add(vestibule);
        for (int k = 1; k <= LoopLegCells; k++) chain.Add(vestibule + outward * k);
        chain.Add(corner);
        for (int k = 1; k <= LoopLegCells; k++) chain.Add(corner + turn * k);
        chain.Add(end);
        chain.Add(end - outward);
        return chain;
    }

    /// <summary>The corridor's footprint, world space, floor to ceiling.</summary>
    private Bounds LoopBounds(List<Vector2Int> chain)
    {
        Bounds bounds = new Bounds(CellCenter(chain[0].x, chain[0].y), Vector3.zero);
        foreach (Vector2Int cell in chain)
        {
            bounds.Encapsulate(CellCenter(cell.x, cell.y) + new Vector3(cellSize, 0f, cellSize) * 0.5f);
            bounds.Encapsulate(CellCenter(cell.x, cell.y) - new Vector3(cellSize, 0f, cellSize) * 0.5f);
        }

        return new Bounds(new Vector3(bounds.center.x, FloorTop + LoopFootprintHeight * 0.5f, bounds.center.z), new Vector3(bounds.size.x, LoopFootprintHeight, bounds.size.z));
    }

    /// <summary>True when nothing in the scene stands where the corridor would. The maze is not built yet, and the corridor lies wholly outside it.</summary>
    private bool LoopHasRoom(Vector2Int entrance, int outDir, int turnDir)
    {
        Bounds footprint = LoopBounds(LoopChain(entrance, outDir, turnDir));
        Vector3 halfExtents = new Vector3(footprint.extents.x, wallHeight * 0.5f - LoopProbeFloorGap, footprint.extents.z);
        Vector3 center = new Vector3(footprint.center.x, FloorTop + LoopProbeFloorGap + halfExtents.y, footprint.center.z);
        return !Physics.CheckBox(center, halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
    }

    /// <summary>Pre-bake, from Awake after BuildSetPieces (which resets _setPieceCells): the entrance cell takes no props or decals.</summary>
    private void ReserveLoopEntrance()
    {
        if (_loopActive) _setPieceCells.Add(_loopEntrance);
    }

    // ---------------------------------------------------------------- the corridor (post-bake)

    /// <summary>
    /// Builds the corridor from the theme's own floor, ceiling, wall and pillar pieces and its lamp, under one `LoopCorridor` group: nothing
    /// of it joins the maze's wall, lamp or pillar lists. Post-bake and outside the bake volume, so no NavMesh exists in it.
    /// </summary>
    private void BuildLoopCorridor()
    {
        if (!_loopActive) return;

        List<Vector2Int> chain = LoopChain(_loopEntrance, _loopOut, _loopTurn);
        Transform root = Group("LoopCorridor");

        BuildLoopRooms(chain, root);
        BuildLoopWalls(chain, root);

        Vector3 outward = DirVector(_loopOut);
        Vector3 turn = DirVector(_loopTurn);
        Transform legA = MakeLoopFrame("LegA", CellCenter(chain[LoopFirstLegIndex].x, chain[LoopFirstLegIndex].y) - outward * (cellSize * 0.5f), outward, root);
        Transform legB = MakeLoopFrame("LegB", CellCenter(chain[LoopSecondLegIndex].x, chain[LoopSecondLegIndex].y) - turn * (cellSize * 0.5f), turn, root);

        WallLamp rearA = MakeLoopLamp("LoopLamp_A_Rear", legA, LoopRearLampCells, -1f, root);
        WallLamp frontA = MakeLoopLamp("LoopLamp_A_Front", legA, LoopFrontLampCells, 1f, root);
        WallLamp[] mirror = { MakeLoopLamp("LoopLamp_B_Rear", legB, LoopRearLampCells, -1f, root), MakeLoopLamp("LoopLamp_B_Front", legB, LoopFrontLampCells, 1f, root) };
        // The first leg's front lamp glints through the corner's mouth seen from the second leg; its twin in the vestibule's stub glints the same
        // way seen from the first leg walking back. Third in the list: the anomalies only ever touch the rear lamp.
        WallLamp[] lamps = { rearA, frontA,TwinLampInLeg(frontA, legB, legA, "LoopLamp_Vestibule", root) };

        Vector3 openingCentre = BoundaryWallPosition(_loopEntrance, _loopOut);
        GameObject sealWall = SpawnThemedWall("LoopSeal", openingCentre, BoundaryWallRotation(_loopOut), cellSize + wallThickness, wallHeight, wallThickness, root).gameObject;
        sealWall.SetActive(false);

        // The anomaly pieces, in the second leg for a lap walked forward and twinned at the same places in the first for one walked back.
        Vector2Int bloodCell = chain[LoopBloodIndex];
        PropPiece blood = PlaceBloodPool(CellCenter(bloodCell.x, bloodCell.y), bloodCell, LoopSalt, root, "Decal_LoopBlood");
        if (blood != null) blood.gameObject.SetActive(false);
        else terror.Log("The loop corridor has no blood decal: this theme lists no " + BloodPoolName + " floor decal", this);

        GameObject locker = BuildLoopLocker(legB, root);
        GameObject handprint = BuildLoopHandprint(chain[LoopSecondLegIndex], legB, root);
        GameObject bloodReverse = TwinInLeg(blood != null ? blood.gameObject : null, legB, legA, root);
        GameObject lockerReverse = TwinInLeg(locker, legB, legA, root);
        GameObject handprintReverse = TwinInLeg(handprint, legB, legA, root);

        // The count and the rules on both legs' walls, and both faces of both hanging signs, so each leg reads the same walked either way.
        TMP_Text[] counters = { BuildLoopCounter(legA, root), BuildLoopCounter(legB, root) };
        BuildLoopNotice(legA, root);
        BuildLoopNotice(legB, root);
        List<TMP_Text> verdicts = new List<TMP_Text>(), counts = new List<TMP_Text>();
        BuildLoopSign(legA, root, verdicts, counts);
        BuildLoopSign(legB, root, verdicts, counts);

        Vector3 backward = -outward;
        LoopCorridor.Setup setup = new LoopCorridor.Setup
        {
            legA = legA,
            legB = legB,
            triggerDistance = loopTriggerCells * cellSize,
            legLength = (LoopLegCells + 1) * cellSize,
            legHalfWidth = (cellSize - wallThickness) * 0.5f,
            trapDistance = LoopLegCells * cellSize,
            // The trigger is (LoopLegCells + 0.5 - loopTriggerCells) cells short of the end cell's centre; the return line is as far past the vestibule's.
            returnDistance = Mathf.Max(LoopLegCells - loopTriggerCells, 0.3f) * cellSize,
            cornerPathDistance = (LoopLegCells + 0.5f) * cellSize,
            footprint = LoopBounds(chain),
            exitPosition = CellCenter(_loopEntrance.x, _loopEntrance.y),
            exitYaw = Mathf.Atan2(backward.x, backward.z) * Mathf.Rad2Deg,
            lamps = lamps,
            mirrorLamps = mirror,
            blood = blood != null ? blood.gameObject : null,
            locker = locker,
            handprint = handprint,
            bloodReverse = bloodReverse,
            lockerReverse = lockerReverse,
            handprintReverse = handprintReverse,
            counters = counters,
            signVerdicts = verdicts.Count > 0 ? verdicts.ToArray() : null,
            signCounts = counts.Count > 0 ? counts.ToArray() : null,
            sealWall = sealWall,
            sealPoint = openingCentre + Vector3.up * LoopSealPointHeight,
            figurePosition = legA.position + legA.forward * (LoopFigureCells * cellSize),
            figureForward = outward,
            // Walked back, the corner ahead is the mouth of the corner seen from the second leg, as far from the lander as the forward one.
            figurePositionReverse = legB.position + legB.forward * ((LoopFigureCells - LoopLegCells) * cellSize),
            figureForwardReverse = -turn,
            terror = terror,
            entranceCell = _loopEntrance,
            tuning = new LoopCorridor.Tuning
            {
                duckGain = loopDuckGain,
                duckSeconds = loopDuckSeconds,
                buzzVolume = loopBuzzVolume,
                buzzMaxDistance = loopBuzzDistance,
                figureDelay = loopFigureDelay,
                figureVanishDistance = loopFigureVanishDistance,
                figureTestHeight = loopFigureTestHeight,
                figureBeamRangeFraction = loopFigureBeamRange,
                figureBeamAngleFraction = loopFigureBeamAngle,
                flickerGap = loopFlickerGap,
                sealHideDistance = loopSealHideDistance
            },
            puzzle = new LoopCorridor.PuzzleTuning
            {
                targetCount = loopTargetCount,
                rewardItems = loopRewardItems,
                rewardRefillsTorch = loopRewardRefillsTorch,
                minAnomalies = loopMinAnomalies,
                anomalyChance = loopAnomalyChance,
                ruleSeconds = loopRuleSeconds,
                chimeVolume = loopChimeVolume,
                thudVolume = loopThudVolume,
                redLamp = loopRedLampColor,
                footstepsBehind = loopFootstepsBehind,
                footstepsSeconds = loopFootstepsSeconds,
                footstepsStride = loopFootstepsStride,
                footstepsVolume = loopFootstepsVolume,
                footstepsMaxDistance = loopBuzzDistance,
                footstepsPitch = loopFootstepsPitch,
                visitorStartDistance = visitorStartDistance,
                visitorCreepSpeed = visitorCreepSpeed,
                visitorMistakeStep = visitorMistakeStep,
                visitorReliefStep = visitorReliefStep,
                visitorCatchDistance = visitorCatchDistance,
                visitorChestHeight = visitorChestHeight,
                visitorHeadHeight = visitorHeadHeight,
                visitorViewMargin = visitorViewMargin,
                visitorStepRange = visitorStepRange,
                visitorStepStride = visitorStepStride,
                visitorStepVolume = visitorStepVolume,
                visitorStepPitch = visitorStepPitch
            }
        };

        _loop = root.gameObject.AddComponent<LoopCorridor>();
        _loop.Define(setup);
        terror.Log($"Loop corridor built at {_loopEntrance}: {chain.Count} cells, footprint {setup.footprint.size.x:0.0} x {setup.footprint.size.z:0.0} m", this);
    }

    /// <summary>A floor tile and a ceiling tile per cell, as the maze lays them.</summary>
    private void BuildLoopRooms(List<Vector2Int> chain, Transform root)
    {
        foreach (Vector2Int cell in chain)
        {
            Vector3 centre = CellCenter(cell.x, cell.y);
            GameObject floor = Instantiate(_theme.FloorTile, centre, Quaternion.identity, root);
            floor.SetActive(true);
            floor.name = $"LoopFloor_{cell.x}_{cell.y}";
            floor.transform.localScale = new Vector3(cellSize / 4.5f, 1f, cellSize / 4.5f);

            GameObject ceiling = Instantiate(_theme.CeilingTile, centre + Vector3.up * wallHeight, Quaternion.identity, root);
            ceiling.SetActive(true);
            ceiling.name = $"LoopCeiling_{cell.x}_{cell.y}";
            ceiling.transform.localScale = new Vector3(cellSize / 4.5f, 1f, cellSize / 4.5f);
        }
    }

    /// <summary>
    /// A wall on every side of every cell except where the corridor continues (the cell before and after in the chain) and the opening into the
    /// maze, each shared wall once, and a pillar at each corner and junction outside the maze's own grid (the maze's pillars cover the rest).
    /// </summary>
    private void BuildLoopWalls(List<Vector2Int> chain, Transform root)
    {
        HashSet<Vector3Int> edges = new HashSet<Vector3Int>();
        for (int i = 0; i < chain.Count; i++)
        {
            for (int dir = 0; dir < 4; dir++)
            {
                Vector2Int next = Neighbour(chain[i], dir);
                bool continues = (i > 0 && next == chain[i - 1]) || (i + 1 < chain.Count && next == chain[i + 1]) || (i == LoopVestibuleIndex && next == _loopEntrance);
                Vector3Int edge = LoopEdgeKey(chain[i], dir);
                if (!continues && !IsMazeBoundaryEdge(edge)) edges.Add(edge); // the vestibule's stub runs along the maze's outer wall, which is already there
            }
        }

        Dictionary<Vector2Int, int> vertexSides = new Dictionary<Vector2Int, int>();
        foreach (Vector3Int edge in edges)
        {
            bool horizontal = edge.z == 0;
            SpawnThemedWall($"LoopWall_{edge.x}_{edge.y}_{(horizontal ? 'H' : 'V')}", LoopEdgePosition(edge), horizontal ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f),
                cellSize + wallThickness, wallHeight, wallThickness, root);

            // Which of north (1), east (2), south (4), west (8) leave each end of the wall.
            Vector2Int first = new Vector2Int(edge.x, edge.y);
            Vector2Int second = horizontal ? new Vector2Int(edge.x + 1, edge.y) : new Vector2Int(edge.x, edge.y + 1);
            vertexSides[first] = (vertexSides.TryGetValue(first, out int a) ? a : 0) | (horizontal ? 2 : 1);
            vertexSides[second] = (vertexSides.TryGetValue(second, out int b) ? b : 0) | (horizontal ? 8 : 4);
        }

        const int straightEastWest = 2 | 8;
        const int straightNorthSouth = 1 | 4;
        foreach (KeyValuePair<Vector2Int, int> vertex in vertexSides)
        {
            bool onMazeGrid = vertex.Key.x >= 0 && vertex.Key.x <= width && vertex.Key.y >= 0 && vertex.Key.y <= height;
            if (onMazeGrid || vertex.Value == straightEastWest || vertex.Value == straightNorthSouth) continue;

            SpawnPillar(vertex.Key.x, vertex.Key.y, root);
        }
    }

    /// <summary>
    /// A wall edge as (x, z, axis): axis 0 is the horizontal (east-west) wall along the south side of cell (x, z) - or the north side of
    /// (x, z - 1) - and axis 1 the vertical one along the west side of (x, z). Both cells that share a wall give it the same key.
    /// </summary>
    private static Vector3Int LoopEdgeKey(Vector2Int cell, int dir)
    {
        switch (dir)
        {
            case 0: return new Vector3Int(cell.x, cell.y + 1, 0);
            case 1: return new Vector3Int(cell.x + 1, cell.y, 1);
            case 2: return new Vector3Int(cell.x, cell.y, 0);
            default: return new Vector3Int(cell.x, cell.y, 1);
        }
    }

    /// <summary>True for an edge (as <see cref="LoopEdgeKey"/>) of the maze's own outer wall: BuildThemedGeometry builds those.</summary>
    private bool IsMazeBoundaryEdge(Vector3Int edge)
    {
        return edge.z == 0
            ? (edge.y == 0 || edge.y == height) && edge.x >= 0 && edge.x < width
            : (edge.x == 0 || edge.x == width) && edge.y >= 0 && edge.y < height;
    }

    private Vector3 LoopEdgePosition(Vector3Int edge)
    {
        return edge.z == 0
            ? new Vector3(origin.x + (edge.x + 0.5f) * cellSize, FloorTop, origin.z + edge.y * cellSize)
            : new Vector3(origin.x + edge.x * cellSize, FloorTop, origin.z + (edge.y + 0.5f) * cellSize);
    }

    /// <summary>Where the maze's outer wall on side `dir` of `cell` stands (its floor-level centre), as BuildThemedGeometry places it.</summary>
    private Vector3 BoundaryWallPosition(Vector2Int cell, int dir)
    {
        return LoopEdgePosition(LoopEdgeKey(cell, dir));
    }

    private static Quaternion BoundaryWallRotation(int dir)
    {
        return dir == 0 || dir == 2 ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
    }

    private static Transform MakeLoopFrame(string frameName, Vector3 floorPosition, Vector3 forward, Transform parent)
    {
        Transform frame = new GameObject(frameName).transform;
        frame.SetParent(parent, true);
        frame.SetPositionAndRotation(floorPosition, Quaternion.LookRotation(forward, Vector3.up));
        return frame;
    }

    /// <summary>A lamp on a leg's wall, `cells` along the leg, on the left (-1) or right (+1) side as seen walking it; the second leg gets the same spot, so the two read alike.</summary>
    private WallLamp MakeLoopLamp(string lampName, Transform leg, float cells, float side, Transform parent)
    {
        Vector3 lateral = leg.right * side;
        float wallOffset = cellSize * 0.5f - wallThickness * 0.5f - LoopLampWallGap;
        Vector3 position = leg.position + leg.forward * (cells * cellSize) + lateral * wallOffset + Vector3.up * lampHeight;
        return CreateThemedLamp(lampName, position, Quaternion.LookRotation(-lateral, Vector3.up), parent, false, _loopRng.Next(1000), false);
    }

    /// <summary>A lamp at the same place and turn in leg `to` as `source` has in leg `from` (as <see cref="TwinInLeg"/>, but a working lamp).</summary>
    private WallLamp TwinLampInLeg(WallLamp source, Transform from, Transform to, string lampName, Transform parent)
    {
        Vector3 position = to.TransformPoint(from.InverseTransformPoint(source.transform.position));
        Quaternion rotation = to.rotation * Quaternion.Inverse(from.rotation) * source.transform.rotation;
        return CreateThemedLamp(lampName, position, rotation, parent, false, _loopRng.Next(1000), false);
    }

    /// <summary>
    /// F106 anomaly 4: a clone of the theme's locker against the second leg's right wall with its door swung open. Scenery only - no trigger, no
    /// collider, no Locker behaviour - and inactive until its anomaly applies. Null when the theme has no locker (that anomaly is then left out).
    /// </summary>
    private GameObject BuildLoopLocker(Transform leg, Transform parent)
    {
        if (_theme == null || _theme.Locker == null) return null;

        Vector3 wallFace = leg.position + leg.forward * (LoopLockerCells * cellSize) + leg.right * (cellSize * 0.5f - wallThickness * 0.5f);
        Locker clone = Instantiate(_theme.Locker, wallFace, Quaternion.LookRotation(-leg.right, Vector3.up), parent);
        clone.name = "LoopLocker";
        if (clone.Door != null) clone.Door.localRotation *= Quaternion.Euler(0f, LoopLockerDoorDegrees, 0f);
        foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true)) Destroy(collider);

        GameObject locker = clone.gameObject;
        Destroy(clone);
        locker.SetActive(false);
        return locker;
    }

    /// <summary>F106 anomaly 8: the theme's blood smear, small, on the second leg's left wall at hand height. Inactive until its anomaly applies; null when the theme lists none.</summary>
    private GameObject BuildLoopHandprint(Vector2Int cell, Transform leg, Transform parent)
    {
        PropPiece decal = FindThemeDecal(PropPiece.MountKind.WallDecal, HandprintDecalName);
        if (decal == null) return null;

        Vector3 towardWall = -leg.right;
        Vector3 position = leg.position + leg.forward * (LoopHandprintCells * cellSize) + towardWall * (cellSize * 0.5f - wallThickness * 0.5f - LoopDecalWallGap) + Vector3.up * LoopHandprintHeight;
        PropPiece clone = Instantiate(decal, position, Quaternion.LookRotation(-towardWall, Vector3.up), parent);
        clone.name = "Decal_LoopHandprint";
        clone.transform.localScale = Vector3.one * decal.DecalSizeRange.x;
        ApplyDecalVariant(clone, cell.x, cell.y, LoopSalt, 0);
        clone.gameObject.SetActive(false);
        return clone.gameObject;
    }

    /// <summary>F106: the chalk numeral that counts the right decisions, world-space text on the first leg's right wall facing into the corridor. Inactive until the trap closes.</summary>
    private TMP_Text BuildLoopCounter(Transform leg, Transform parent)
    {
        Vector3 towardWall = leg.right;
        GameObject holder = new GameObject("LoopCounter");
        holder.transform.SetParent(parent, false);
        // A text mesh is seen from behind its forward, so forward points into the wall.
        holder.transform.SetPositionAndRotation(
            leg.position + leg.forward * (loopCounterCells * cellSize) + towardWall * (cellSize * 0.5f - wallThickness * 0.5f - LoopDecalWallGap) + Vector3.up * loopCounterHeight,
            Quaternion.LookRotation(towardWall, Vector3.up));

        TextMeshPro text = holder.AddComponent<TextMeshPro>();
        TMP_FontAsset font = loopCounterFont != null ? loopCounterFont : RuntimeUi.ResolveFont();
        if (font != null) text.font = font;
        if (loopCounterFont != null && loopCounterTextMaterial != null) text.fontSharedMaterial = loopCounterTextMaterial;
        text.text = $"0 / {loopTargetCount}";
        text.fontSize = loopCounterFontSize;
        text.fontStyle = loopCounterFont != null ? FontStyles.Normal : FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.45f, 0.04f, 0.03f);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(1f, 1f);
        holder.SetActive(false);
        return text;
    }

    /// <summary>
    /// The rules, always there to re-read: a printed notice taped to the first leg's left wall near the opening (the chalk count is on the right
    /// wall), lit text on paper. Part of the corridor, so the same on every lap - it is never an anomaly.
    /// </summary>
    private void BuildLoopNotice(Transform leg, Transform parent)
    {
        Vector3 towardWall = -leg.right;
        // Like the count: forward into the wall, the text read from the corridor side.
        GameObject holder = BuildPaperNotice(
            leg.position + leg.forward * (loopNoticeCells * cellSize) + towardWall * (cellSize * 0.5f - wallThickness * 0.5f - LoopDecalWallGap) + Vector3.up * loopNoticeHeight,
            Quaternion.LookRotation(towardWall, Vector3.up) * Quaternion.Euler(0f, 0f, -1.5f),
            parent, L10n.T("world.loop.rules", ("n", loopTargetCount)), new Vector2(0.95f, 0.72f), new Vector2(0.85f, 0.62f));
        holder.name = "LoopNotice";
    }

    /// <summary>
    /// F123: a printed paper notice (the F109 paper material, lit text, auto-sized) at a pose whose forward points INTO the wall, so the text reads from the corridor
    /// side. Extracted unchanged from BuildLoopNotice (the loop calls it with its old numbers); floor 5's house rules use it with a taller sheet.
    /// </summary>
    private GameObject BuildPaperNotice(Vector3 position, Quaternion rotation, Transform parent, string content, Vector2 paperScale, Vector2 textSize)
    {
        GameObject holder = new GameObject("PaperNotice");
        holder.transform.SetParent(parent, false);
        holder.transform.SetPositionAndRotation(position, rotation);

        if (loopNoticePaper != null)
        {
            GameObject paper = GameObject.CreatePrimitive(PrimitiveType.Quad); // a textured paper sheet, not decor primitive: the material is the F109 paper
            paper.name = "Paper";
            Destroy(paper.GetComponent<Collider>());
            paper.transform.SetParent(holder.transform, false);
            paper.transform.localPosition = new Vector3(0f, 0f, 0.006f);
            paper.transform.localScale = new Vector3(paperScale.x, paperScale.y, 1f);
            Renderer r = paper.GetComponent<Renderer>();
            r.sharedMaterial = loopNoticePaper;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(holder.transform, false);
        TextMeshPro text = textGo.AddComponent<TextMeshPro>();
        TMP_FontAsset font = loopNoticeFont != null ? loopNoticeFont : RuntimeUi.ResolveFont();
        if (font != null) text.font = font;
        if (loopNoticeFont != null && loopNoticeTextMaterial != null) text.fontSharedMaterial = loopNoticeTextMaterial;
        text.text = content;
        text.alignment = TextAlignmentOptions.Center;
        text.color = loopNoticePaper != null ? new Color(0.1f, 0.09f, 0.08f) : new Color(0.75f, 0.72f, 0.65f);
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.3f;
        text.fontSizeMax = 1.2f;
        text.lineSpacing = -10f;
        text.rectTransform.sizeDelta = textSize;
        return holder;
    }

    /// <summary>
    /// A light-box sign hanging from the ceiling over the leg: the verdict on the last decision (empty until the first), the count, and the rule
    /// in two lines, on both faces so a player walking either way reads it. Its panel glows on its own (no Light). One per leg, built the same,
    /// so the seamless cuts show the same sign in the same place. Adds each face's verdict and count lines to the lists (nothing without a panel
    /// material).
    /// </summary>
    private void BuildLoopSign(Transform leg, Transform parent, List<TMP_Text> verdicts, List<TMP_Text> counts)
    {
        if (loopSignPanel == null) return;

        const float width = 1.5f, height = 0.8f, depth = 0.06f;
        float bottom = Mathf.Min(2.1f, wallHeight - height - 0.25f);
        GameObject holder = new GameObject("LoopSign");
        holder.transform.SetParent(parent, false);
        holder.transform.SetPositionAndRotation(leg.position + leg.forward * (loopSignCells * cellSize) + Vector3.up * (bottom + height * 0.5f), leg.rotation);

        SignBox(holder.transform, "Panel", new Vector3(0f, 0f, 0f), new Vector3(width, height, depth), loopSignPanel);
        Material frame = loopSignFrame != null ? loopSignFrame : loopSignPanel;
        SignBox(holder.transform, "FrameTop", new Vector3(0f, height * 0.5f + 0.02f, 0f), new Vector3(width + 0.08f, 0.04f, depth + 0.02f), frame);
        SignBox(holder.transform, "FrameBottom", new Vector3(0f, -height * 0.5f - 0.02f, 0f), new Vector3(width + 0.08f, 0.04f, depth + 0.02f), frame);
        SignBox(holder.transform, "FrameLeft", new Vector3(-width * 0.5f - 0.02f, 0f, 0f), new Vector3(0.04f, height, depth + 0.02f), frame);
        SignBox(holder.transform, "FrameRight", new Vector3(width * 0.5f + 0.02f, 0f, 0f), new Vector3(0.04f, height, depth + 0.02f), frame);
        float chain = wallHeight - (bottom + height) - 0.02f;
        if (chain > 0.02f)
        {
            foreach (float side in new[] { -1f, 1f })
                SignBox(holder.transform, "Chain", new Vector3(side * width * 0.38f, height * 0.5f + 0.04f + chain * 0.5f, 0f), new Vector3(0.02f, chain, 0.02f), frame);
        }

        // Text is read from behind its forward: the face toward a player walking the leg forward has the leg's forward, the back face the opposite.
        foreach (bool back in new[] { false, true })
        {
            Transform face = new GameObject(back ? "BackFace" : "FrontFace").transform;
            face.SetParent(holder.transform, false);
            face.localPosition = new Vector3(0f, 0f, -depth * 0.5f - 0.004f);
            if (back)
            {
                face.localPosition = -face.localPosition;
                face.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }

            verdicts.Add(SignLine(face, "Verdict", new Vector3(0f, 0.25f, 0f), new Vector2(width - 0.12f, 0.2f), 1.6f, ""));
            counts.Add(SignLine(face, "Count", new Vector3(0f, 0.04f, 0f), new Vector2(width - 0.12f, 0.24f), 2.4f, $"0 / {loopTargetCount}"));
            SignLine(face, "Rule", new Vector3(0f, -0.24f, 0f), new Vector2(width - 0.12f, 0.26f), 1.0f,
                L10n.T("world.loop.continue") + "\n" + L10n.T("world.loop.turn"));
        }
    }

    /// <summary>A copy of `source` (an inactive anomaly piece) at the same place and turn in leg `to` as it has in leg `from`. Null for null.</summary>
    private static GameObject TwinInLeg(GameObject source, Transform from, Transform to, Transform parent)
    {
        if (source == null) return null;

        Vector3 position = to.TransformPoint(from.InverseTransformPoint(source.transform.position));
        Quaternion rotation = to.rotation * Quaternion.Inverse(from.rotation) * source.transform.rotation;
        GameObject twin = Instantiate(source, position, rotation, parent);
        twin.name = source.name + "_Reverse";
        twin.SetActive(false);
        return twin;
    }

    private void SignBox(Transform parent, string name, Vector3 local, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube); // a sign part in the F109 sign materials, not bare decor
        box.name = name;
        Destroy(box.GetComponent<Collider>());
        box.transform.SetParent(parent, false);
        box.transform.localPosition = local;
        box.transform.localScale = size;
        Renderer r = box.GetComponent<Renderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private TMP_Text SignLine(Transform parent, string name, Vector3 local, Vector2 size, float maxFontSize, string text)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        TextMeshPro line = go.AddComponent<TextMeshPro>();
        TMP_FontAsset font = loopNoticeFont != null ? loopNoticeFont : RuntimeUi.ResolveFont();
        if (font != null) line.font = font;
        // Plain ink on the glowing panel: the font's own unlit material, so the verdict's green and red read in the dark.
        line.text = text;
        line.alignment = TextAlignmentOptions.Center;
        line.color = new Color(0.08f, 0.07f, 0.06f);
        line.enableAutoSizing = true;
        line.fontSizeMin = 0.2f;
        line.fontSizeMax = maxFontSize;
        line.rectTransform.sizeDelta = size;
        return line;
    }

    /// <summary>Hands the corridor the player's controller, camera, torch, the sound it ducks, the hunter whose body the Visitor copies, the outcome that ends the run, the HUD, the floor map and the throw controller (the reward's bottles), once SetUpAtmosphere has them.</summary>
    private void BindLoop(Transform player, ThirdPersonController controller, Camera camera, Flashlight flashlight, HorrorAudioDirector director, MazeAmbience ambience,
        GameOutcome outcome, PlayerHud hud, FloorMapUi map, ThrowController throws)
    {
        if (_loop != null) _loop.Bind(player, controller, camera, flashlight, director, ambience, StalkerTemplate, aiFollower, outcome, hud, map, throws);
    }

    // ---------------------------------------------------------------- debug map extras

    /// <summary>Marks the loop's entrance cell L, whatever else stands in it.</summary>
    private void AddLoopMark(string[,] marks)
    {
        if (_loopActive) marks[_loopEntrance.x, _loopEntrance.y] += "L";
    }

    private static readonly string[] EdgeNames = { "north", "east", "south", "west" };

    /// <summary>A legend line for the loop: where its opening is, which way the corridor turns, and how far the player has got.</summary>
    private void AppendLoopLegend(StringBuilder sb)
    {
        if (!_loopActive)
        {
            sb.Append("L loop corridor: none on this floor\n");
            return;
        }

        string side = (_loopTurn - _loopOut + 4) % 4 == 1 ? "right" : "left";
        string state = _loop == null ? "not built" : $"lap {_loop.Lap}, count {_loop.Count}, {(_loop.IsInside ? "player inside" : "player outside")}, {(_loop.IsTrapped ? "trapped" : _loop.IsSealed ? "sealed" : _loop.SealPending ? "sealing" : "open")}";
        sb.Append($"L loop corridor: opening in the {EdgeNames[_loopOut]} wall of {_loopEntrance} (the wall is closed in the maze's own data), turning {side} outside; {state}\n");
    }

    /// <summary>
    /// Testing (KEEP IT LIT &gt; Debug &gt; Go To Loop Entrance): puts the player in the loop's entrance cell, 1 m back from the opening and
    /// facing it. Returns what happened; nothing moves when this floor has no loop corridor or it is already sealed.
    /// </summary>
    public string DebugGoToLoopEntrance()
    {
        if (!_loopActive || _loop == null) return "This floor has no loop corridor. Tick Debug Force Loop on the MazeGenerator (a themed floor) and start again.";
        if (_loop.IsSealed) return "The loop corridor on this floor is already sealed.";
        ThirdPersonController player = FindAnyObjectByType<ThirdPersonController>();
        if (player == null) return "No player found.";

        Vector3 facing = DirVector(_loopOut);
        Vector3 spot = CellCenter(_loopEntrance.x, _loopEntrance.y) - facing * 1f;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = new Vector3(spot.x, player.transform.position.y, spot.z);
        if (controller != null) controller.enabled = true;
        Camera view = Camera.main;
        float currentYaw = view != null ? view.transform.eulerAngles.y : player.transform.eulerAngles.y;
        player.AddYaw(Mathf.DeltaAngle(currentYaw, Quaternion.LookRotation(facing).eulerAngles.y));
        return $"At the loop entrance {_loopEntrance}, facing the opening ({EdgeNames[_loopOut]}).";
    }
}
