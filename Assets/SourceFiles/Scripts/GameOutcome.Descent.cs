using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F107 B2: how a DESCENT level ends. A cleared level shows a short typed slip over the blackout and goes one level deeper (never the shop, the
/// finale, a payout or a save); a death shows the descent end screen, the readmission slip, and updates the record. Reuses the slip typing
/// machinery of the story's ledger (TypeLedger, PollLedgerInput) in GameOutcome.cs.
/// </summary>
public partial class GameOutcome
{
    /// <summary>The cleared-level slip waits at most this long for CONTINUE before the next level (s).</summary>
    private const float DescentSlipMaxSeconds = 12f;

    private static readonly Color DescentTitleColor = new Color(0.25f, 1f, 0.7f);

    /// <summary>The story pays a consolation for a failed floor; a descent has no shards to pay it in.</summary>
    private void BankConsolation()
    {
        if (GameFlow.IsDescent) return;

        _consolationPayout = PlayerWallet.ComputeConsolation(GameFlow.CurrentFloor, _collected);
        PlayerWallet.Deposit(_consolationPayout);
    }

    // ---------------------------------------------------------------- cleared

    /// <summary>The tail of FloorClearSequence without the payout, contract, shop and save: black, a typed three-line slip, then GameFlow.NextFloor's descent branch.</summary>
    private IEnumerator DescentClearSequence()
    {
        IsEnding = true; // blocks pause, dread, phantoms and a second ending
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        DescentRun.CommitLevel(_runTime, _collected);
        NoteMetThisLevel(); // F122: the codex works in a DESCENT too

        EndTheHunt();
        if (_flashlight != null) { _flashlight.SetOn(false); _flashlight.InputEnabled = false; }
        PlayerLock.Freeze(_player, true);

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = null;
        if (canvas != null)
        {
            blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>();
            blackout.transform.SetAsLastSibling();
        }

        if (blackout != null) yield return Fade(blackout, 0f, 1f, 0.6f);
        else yield return new WaitForSeconds(0.6f);

        if (blackout != null) BuildDescentSlip(blackout.transform);
        _slipLedger = blackout != null;
        _typing = _slipLedger; // set before the coroutine's first frame so an early press cannot continue past the typing
        if (_slipLedger) StartCoroutine(TypeLedger());
        _showingLedger = true; // Update frees the cursor while this is true
        PlayerLock.SetCursorFree(true);

        _continuePressed = false;
        float waited = 0f;
        while (!_continuePressed && waited < DescentSlipMaxSeconds)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }
        _showingLedger = false;
        _slipLedger = false;

        // The screen stays black; the reload lands in the next level.
        IsEnding = false;
        GameFlow.NextFloor();
    }

    /// <summary>LEVEL -n CLEARED / STARS HELD · TIME BELOW / DEEPER., typed out, with a CONTINUE button.</summary>
    private void BuildDescentSlip(Transform parent)
    {
        RuntimeUi.EnsureEventSystem();
        _ledgerTexts.Clear();

        Transform root = RuntimeUi.CreatePanel(parent, "DescentSlip", Color.clear).transform;

        TextMeshProUGUI title = AddSlipLine(root, L10n.T("hud.descent.cleared", ("n", DescentRun.Depth)), 76f, DescentTitleColor, -300f);
        title.fontStyle = FontStyles.Bold;
        AddSlipLine(root, L10n.T("hud.descent.stats", ("stars", DescentRun.StarsTaken), ("time", MazeEscape.FormatTime(DescentRun.TotalRunSeconds))), 40f, Grey, -420f);
        AddSlipLine(root, L10n.T("hud.descent.deeper"), 52f, Amber, -520f);

        Button continueButton = RuntimeUi.CreateButton(root, L10n.T("ui.common.continue"), new Vector2(0f, 160f), new Vector2(560f, 86f), 42f,
            new Color(0.08f, 0.32f, 0.24f, 0.95f), new Color(0.15f, 0.55f, 0.42f, 1f), Color.white);
        continueButton.onClick.AddListener(OnLedgerContinue);
        RuntimeUi.BindButton(continueButton, "ui.common.continue");
    }

    /// <summary>One centred, auto-fitting line of a typed slip; it joins _ledgerTexts so TypeLedger types it.</summary>
    private TextMeshProUGUI AddSlipLine(Transform root, string text, float size, Color color, float y)
    {
        TextMeshProUGUI line = RuntimeUi.CreateText(root, "SlipLine", text, size, color);
        RuntimeUi.AutoFit(line, 0.6f);
        line.characterSpacing = 4f;
        RuntimeUi.Place(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1500f, size * 1.4f));
        _ledgerTexts.Add(line);
        return line;
    }

    // ---------------------------------------------------------------- death

    /// <summary>The descent's end screen: the readmission slip typed over a dark panel, then DESCEND AGAIN / MAIN MENU. Updates the record exactly once (the caller is single-shot).</summary>
    private void ShowDescentEndScreen()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        DescentRun.CommitLevel(_runTime, _collected);
        NoteMetThisLevel(); // F122: the codex works in a DESCENT too
        int depth = DescentRun.Depth;
        int previousBest = DescentRecords.BestDepth;
        bool newRecord = DescentRecords.Submit(depth, DescentRun.TotalRunSeconds);

        Transform root = RuntimeUi.CreatePanel(canvas.transform, "EndScreen", new Color(0f, 0f, 0f, 0.92f)).transform;
        _endRoot = root.gameObject;
        _ledgerTexts.Clear();

        float y = -150f;
        TextMeshProUGUI heading = AddSlipLine(root, L10n.T("ui.descent.end.heading"), 34f, Amber, y);
        heading.characterSpacing = 6f;
        y -= 90f;
        TextMeshProUGUI title = AddSlipLine(root, L10n.T("ui.descent.end.depth", ("n", depth)), 84f, Red, y);
        title.fontStyle = FontStyles.Bold;
        y -= 110f;
        AddSlipLine(root, L10n.T("ui.descent.end.stats", ("stars", DescentRun.StarsTaken), ("time", MazeEscape.FormatTime(DescentRun.TotalRunSeconds))), 36f, Grey, y);
        y -= 58f;
        AddSlipLine(root, L10n.T("ui.descent.end.cause", ("cause", L10n.T("ui.descent.cause." + CauseWord()))), 36f, Grey, y);
        y -= 58f;
        string learned = LearnedHabitsText(title.font);
        if (learned != null)
        {
            AddSlipLine(root, L10n.T("ui.descent.end.learned", ("habits", learned)), 32f, Grey, y);
            y -= 54f;
        }
        AddSlipLine(root, L10n.T("ui.descent.end.seed", ("seed", DescentRun.LevelSeed)), 30f, new Color(Grey.r, Grey.g, Grey.b, 0.7f), y);
        y -= 54f;
        AddSlipLine(root, L10n.T("ui.descent.end.best", ("n", Mathf.Max(depth, previousBest))), 36f, DescentTitleColor, y);
        y -= 58f;
        if (newRecord)
        {
            AddSlipLine(root, L10n.T("ui.descent.end.new_best"), 36f, Amber, y);
            y -= 58f;
        }
        AddSlipLine(root, L10n.T("ui.descent.end.close"), 44f, Red, y - 12f);

        Vector2 size = new Vector2(560f, 86f);
        Button again = RuntimeUi.CreateButton(root, L10n.T("ui.descent.again"), new Vector2(0f, 220f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        again.onClick.AddListener(DescendAgain);
        RuntimeUi.BindButton(again, "ui.descent.again");

        Button menu = RuntimeUi.CreateButton(root, L10n.T("ui.end.main_menu"), new Vector2(0f, 110f), size, 38f, ButtonIdle, ButtonHover, Color.white);
        menu.onClick.AddListener(GameFlow.ReturnToMenu);
        RuntimeUi.BindButton(menu, "ui.end.main_menu");

        TryShowReplay(root, true); // F120: the CCTV loop on the slip (a hunter death only)
        // The slip types out like the story's: the first press of CONTINUE-style input completes the text. Nothing waits on it here.
        _slipLedger = true;
        _showingLedger = true;
        _typing = true;
        StartCoroutine(TypeLedger());
        PlayerLock.SetCursorFree(true);
    }

    /// <summary>The suffix of the cause word's key: the entity's own name when an entity ended it (listener = HEARD), the cause's otherwise.</summary>
    private static string CauseWord()
    {
        if (DescentRun.Cause == DescentCause.Entity && DescentRun.CauseEntity != EntityKind.None) return DescentRun.CauseEntity.ToString().ToLowerInvariant();
        return DescentRun.Cause.ToString().ToLowerInvariant();
    }

    /// <summary>"LOCKERS ▮▮▯ · DARK ▮▯▯" for every habit the hunter learned during this descent (level 1 or more), or null when it learned nothing.</summary>
    private static string LearnedHabitsText(TMP_FontAsset font)
    {
        List<string> parts = new List<string>();
        foreach (HunterMemory.Habit habit in System.Enum.GetValues(typeof(HunterMemory.Habit)))
        {
            if (HunterMemory.Level(habit) < 1) continue;
            parts.Add(HunterMemory.NameText(habit) + " " + HunterMemory.LevelBar(habit, font));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>DESCEND AGAIN: a brand-new descent from level -1 (the descent flag is already on), through the same reload a Retry uses.</summary>
    private static void DescendAgain()
    {
        DescentRun.Begin();
        GameFlow.IsDescent = true;
        GameFlow.CurrentFloor = 1;
        GameFlow.Restart(false, 0);
    }
}
