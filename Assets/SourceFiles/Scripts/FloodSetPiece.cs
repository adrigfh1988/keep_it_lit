using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F93 (S2, floor 3): the walk from the Boiler Room to the Morgue through the basement hall (HallUnderground). The hall's lights die one by
/// one behind the player as they advance, and Tomas knocks three times when the player first steps into the hall. It used to be a flooded
/// hall (water plane, wading, floaters); the water was removed on 8 Oct 2026 - it made no sense with a dry Boiler Room next to it - so the
/// class and its FloodSet object keep the old name. Everything is undone when the hub is left.
/// </summary>
public class FloodSetPiece : HubSetPiece
{
    [Tooltip("A trigger box over the hall (world AABB used): the player is 'in the hall' while inside it.")]
    [SerializeField] private BoxCollider zone;
    [SerializeField] private float lampBehindMetres = 2.2f;

    private struct Lamp { public Light light; public float baseIntensity; public bool dying; public float t; public bool done; }

    private readonly List<Lamp> _lamps = new List<Lamp>();
    private AudioSource _audio;
    private AudioClip _pop;
    private bool _inside, _knocked;

    public bool IsComplete => zone != null;

    protected override void OnBegin()
    {
        AsylumHub hub = Hub;
        if (hub == null || !IsComplete)
        {
            Debug.LogError("FloodSetPiece: the hall zone is missing. Run KEEP IT LIT > Build Missing Pieces. The hall's lights stay on.", this);
            return;
        }
        _knocked = false;
        _inside = false;

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
        foreach (Lamp l in _lamps)
        {
            if (l.light == null) continue;
            l.light.intensity = l.baseIntensity;
            l.light.enabled = true;
        }
        _lamps.Clear();
        _inside = false;
    }

    private void EnsureAudio()
    {
        if (_audio != null) return;
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;
        _audio.volume = 0.8f;
        _pop = DoorAudio.BuildClunkClip(0.12f, 650f, 0.5f);
    }

    private void Update()
    {
        AsylumHub hub = Hub;
        if (!Running || hub == null || hub.Player == null || !IsComplete || Time.timeScale <= 0f) return;

        Vector3 p = hub.Player.position;
        Bounds zb = zone.bounds;
        bool inside = zb.Contains(new Vector3(p.x, zb.center.y, p.z)) && p.y < zb.min.y + 1.05f; // feet within 0.85 m of the hall floor (the zone starts 0.2 m under it), so not on the stairs above
        if (inside != _inside)
        {
            _inside = inside;
            if (inside && !_knocked)
            {
                _knocked = true;
                HubPatient tomas = hub.life != null ? hub.life.Find("Patients/Tomas")?.GetComponent<HubPatient>() : null;
                if (tomas != null) tomas.KnockNow(3);
                hub.SubtitleLine(L10n.T("hub.flood.knocks"), 3.5f);
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
                    DoorAudio.Note("hall lamp pop");
                    if (!GameAudio.PlayAt(GameAudio.Library.bulbPop, l.light.transform.position, 0.8f)) _audio.PlayOneShot(_pop, 0.6f); // F97
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
