using UnityEngine;

/// <summary>F122: a cleared floor's letter. Saved as an int (campaignGrades): append only; 0 = no grade.</summary>
public enum Grade { None = 0, D = 1, C = 2, B = 3, A = 4, S = 5 }

/// <summary>Everything the grade is computed from. Plain data so the pure functions are testable by eval.</summary>
public struct GradeInput
{
    public int floor;
    public float runSeconds, parSeconds, secondsLeft, countdownSeconds;
    public int chases, secondWinds, grabs, stanTouches, batteriesUsed, shardsFound, shardsPlaced, deathsOnFloor;
}

/// <summary>
/// F122 D8: floor grades S / A / B / C / D. <see cref="Score"/> and <see cref="For"/> are pure; the per-attempt counters come from
/// <see cref="FloorGradeTracker"/>; this class also holds the campaign's grade per floor and deaths per floor (statics, saved with the checkpoint).
/// The par times and weights are a FIRST GUESS: tune the constants here after a playtest.
/// </summary>
public static class FloorGrade
{
    // ---- tuning (first guess)
    public const float ParBase = 60f, ParPerCell = 2.5f, ParPerObjective = 30f;
    public const float PaceMax = 30f, MarginMax = 15f, UnseenMax = 20f, UntouchedMax = 20f, TorchMax = 10f, ThoroughMax = 5f;
    public const float ScoreS = 85f, ScoreA = 70f, ScoreB = 50f, ScoreC = 30f;

    /// <summary>Under plain STANDARD the grade pays nothing (it keeps the economy exactly as it was). The bonus needs an Oath (rank 1+) and an unassisted campaign.</summary>
    public const bool BonusNeedsOath = true;

    /// <summary>This campaign's best grade per floor (index = floor, 1..FinalFloor).</summary>
    public static readonly int[] Grades = new int[FloorProfile.FinalFloor + 1];

    /// <summary>Deaths on each floor this campaign (index = floor). A death never saves (F83), so this resets on CONTINUE from an older checkpoint - accepted.</summary>
    public static readonly int[] DeathsOnFloor = new int[FloorProfile.FinalFloor + 1];

    public static float ParSeconds(FloorProfile p)
    {
        if (p == null) return 300f;
        return ParBase + ParPerCell * (p.Width * p.Height) + ParPerObjective * (p.StarCount + p.FuseCount + p.KeyCount);
    }

    /// <summary>0..100: Pace 30 / Margin 15 / Unseen 20 / Untouched 20 / Torch 10 / Thorough 5.</summary>
    public static float Score(GradeInput g)
    {
        float par = Mathf.Max(1f, g.parSeconds);
        float pace = PaceMax * Mathf.Clamp01(2f - g.runSeconds / par);
        float margin = g.countdownSeconds > 0f ? MarginMax * Mathf.Clamp01(g.secondsLeft / (0.5f * g.countdownSeconds)) : 0f;
        float unseen = g.chases <= 0 ? UnseenMax : g.chases == 1 ? 14f : g.chases == 2 ? 8f : 0f;
        float untouched = Mathf.Max(0f, UntouchedMax - 20f * g.secondWinds - 10f * g.grabs - 5f * g.stanTouches);
        float torch = g.batteriesUsed <= 0 ? TorchMax : g.batteriesUsed == 1 ? 5f : 0f;
        float thorough = g.shardsPlaced <= 0 || g.shardsFound >= 0.6f * g.shardsPlaced ? ThoroughMax : 0f;
        return pace + margin + unseen + untouched + torch + thorough;
    }

    /// <summary>S needs 85+ and no death and no Second Wind; A 70+; B 50+; C 30+; else D. A death on the floor caps at A, three at B. Hiding is not penalised.</summary>
    public static Grade For(GradeInput g)
    {
        float score = Score(g);
        Grade grade = score >= ScoreS ? Grade.S : score >= ScoreA ? Grade.A : score >= ScoreB ? Grade.B : score >= ScoreC ? Grade.C : Grade.D;
        if (grade == Grade.S && (g.deathsOnFloor > 0 || g.secondWinds > 0)) grade = Grade.A;
        if (g.deathsOnFloor >= 3 && grade > Grade.B) grade = Grade.B;
        else if (g.deathsOnFloor >= 1 && grade > Grade.A) grade = Grade.A;
        return grade;
    }

