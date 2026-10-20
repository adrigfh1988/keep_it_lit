using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// State that has to survive a scene reload, and the reload itself. There is nothing else in the
/// project that outlives the scene, so this is deliberately a plain static class.
/// </summary>
public static class GameFlow
{
    /// <summary>Set by Restart; consumed by MainMenu.Start so a retry skips the title screen.</summary>
    public static bool SkipMenuOnLoad;

    /// <summary>Seed the next maze is built from. 0 = none pending; consumed by MazeGenerator.Awake.</summary>
    public static int PendingSeed;

    /// <summary>Time.time when the run started. Time.time stands still at timeScale 0, so this is net of menu and pause.</summary>
    public static float RunStartTime;

    /// <summary>True from the moment the game is started until an ending takes over.</summary>
    public static bool IsRunActive;

    /// <summary>Which floor the next maze is built for, 1 to FloorProfile.FinalFloor. Survives reloads so a retry stays on the same floor; only the main menu resets it.</summary>
    public static int CurrentFloor = 1;

    /// <summary>
    /// True while the player is in the room between floors. Not a run (lockers, capture, dread are off)
    /// and not an ending (cursor stays locked, pause works).
    /// </summary>
    public static bool IsInShop;

    /// <summary>
    /// F86: true while the player is in the Intake (the playable tutorial room before floor 1). Like the shop it is
    /// not a run (IsRunActive stays false: hunter, Stan, dread, phantoms and the escape timer stay inert), but unlike
    /// the shop the interactor, throwing and lockers all work - they are what it teaches. Reset in Reload (which every
    /// path out of the Intake goes through), ResetStatics and ReturnToMenu.
    /// </summary>
    public static bool IsInIntake;

    /// <summary>
    /// F92: true while the player is in the Asylum hub (the hospital between mazes). Like the shop and the Intake it is not a run
    /// (IsRunActive stays false, so the hunter, dread, phantoms, Stan and the escape timer are inert), but unlike the shop the
    /// interactor works - the wing doors need it. Set by AsylumHub, cleared when the wing door hands over to the maze; reset in
    /// Reload (every way out goes through it), ResetStatics and ReturnToMenu.
    /// </summary>
    public static bool IsInHub;

    /// <summary>F86: a safe room that is not a run - the HUD star counter, shard counter and consumable slots stay hidden. Readers that must stay ON in the Intake test IsInIntake explicitly instead. F92: the hub is one too.</summary>
    public static bool IsInSafeRoom => IsInShop || IsInIntake || IsInHub;

    /// <summary>
    /// F107 B1: true while the player is in a DESCENT (the endless mode outside the story). Set by the title button, cleared by START GAME and CONTINUE
    /// (the story can never start with it on). Unlike the flags above it is NOT reset by Reload: it has to survive the reload between levels. Reset in
    /// ResetStatics and ReturnToMenu. Every descent behaviour sits behind it, so with it false the story code path is unchanged.
    /// </summary>
    public static bool IsDescent;

    /// <summary>F86: PlayerPrefs flag set once the Intake has been finished or skipped. Only changes wording ("Welcome back"); the Intake is still offered every new campaign.</summary>
    public const string TutorialDoneKey = "keepitlit.tutorial.done";

    public static bool TutorialDone
    {
        get { try { return PlayerPrefs.GetInt(TutorialDoneKey, 0) == 1; } catch { return false; } }
        set { try { PlayerPrefs.SetInt(TutorialDoneKey, value ? 1 : 0); PlayerPrefs.Save(); } catch { /* best effort */ } }
    }

    /// <summary>The one place a run begins: stamps the run clock and switches IsRunActive on. Shared by MainMenu.StartGame, the no-title-screen path and leaving the Intake.</summary>
    public static void BeginRun()
    {
        RunStartTime = Time.time;
        IsRunActive = true;
    }

    /// <summary>F77: the mechanics pair each floor rolled this campaign, by floor (0 = not rolled yet). FloorProfile.RollObjectives
    /// reads floor n-1's entry so two consecutive floors never roll the same pair; a Retry of floor n re-reads n-1 and lands on the
    /// same pair. Campaign-scoped: reset here and in ResetStatics.</summary>
    public static int[] RolledObjectives = new int[FloorProfile.FinalFloor + 1];

    /// <summary>F127: the MazeMorphology each story floor rolled this campaign, by floor, so two consecutive floors never share the same morphology. Reset in ReturnToMenu, ResetStatics and DescentRun.Begin.</summary>
    public static int[] RolledMorphologies = new int[FloorProfile.FinalFloor + 1];

    /// <summary>F83: set by SaveSystem.ResumeCampaign for a save taken in the shop; consumed by MainMenu.Start (together with
    /// SkipMenuOnLoad), which enters the shop via GameOutcome.EnterShopDirectly instead of starting a run. Reset here, in ResetStatics and in ReturnToMenu.</summary>
    public static bool ResumeInShop;

    /// <summary>F92: set by NextFloor and by CONTINUE from a floor-start save; consumed by MainMenu.Start, which then opens the floor in the Asylum hub (when the scene has a usable one) instead of starting the run. Retry and Restart clear it. Reset here, in ResetStatics and in ReturnToMenu.</summary>
    public static bool ArriveInHub;

