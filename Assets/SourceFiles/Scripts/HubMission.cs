using System;
using TMPro;
using UnityEngine;

/// <summary>
/// F98: one floor's mini mission in the asylum hub (AsylumHub/Life/Missions/Floor n, floors 1-4; floor 5 keeps its key run). The wing
/// door of the floor stays LOCKED (WingDoor.lockedLine, the pattern KeyRunSetPiece uses) until a person in the hospital hands the player
/// the wing's key. Stages: Offered (nobody has been talked to) -> Active (the job is taken, the items are on their spots) -> Fetched
/// (everything is in the player's hands) -> Done (the key is given, the door is unlocked).
///
/// State lifetime: plain instance fields, deliberately NOT reset in OnDisable / on AsylumHub.OnLeaveHub (unlike HubSetPiece): the hub
/// content switches off and on around a shop round trip and the mission must still be where it was. A scene reload (next floor, CONTINUE,
/// Retry) creates fresh components, which is the reset - nothing is saved. Items, the door line, the HUD line and the goal glow are all
/// re-applied from the stage on every OnEnterHub. MazeGenerator.superEasyMode does not skip missions (it only makes the mazes trivial);
/// use <see cref="DebugComplete"/> to skip one in a test. Never active in the finale.
/// </summary>
public class HubMission : MonoBehaviour
{
    public enum Stage { Offered = 0, Active = 1, Fetched = 2, Done = 3 }

    [Range(1, 4)] public int floor = 1;
    public string title = "";
    [Tooltip("Whoever offers the job and, on Fetched, hands the key over (floors 1, 3, 4) - or just offers it (floor 2).")]
    public HubNpc giver;
    [Tooltip("Floor 2: Josef, who takes the tray and hands over the key.")]
    public HubNpc recipient;
    public HubMissionItem[] items = new HubMissionItem[0];

    [Header("Texts")]
    [Tooltip("The exact line the locked wing door answers E with, e.g. LOCKED · SISTER AGNES HAS THE KEY · NURSE STATION.")]
    public string lockedLine = "";
    [Tooltip("Shown with 'THE DOOR IS UNLOCKED' when the key is given, e.g. THE WARD A KEY.")]
    public string keyName = "THE KEY";
    public string objectiveOffered = "";
    public string objectiveActive = "";
    public string objectiveFetched = "";
    [Tooltip("Second HUD line while Active / Fetched, {n} / {total} = items taken. Empty = none.")]
    public string progressActive = "";
    public string progressFetched = "";

    [Header("Look")]
    [Tooltip("One light shared by all four missions (only one is ever in the hub's use): moved to the first thing left to find while Active.")]
    public Light goalLight;

    public Stage Current { get; private set; } = Stage.Offered;
    public int Collected { get; private set; }

    private static readonly Color HudColour = new Color(0.95f, 0.78f, 0.45f);

    private TextMeshProUGUI _hud;
    private bool _wasHere;
    private float _pulse;

    private AsylumHub Hub => AsylumHub.Active;
    private WingDoor Door => Hub != null && Hub.wingDoors != null && floor >= 1 && floor <= Hub.wingDoors.Length ? Hub.wingDoors[floor - 1] : null;

    /// <summary>The player is in the hub on this mission's floor (and it is not the finale).</summary>
    public bool IsHere => Hub != null && GameFlow.IsInHub && !HubState.IsFinale && HubState.Floor == floor;

    // ---------------------------------------------------------------- lifecycle

    private void OnEnable()
    {
        AsylumHub.OnEnterHub += HandleEnter;
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        // Not a reset: the stage stays. Only what is drawn goes.
        if (_hud != null) _hud.gameObject.SetActive(false);
        if (goalLight != null && IsOurLight()) goalLight.enabled = false;
        _wasHere = false;
    }

    private void OnDestroy()
    {
        if (_hud != null) Destroy(_hud.gameObject);
    }

    private bool IsOurLight() => goalLight != null && _ownsLight;
    private bool _ownsLight;

    private void HandleEnter(int enteredFloor)
    {
        Apply();
    }

    private void Update()
    {
        bool here = IsHere;
        if (here != _wasHere) Apply();
        if (here && _ownsLight && goalLight != null && goalLight.enabled)
        {
            _pulse += Time.deltaTime;
            goalLight.intensity = 0.55f + 0.2f * Mathf.Sin(_pulse * 2.6f);
        }
    }

    // ---------------------------------------------------------------- state -> world

