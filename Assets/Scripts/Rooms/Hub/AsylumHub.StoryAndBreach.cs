using System.Collections;
using UnityEngine;

public partial class AsylumHub
{
    // ---------------------------------------------------------------- debug entry points (F93 E11)

    /// <summary>
    /// Testing only (RunCommand in Play; never reachable from player UI): makes `floor` the current floor and runs the normal
    /// enter path at that floor's arrival point (the front door for floor 1). Call it on the title screen, in the Intake or while
    /// already in the hub - not during a maze run. Returns a one-line report.
    /// </summary>
    public static string DebugEnter(int floor)
    {
        AsylumHub h = Active;
        if (h == null || !h.CanEnter) return "no usable hub";
        if (h.IsTransitioning || h.Leaving) return "hub is busy";
        if (GameFlow.IsRunActive) return "a maze run is active";
        floor = Mathf.Clamp(floor, 1, 5);
        GameFlow.CurrentFloor = floor;
        HubState.IsFinale = false;
        if (!GameFlow.IsInHub) h.RememberReturn();
        Transform arrival = floor >= 2 && h.wingDoors[floor - 2] != null ? h.wingDoors[floor - 2].arrivalPoint : h.mainEntrance;
        h.StartCoroutine(h.EnterRoutine(arrival, floor));
        return $"entering the hub for floor {floor} at {arrival.name}";
    }

    /// <summary>
    /// Testing only: the finale stage (G6 finishes it). Sets HubState.IsFinale, floor 5, optionally marks case files 100..104 read
    /// (never 105 - that is read at the Director's desk), and enters at the Director_Desk point.
    /// </summary>
    public static string DebugFinale(bool allFiles)
    {
        AsylumHub h = Active;
        if (h == null || !h.CanEnter) return "no usable hub";
        if (h.IsTransitioning || h.Leaving) return "hub is busy";
        if (GameFlow.IsRunActive) return "a maze run is active";
        GameFlow.CurrentFloor = 5;
        HubState.IsFinale = true;
        if (allFiles)
        {
            for (int i = 0; i < PatientFiles.MazeFileCount; i++) LoreArchive.MarkRead(PatientFiles.FirstId + i);
        }
        if (!h.CanFinale) return "the finale piece (AsylumHub/Life/Finale) is missing or incomplete";
        h.EnterFinale();
        return $"finale: the Director's desk, files read {HubState.FilesRead}/5";
    }

    /// <summary>GameOutcome.FinaleSequence, at black: clearing floor 5 brings the player back to the Director's desk. Single use per run (GameOutcome guards).</summary>
    public void EnterFinale()
    {
        if (!CanFinale) return;
        HubState.IsFinale = true;
        GameFlow.CurrentFloor = Mathf.Max(GameFlow.CurrentFloor, 5);
        if (!GameFlow.IsInHub) RememberReturn();
        Transform desk = points != null ? points.Find("Director_Desk") : null;
        StartCoroutine(EnterRoutine(desk != null ? desk : mainEntrance, 5));
    }

    /// <summary>F100: the floor's mission has been finished (key handed over). False when the floor has none.</summary>
    public bool MissionDone(int floor)
    {
        HubMission m = HubMission.ForFloor(floor);
        return m != null && m.Current == HubMission.Stage.Done;
    }

    /// <summary>True while a hub pursuer is spotting, chasing or catching the player (the Commissary counter and the dialogue panel refuse then).</summary>
    public bool PursuerAfterPlayer()
    {
        if (life == null) return false;
        foreach (HubStalker pursuer in life.GetComponentsInChildren<HubStalker>(true))
        {
            if (pursuer == null || !pursuer.isActiveAndEnabled) continue;
            HubStalker.Mode mode = pursuer.CurrentMode;
            if (mode == HubStalker.Mode.Spotted || mode == HubStalker.Mode.Chase || mode == HubStalker.Mode.Catching) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- story helpers (F93 G4+)

    /// <summary>Binds every hub locker (AsylumHub/Life/Lockers, clones of the Ward locker) to the player systems, like MazeGenerator does for a maze's.</summary>
    private void BindLockers()
    {
        if (life == null || _player == null) return;
        foreach (Locker locker in life.GetComponentsInChildren<Locker>(true))
        {
            Vector3 inside = locker.InsideAnchor != null ? locker.InsideAnchor.position : locker.transform.TransformPoint(new Vector3(0f, 0f, 0.35f));
            Vector3 front = locker.FrontAnchor != null ? locker.FrontAnchor.position : locker.transform.TransformPoint(new Vector3(0f, 0f, 1.6f));
            locker.Configure(inside, front, locker.transform.eulerAngles.y, locker.Door);
            locker.Bind(_player, _stealth, _flashlight, _hud, _interactor);
        }
    }

    /// <summary>Moves the player (CharacterController off around the write) and turns the view.</summary>
    public void TeleportPlayer(Vector3 position, float yaw) => Teleport(position, yaw);

    /// <summary>Moves the player without touching the view (rides, scripted carries).</summary>
    public void MovePlayerTo(Vector3 position) => MovePlayer(position);

    /// <summary>A set piece is moving the player by script (the lift ride): pausing and catches are held off until it is switched off again.</summary>
    public void SetScripted(bool on) { IsTransitioning = on; }

    /// <summary>
    /// A creature caught the player in the hub (never GameOutcome - the hub has no run to lose): black, a sting, the player wakes at the
    /// door they came through, `reset` puts the creature back, a line. False when something else is already moving the player.
    /// </summary>
    public bool BeginCatch(System.Action reset, string line = null)
    {
        if (IsTransitioning || Leaving || !ContentActive || GameFlow.IsRunActive) return false;
        StartCoroutine(CatchRoutine(reset, line));
        return true;
    }

    private IEnumerator CatchRoutine(System.Action reset, string line)
    {
        IsTransitioning = true;
        PlayerLock.Freeze(_player, true);
        if (_hud != null) { _hud.SetPrompt(null); _hud.SetFocusPrompt(null); }
        HorrorAudioDirector director = FindAnyObjectByType<HorrorAudioDirector>();
        if (GameAudio.Play2D(GameAudio.Library.caught, 1f)) GameAudio.Play2D(GameAudio.Library.caughtLayer, 0.8f); // F97: a hard hit, with a dark boom under it
        else if (director != null) director.PlayCaptureSting();
        else GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.9f, 55f, 0.9f), _player.position, 1f);
        EnsureBlackout();
        yield return Fade(_blackout != null ? _blackout.color.a : 0f, 1f, 0.25f);
        yield return new WaitForSecondsRealtime(0.8f);
        foreach (Locker locker in life != null ? life.GetComponentsInChildren<Locker>(true) : new Locker[0])
        {
            if (locker != null && locker.Occupied) locker.ForceLeave();
        }
        Teleport(ArrivalPosition, ArrivalYaw);
        reset?.Invoke();
        if (_flashlight != null) { _flashlight.InputEnabled = true; _flashlight.SetOn(true); }
        if (_controller != null) _controller.MovementLocked = false;
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
        if (line == null) line = L10n.T("hub.catch.wake"); // S9: the default line is translated when it is shown
        if (_hud != null && !string.IsNullOrEmpty(line)) _hud.ShowSubtitle(line, 3.2f);
        yield return Fade(1f, 0f, 0.9f);
        if (_blackoutRoot != null) _blackoutRoot.SetActive(false);
        IsTransitioning = false;
    }
}
