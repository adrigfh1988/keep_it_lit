/// <summary>
/// F118: stepwise SaveData migrations. A save older than <see cref="SaveSystem.Version"/> is brought forward one version at a
/// time. JsonUtility always fills the CURRENT SaveData shape (missing fields read 0, unknown ones are dropped), which is why a
/// SaveData field is never renamed or removed - see pattern P1 in plannings/f118-release-foundations-plan.md.
/// </summary>
public static class SaveMigrations
{
    /// <summary>Runs every step from d.version up to SaveSystem.Version. False when a step is missing.</summary>
    public static bool Run(SaveData d)
    {
        while (d.version < SaveSystem.Version)
        {
            if (!Step(d, d.version)) return false;
            d.version++;
        }
        return true;
    }

    private static bool Step(SaveData d, int from)
    {
        switch (from)
        {
            case 1: V1ToV2(d); return true;
            default: return false;
        }
    }

    /// <summary>v1 -> v2: the HUD tint was retired by F110 (always the default), so a saved choice means nothing now.</summary>
    private static void V1ToV2(SaveData d)
    {
        d.hudTintIndex = 0;
        d.ownedHudTints = 1;
    }
}
