using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F93 (E9, idea 5): the asylum gets less safe every floor. On every hub entry, from HubState alone: a growing fraction of the hub's
/// lights is dead (cumulative - ordered by a stable hash of the hierarchy path, so a lamp dead on floor 3 stays dead on floor 4, and the
/// fixture's glow goes with it), more of the survivors flicker, the fog thickens by 0.005 a floor, distant noises come more often
/// (footsteps overhead from floor 2, a far scream from 4) and from floor 3 a few open doors swing on their own. The finale lights the
/// hub itself (HubFinale), so decay stands down there. Lives under AsylumHub/Life/Decay (enabled with the hub content).
/// </summary>
public class HubDecay : MonoBehaviour
{
    [SerializeField] private AsylumHub hub;
    [SerializeField, Range(0f, 0.02f)] private float fogPerFloor = 0.005f;
    [SerializeField] private int swingingDoors = 4;
    [SerializeField] private float swingDegrees = 3f;

    private struct Slot
    {
        public Light light;
        public int hash;
        public List<Renderer> fixtures;
    }

    private readonly List<Slot> _slots = new List<Slot>();
    private readonly Dictionary<Light, HubLampFlicker> _flickers = new Dictionary<Light, HubLampFlicker>();
    private readonly List<Transform> _swingers = new List<Transform>();
    private readonly List<Quaternion> _swingRest = new List<Quaternion>();
    private MaterialPropertyBlock _block;
    private bool _built;
    private int _floor = 1;

    private void OnEnable()
    {
        AsylumHub.OnEnterHub += HandleEnter;
        AsylumHub.OnLeaveHub += HandleLeave;
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        AsylumHub.OnLeaveHub -= HandleLeave;
    }

    private void HandleLeave()
    {
        RestoreDoors();
        _floor = 0;
    }

    private void HandleEnter(int floor)
    {
        if (hub == null) hub = AsylumHub.Active;
        if (hub == null) return;
        if (!_built) Build();
        _floor = floor;
        if (HubState.IsFinale) return; // HubFinale owns the lights then
        Apply(floor);
    }

    private void Build()
    {
        _built = true;
        _block = new MaterialPropertyBlock();
        List<Renderer> emissive = new List<Renderer>();
        foreach (Renderer r in hub.building.GetComponentsInChildren<Renderer>(true))
        {
            Material m = r.sharedMaterial;
            if (m != null && m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.05f) emissive.Add(r);
        }

        HashSet<Light> exempt = new HashSet<Light>();
        foreach (WingDoor door in hub.wingDoors) if (door != null && door.beacon != null) exempt.Add(door.beacon);

        foreach (Light l in hub.GetComponentsInChildren<Light>(true))
        {
            if (exempt.Contains(l)) continue;
            if (hub.life != null && l.transform.IsChildOf(hub.life)) continue;
            Slot s = new Slot { light = l, hash = HubState.StableHash(PathOf(l.transform)), fixtures = new List<Renderer>() };
            foreach (Renderer r in emissive)
            {
                if ((r.bounds.center - l.transform.position).sqrMagnitude < 1.6f * 1.6f) s.fixtures.Add(r);
            }
            _slots.Add(s);
        }
        _slots.Sort((a, b) => a.hash.CompareTo(b.hash));

        // Doors that stand open or ajar (a DoorHinge turned away from rest) can swing from floor 3.
        foreach (Transform t in hub.building.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "DoorHinge" || Quaternion.Angle(t.localRotation, Quaternion.identity) < 10f) continue;
            _swingers.Add(t);
            _swingRest.Add(t.localRotation);
        }
        // Stable order, then keep the first few.
        List<(int h, Transform t, Quaternion q)> keyed = new List<(int, Transform, Quaternion)>();
        for (int i = 0; i < _swingers.Count; i++) keyed.Add((HubState.StableHash(PathOf(_swingers[i])), _swingers[i], _swingRest[i]));
        keyed.Sort((a, b) => a.h.CompareTo(b.h));
        _swingers.Clear();
        _swingRest.Clear();
        for (int i = 0; i < keyed.Count && i < swingingDoors; i++) { _swingers.Add(keyed[i].t); _swingRest.Add(keyed[i].q); }
    }

    private static string PathOf(Transform t)
    {
        string s = t.name;
        while (t.parent != null && t.parent.parent != null) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    /// <summary>The number of hub lights that are dead on `floor` (testing and the audit read it).</summary>
    public int DeadCount(int floor) => Mathf.RoundToInt(HubState.LightsOffFraction(floor) * _slots.Count);

    public int LightCount => _slots.Count;

    private void Apply(int floor)
    {
        int count = _slots.Count;
        int dead = Mathf.RoundToInt(HubState.LightsOffFraction(floor) * count);
        int flicker = Mathf.RoundToInt(HubState.FlickerFraction(floor) * count * 0.5f);
        for (int i = 0; i < count; i++)
        {
            Slot s = _slots[i];
            if (s.light == null) continue;
            bool isDead = i < dead;
            s.light.enabled = !isDead;
            foreach (Renderer r in s.fixtures)
            {
                if (r == null) continue;
                if (isDead)
                {
                    _block.Clear();
                    _block.SetColor("_EmissionColor", Color.black);
                    r.SetPropertyBlock(_block);
                }
                else r.SetPropertyBlock(null);
            }

            bool wantFlicker = !isDead && i >= dead && i < dead + flicker;
            if (s.light.GetComponent<HubLampFlicker>() != null && !_flickers.ContainsKey(s.light)) continue; // the builder's own flickering lamps stay as they are
            _flickers.TryGetValue(s.light, out HubLampFlicker f);
            if (wantFlicker && f == null)
            {
                f = s.light.gameObject.AddComponent<HubLampFlicker>();
                f.Configure(s.light, null, null);
                _flickers[s.light] = f;
            }
            if (f != null) f.enabled = wantFlicker;
        }

        RenderSettings.fogDensity = hub.hubFogDensity + fogPerFloor * (floor - 1);
        if (hub.Ambience != null) hub.Ambience.SetDecay(HubState.EventsPerMinute(floor), floor);
        RestoreDoors();
    }

    private void RestoreDoors()
    {
        for (int i = 0; i < _swingers.Count; i++) if (_swingers[i] != null) _swingers[i].localRotation = _swingRest[i];
    }

    private void Update()
    {
        if (!_built || _floor < 3 || HubState.IsFinale || hub == null || !hub.ContentActive) return;
        for (int i = 0; i < _swingers.Count; i++)
        {
            if (_swingers[i] == null) continue;
            float swing = Mathf.Sin(Time.time * (0.35f + 0.11f * i) + i * 1.7f) * swingDegrees;
            _swingers[i].localRotation = _swingRest[i] * Quaternion.Euler(0f, swing, 0f);
        }
    }
}
