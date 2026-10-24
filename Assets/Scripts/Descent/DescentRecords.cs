using UnityEngine;

/// <summary>
/// F107 B1: the deepest level ever reached in a DESCENT and how long it took, kept in PlayerPrefs. Machine stats like the settings: never in
/// SaveData, not cleared by GameFlow.ReturnToMenu. Every read and write is wrapped like GameFlow.TutorialDone, since PlayerPrefs can throw
/// (WebGL, private windows).
/// </summary>
public static class DescentRecords
{
    public const string BestDepthKey = "keepitlit.descent.bestDepth";
    public const string BestSecondsKey = "keepitlit.descent.bestSeconds";

    /// <summary>Deepest level reached, 0 when there is no record yet.</summary>
    public static int BestDepth
    {
        get { try { return PlayerPrefs.GetInt(BestDepthKey, 0); } catch { return 0; } }
    }

    /// <summary>Run time taken to reach <see cref="BestDepth"/>.</summary>
    public static float BestSeconds
    {
        get { try { return PlayerPrefs.GetFloat(BestSecondsKey, 0f); } catch { return 0f; } }
    }

    public static bool HasRecord => BestDepth > 0;

    /// <summary>
    /// A descent ended at `depth` after `totalSeconds`. A deeper level replaces the record; the same depth keeps whichever time was faster.
    /// Returns true when it beat an existing record (the first descent ever sets the record without saying so).
    /// </summary>
    public static bool Submit(int depth, float totalSeconds)
    {
        try
        {
            int best = BestDepth;
            bool deeper = depth > best;
            bool fasterTie = depth == best && (BestSeconds <= 0f || totalSeconds < BestSeconds);
            if (deeper || fasterTie)
            {
                PlayerPrefs.SetInt(BestDepthKey, depth);
                PlayerPrefs.SetFloat(BestSecondsKey, totalSeconds);
                PlayerPrefs.Save();
            }

            return deeper && best > 0;
        }
        catch
        {
            return false; // best effort: a failed write never breaks the end screen
        }
    }
}