    public static string Letter(Grade g) => g == Grade.None ? "-" : g.ToString();

    public static Color LetterColour(Grade g)
    {
        switch (g)
        {
            case Grade.S: return new Color(1f, 0.82f, 0.30f);
            case Grade.A: return new Color(0.25f, 1f, 0.7f);
            case Grade.B: return new Color(0.9f, 0.88f, 0.8f);
            default: return new Color(0.62f, 0.62f, 0.66f);
        }
    }

    /// <summary>The shard bonus for a grade on a floor: S 30 / A 15 / B 5, x(1 + 0.15 (floor - 1)), rounded to 5; 0 for C and D.</summary>
    public static int BonusFor(Grade g, int floor)
    {
        int basis = g == Grade.S ? 30 : g == Grade.A ? 15 : g == Grade.B ? 5 : 0;
        if (basis == 0) return 0;
        float scaled = basis * (1f + 0.15f * Mathf.Max(0, floor - 1));
        return Mathf.Max(5, Mathf.RoundToInt(scaled / 5f) * 5);
    }

    /// <summary>D9: adds the GRADE line when the campaign earns a bonus (an Oath, unassisted). A no-op for plain STANDARD, so its payout is unchanged.</summary>
    public static void AppendBonus(Payout payout, Grade grade, int floor)
    {
        if (payout == null) return;
        if (BonusNeedsOath && (CampaignRules.OathRank < 1 || CampaignRules.IsAssisted)) return;
        int bonus = BonusFor(grade, floor);
        if (bonus <= 0) return;
        payout.Lines.Add(new PayoutLine { Label = L10n.T("ui.ledger.grade_bonus", ("grade", Letter(grade))), Amount = bonus, Kind = PayoutKind.Grade });
        payout.Total += bonus;
    }

    /// <summary>Keeps the better of an earlier grade for this floor.</summary>
    public static void Store(int floor, Grade grade)
    {
        if (floor < 1 || floor >= Grades.Length) return;
        if ((int)grade > Grades[floor]) Grades[floor] = (int)grade;
    }

    public static Grade Stored(int floor) => floor >= 1 && floor < Grades.Length ? (Grade)Grades[floor] : Grade.None;

    public static void AddDeath(int floor)
    {
        if (floor >= 1 && floor < DeathsOnFloor.Length) DeathsOnFloor[floor]++;
    }

    public static int DeathsOn(int floor) => floor >= 1 && floor < DeathsOnFloor.Length ? DeathsOnFloor[floor] : 0;

    /// <summary>"B · A · A · S · -": the end screen's line.</summary>
    public static string GradesLine()
    {
        string[] parts = new string[FloorProfile.FinalFloor];
        for (int f = 1; f <= FloorProfile.FinalFloor; f++) parts[f - 1] = Letter(Stored(f));
        return string.Join(" · ", parts);
    }

    public static void ResetCampaign()
    {
        System.Array.Clear(Grades, 0, Grades.Length);
        System.Array.Clear(DeathsOnFloor, 0, DeathsOnFloor.Length);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();

    public static void CaptureTo(SaveData data)
    {
        data.campaignGrades = (int[])Grades.Clone();
        data.floorDeaths = (int[])DeathsOnFloor.Clone();
    }

    public static void RestoreFrom(SaveData data)
    {
        ResetCampaign();
        if (data.campaignGrades != null)
        {
            for (int i = 0; i < Mathf.Min(data.campaignGrades.Length, Grades.Length); i++) Grades[i] = Mathf.Clamp(data.campaignGrades[i], 0, (int)Grade.S);
        }
        if (data.floorDeaths != null)
        {
            for (int i = 0; i < Mathf.Min(data.floorDeaths.Length, DeathsOnFloor.Length); i++) DeathsOnFloor[i] = Mathf.Max(0, data.floorDeaths[i]);
        }
    }
}