    /// <summary>F94/F116: the floor the shop sits after (the one just cleared). In the old shop room (fallback, IsInShop) CurrentFloor is still the cleared floor; in the hub's Commissary (IsInHub) AdvanceFloor has already run, so CurrentFloor is the floor about to be played.</summary>
    public static int ShopClearedFloor => IsInHub ? CurrentFloor - 1 : CurrentFloor;

    /// <summary>F94: the floor the shop's door leads to.</summary>
    public static int ShopNextFloor => ShopClearedFloor + 1;

    public static void Restart(bool sameMaze, int currentSeed, bool viaHub = false)
    {
        SkipMenuOnLoad = true;
        ArriveInHub = viaHub;
        PendingSeed = sameMaze ? currentSeed : 0;
        Reload();
    }

    /// <summary>F122: set by the new-campaign screen when its choice changes how floor 1 is built, so the scene is rebuilt once with the rules in place; consumed by MainMenu.Start, which then begins the campaign (Intake) instead of showing the title. Survives the reload on purpose; reset in ResetStatics and ReturnToMenu.</summary>
    public static bool BeginCampaignOnLoad;

    /// <summary>F122: reload into a brand-new scene that behaves like the title screen (not a retry), e.g. to rebuild floor 1 under a chosen difficulty.</summary>
    public static void ReloadFresh()
    {
        SkipMenuOnLoad = false;
        PendingSeed = 0;
        ArriveInHub = false;
        ResumeInShop = false;
        Reload();
    }

    /// <summary>Go up one floor with a fresh maze. Callers only offer this below the final floor.</summary>
    public static void NextFloor()
    {
        if (IsDescent)
        {
            // F107 B2: one level deeper. No save, no clamp at the final floor, no hub; CurrentFloor follows min(depth, FinalFloor) so the story
            // lookups stay in range. AdvanceFloor makes what was found for "next level" active (the level's seconds and stars were committed by the caller).
            DescentRun.Depth++;
            CurrentFloor = Mathf.Min(DescentRun.Depth, FloorProfile.FinalFloor);
            PlayerInventory.AdvanceFloor();
            Restart(false, 0);
            return;
        }

        CurrentFloor = Mathf.Min(CurrentFloor + 1, FloorProfile.FinalFloor);
        PlayerInventory.AdvanceFloor();
        // F83: floor-start checkpoint, after AdvanceFloor so ActiveModifiers are the perks for the floor being started.
        SaveSystem.WriteCheckpoint(CurrentFloor, false);
        Restart(false, 0, true); // F92: the new floor opens in the Asylum hub when the scene has one
    }

    public static void ReturnToMenu()
    {
        SkipMenuOnLoad = false;
        CurrentFloor = 1;
        PendingSeed = 0;
        ResumeInShop = false;
        ArriveInHub = false;
        PatientRecord.CommitShards(); // F122: bank the campaign's earnings in the record BEFORE the wallet is cleared (a descent banks nothing)
        IsDescent = false; // F107
        BeginCampaignOnLoad = false; // F122
        DescentRun.ResetState();
        // F83: deliberately does NOT delete the save - that is what puts CONTINUE on the title screen.
        PlayerWallet.ResetCampaign();
        PlayerInventory.ResetCampaign();
        DreadDirector.ResetCampaign();
        LoreArchive.ResetCampaign();
        RunRules.ResetCampaign();
        ContractState.ResetCampaign();
        HunterMemory.ResetCampaign(); // F105 C6
        LoopCorridor.ResetCampaign(); // F105 C4
        HubCaches.ResetCampaign(); // F114
        HubState.ResetCampaign(); // F117 (+ F121 BreachDone)
        HubBreach.ResetCampaign(); // F121
        HunterGrab.DisabledByRules = false; // F120
        CampaignRules.ResetCampaign(); // F122 (re-derives HubBreach.Mode)
        NightNurse.ResetCampaign();
        DarkBruno.ResetCampaign();
        System.Array.Clear(RolledObjectives, 0, RolledObjectives.Length);
        System.Array.Clear(RolledMorphologies, 0, RolledMorphologies.Length);
        Reload();
    }

    /// <summary>False in a WebGL build: Application.Quit does nothing in a browser tab, so menus hide their Quit/Exit buttons.</summary>
    public static bool CanQuit => Application.platform != RuntimePlatform.WebGLPlayer;

    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private static void Reload()
    {
        IsRunActive = false;
        IsInShop = false;
        IsInIntake = false;
        IsInHub = false;
        HubBreach.InProgress = false; // F121: every way out of the hub goes through here (Retrying survives on purpose)
        HubState.IsFinale = false; // F93
        // Restart can be called from the pause menu with both engaged. MainMenu.Awake zeroes the
        // clock again in the new scene anyway.
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // With Enter Play Mode > Reload Domain disabled, statics survive between presses of Play. A stale
    // SkipMenuOnLoad would bypass the title screen on the next run.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SkipMenuOnLoad = false;
        PendingSeed = 0;
        RunStartTime = 0f;
        IsRunActive = false;
        CurrentFloor = 1;
        IsInShop = false;
        IsInIntake = false;
        IsInHub = false;
        ResumeInShop = false;
        ArriveInHub = false;
        IsDescent = false; // F107
        BeginCampaignOnLoad = false; // F122
        System.Array.Clear(RolledObjectives, 0, RolledObjectives.Length);
        System.Array.Clear(RolledMorphologies, 0, RolledMorphologies.Length);
    }
}
