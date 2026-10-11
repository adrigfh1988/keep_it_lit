using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F100 (layer 2): Patient 7's stars. After each floor, fresh chalk stars lead from the arrival point to whoever holds the next key
/// (floors 2-4: the mission's giver; floor 5: the records room, then the Director's door), and once the key is in hand the trail is
/// drawn again to the wing door. One star is scratched on the SHOP door while the shop is open. Nobody comments on them except one
/// subtitle the first time the player walks up to one (floor 2).
///
/// The stars are a pool of inactive wall quads (AsylumHub/Life/Wayfinding/Stars) lit only by their own faint emission - no Light. The path is
/// the hub NavMesh path ("Hub" agent type) from the arrival point to the target; every few metres a star is put on the nearest plain
/// building wall beside the path, 1.1-1.7 m up, its tail along the direction of travel. Everything about a star (side, height, variant,
/// roll, doubling) comes from a hash of (floor, index): no rng stream. Lives under Life, so it exists only while the hub is up; a shop
/// round trip (AsylumHub.IsShopReturn) leaves the trail exactly as it was, a floor change or a re-entry draws it afresh.
/// </summary>
public class HubStarTrail : MonoBehaviour
{
    [Tooltip("The pool of wall quads (inactive in the prefab). Built by KEEP IT LIT > Build Missing Pieces.")]
    [SerializeField] private Renderer[] pool = new Renderer[0];
    [Tooltip("The star scratched on the SHOP door's leaf.")]
    [SerializeField] private Renderer shopStar;
    /// <summary>A star's size on the wall (m) on floor 2 at 2.5 m from the path; later floors and farther walls scale it up.</summary>
    private const float baseSize = 0.4f;
    [Tooltip("Log how many stars were placed and which samples were skipped (Editor and development builds).")]
    [SerializeField] private bool logPlacement = true;

    public bool IsComplete => pool != null && pool.Length >= 40 && shopStar != null;

    /// <summary>Stars currently on a wall (testing).</summary>
    public int LiveCount => _live.Count;

    // floors 2..5
    private static readonly float[] Spacing = { 5f, 4f, 3f, 3f };
    private static readonly float[] SizeScale = { 1f, 1.1f, 1.3f, 1.5f };
    private static readonly float[] Glow = { 1f, 1.15f, 1.3f, 1.7f };
    private static readonly float[] Roll = { 0f, 8f, 28f, 14f };
    private static readonly string[] SkipNames = { "Door", "Frame", "Arch", "Elevator", "Lift", "Gate", "Window", "Stair" };

    private readonly List<int> _live = new List<int>();
    private readonly bool[] _used = new bool[64];
    private MaterialPropertyBlock _block;
    private Coroutine _routine;
    private int _builtFloor = -1;
    private int _stage; // 0 = toward the giver / the records room, 1 = toward the wing door
    private float _poll;
    private bool _noted;
    private int _floorGlow = 1;

    private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    private static readonly Color Chalk = new Color(0.96f, 0.95f, 0.88f, 1f);
    private static readonly Color Pale = new Color(0.52f, 0.66f, 0.55f, 1f);

    private AsylumHub Hub => AsylumHub.Active;

    // ---------------------------------------------------------------- lifecycle

    private void OnEnable()
    {
        AsylumHub.OnEnterHub += HandleEnter;
        AsylumHub.OnLeaveHub += HandleLeave;
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        AsylumHub.OnLeaveHub -= HandleLeave;
        if (_routine != null) { StopCoroutine(_routine); _routine = null; _builtFloor = -1; } // an interrupted build is redone on the next entry
    }

