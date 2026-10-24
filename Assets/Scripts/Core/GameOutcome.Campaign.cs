using TMPro;
using UnityEngine;

/// <summary>
/// F122: how the campaign layer shows up in the endings - the floor grade (computed in all three clear paths), the Oath and grade payout lines, the
/// Patient Record commit points, and the campaign tag / grade stamp / GRADES line on the ledger and the end screen. Kept in its own partial so
/// GameOutcome.cs only gains one-line calls. Everything here is story-only (a DESCENT has no grade, no Oath and no tag) and, for plain STANDARD,
/// changes no payout line or total.
/// </summary>
public partial class GameOutcome
{
    private FloorGradeTracker _grades;
    private Grade _floorGrade;
    private bool _floorGradeNewBest;

    /// <summary>F122: the attempt's counters. May stay null (no grade).</summary>
    public void BindGrades(FloorGradeTracker grades) => _grades = grades;

    /// <summary>The grade of the floor just cleared (None before a clear and in a descent).</summary>
    public Grade FloorGradeNow => _floorGrade;

    /// <summary>
    /// Called by the three clear paths right after PlayerWallet.ComputeFloorClear (story only): the Oath line, the grade and its bonus line, and the
    /// record's floor / habit / entity notes. The grade bonus and the Oath line are only ever added for a campaign with Oaths, so a plain
    /// STANDARD payout is exactly the base payout.
    /// </summary>
    private void ScoreFloorClear(Payout payout, float secondsLeft)
    {
        if (GameFlow.IsDescent) return;
        int floor = GameFlow.CurrentFloor;
        CampaignRules.AppendOathLine(payout);

        float countdown = _escape != null ? _escape.CountdownSeconds : 0f;
        Grade grade = _grades != null ? _grades.Compute(_runTime, secondsLeft, countdown) : Grade.None;
        _floorGrade = grade;
        FloorGrade.Store(floor, grade);
        FloorGrade.AppendBonus(payout, grade, floor);

        _floorGradeNewBest = PatientRecord.NoteFloorCleared(floor, grade, _runTime, CampaignRules.IsAssisted, CampaignRules.OathRank);
        PatientRecord.NoteHabits();
        NoteMetThisLevel();
    }

    /// <summary>The record's codex: what the finished level held (the hunter always, Stan / Listener / Tenant / bodies per its roster, the Visitor once the loop has appeared). Level of 20 s or more only.</summary>
    private void NoteMetThisLevel()
    {
        if (_hubDeath) return;
        EntityKind set = _maze != null ? _maze.EntitySet : EntityKind.None;
        PatientRecord.NoteMet(set, !GameFlow.IsDescent && LoopCorridor.UsedThisCampaign, _runTime);
    }

    /// <summary>A death on this floor (a hub death is not one: no maze was played).</summary>
    private void CountDeath()
    {
        if (GameFlow.IsDescent || _hubDeath) return;
        FloorGrade.AddDeath(GameFlow.CurrentFloor);
    }

    /// <summary>An ending (A or B) completed: the record notes it. The plain no-hub final Win counts as Discharged.</summary>
    private void NoteEndingReached(bool discharged)
    {
        if (GameFlow.IsDescent) return;
        PatientRecord.NoteEnding(discharged, CampaignRules.IsAssisted, CampaignRules.OathRank);
        PatientRecord.CommitShards();
    }

    // ---------------------------------------------------------------- the ledger

    private static readonly Color RecordAmber = new Color(1f, 0.78f, 0.45f);

    /// <summary>The ledger's extras: the campaign tag (top right), a "PATIENT RECORD - n NEW" line (top left) and the grade stamp (right). None of it reflows the lines.</summary>
    private void AddLedgerExtras(Transform root, bool slip)
    {
        AddCampaignTag(root);
        AddRecordNewNote(root);
        AddGradeStamp(root, slip, -420f);
    }

    private void AddRecordNewNote(Transform root)
    {
        int fresh = PatientRecord.NewCount;
        if (fresh <= 0) return;
        TextMeshProUGUI note = RuntimeUi.CreateText(root, "RecordNew", L10n.T("ui.ledger.record_new", ("n", fresh)), 28f, RecordAmber);
        note.alignment = TextAlignmentOptions.TopLeft;
        note.textWrappingMode = TextWrappingModes.NoWrap;
        RuntimeUi.AutoFit(note, 0.6f);
        PlaceCorner(note.rectTransform, new Vector2(0f, 1f), new Vector2(40f, -30f), new Vector2(800f, 40f));
    }

    private void AddCampaignTag(Transform root)
    {
        if (GameFlow.IsDescent) return;
        TextMeshProUGUI tag = RuntimeUi.CreateText(root, "CampaignTag", CampaignRules.Label(), 28f, Grey);
        tag.alignment = TextAlignmentOptions.TopRight;
        tag.characterSpacing = 4f;
        tag.textWrappingMode = TextWrappingModes.NoWrap;
        RuntimeUi.AutoFit(tag, 0.6f);
        PlaceCorner(tag.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -30f), new Vector2(900f, 40f));
    }

    private void AddGradeStamp(Transform root, bool slip, float y)
    {
        if (GameFlow.IsDescent || _floorGrade == Grade.None) return;

        TextMeshProUGUI label = RuntimeUi.CreateText(root, "GradeLabel", L10n.T("ui.ledger.grade"), 28f, Grey);
        label.characterSpacing = 6f;
        RuntimeUi.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(780f, y + 140f), new Vector2(220f, 36f));

        TextMeshProUGUI letter = RuntimeUi.CreateText(root, "GradeLetter", FloorGrade.Letter(_floorGrade), 190f, FloorGrade.LetterColour(_floorGrade));
        letter.fontStyle = FontStyles.Bold;
        RuntimeUi.Place(letter.rectTransform, new Vector2(0.5f, 1f), new Vector2(780f, y), new Vector2(220f, 220f));

        TextMeshProUGUI best = null;
        if (_floorGradeNewBest)
        {
            best = RuntimeUi.CreateText(root, "GradeNewBest", L10n.T("ui.ledger.grade_new_best"), 26f, RecordAmber);
            best.characterSpacing = 4f;
            RuntimeUi.AutoFit(best, 0.6f);
            RuntimeUi.Place(best.rectTransform, new Vector2(0.5f, 1f), new Vector2(780f, y - 140f), new Vector2(240f, 36f));
        }

        if (slip)
        {
            // The readmission slip types its lines; the grade is stamped in last.
            _ledgerTexts.Add(label);
            _ledgerTexts.Add(letter);
            if (best != null) _ledgerTexts.Add(best);
        }
    }

    private static void PlaceCorner(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    // ---------------------------------------------------------------- the end screen

    /// <summary>The end screen's extras for a story run: the campaign tag, and on a won final floor the grade stamp and the GRADES line.</summary>
    private void AddEndScreenExtras(Transform root, bool won, bool finalWin, bool longBody)
    {
        if (GameFlow.IsDescent) return;
        AddCampaignTag(root);
        AddRecordNewNote(root);

        if (!won || !finalWin) return;
        AddGradeStamp(root, false, -330f);
        TextMeshProUGUI grades = RuntimeUi.CreateText(root, "Grades", L10n.T("ui.end.grades", ("grades", FloorGrade.GradesLine())), 32f, Grey);
        grades.characterSpacing = 4f;
        RuntimeUi.AutoFit(grades, 0.65f);
        RuntimeUi.Place(grades.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, longBody ? -640f : -480f), new Vector2(1600f, 50f));
    }
}
