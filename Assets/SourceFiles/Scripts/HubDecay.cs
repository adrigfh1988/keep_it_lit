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
    [Header("F96 flicker bursts")]
    [Tooltip("Share of the living lamps that get occasional flicker bursts (picked by a stable hash).")]
    [SerializeField, Range(0f, 1f)] private float burstShare = 0.25f;
    [SerializeField] private Vector2 secondsBetweenBursts = new Vector2(6f, 25f);
    [SerializeField] private Vector2 burstSeconds = new Vector2(0.2f, 1.2f);
    [SerializeField, Range(0f, 1f)] private float brownoutChance = 0.12f;
    [SerializeField] private Vector2 brownoutSeconds = new Vector2(2f, 3f);

    // One lamp that bursts. Only a light that is ON when the burst starts is touched, and exactly what was changed is put back.
    private class Burster
    {
        public Light light;
        public List<Renderer> fixtures;
        public HubLampFlicker flicker; // a lamp with its own flicker takes the burst as a multiplier so the buzz follows
        public float next, end, nextStep, level = 1f, baseIntensity, lastWritten = -1f;
        public bool bursting, brown, useFlicker;
        public Color[] baseEmission;
    }

    private readonly List<Burster> _bursters = new List<Burster>();
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
        EndAllBursts();
    }

    private void HandleLeave()
    {
        EndAllBursts();
        RestoreDoors();
        _floor = 0;
    }

    private void HandleEnter(int floor)
    {
        if (hub == null) hub = AsylumHub.Active;
        if (hub == null) return;
        if (!_built) Build();
        _floor = floor;
        EndAllBursts();
        if (HubState.IsFinale) return; // HubFinale owns the lights then
        Apply(floor);
        PickBursters(floor);
    }

    // ---------------------------------------------------------------- F96 flicker bursts

    private void PickBursters(int floor)
    {
        _bursters.Clear();
        int dead = Mathf.RoundToInt(HubState.LightsOffFraction(floor) * _slots.Count);
        for (int i = dead; i < _slots.Count; i++) // the first `dead` slots are the dead ones: they stay dead
        {
            Slot s = _slots[i];
            if (s.light == null || !s.light.transform.IsChildOf(hub.building)) continue;
            bool builderFlicker = s.light.GetComponent<HubLampFlicker>() != null && !_flickers.ContainsKey(s.light); // the lamps with a buzz source: always burst
            if (!builderFlicker &&(uint)s.hash % 100u >= burstShare * 100f) continue;
            Burster b = new Burster { light = s.light, fixtures = s.fixtures, flicker = s.light.GetComponent<HubLampFlicker>() };
            b.baseEmission = new Color[s.fixtures.Count];
            for (int k = 0; k < s.fixtures.Count; k++)
            {
                Material m = s.fixtures[k] != null ? s.fixtures[k].sharedMaterial : null;
                b.baseEmission[k] = m != null && m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            }
            float slow = s.light.shadows != LightShadows.None ? 2.5f : 1f; // the shadowed lamps flicker rarely
            b.next = Time.time + Random.Range(2f, secondsBetweenBursts.y) * slow;
            _bursters.Add(b);
        }
    }

    private void UpdateBursts()
    {
        float t = Time.time;
        for (int i = 0; i < _bursters.Count; i++)
        {
            Burster b = _bursters[i];
            if (b.light == null) continue;
            if (!b.bursting)
            {
                if (t < b.next) continue;
                bool own = b.flicker != null && b.flicker.enabled;
                // Only a lamp that is on right now: a dead or switched-off one is skipped and tried again later.
                if (!b.light.enabled || (!own && b.light.intensity < 0.01f)) { b.next = t + Random.Range(secondsBetweenBursts.x, secondsBetweenBursts.y); continue; }
                b.useFlicker = own;
                b.baseIntensity = b.light.intensity;
                b.brown = Random.value < brownoutChance;
                b.end = t + (b.brown ? Random.Range(brownoutSeconds.x, brownoutSeconds.y) : Random.Range(burstSeconds.x, burstSeconds.y));
                b.nextStep = t;
                b.bursting = true;
                // F97: a quiet zap from the lamp on about half of the flicker bursts (recorded clips only, audio only).
                if (!b.brown && Random.value < 0.5f) GameAudio.PlayAt(GameAudio.Library.flickerCrackle, b.light.transform.position, 0.35f, 1f, 14f);
            }

            if (!b.light.enabled) { EndBurst(b, false); continue; } // someone else switched it off: leave it off
            // Someone else is driving the intensity (the flood's dying lamps): hand it over, do not write it back.
            if (!b.useFlicker && Mathf.Abs(b.light.intensity - b.lastWritten) > 0.001f && b.lastWritten >= 0f) { b.lastWritten = -1f; EndBurst(b, false, false); continue; }
            if (t >= b.end) { EndBurst(b, true); continue; }
            if (t >= b.nextStep)
            {
                if (b.brown) { b.level = Random.Range(0.12f, 0.38f); b.nextStep = t + Random.Range(0.08f, 0.3f); }
                else
                {
                    float r = Random.value;
                    b.level = r < 0.4f ? 0f : r < 0.65f ? Random.Range(0.1f, 0.35f) : r < 0.85f ? Random.Range(0.5f, 0.8f) : 1f;
                    b.nextStep = t + Random.Range(0.03f, 0.12f);
                }
            }
            SetBurstLevel(b, b.level);
        }
    }

    private void SetBurstLevel(Burster b, float level)
    {
        if (b.useFlicker && b.flicker != null) b.flicker.External = level;
        else { b.light.intensity = b.baseIntensity * level; b.lastWritten = b.light.intensity; }
        for (int k = 0; k < b.fixtures.Count; k++)
        {
            Renderer r = b.fixtures[k];
            if (r == null) continue;
            _block.Clear();
            _block.SetColor("_EmissionColor", b.baseEmission[k] * level);
            r.SetPropertyBlock(_block);
        }
    }

    private void EndBurst(Burster b, bool lampStillOn, bool restoreIntensity = true)
    {
        b.lastWritten = -1f;
        if (b.bursting)
        {
            if (b.flicker != null) b.flicker.External = 1f;
            if (!b.useFlicker && b.light != null && lampStillOn && restoreIntensity) b.light.intensity = b.baseIntensity;
            for (int k = 0; k < b.fixtures.Count; k++)
            {
                Renderer r = b.fixtures[k];
                if (r == null) continue;
                if (b.light != null && b.light.enabled) r.SetPropertyBlock(null);
                else { _block.Clear(); _block.SetColor("_EmissionColor", Color.black); r.SetPropertyBlock(_block); }
            }
        }
        b.bursting = false;
        b.next = Time.time + Random.Range(secondsBetweenBursts.x, secondsBetweenBursts.y) * (b.light != null && b.light.shadows != LightShadows.None ? 2.5f : 1f);
    }

    private void EndAllBursts()
    {
        if (_block == null) return;
        foreach (Burster b in _bursters) if (b.bursting) EndBurst(b, b.light != null && b.light.enabled);
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
        if (!_built || HubState.IsFinale || hub == null || !hub.ContentActive) return;
        UpdateBursts();
        if (_floor < 3) return;
        for (int i = 0; i < _swingers.Count; i++)
        {
            if (_swingers[i] == null) continue;
            float swing = Mathf.Sin(Time.time * (0.35f + 0.11f * i) + i * 1.7f) * swingDegrees;
            _swingers[i].localRotation = _swingRest[i] * Quaternion.Euler(0f, swing, 0f);
        }
    }
}
