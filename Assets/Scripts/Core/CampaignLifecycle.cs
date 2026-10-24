using System;
using UnityEngine;

/// <summary>
/// Single registry for campaign-scoped static state: resetting across a campaign or DESCENT start
/// (<see cref="ResetAllCampaignState"/>) and snapshotting / restoring <see cref="SaveData"/>
/// (<see cref="CaptureCampaignState"/>, <see cref="RestoreCampaignState"/>).
/// </summary>
public static class CampaignLifecycle
{
    /// <summary>
    /// Resets every campaign-scoped static system back to its clean baseline.
    /// Called by <see cref="GameFlow.ReturnToMenu"/> and <see cref="DescentRun.Begin"/>.
    /// </summary>
    public static void ResetAllCampaignState()
    {
        DescentRun.ResetState();
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
        Elias.ResetCampaign();
        GameFlow.CurrentFloor = 1;
        Array.Clear(GameFlow.RolledObjectives, 0, GameFlow.RolledObjectives.Length);
        Array.Clear(GameFlow.RolledMorphologies, 0, GameFlow.RolledMorphologies.Length);
    }

    /// <summary>Snapshots all campaign-scoped systems into <paramref name="d"/>.</summary>
    public static void CaptureCampaignState(SaveData d)
    {
        if (d == null) return;
        d.rolledObjectives = (int[])GameFlow.RolledObjectives.Clone();
        PlayerWallet.CaptureTo(d);
        PlayerInventory.CaptureTo(d);
        LoreArchive.CaptureTo(d);
        DreadDirector.CaptureTo(d);
        ContractState.CaptureTo(d);
        HunterMemory.CaptureTo(d);
        LoopCorridor.CaptureTo(d);
        HubCaches.CaptureTo(d);
        HubState.CaptureTo(d);
        CampaignRules.CaptureTo(d);
        NightNurse.CaptureTo(d);
        DarkBruno.CaptureTo(d);
        Elias.CaptureTo(d);
    }

    /// <summary>Restores all campaign-scoped systems from <paramref name="d"/>.</summary>
    public static void RestoreCampaignState(SaveData d)
    {
        if (d == null) return;
        PlayerWallet.RestoreFrom(d);
        PlayerInventory.RestoreFrom(d);
        LoreArchive.RestoreFrom(d);
        DreadDirector.RestoreFrom(d);
        ContractState.RestoreFrom(d);
        HunterMemory.RestoreFrom(d);
        LoopCorridor.RestoreFrom(d);
        HubCaches.RestoreFrom(d);
        HubState.RestoreFrom(d);
        CampaignRules.RestoreFrom(d);
        NightNurse.RestoreFrom(d);
        DarkBruno.RestoreFrom(d);
        Elias.RestoreFrom(d);
        PatientRecord.ResetShardBaseline(PlayerWallet.TotalEarned);
        GameFlow.CurrentFloor = d.floor;
        Array.Clear(GameFlow.RolledObjectives, 0, GameFlow.RolledObjectives.Length);
        if (d.rolledObjectives != null)
        {
            Array.Copy(d.rolledObjectives, GameFlow.RolledObjectives, Mathf.Min(d.rolledObjectives.Length, GameFlow.RolledObjectives.Length));
        }
        GameFlow.ResumeInShop = d.inShop;
    }
}
