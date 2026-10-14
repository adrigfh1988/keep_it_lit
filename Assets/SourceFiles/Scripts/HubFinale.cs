using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F93 (E10, idea 9): the Director's-office finale. Clearing floor 5 brings the player back into the asylum at the Director's desk
/// (GameOutcome.FinaleSequence -> AsylumHub.EnterFinale). Here the hub is dressed for it: every light is dead except the desk lamp and up to ten
/// red emergency lights along the way to the main doors, the three patients stand along that route as memories (HubPatient), and there is
/// no pursuer. Two ways out, both ending through <see cref="GameOutcome.CompleteFinale"/>:
///  - A DISCHARGED (always): the main doors (<see cref="HubExitDoor"/>) swing open, the player walks out into a cold grey-white;
///  - B LIGHTS OUT (only with all five maze files read, then the Director's own file 105 from the desk drawer): the master switch behind the
///    desk, hold E for 3 s - every lamp and every sound cut at once, 2.5 s of nothing, then the ending.
/// The first entry that is not the finale puts every light back.
/// </summary>
public class HubFinale : MonoBehaviour
{
    [SerializeField] private HubFinaleDesk desk;
    [SerializeField] private MasterSwitch masterSwitch;
    [SerializeField] private HubExitDoor exitDoor;
    [SerializeField] private int routeLights = 10;
    [SerializeField] private Color emergency = new Color(1f, 0.1f, 0.06f);

    private struct Saved { public Light light; public bool enabled; public Color color; public float intensity; }
    private readonly List<Saved> _saved = new List<Saved>();
    private bool _dressed, _finished;

    public bool IsComplete => desk != null && masterSwitch != null && exitDoor != null;
    public bool Finished => _finished;

    private void OnEnable() { AsylumHub.OnEnterHub += HandleEnter; }
    private void OnDisable() { AsylumHub.OnEnterHub -= HandleEnter; }

    private void HandleEnter(int floor)
    {
        if (HubState.IsFinale) Begin();
        else Restore();
    }