    /// <summary>Re-applies the stage: the door line, the items, the glow and the HUD line. Idempotent.</summary>
    public void Apply()
    {
        bool here = IsHere;
        _wasHere = here;

        WingDoor door = Door;
        if (door != null)
        {
            if (here && Current != Stage.Done) door.lockedLine = lockedLine;
            else if (door.lockedLine == lockedLine) door.lockedLine = null; // never clobber another piece's line (the floor-5 key run)
        }

        HubMissionItem target = null;
        foreach (HubMissionItem item in items)
        {
            if (item == null) continue;
            bool show = here && Current == Stage.Active && !item.Taken;
            if (item.gameObject.activeSelf != show) item.gameObject.SetActive(show);
            if (show && target == null) target = item;
        }

        if (goalLight != null)
        {
            if (target != null)
            {
                _ownsLight = true;
                goalLight.transform.position = target.transform.position + Vector3.up * 0.35f;
                goalLight.enabled = true;
            }
            else if (_ownsLight)
            {
                _ownsLight = false;
                goalLight.enabled = false;
            }
        }

        RefreshHud(here);
    }

    private void RefreshHud(bool here)
    {
        if (!here || Current == Stage.Done)
        {
            if (_hud != null) _hud.gameObject.SetActive(false);
            return;
        }
        if (_hud == null)
        {
            Canvas canvas = RuntimeUi.ResolveCanvas();
            if (canvas == null) return;
            _hud = RuntimeUi.CreateText(canvas.transform, $"MissionLine_F{floor}", "", 24f, HudColour);
            _hud.alignment = TextAlignmentOptions.TopLeft;
            _hud.characterSpacing = 3f;
            _hud.textWrappingMode = TextWrappingModes.Normal;
            RectTransform r = _hud.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(38f, -164f);
            r.sizeDelta = new Vector2(760f, 80f);
        }
        string text = Fill(CurrentObjective());
        string progress = Fill(Current == Stage.Active ? progressActive : Current == Stage.Fetched ? progressFetched : "");
        _hud.text = string.IsNullOrEmpty(progress) ? $"OBJECTIVE  ·  {text}" : $"OBJECTIVE  ·  {text}\n<size=85%><color=#C9B48A>{progress}</color></size>";
        _hud.gameObject.SetActive(true);
    }