    private void HandleLeave()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; _builtFloor = -1; }
    }

    private void HandleEnter(int floor)
    {
        AsylumHub hub = Hub;
        if (hub == null || !IsComplete) return;
        bool wanted = !HubState.IsFinale && floor >= 2 && floor <= 5;
        SetShopStar(wanted && hub.shopDoor != null && hub.shopDoor.IsOpen);
        if (!wanted)
        {
            if (_routine != null) { StopCoroutine(_routine); _routine = null; }
            ClearAll();
            _builtFloor = -1;
            return;
        }

        // Back from the shop: the trail is as the player left it.
        if (hub.IsShopReturn && _builtFloor == floor && _routine == null)
        {
            foreach (int i in _live) { pool[i].gameObject.SetActive(true); Paint(i, 1f); }
            return;
        }
        if (_routine != null) StopCoroutine(_routine);
        ClearAll();
        _builtFloor = -1;
        _stage = StageNow(floor);
        _routine = StartCoroutine(Build(floor, _stage, hub.ArrivalPosition, false));
    }

    private void Update()
    {
        AsylumHub hub = Hub;
        if (hub == null || !IsComplete || _routine != null || _builtFloor < 0) return;
        _poll -= Time.deltaTime;
        if (_poll > 0f) return;
        _poll = 0.25f;

        if (hub.IsTransitioning || hub.Leaving) return;
        int stage = StageNow(_builtFloor);
        if (stage != _stage)
        {
            _stage = stage;
            Vector3 from = hub.Player != null ? hub.Player.position : hub.ArrivalPosition;
            _routine = StartCoroutine(Build(_builtFloor, stage, from, true));
            return;
        }

        // The one comment the stars get: the first time the player comes up to one (floor 2).
        if (!_noted && _builtFloor == 2 && hub.Player != null && hub.Hud != null)
        {
            Vector3 p = hub.Player.position;
            foreach (int i in _live)
            {
                if ((pool[i].transform.position - p).sqrMagnitude < 4.5f * 4.5f)
                {
                    _noted = true;
                    hub.Hud.QueueSubtitle(L10n.T("hub.stars.first"), 4.5f);
                    break;
                }
            }
        }
    }

    // ---------------------------------------------------------------- where the trail leads

    /// <summary>0 while the key is still to be fetched, 1 once the way to the wing door is all that is left.</summary>
    private int StageNow(int floor)
    {
        if (floor == 5)
        {
            KeyRunSetPiece run = Hub != null && Hub.life != null ? Hub.life.GetComponentInChildren<KeyRunSetPiece>(true) : null;
            return run != null && run.KeyHeld ? 1 : 0;
        }
        HubMission m = HubMission.ForFloor(floor);
        return m != null && m.Current == HubMission.Stage.Done ? 1 : 0;
    }

    private bool TryTarget(int floor, int stage, out Vector3 target)
    {
        target = default;
        AsylumHub hub = Hub;
        if (hub == null) return false;
        if (stage == 1)
        {
            WingDoor door = hub.wingDoors[floor - 1];
            if (door == null || door.arrivalPoint == null) return false;
            target = door.arrivalPoint.position;
            return true;
        }
        if (floor == 5)
        {
            Transform records = hub.points != null ? hub.points.Find("Records_Room") : null;
            if (records == null) return false;
            target = records.position;
            return true;
        }
        HubMission m = HubMission.ForFloor(floor);
        if (m == null || m.giver == null || m.giver.body == null) return false;
        target = m.giver.body.position;
        return true;
    }

    private static int HubAgentType()
    {
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            NavMeshBuildSettings s = NavMesh.GetSettingsByIndex(i);
            if (NavMesh.GetSettingsNameFromID(s.agentTypeID) == "Hub") return s.agentTypeID;
        }
        return -1;
    }

    // ---------------------------------------------------------------- build

    private IEnumerator Build(int floor, int stage, Vector3 from, bool replace)
    {
        AsylumHub hub = Hub;
        // Never during the arrival fade or a scripted move.
        while (hub != null && (hub.IsTransitioning || hub.Leaving)) yield return null;
        if (hub == null) { _routine = null; yield break; }

        if (replace && _live.Count > 0)
        {
            for (float t = 0f; t < 1.2f; t += Time.deltaTime) { SetAll(1f - t / 1.2f); yield return null; }
            ClearAll();
        }

        Physics.SyncTransforms();
        Place(hub, floor, stage, from);
        _builtFloor = floor;
        for (float t = 0f; t < 1.2f; t += Time.deltaTime) { SetAll(t / 1.2f); yield return null; }
        SetAll(1f);
        _routine = null;
    }

    private int Place(AsylumHub hub, int floor, int stage, Vector3 from)
    {
        if (!TryTarget(floor, stage, out Vector3 target)) { Log($"floor {floor}: no target"); return 0; }
        int type = HubAgentType();
        if (type == -1) { Log($"floor {floor}: no Hub agent type"); return 0; } // ids can be negative; -1 is "none" here, as in HubStalker
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = type, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(from, out NavMeshHit a, 3f, filter) || !NavMesh.SamplePosition(target, out NavMeshHit b, 3f, filter)) { Log($"floor {floor}: start or target is off the Hub NavMesh"); return 0; }
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(a.position, b.position, filter, path) || path.corners.Length < 2) { Log($"floor {floor}: no path"); return 0; }

        int fi = Mathf.Clamp(floor, 2, 5) - 2;
        _floorGlow = fi;
        Vector3[] c = path.corners;
        float length = 0f;
        for (int i = 1; i < c.Length; i++) length += Vector3.Distance(c[i - 1], c[i]);

        float spacing = Spacing[fi];
        int samples = 0, skipped = 0, index = 0;
        List<Vector3> taken = new List<Vector3>();
        for (float s = 2f; s < length - 1.6f; s += spacing)
        {
            samples++;
            if (!TrySpot(hub, c, s, floor, index, taken, out Vector3 pos, out Vector3 normal, out Vector3 travel, out float dist)) { skipped++; index++; continue; }
            if (!Claim(floor, fi, index, pos, normal, travel, 0f, dist)) { Log($"floor {floor}: pool of {pool.Length} is full"); break; }
            taken.Add(pos);
            // floor 4 is frantic: now and then a second star right beside the first, smaller and turned
            // (only while the pool still has room for every sample left on the path)
            int samplesLeft = Mathf.CeilToInt((length - 1.6f - s) / spacing);
            if (floor == 4 && H(floor, index, 5) < 0.45f && pool.Length - _live.Count - 2 > samplesLeft)
            {
                Vector3 w = Vector3.Cross(Vector3.up, normal) * (H(floor, index, 6) < 0.5f ? 0.42f : -0.42f);
                Vector3 p2 = pos + w + Vector3.up * (H(floor, index, 7) * 0.3f - 0.1f);
                Claim(floor, fi, index + 500, p2, normal, travel, 0.65f, dist);
                taken.Add(p2);
            }
            index++;
        }
        Log($"floor {floor} stage {stage}: {samples} samples, {_live.Count} stars placed ({skipped} samples found no plain wall), path {length:F1} m; rejected tries: " + (_why.Count == 0 ? "none" : string.Join(", ", _why.Select(kv => kv.Key + " x" + kv.Value))));
        _why.Clear();
        return _live.Count;
    }

    private readonly Dictionary<string, int> _why = new Dictionary<string, int>();
    private void Why(string reason) { _why.TryGetValue(reason, out int n); _why[reason] = n + 1; }

    private bool TrySpot(AsylumHub hub, Vector3[] corners, float s, int floor, int index, List<Vector3> taken, out Vector3 pos, out Vector3 normal, out Vector3 travel, out float dist)
    {
        pos = default; normal = default; travel = default; dist = 0f;
        // The sample itself, then nudges either way along the path (a doorway, a window, a curtain, a lamp bracket ...).
        foreach (float shift in new[] { 0f, 0.8f, -0.8f, 1.6f, -1.6f })
        {
            if (!PointAt(corners, s + shift, out Vector3 p, out Vector3 t)) { Why("off the path"); continue; }
            float h = 1.1f + 0.6f * H(floor, index, 1);
            Vector3 side = Vector3.Cross(Vector3.up, t);
            float first = H(floor, index, 2) < 0.5f ? 1f : -1f;
            for (int k = 0; k < 2; k++)
            {
                Vector3 dir = side * (k == 0 ? first : -first);
                if (!WallAt(hub, new Vector3(p.x, p.y + h, p.z), dir, out Vector3 point, out Vector3 n, out string why)) { Why(why); continue; }
                Vector3 candidate = point + n * 0.02f;
                bool near = false;
                foreach (Vector3 q in taken) if ((q - candidate).sqrMagnitude < 4f) { near = true; break; }
                if (near) { Why("within 2 m of another star"); continue; }
                if (NearDoor(hub, candidate)) { Why("beside a door"); continue; }
                pos = candidate; normal = n; travel = t; dist = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(point.x, 0f, point.z));
                return true;
            }
        }
        return false;
    }

    private static bool PointAt(Vector3[] c, float s, out Vector3 point, out Vector3 tangent)
    {
        point = default; tangent = default;
        if (s < 0f) return false;
        for (int i = 1; i < c.Length; i++)
        {
            float seg = Vector3.Distance(c[i - 1], c[i]);
            if (s <= seg && seg > 0.001f)
            {
                point = Vector3.Lerp(c[i - 1], c[i], s / seg);
                tangent = c[i] - c[i - 1];
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 0.0001f) return false;
                tangent.Normalize();
                return true;
            }
            s -= seg;
        }
        return false;
    }
    /// <summary>A plain, flat stretch of building wall within reach: not a door, frame, arch, lift or stair, nothing leaning on it, no gap in it. `why` says what was wrong when it is not.</summary>
    private static bool WallAt(AsylumHub hub, Vector3 origin, Vector3 dir, out Vector3 point, out Vector3 normal, out string why)
    {
        point = default; normal = default; why = null;
        if (!Physics.Raycast(origin, dir, out RaycastHit hit, ReachMetres, ~0, QueryTriggerInteraction.Ignore)) { why = "no wall within reach"; return false; }
        if (hub.building == null || !hit.collider.transform.IsChildOf(hub.building)) { why = "first hit is not the building (" + hit.collider.name + ")"; return false; }
        if (Mathf.Abs(hit.normal.y) > 0.25f) { why = "hit is not vertical"; return false; }
        for (Transform t = hit.collider.transform; t != null && t != hub.building; t = t.parent)
        {
            foreach (string token in SkipNames) if (t.name.Contains(token)) { why = "a " + token.ToLowerInvariant(); return false; }
        }
        Vector3 n = hit.normal; n.y = 0f; n.Normalize();
        Vector3 across = Vector3.Cross(Vector3.up, n);
        // four probes around the star's footprint must all meet the same plane (a wall may be several colliders, so only the plane counts)
        for (int i = 0; i < 4; i++)
        {
            Vector3 off = across * (i < 2 ? 0.35f : -0.35f) + Vector3.up * (i % 2 == 0 ? 0.2f : -0.2f); // wider than tall: keeps a star off a corner
            Vector3 o = hit.point + n * 0.3f + off;
            if (!Physics.Raycast(o, -n, out RaycastHit probe, 0.5f, ~0, QueryTriggerInteraction.Ignore)) { why = "a gap under the star"; return false; }
            if (!probe.collider.transform.IsChildOf(hub.building) || Mathf.Abs(probe.distance - 0.3f) > 0.08f) { why = "the wall is not flat there"; return false; }
        }
        point = hit.point; normal = n;
        return true;
    }

    /// <summary>How far to either side of the path a wall may be. The plan said 2.5 m, but the hub's rooms and halls are wide (the path runs down their middle, walls 3-8 m away): stars on a farther wall are drawn larger (see Claim).</summary>
    private const float ReachMetres = 6.5f;

    private static bool NearDoor(AsylumHub hub, Vector3 p)
    {
        foreach (WingDoor d in hub.wingDoors) if (d != null && Flat(d.transform.position - p) < 1.5f) return true;
        return hub.shopDoor != null && Flat(hub.shopDoor.transform.position - p) < 1.5f;
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    // ---------------------------------------------------------------- the stars themselves

    /// <summary>Takes a free quad, puts it on the wall and returns false when the pool is empty.</summary>
    private bool Claim(int floor, int fi, int index, Vector3 pos, Vector3 normal, Vector3 travel, float shrink, float dist)
    {
        int slot = -1;
        for (int i = 0; i < pool.Length && i < _used.Length; i++) if (!_used[i]) { slot = i; break; }
        if (slot < 0) return false;
        _used[slot] = true;
        _live.Add(slot);

        // forward into the wall, right along the way the path goes (the tail points on), rolled a little by the floor's frenzy
        Vector3 forward = -normal;
        Vector3 right = travel - normal * Vector3.Dot(travel, normal);
        if (right.sqrMagnitude < 0.05f) right = Vector3.Cross(Vector3.up, normal);
        right.Normalize();
        Quaternion look = Quaternion.LookRotation(forward, Vector3.Cross(forward, right));
        float roll = (H(floor, index, 3) * 2f - 1f) * Roll[fi];
        Transform q = pool[slot].transform;
        q.SetPositionAndRotation(pos, look * Quaternion.Euler(0f, 0f, roll));
        float far = Mathf.Clamp(dist / 2.5f, 1f, 2.4f); // a wall 6 m off needs a bigger star to read as one
        float size = baseSize * far * SizeScale[fi] * (floor == 3 ? 0.85f + 0.3f * H(floor, index, 4) : 1f) * (shrink > 0f ? shrink : 1f);
        q.localScale = new Vector3(size * (floor == 3 ? 1.15f : 1f), size, 1f); // floor 3's are smeared a little wider
        int variant = Mathf.FloorToInt(H(floor, index, 8) * 4f) % 4;
        _variant[slot] = variant;
        _alpha[slot] = floor == 3 ? 0.7f + 0.3f * H(floor, index, 9) : 1f;
        q.gameObject.SetActive(true);
        Paint(slot, 0f);
        return true;
    }

    private readonly int[] _variant = new int[64];
    private readonly float[] _alpha = new float[64];

    private void Paint(int slot, float k)
    {
        if (_block == null) _block = new MaterialPropertyBlock();
        int v = _variant[slot];
        _block.SetVector(BaseMapSt, new Vector4(0.5f, 0.5f, (v & 1) * 0.5f, (v >> 1) * 0.5f));
        Color baseColor = Chalk;
        baseColor.a = _alpha[slot] * k;
        _block.SetColor(BaseColor, baseColor);
        _block.SetColor(EmissionColor, Pale * (Glow[_floorGlow] * k));
        pool[slot].SetPropertyBlock(_block);
    }

    private void SetAll(float k)
    {
        foreach (int i in _live) Paint(i, Mathf.Clamp01(k));
    }

    private void ClearAll()
    {
        foreach (int i in _live)
        {
            if (pool[i] != null) pool[i].gameObject.SetActive(false);
            _used[i] = false;
        }
        _live.Clear();
    }

    /// <summary>The SHOP door's own star: on while the shop can be entered. Same chalk, a fixed variant, never fades.</summary>
    private void SetShopStar(bool on)
    {
        if (shopStar == null) return;
        shopStar.gameObject.SetActive(on);
        if (!on) return;
        if (_block == null) _block = new MaterialPropertyBlock();
        _block.SetVector(BaseMapSt, new Vector4(0.5f, 0.5f, 0.5f, 0f));
        _block.SetColor(BaseColor, Chalk);
        _block.SetColor(EmissionColor, Pale * 1.15f);
        shopStar.SetPropertyBlock(_block);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A repeatable 0..1 from (floor, index, salt): the trail is the same every visit to the same floor.</summary>
    private static float H(int floor, int index, int salt)
    {
        unchecked
        {
            uint x = (uint)(floor * 73856093) ^ (uint)(index * 19349663) ^ (uint)(salt * 83492791);
            x ^= x >> 13; x *= 0x5bd1e995u; x ^= x >> 15; x *= 0x27d4eb2du; x ^= x >> 16;
            return (x & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private void Log(string message)
    {
        if (logPlacement && (Application.isEditor || Debug.isDebugBuild)) Debug.Log("HubStarTrail: " + message);
    }
}
