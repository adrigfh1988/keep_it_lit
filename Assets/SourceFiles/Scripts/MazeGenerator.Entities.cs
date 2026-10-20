using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F107 D, the maze side of the entity roster: which entities a level gets (<see cref="EntityRoster"/>, or the Inspector's debugEntities),
/// where each is placed (stream +25, after BuildStalker) and the late binding of them to the player systems. The entities themselves are
/// MazeEntity subclasses (Listener, LockerTenant, LivingBody); Stan stays a Stalker. Spec: plannings/descent-mode-spec.md D1-D4.
/// </summary>
public partial class MazeGenerator
{
    /// <summary>Which body a debug level uses for the living body (D4), when it has both kinds.</summary>
    public enum LivingBodyTest { Auto, Hanging, Floor }

    private const int EntitySalt = 25; // the +25 stream
    private const int NightNurseSalt = 0x41474E53; // "AGNS": own salted stream so +25 draws for Listener/Tenant/LivingBody are untouched
    private const int DarkBrunoSalt = 0x4252554E; // "BRUN": own salted stream so +25 draws for Listener/Tenant/LivingBody are untouched

    [Header("F107 - Entities (run 2)")]
    [Tooltip("The Listener spawns at least this many BFS steps from the start")]
    [SerializeField, Min(0)] private int listenerMinStartSteps = 6;
    [Tooltip("... and at least this many from the hunter's spawn cell")]
    [SerializeField, Min(0)] private int listenerMinHunterSteps = 4;
    [Tooltip("The living body prefers a hanging body or corpse at least this many BFS steps from the start")]
    [SerializeField, Min(0)] private int livingBodyMinStartSteps = 5;
    [Tooltip("Chance that a descent level from depth 5 has a living body, when it placed a candidate (the roll comes from the +25 stream)")]
    [SerializeField, Range(0f, 1f)] private float livingBodyChance = 0.6f;

    /// <summary>One hanging body or corpse recorded while the maze was dressed, with no rng draw. The living body is chosen among these.</summary>
    private struct LivingCandidate
    {
        public Transform Root;
        public Vector2Int Cell;
        public bool Hanging;
    }

    private EntityKind _entitySet;
    private readonly List<MazeEntity> _entities = new List<MazeEntity>();
    private readonly List<LivingCandidate> _livingCandidates = new List<LivingCandidate>();

    /// <summary>F107 D1: the roster this level was built with (the Inspector's debugEntities if it was set).</summary>
    public EntityKind EntitySet => _entitySet;

    /// <summary>F107 D1: every MazeEntity built this level (the Listener, the Tenant, the living bodies and their decoys). Not the Stalker.</summary>
    public IReadOnlyList<MazeEntity> Entities => _entities;

    /// <summary>F107 D1: resolves the level's entity set once, up front, so BuildStalker, BuildEntities and the dressing passes all read the same answer. Also empties the living-body candidates before the dressing records into them.</summary>
    private void ResolveEntitySet(int usedSeed)
    {
        _livingCandidates.Clear();
        if (debugEntities != EntityKind.None) _entitySet = debugEntities;
        else if (_profile == null) _entitySet = EntityKind.None;
        else
        {
            _entitySet = EntityRoster.For(GameFlow.IsDescent, GameFlow.IsDescent ? DescentRun.Depth : _profile.Floor, usedSeed);
            // F122 STAN WAKES EARLY: Stan from floor 2 instead of 4 (StalkerFromFloor is 4 without the Oath, i.e. the roster's own answer). Story only.
            // Assigns rather than ORs when the Oath wakes Stan early on floors 2-3 so the floor still has at most 1 extra entity besides the Hunter.
            if (!GameFlow.IsDescent && _profile.Floor < EntityRoster.StalkerFrom && _profile.Floor >= CampaignRules.StalkerFromFloor)
                _entitySet = EntityKind.Stalker;
        }

        terror.Log($"Entities set for this level: {_entitySet}{(debugEntities != EntityKind.None ? " (debugEntities)" : "")}", this);
    }

    /// <summary>One [Terror]-tagged Console line, only while TerrorSettings.logEvents is on. The entities log their state changes through it.</summary>
    public void LogTerror(string message, Object context = null) => terror.Log(message, context);

    /// <summary>F107 D4: records a hanging body or a corpse the dressing just placed, so BuildEntities can wake one. No rng draw.</summary>
    private void NoteLivingCandidate(Component clone, Vector2Int cell, bool hanging)
    {
        _livingCandidates.Add(new LivingCandidate { Root = clone.transform, Cell = cell, Hanging = hanging });
    }

    /// <summary>F107 D4: whether a floor corpse may crawl onto this point: inside the maze, and never into a locker or a squeeze cell (the loop corridor lies outside the maze, so it is excluded by the first test).</summary>
    public bool AllowsLivingBodyAt(Vector3 point)
    {
        if (!TryWorldToCell(point, out Vector2Int cell)) return false;
        return !_lockerCells.Contains(cell) && !_squeezeCells.Contains(cell);
    }

    /// <summary>F107 D2: true if a killer entity (the Listener) is within `distance` metres, flat, of `position`. PhantomDirector treats one like the hunter.</summary>
    public bool IsKillerEntityNear(Vector3 position, float distance)
    {
        for (int i = 0; i < _entities.Count; i++)
        {
            MazeEntity entity = _entities[i];
            if (entity == null || !entity.IsKiller) continue;

            Vector3 d = entity.transform.position - position;
            d.y = 0f;
            if (d.magnitude <= distance) return true;
        }

        return false;
    }

    // ---------------------------------------------------------------- BuildEntities (+25)

    /// <summary>
    /// F107 D1, post-BuildStalker, stream +25: spawns every entity in the level's set except the Stalker. The draws happen in a fixed order -
    /// the Listener's cell, the Tenant's locker, the living body's roll and pick, then its decoys - and only for kinds that are in the set.
    /// Capped at 1 extra entity besides the Hunter unless debugEntities explicitly forces multiple flags.
    /// </summary>
    private void BuildEntities(System.Random rng)
    {
        _entities.Clear();
        EntityKind set = _entitySet & ~EntityKind.Stalker;
        bool decoys = GameFlow.IsDescent && DescentRun.BodiesWoke && _entitySet == EntityKind.None;
        if (set == EntityKind.None && !decoys) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);

        if ((set & EntityKind.Listener) != 0) BuildListener(rng);
        if ((set & EntityKind.Tenant) != 0) BuildTenant(rng);
        if ((set & EntityKind.LivingBody) != 0 || decoys) BuildLivingBodies(rng, (set & EntityKind.LivingBody) != 0, decoys);
        if ((set & EntityKind.NightNurse) != 0) BuildNightNurse(new System.Random(UsedSeed ^ NightNurseSalt));
        if ((set & EntityKind.Butcher) != 0) BuildDarkBruno(new System.Random(UsedSeed ^ DarkBrunoSalt));

        terror.Log($"Entities: {(_entities.Count == 0 ? "none" : string.Join(", ", _entities.ConvertAll(e => e.name + ":" + e.Kind)))}", this);
    }

    private void BuildNightNurse(System.Random rng)
    {
        Vector2Int start = _startCell;
        HashSet<Vector2Int> reachable = ReachableCells(start, null);
        Dictionary<Vector2Int, int> fromStart = BfsWithin(start, reachable);
        Dictionary<Vector2Int, int> fromHunter = BfsWithin(FarthestCell(), reachable);
        HashSet<Vector2Int> starCells = new HashSet<Vector2Int>(_starCells);

        List<Vector2Int> candidates = new List<Vector2Int>();
        Vector2Int fallback = start;
        int fallbackDistance = -1;
        foreach (Vector2Int cell in reachable)
        {
            if (cell == start || starCells.Contains(cell) || _roomCells.Contains(cell) || IsLayoutReserved(cell)) continue;
            if (!fromStart.TryGetValue(cell, out int ds)) continue;

            if (ds > fallbackDistance) { fallbackDistance = ds; fallback = cell; }
            if (ds < listenerMinStartSteps) continue;
            if (!fromHunter.TryGetValue(cell, out int dh) || dh < listenerMinHunterSteps) continue;
            candidates.Add(cell);
        }

        if (candidates.Count == 0)
        {
            if (fallbackDistance < 1)
            {
                Debug.LogWarning("MazeGenerator: no cell fits the Night Nurse on this small maze - skipping it.", this);
                return;
            }
            candidates.Add(fallback);
        }

        Shuffle(candidates, rng);
        Vector2Int chosen = candidates[0];
        Vector3 position = CellCenter(chosen.x, chosen.y);
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, cellSize, NavMesh.AllAreas)) position = hit.position;

        Quaternion rot = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);
        GameObject instance = null;

        if (interactableKit != null && interactableKit.HasNightNurse)
        {
            instance = Instantiate(interactableKit.NightNurse.gameObject, position, rot, _mazeRoot);
        }
        else
        {
            instance = BuildNightNurseFallbackFromScene(position, rot);
            if (instance == null)
            {
                Debug.LogError("MazeGenerator: no InteractableKit (nightNurse) in the scene - skipping the Night Nurse this run. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
                return;
            }
        }

        instance.name = "NightNurse";
        instance.SetActive(true);

        NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
        if (agent == null) agent = instance.AddComponent<NavMeshAgent>();
        agent.radius = 0.3f;
        agent.speed = 1.85f;
        agent.acceleration = 6f;
        agent.angularSpeed = 360f;
        if (!agent.Warp(position)) Debug.LogWarning($"MazeGenerator: could not warp the Night Nurse onto the navmesh at {position}.", this);

        NightNurse nurse = instance.GetComponent<NightNurse>();
        if (nurse == null) nurse = instance.AddComponent<NightNurse>();
        _entities.Add(nurse);
    }

    private GameObject BuildNightNurseFallbackFromScene(Vector3 position, Quaternion rotation)
    {
        AsylumHub hub = FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform hubAgnesBody = null;
        if (hub != null && hub.life != null)
        {
            foreach (HubNpc npc in hub.life.GetComponentsInChildren<HubNpc>(true))
            {
                if (npc != null && npc.who == HubNpc.Who.Agnes && npc.body != null)
                {
                    hubAgnesBody = npc.body;
                    break;
                }
            }
        }

        if (hubAgnesBody == null && interactableKit != null && interactableKit.HasListener)
        {
            Transform listenerBody = interactableKit.Listener.transform.Find("Body");
            if (listenerBody != null) hubAgnesBody = listenerBody;
        }
        if (hubAgnesBody == null) return null;

        GameObject root = new GameObject("NightNurse");
        root.transform.SetParent(_mazeRoot, false);
        root.transform.SetPositionAndRotation(position, rotation);

        GameObject body = Instantiate(hubAgnesBody.gameObject, root.transform);
        body.name = "Body_NurseAgnes";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one;
        body.SetActive(true);

        root.AddComponent<NightNurse>();
        return root;
    }

    private void BuildDarkBruno(System.Random rng)
    {
        Vector2Int start = _startCell;
        HashSet<Vector2Int> reachable = ReachableCells(start, null);
        Dictionary<Vector2Int, int> fromStart = BfsWithin(start, reachable);
        Dictionary<Vector2Int, int> fromHunter = BfsWithin(FarthestCell(), reachable);
        HashSet<Vector2Int> starCells = new HashSet<Vector2Int>(_starCells);

        List<Vector2Int> roomCandidates = new List<Vector2Int>();
        List<Vector2Int> corridorCandidates = new List<Vector2Int>();
        Vector2Int fallback = start;
        int fallbackDistance = -1;
        foreach (Vector2Int cell in reachable)
        {
            if (cell == start || starCells.Contains(cell) || IsLayoutReserved(cell)) continue;
            if (!fromStart.TryGetValue(cell, out int ds)) continue;

            if (ds > fallbackDistance) { fallbackDistance = ds; fallback = cell; }
            if (ds < listenerMinStartSteps) continue;
            if (!fromHunter.TryGetValue(cell, out int dh) || dh < listenerMinHunterSteps) continue;
            if (_roomCells.Contains(cell)) roomCandidates.Add(cell);
            else corridorCandidates.Add(cell);
        }

        List<Vector2Int> candidates = roomCandidates.Count > 0 ? roomCandidates : corridorCandidates;
        if (candidates.Count == 0)
        {
            if (fallbackDistance < 1)
            {
                Debug.LogWarning("MazeGenerator: no cell fits Dark Bruno on this small maze - skipping it.", this);
                return;
            }
            candidates.Add(fallback);
        }

        Shuffle(candidates, rng);
        Vector2Int chosen = candidates[0];
        Vector3 position = CellCenter(chosen.x, chosen.y);
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, cellSize, NavMesh.AllAreas)) position = hit.position;

        Quaternion rot = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);
        GameObject instance = null;

        if (interactableKit != null && interactableKit.HasDarkBruno)
        {
            instance = Instantiate(interactableKit.DarkBruno.gameObject, position, rot, _mazeRoot);
        }
        else
        {
            instance = BuildDarkBrunoFallbackFromScene(position, rot);
            if (instance == null)
            {
                Debug.LogError("MazeGenerator: no InteractableKit (darkBruno) in the scene - skipping Dark Bruno this run. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
                return;
            }
        }

        instance.name = "DarkBruno";
        instance.SetActive(true);

        NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
        if (agent == null) agent = instance.AddComponent<NavMeshAgent>();
        agent.radius = 0.32f;
        agent.speed = 1.95f;
        agent.acceleration = 8f;
        agent.angularSpeed = 360f;
        if (!agent.Warp(position)) Debug.LogWarning($"MazeGenerator: could not warp Dark Bruno onto the navmesh at {position}.", this);

        DarkBruno butcher = instance.GetComponent<DarkBruno>();
        if (butcher == null) butcher = instance.AddComponent<DarkBruno>();
        _entities.Add(butcher);
    }

    private GameObject BuildDarkBrunoFallbackFromScene(Vector3 position, Quaternion rotation)
    {
        AsylumHub hub = FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform hubBrunoBody = null;
        if (hub != null && hub.life != null)
        {
            foreach (HubNpc npc in hub.life.GetComponentsInChildren<HubNpc>(true))
            {
                if (npc != null && npc.who == HubNpc.Who.Bruno && npc.body != null)
                {
                    hubBrunoBody = npc.body;
                    break;
                }
            }
        }

        if (hubBrunoBody == null && interactableKit != null && interactableKit.HasNightNurse)
        {
            Transform nurseBody = interactableKit.NightNurse.transform.Find("Body_NurseAgnes");
            if (nurseBody != null) hubBrunoBody = nurseBody;
        }
        if (hubBrunoBody == null) return null;

        GameObject root = new GameObject("DarkBruno");
        root.transform.SetParent(_mazeRoot, false);
        root.transform.SetPositionAndRotation(position, rotation);

        GameObject body = Instantiate(hubBrunoBody.gameObject, root.transform);
        body.name = "Body_CookBruno";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one;
        body.SetActive(true);

        root.AddComponent<DarkBruno>();
        return root;
    }

    /// <summary>
    /// Collects 3-5 Chopping Block station world positions for Dark Bruno ("The Butcher").
    /// Prioritises carved rooms (<see cref="_carvedRooms"/> / <see cref="_roomCells"/>) far enough from the player start,
    /// then supplements with well-spaced junctions/dead-ends when a floor has fewer than 3 carved rooms.
    /// </summary>
    public void CollectButcherStations(List<Vector3> stations)
    {
        if (stations == null) return;
        stations.Clear();

        System.Random rng = new System.Random(UsedSeed ^ (DarkBrunoSalt + 17));
        Vector2Int start = _startCell;
        HashSet<Vector2Int> reachable = ReachableCells(start, null);
        Dictionary<Vector2Int, int> fromStart = BfsWithin(start, reachable);

        const int targetMinStations = 3;
        const int targetMaxStations = 5;
        float minSeparation = Mathf.Max(cellSize * 2.5f, 8.5f);

        // 1. Primary stations: one cell inside each carved room.
        List<RectInt> rooms = new List<RectInt>(_carvedRooms);
        for (int i = rooms.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (rooms[i], rooms[j]) = (rooms[j], rooms[i]);
        }
        foreach (RectInt r in rooms)
        {
            if (stations.Count >= targetMaxStations) break;
            Vector2Int center = new Vector2Int(r.xMin + r.width / 2, r.yMin + r.height / 2);
            if (!reachable.Contains(center)) continue;
            if (fromStart.TryGetValue(center, out int ds) && ds < 3) continue;

            Vector3 world = CellCenter(center.x, center.y);
            if (NavMesh.SamplePosition(world, out NavMeshHit hit, cellSize, NavMesh.AllAreas)) world = hit.position;
            if (IsFarEnoughFromStations(world, stations, minSeparation * 0.7f))
                stations.Add(world);
        }

        // 2. If _roomCells exist without enough RectInt entries, sample from _roomCells.
        if (stations.Count < targetMinStations && _roomCells.Count > 0)
        {
            List<Vector2Int> roomPool = new List<Vector2Int>(_roomCells);
            Shuffle(roomPool, rng);
            foreach (Vector2Int cell in roomPool)
            {
                if (stations.Count >= targetMaxStations) break;
                if (!reachable.Contains(cell) || cell == start || IsLayoutReserved(cell)) continue;
                if (fromStart.TryGetValue(cell, out int ds) && ds < 3) continue;

                Vector3 world = CellCenter(cell.x, cell.y);
                if (NavMesh.SamplePosition(world, out NavMeshHit hit, cellSize, NavMesh.AllAreas)) world = hit.position;
                if (IsFarEnoughFromStations(world, stations, minSeparation))
                    stations.Add(world);
            }
        }

        // 3. Supplement with well-spaced reachable maze cells so Bruno always has at least 3 stations to rotate between every 2 minutes.
        if (stations.Count < targetMinStations)
        {
            List<Vector2Int> fallbackPool = new List<Vector2Int>(reachable);
            Shuffle(fallbackPool, rng);
            foreach (Vector2Int cell in fallbackPool)
            {
                if (stations.Count >= targetMinStations + 1) break;
                if (cell == start || IsLayoutReserved(cell) || _squeezeCells.Contains(cell)) continue;
                if (fromStart.TryGetValue(cell, out int ds) && ds < 4) continue;

                Vector3 world = CellCenter(cell.x, cell.y);
                if (NavMesh.SamplePosition(world, out NavMeshHit hit, cellSize, NavMesh.AllAreas)) world = hit.position;
                if (IsFarEnoughFromStations(world, stations, minSeparation * 0.8f))
                    stations.Add(world);
            }
        }

        // 4. Ultimate fallback on tiny test mazes: add any reachable cell not equal to start.
        if (stations.Count < 2)
        {
            foreach (Vector2Int cell in reachable)
            {
                if (stations.Count >= 2) break;
                if (cell == start) continue;
                Vector3 world = CellCenter(cell.x, cell.y);
                if (NavMesh.SamplePosition(world, out NavMeshHit hit, cellSize, NavMesh.AllAreas)) world = hit.position;
                if (IsFarEnoughFromStations(world, stations, cellSize * 0.8f))
                    stations.Add(world);
            }
        }
    }

    private static bool IsFarEnoughFromStations(Vector3 candidate, List<Vector3> existing, float minDistance)
    {
        float minSq = minDistance * minDistance;
        for (int i = 0; i < existing.Count; i++)
        {
            Vector3 d = existing[i] - candidate;
            d.y = 0f;
            if (d.sqrMagnitude < minSq) return false;
        }
        return true;
    }

    private void BuildListener(System.Random rng)
    {
        if (interactableKit == null || !interactableKit.HasListener)
        {
            Debug.LogError("MazeGenerator: no InteractableKit (listener) in the scene - skipping the Listener this run. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        Vector2Int start = _startCell;
        HashSet<Vector2Int> reachable = ReachableCells(start, null);
        Dictionary<Vector2Int, int> fromStart = BfsWithin(start, reachable);
        Dictionary<Vector2Int, int> fromHunter = BfsWithin(FarthestCell(), reachable);
        HashSet<Vector2Int> starCells = new HashSet<Vector2Int>(_starCells);

        List<Vector2Int> candidates = new List<Vector2Int>();
        Vector2Int fallback = start;
        int fallbackDistance = -1;
        foreach (Vector2Int cell in reachable)
        {
            if (cell == start || starCells.Contains(cell) || _roomCells.Contains(cell) || IsLayoutReserved(cell)) continue;
            if (!fromStart.TryGetValue(cell, out int ds)) continue;

            if (ds > fallbackDistance) { fallbackDistance = ds; fallback = cell; }
            if (ds < listenerMinStartSteps) continue;
            if (!fromHunter.TryGetValue(cell, out int dh) || dh < listenerMinHunterSteps) continue;
            candidates.Add(cell);
        }

        if (candidates.Count == 0)
        {
            if (fallbackDistance < 1)
            {
                Debug.LogWarning("MazeGenerator: no cell fits the Listener on this small maze - skipping it.", this);
                return;
            }

            candidates.Add(fallback);
        }

        Shuffle(candidates, rng);
        Vector2Int chosen = candidates[0];
        Vector3 position = CellCenter(chosen.x, chosen.y);
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, cellSize, NavMesh.AllAreas)) position = hit.position;

        GameObject instance = Instantiate(interactableKit.Listener.gameObject, position, Quaternion.Euler(0f, rng.Next(4) * 90f, 0f), _mazeRoot);
        instance.name = "Listener";
        instance.SetActive(true);

        NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
        if (agent == null) agent = instance.AddComponent<NavMeshAgent>();
        agent.radius = 0.3f; // at most the maze surface's agent radius
        agent.speed = 1.4f;
        agent.acceleration = 40f;
        agent.angularSpeed = 360f;
        if (!agent.Warp(position)) Debug.LogWarning($"MazeGenerator: could not warp the Listener onto the navmesh at {position}.", this);

        _entities.Add(instance.GetComponent<Listener>());
    }

    private void BuildTenant(System.Random rng)
    {
        if (_lockers.Count < 3 || _lockers.Count != _lockerCells.Count)
        {
            terror.Log($"No Tenant: {_lockers.Count} lockers (needs 3)", this);
            return;
        }

        // Never the locker nearest the start, never the locker at a bait dead end's mouth (that one is the bait sanctuary).
        int nearest = 0;
        for (int i = 1; i < _lockerCells.Count; i++)
        {
            if (_distance[_lockerCells[i].x, _lockerCells[i].y] < _distance[_lockerCells[nearest].x, _lockerCells[nearest].y]) nearest = i;
        }

        List<int> candidates = new List<int>();
        for (int i = 0; i < _lockerCells.Count; i++)
        {
            Vector2Int cell = _lockerCells[i];
            if (i == nearest || _baitDeadEnds.Exists(b => MouthOf(b) == cell)) continue;
            candidates.Add(i);
        }

        if (candidates.Count == 0) return;

        Locker locker = _lockers[candidates[rng.Next(candidates.Count)]];
        _entities.Add(locker.gameObject.AddComponent<LockerTenant>());

        // The hunter's peek and locker-heat logic must ignore a locker that is never the player's.
        List<Vector3> fronts = new List<Vector3>();
        foreach (Locker other in _lockers)
        {
            if (other != locker) fronts.Add(other.FrontPosition);
        }
        aiFollower.SetHidingSpots(fronts);
    }

    private void BuildLivingBodies(System.Random rng, bool real, bool decoys)
    {
        List<int> pool = new List<int>();
        for (int i = 0; i < _livingCandidates.Count; i++)
        {
            LivingCandidate c = _livingCandidates[i];
            if (c.Root == null || IsLayoutReserved(c.Cell) || _lockerCells.Contains(c.Cell)) continue;
            if (debugLivingBody == LivingBodyTest.Hanging && !c.Hanging) continue;
            if (debugLivingBody == LivingBodyTest.Floor && c.Hanging) continue;
            pool.Add(i);
        }

        if (real)
        {
            bool roll = debugEntities != EntityKind.None || rng.NextDouble() < livingBodyChance;
            if (roll && pool.Count > 0)
            {
                // Prefer a body far enough from the start that the player has already settled in.
                List<int> far = pool.FindAll(i => _distance[_livingCandidates[i].Cell.x, _livingCandidates[i].Cell.y] >= livingBodyMinStartSteps);
                List<int> from = far.Count > 0 ? far : pool;
                int pick = from[rng.Next(from.Count)];
                if (AddLivingBody(_livingCandidates[pick], false)) pool.Remove(pick);
            }
            else if (roll)
            {
                terror.Log("No living body: this level placed no hanging body or corpse", this);
            }
        }

        if (decoys)
        {
            int want = 1 + rng.Next(2);
            while (want > 0 && pool.Count > 0)
            {
                int index = rng.Next(pool.Count);
                int pick = pool[index];
                pool.RemoveAt(index);
                if (AddLivingBody(_livingCandidates[pick], true)) want--;
            }
        }
    }

    private bool AddLivingBody(LivingCandidate candidate, bool decoy)
    {
        LivingBody body = candidate.Root.gameObject.AddComponent<LivingBody>();
        if (!body.Setup(candidate.Hanging ? LivingBody.BodyMode.Hanging : LivingBody.BodyMode.Floor, decoy))
        {
            Destroy(body);
            return false;
        }

        _entities.Add(body);
        return true;
    }

    /// <summary>F107 D1: late-binds every entity to the player systems (SetUpAtmosphere, once the HUD, outcome and escape exist).</summary>
    private void ConfigureEntities(Transform player)
    {
        foreach (MazeEntity entity in _entities)
        {
            if (entity != null) entity.Configure(this, player, aiFollower);
        }
    }

    // ---------------------------------------------------------------- DebugMap

    /// <summary>DebugMap: l the Listener, t the Tenant's locker, b the living body, d a decoy; each only on a cell with room left for it.</summary>
    private void AddEntityMarks(string[,] marks)
    {
        foreach (MazeEntity entity in _entities)
        {
            if (entity == null || !TryWorldToCell(entity.transform.position, out Vector2Int cell)) continue;
            if ((marks[cell.x, cell.y] ?? "").Length < 3) marks[cell.x, cell.y] += entity.DebugGlyph;
        }
    }

    private void AppendEntityLegend(System.Text.StringBuilder sb)
    {
        sb.Append($"entities: {_entitySet}");
        foreach (MazeEntity entity in _entities)
        {
            if (entity == null) continue;

            string cell = TryWorldToCell(entity.transform.position, out Vector2Int c) ? c.ToString() : "?";
            string detail = entity is Listener listener ? listener.CurrentMode.ToString()
                : entity is NightNurse nurse ? nurse.CurrentMode.ToString()
                : entity is DarkBruno butcher ? butcher.CurrentMode.ToString()
                : entity is LockerTenant tenant ? (tenant.Held ? "held" : "waiting")
                : entity is LivingBody body ? $"{body.Mode}{(body.IsDecoy ? " decoy" : "")}{(body.HasActed ? " done" : "")}" : "";
            sb.Append($"\n  {entity.DebugGlyph} {entity.Kind} at {cell} {detail}");
        }

        sb.Append('\n');
    }
}