    private void Begin()
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || !IsComplete) return;
        _finished = false;
        desk.Bind(this);
        masterSwitch.Bind(this);
        exitDoor.Bind(this);
        masterSwitch.gameObject.SetActive(false);
        Dress(hub);
    }

    // ---------------------------------------------------------------- the lights

    private void Dress(AsylumHub hub)
    {
        Restore();
        _dressed = true;
        Transform deskPoint = hub.points != null ? hub.points.Find("Director_Desk") : null;
        Vector3 deskPos = deskPoint != null ? deskPoint.position : hub.transform.position;

        List<Light> all = new List<Light>();
        HashSet<Light> exempt = new HashSet<Light>();
        foreach (WingDoor door in hub.wingDoors) if (door != null && door.beacon != null) exempt.Add(door.beacon);
        foreach (Light l in hub.GetComponentsInChildren<Light>(true))
        {
            if (hub.life != null && l.transform.IsChildOf(hub.life)) continue;
            all.Add(l);
            _saved.Add(new Saved { light = l, enabled = l.enabled, color = l.color, intensity = l.intensity });
        }

        // The desk lamp: the nearest light to the desk, as it was.
        // F115: never a wing beacon (they are switched off below) - the office fill light over the desk is what is meant.
        Light lamp = Nearest(all, deskPos, exempt, 9f);
        HashSet<Light> keep = new HashSet<Light>();
        if (lamp != null) keep.Add(lamp);

        // The red route: the way to the main doors, picked off the Hub navmesh path.
        List<Vector3> samples = RouteSamples(hub, deskPos);
        int picked = 0;
        foreach (Vector3 p in samples)
        {
            if (picked >= routeLights) break;
            Light l = Nearest(all, p, keep, 5.5f);
            if (l == null) continue;
            keep.Add(l);
            l.color = emergency;
            l.intensity = Mathf.Max(0.9f, l.intensity * 0.55f);
            picked++;
        }

        foreach (Light l in all)
        {
            if (keep.Contains(l)) l.enabled = true;
            else l.enabled = false;
        }
        // The wing beacons are story pieces of the maze run; the finale has none.
        foreach (Light b in exempt) b.enabled = false;
        // Anything that would re-light them (flickers added by HubDecay) is stood down by HubDecay itself in the finale.
        if (hub.Hud != null) hub.Hud.ShowSubtitle(L10n.T("hub.finale.office"), 4f);
    }

    private static Light Nearest(List<Light> all, Vector3 p, HashSet<Light> exclude, float max)
    {
        Light best = null;
        float bestD = max;
        foreach (Light l in all)
        {
            if (exclude != null && exclude.Contains(l)) continue;
            float d = Vector3.Distance(l.transform.position, p);
            if (d < bestD) { bestD = d; best = l; }
        }
        return best;
    }

    private static List<Vector3> RouteSamples(AsylumHub hub, Vector3 from)
    {
        List<Vector3> samples = new List<Vector3>();
        int type = -1;
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            NavMeshBuildSettings s = NavMesh.GetSettingsByIndex(i);
            if (NavMesh.GetSettingsNameFromID(s.agentTypeID) == "Hub") type = s.agentTypeID;
        }
        NavMeshPath path = new NavMeshPath();
        if (type != -1 && NavMesh.CalculatePath(from, hub.mainEntrance.position, new NavMeshQueryFilter { agentTypeID = type, areaMask = NavMesh.AllAreas }, path) && path.corners.Length > 1)
        {
            Vector3[] c = path.corners;
            float carry = 0f;
            samples.Add(c[0]);
            for (int i = 1; i < c.Length; i++)
            {
                float seg = Vector3.Distance(c[i - 1], c[i]);
                float at = 3f - carry;
                while (at <= seg)
                {
                    samples.Add(Vector3.Lerp(c[i - 1], c[i], at / seg));
                    at += 3f;
                }
                carry = seg - (at - 3f);
            }
            samples.Add(c[c.Length - 1]);
        }
        else samples.Add(hub.mainEntrance.position);
        return samples;
    }

    private void Restore()
    {
        if (!_dressed) return;
        foreach (Saved s in _saved)
        {
            if (s.light == null) continue;
            s.light.enabled = s.enabled;
            s.light.color = s.color;
            s.light.intensity = s.intensity;
        }
        _saved.Clear();
        _dressed = false;
    }

    // ---------------------------------------------------------------- the desk and the switch

    /// <summary>The drawer opened and file 105 was read: the switch behind the desk shows.</summary>
    public void RevealSwitch()
    {
        if (masterSwitch != null && !masterSwitch.gameObject.activeSelf)
        {
            masterSwitch.gameObject.SetActive(true);
            GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.4f, 140f, 0.6f), masterSwitch.transform.position, 0.8f);
        }
    }

    // ---------------------------------------------------------------- the endings

    /// <summary>Ending A: out through the main doors into the grey light.</summary>
    public void Discharge()
    {
        if (_finished) return;
        if (AsylumHub.Active != null && AsylumHub.Active.Reader != null) AsylumHub.Active.Reader.Close();
        _finished = true;
        StartCoroutine(exitDoor.WalkOut(() => CompleteWith(GameOutcome.FinaleEnding.Discharged)));
    }

    /// <summary>Ending B: the master switch. Everything dies at once, then 2.5 s of nothing.</summary>
    public void PullSwitch()
    {
        if (_finished) return;
        if (AsylumHub.Active != null && AsylumHub.Active.Reader != null) AsylumHub.Active.Reader.Close();
        _finished = true;
        StartCoroutine(LightsOut());
    }

    private IEnumerator LightsOut()
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub != null)
        {
            hub.SetScripted(true);
            if (hub.Controller != null) hub.Controller.MovementLocked = true;
            if (hub.Torch != null) { hub.Torch.InputEnabled = false; hub.Torch.ForceOff(); }
            if (hub.Hud != null) { hub.Hud.SetPrompt(null); hub.Hud.SetFocusPrompt(null); }
            if (hub.Ambience != null) hub.Ambience.Mute();
            foreach (Light l in hub.GetComponentsInChildren<Light>(true)) l.enabled = false;
            AudioListener.pause = false;
        }
        Vector3 switchAt = masterSwitch != null ? masterSwitch.transform.position : Vector3.zero;
        // F97: the recorded lever, then the building winding down (one-shots live at the scene root, so the sweep below leaves them alone).
        bool lever = GameAudio.PlayAt(GameAudio.Library.masterLever, switchAt, 1f);
        if (!lever) GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.5f, 70f, 0.7f), switchAt, 1f);
        // Everything that makes a sound in the hub is silent now: stop any source under the hub.
        if (hub != null) foreach (AudioSource a in hub.GetComponentsInChildren<AudioSource>(true)) if (a.isPlaying) a.Stop();
        if (lever) yield return new WaitForSecondsRealtime(0.6f);
        GameAudio.PlayAt(GameAudio.Library.powerDown, switchAt, 1f);
        yield return new WaitForSecondsRealtime(lever ? 1.9f : 2.5f);
        CompleteWith(GameOutcome.FinaleEnding.LightsOut);
    }

    private void CompleteWith(GameOutcome.FinaleEnding ending)
    {
        AsylumHub h = AsylumHub.Active;
        if (h != null && h.Reader != null) h.Reader.Close(); // a case file left open must not sit under the ending text
        if (h != null && h.Controller != null) h.Controller.enabled = false; // nothing left to walk; also stops gravity pulling the player out through the open door
        GameOutcome outcome = FindAnyObjectByType<GameOutcome>();
        if (outcome != null) outcome.CompleteFinale(ending);
        else Debug.LogError("HubFinale: there is no GameOutcome, so the ending cannot complete.", this);
    }
}