    private string Fill(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace("{n}", Collected.ToString()).Replace("{total}", items.Length.ToString());

    /// <summary>The objective for the current stage (HUD line text, also what the hub's repeating hint says).</summary>
    public string CurrentObjective()
    {
        switch (Current)
        {
            case Stage.Offered: return objectiveOffered;
            case Stage.Active: return objectiveActive;
            case Stage.Fetched: return objectiveFetched;
            default: return "";
        }
    }

    /// <summary>The current objective of the mission on `floor` (null = none, finished, or not in the hub): what AsylumHub's hint loop repeats.</summary>
    public static string HintFor(int floor)
    {
        HubMission m = ForFloor(floor);
        if (m == null || !m.IsHere || m.Current == Stage.Done) return null;
        string text = m.Fill(m.CurrentObjective());
        return string.IsNullOrEmpty(text) ? null : text;
    }

    // ---------------------------------------------------------------- items

    public bool IsItemAvailable(HubMissionItem item) => IsHere && Current == Stage.Active && item != null && !item.Taken;

    /// <summary>HubMissionItem.Interact: the item is in the player's hands.</summary>
    public void Take(HubMissionItem item)
    {
        Collected++;
        if (item != null) item.gameObject.SetActive(false);
        PlaySound(item != null ? item.transform.position : transform.position, floor == 1);
        bool all = Collected >= items.Length;
        if (all) Current = Stage.Fetched;
        string line = item != null ? Fill(item.TakenLine) : "";
        Apply();
        if (Hub != null && !string.IsNullOrEmpty(line)) Hub.SubtitleLine(line, 2.4f);
    }

    private void PlaySound(Vector3 at, bool key)
    {
        if (key && GameAudio.PlayAt(GameAudio.Library.keyJingle, at, 0.7f)) return;
        if (GameAudio.PlayAt(GameAudio.Library.paper, at, 0.7f)) return;
        AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.1f, key ? 900f : 520f, 0.18f), at, 0.7f);
    }

    // ---------------------------------------------------------------- conversations

    private void Accept()
    {
        if (Current != Stage.Offered) return;
        Current = Stage.Active;
        Apply();
        string text = Fill(objectiveActive);
        if (Hub != null && !string.IsNullOrEmpty(text)) Hub.SubtitleLine(text, 3.2f);
    }

    private void Complete()
    {
        if (Current == Stage.Done) return;
        Current = Stage.Done;
        Apply();
        Vector3 at = giver != null && giver.body != null ? giver.body.position : transform.position;
        if (recipient != null && recipient.body != null && floor == 2) at = recipient.body.position;
        if (!GameAudio.PlayAt(GameAudio.Library.keyJingle, at, 0.8f)) AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.12f, 900f, 0.2f), at, 0.8f);
        if (Hub != null) Hub.SubtitleLine($"{keyName} · THE DOOR IS UNLOCKED", 3.2f);
    }

    /// <summary>The nodes to say and what happens when they have been heard to the end. False = nothing to say (the panel does not open).</summary>
    public static bool TryConversation(HubNpc npc, out HubDialogue.Node[] nodes, out Action onDone, out int count, out int total)
    {
        nodes = null;
        onDone = null;
        count = 0;
        total = 0;
        if (npc == null) return false;

        if (npc.who == HubNpc.Who.Clerk)
        {
            string[] set = { "clerk.a", "clerk.b", "clerk.c" };
            nodes = HubDialogue.Get(HubState.IsFinale ? "clerk.finale" : set[npc.Talks % set.Length]);
            return nodes != null;
        }

        HubMission m = ForNpc(npc);
        if (m == null) return false;
        count = m.Collected;
        total = m.items != null ? m.items.Length : 0;

        string who = npc.who.ToString().ToLowerInvariant();
        int f = HubState.Floor;
        string key;
        if (m.floor > f) key = who + ".pre";
        else if (m.floor < f) key = HubDialogue.Has(who + ".later" + f) ? who + ".later" + f : who + ".after";
        else key = m.KeyHere(npc, who, out onDone);

        nodes = HubDialogue.Get(key);
        if (nodes == null) Debug.LogWarning($"HubMission: no conversation '{key}' for {npc.name} on floor {f}.", npc);
        return nodes != null;
    }

    private string KeyHere(HubNpc npc, string who, out Action onDone)
    {
        onDone = null;
        bool isGiver = npc == giver;
        bool twoPeople = recipient != null; // floor 2: the giver only sends, the recipient hands over the key
        if (isGiver)
        {
            switch (Current)
            {
                case Stage.Offered: onDone = Accept; return who + ".offer";
                case Stage.Active: return who + ".remind";
                case Stage.Fetched:
                    if (twoPeople) return who + ".carry";
                    onDone = Complete;
                    return who + ".return";
                default: return who + ".after";
            }
        }
        switch (Current)
        {
            case Stage.Offered: return who + ".hello";
            case Stage.Active: return HubDialogue.Has(who + ".remind") ? who + ".remind" : who + ".hello";
            case Stage.Fetched: onDone = Complete; return who + ".deliver";
            default: return who + ".after";
        }
    }

    // ---------------------------------------------------------------- lookups and debug

    public static HubMission ForFloor(int floor)
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.life == null) return null;
        foreach (HubMission m in hub.life.GetComponentsInChildren<HubMission>(true)) if (m.floor == floor) return m;
        return null;
    }

    private static HubMission ForNpc(HubNpc npc)
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.life == null) return null;
        foreach (HubMission m in hub.life.GetComponentsInChildren<HubMission>(true)) if (m.giver == npc || m.recipient == npc) return m;
        return null;
    }

    /// <summary>Testing only: finishes the mission of `floor` as if the key had been handed over (door unlocked, items gone). Returns a one-line report.</summary>
    public static string DebugComplete(int floor)
    {
        HubMission m = ForFloor(floor);
        if (m == null) return $"no mission on floor {floor}";
        foreach (HubMissionItem item in m.items) if (item != null) item.MarkTaken();
        m.Collected = m.items.Length;
        m.Current = Stage.Done;
        m.Apply();
        return $"floor {floor} mission '{m.title}' completed";
    }

    /// <summary>Testing only: one-line state.</summary>
    public static string DebugState(int floor)
    {
        HubMission m = ForFloor(floor);
        if (m == null) return $"no mission on floor {floor}";
        WingDoor door = m.Door;
        return $"floor {floor} '{m.title}': {m.Current}, items {m.Collected}/{m.items.Length}, door line '{(door != null ? door.lockedLine : "?")}'";
    }
}
