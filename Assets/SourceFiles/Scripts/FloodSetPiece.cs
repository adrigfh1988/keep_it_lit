using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F93 (S2, floor 3): the walk from the Boiler Room to the Morgue is through a flooded hall. The basement hall (HallUnderground) is under
/// 0.25 m of dark water (a transparent URP Lit plane, smoothness 0.8 - the documented exception to the 0.35 clamp, so the torch glints on it),
/// the player wades at 0.75 x speed (ThirdPersonController.SpeedMultiplier) with splash footsteps, paper, cans and a sheeted body float and bob,
/// the hall's lights die one by one behind the player as they advance, and Tomas knocks three times when the player first steps into the water.
/// Everything is undone when the hub is left.
/// </summary>
public class FloodSetPiece : HubSetPiece
{
    [SerializeField] private Transform water;
    [Tooltip("A trigger box over the flooded part of the hall (world AABB used): the player is 'in the water' while inside it.")]
    [SerializeField] private BoxCollider zone;
    [SerializeField] private Transform[] floaters = new Transform[0];
    [SerializeField, Range(0.3f, 1f)] private float wadeSpeed = 0.75f;
    [SerializeField] private float lampBehindMetres = 2.2f;

    private struct Lamp { public Light light; public float baseIntensity; public bool dying; public float t; public bool done; }

    private readonly List<Lamp> _lamps = new List<Lamp>();
    private Vector3[] _floatBase;
    private Quaternion[] _floatRot;
    private AudioSource _audio;
    private AudioClip _splash, _pop;
    private bool _inWater, _knocked;
    private float _nextSplash;
    private MaterialPropertyBlock _block;
    private Renderer _waterRenderer;

    public bool IsComplete => water != null && zone != null;

    protected override void OnBegin()
    {
        AsylumHub hub = Hub;
        if (hub == null || !IsComplete)
        {
            Debug.LogError("FloodSetPiece: the water or its zone is missing. Run LIGHTS OUT > Build Missing Pieces. The hall stays dry.", this);
            return;
        }
        _knocked = false;
        _inWater = false;
        water.gameObject.SetActive(true);
        _waterRenderer = water.GetComponent<Renderer>();
        _block = new MaterialPropertyBlock();

        _floatBase = new Vector3[floaters.Length];
        _floatRot = new Quaternion[floaters.Length];
        for (int i = 0; i < floaters.Length; i++)
        {
            if (floaters[i] == null) continue;
            floaters[i].gameObject.SetActive(true);
            _floatBase[i] = floaters[i].position;
            _floatRot[i] = floaters[i].rotation;
        }

        // The hall's own lamps, so they can die behind the player.
        _lamps.Clear();
        Bounds b = zone.bounds;
        b.Expand(new Vector3(1.5f, 4f, 1.5f));
        foreach (Light l in hub.GetComponentsInChildren<Light>(false))
        {
            if (!l.enabled || !b.Contains(l.transform.position)) continue;
            _lamps.Add(new Lamp { light = l, baseIntensity = l.intensity });
        }
        EnsureAudio();
    }

    protected override void OnEnd()
    {
        AsylumHub hub = Hub;
        if (hub != null && hub.Controller != null) hub.Controller.SpeedMultiplier = 1f;
        if (water != null) water.gameObject.SetActive(false);
        for (int i = 0; i < floaters.Length; i++) if (floaters[i] != null) floaters[i].gameObject.SetActive(false);
        foreach (Lamp l in _lamps)
        {
            if (l.light == null) continue;
            l.light.intensity = l.baseIntensity;
            l.light.enabled = true;
        }
        _lamps.Clear();
        _inWater = false;
    }

    private void EnsureAudio()
    {
        if (_audio != null) return;
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;
        _audio.volume = 0.8f;
        int rate = 22050, n = (int)(rate * 0.32f);
        float[] d = new float[n];
        uint seed = 7;
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            seed = seed * 1664525u + 1013904223u;
            float noise = ((seed >> 16) & 0xFFFF) / 32767.5f - 1f;
            low += (noise - low) * 0.25f;
            float t = i / (float)rate;
            d[i] = (low * 0.9f + noise * 0.15f) * Mathf.Exp(-t * 11f) * Mathf.Clamp01(t * 90f);
        }
        _splash = AudioClip.Create("WadeSplash", n, 1, rate, false);
        _splash.SetData(d, 0);
        _pop = DoorAudio.BuildClunkClip(0.12f, 650f, 0.5f);
    }

    private void Update()
    {
        AsylumHub hub = Hub;
        if (!Running || hub == null || hub.Player == null || !IsComplete || Time.timeScale <= 0f) return;

        // water: a slow drift of the ripples
        if (_waterRenderer != null)
        {
            _waterRenderer.GetPropertyBlock(_block);
            _block.SetVector("_BaseMap_ST", new Vector4(5f, 12f, Time.time * 0.012f, Time.time * 0.02f));
            _waterRenderer.SetPropertyBlock(_block);
        }
        for (int i = 0; i < floaters.Length; i++)
        {
            if (floaters[i] == null) continue;
            float t = Time.time + i * 1.7f;
            floaters[i].SetPositionAndRotation(_floatBase[i] + Vector3.up * (Mathf.Sin(t * 1.3f) * 0.025f),
                _floatRot[i] * Quaternion.Euler(Mathf.Sin(t * 0.9f) * 2.5f, Mathf.Sin(t * 0.3f) * 6f, Mathf.Cos(t * 1.1f) * 2.5f));
        }

        // wading
        Vector3 p = hub.Player.position;
        Bounds zb = zone.bounds;
        bool inside = zb.Contains(new Vector3(p.x, zb.center.y, p.z)) && p.y < water.position.y + 0.6f;
        if (inside != _inWater)
        {
            _inWater = inside;
            if (hub.Controller != null) hub.Controller.SpeedMultiplier = inside ? wadeSpeed : 1f;
            if (inside && !_knocked)
            {
                _knocked = true;
                HubPatient tomas = hub.life != null ? hub.life.Find("Patients/Tomas")?.GetComponent<HubPatient>() : null;
                if (tomas != null) tomas.KnockNow(3);
                hub.SubtitleLine("THE WATER IS COLD. SOMETHING KNOCKS THREE TIMES.", 3.5f);
            }
        }
        if (inside)
        {
            CharacterController cc = hub.Player.GetComponent<CharacterController>();
            float speed = cc != null ? new Vector3(cc.velocity.x, 0f, cc.velocity.z).magnitude : 0f;
            if (speed > 0.4f && Time.time >= _nextSplash)
            {
                _nextSplash = Time.time + (speed > 3f ? 0.3f : 0.5f);
                _audio.pitch = Random.Range(0.85f, 1.15f);
                _audio.PlayOneShot(_splash, 0.8f);
            }
        }

        // the lights die behind the player, one at a time
        for (int i = 0; i < _lamps.Count; i++)
        {
            Lamp l = _lamps[i];
            if (l.done || l.light == null) continue;
            if (!l.dying)
            {
                // "behind" is back toward the Boiler Room: smaller x than the player (the route runs east through the hall)
                if (inside && l.light.transform.position.x < p.x - lampBehindMetres)
                {
                    l.dying = true;
                    _audio.PlayOneShot(_pop, 0.6f);
                }
            }
            else
            {
                l.t += Time.deltaTime / 0.6f;
                l.light.intensity = l.baseIntensity * Mathf.Clamp01(1f - l.t) * (Random.value > 0.3f ? 1f : 0.3f);
                if (l.t >= 1f) { l.light.enabled = false; l.done = true; }
            }
            _lamps[i] = l;
        }
    }
}
