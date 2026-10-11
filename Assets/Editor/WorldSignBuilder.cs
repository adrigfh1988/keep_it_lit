using UnityEditor;
using UnityEngine;

/// <summary>
/// F109: world signs. Real signs instead of bare floating TMP text: a backing per style, lit worn lettering, one font family per place, a name plate
/// at every room entrance, and a few horror details (SignDeadLetter, SignSway, SignShift, SignRepaint, ghost text). Everything restyles the existing
/// sign GameObjects in place (so IntakeAmbience.flickerSigns, ShopRoom.counterSign/doorSign, WingDoor.plaque, ShopDoor.plaque and LocalizedText keep
/// working) and is build-missing-only: a restyled sign carries a SignBacking / WorldSignStyle child. Never regenerates the Intake, the shop or the hub.
/// Files: .Fonts (static TMP font assets + lit materials), .Textures, .Meshes, .Styles (the Style entry point), .Intake, .Shop, .Hub and
/// AsylumHubBuilder.Signs (the doorway finder and room plates). Spec: plannings/world-signs-plan.md.
/// </summary>
public static partial class WorldSignBuilder
{
    [MenuItem("KEEP IT LIT/Build/World Signs (restyle missing)", priority = 109)]
    public static void RestyleMissingMenu()
    {
        string report = RestyleAll();
        Debug.Log("[WorldSigns] " + report + "\nMark the scene dirty by hand if needed and save it (Ctrl+S); the prefabs are already written.");
    }

    /// <summary>Fonts, then the Intake and shop prefabs, then the scene hub (applied to its prefab). Returns a report.</summary>
    public static string RestyleAll()
    {
        EnsureFonts();
        string report = RestyleIntake() + "\n" + RestyleShop() + "\n" + RestyleHub();
        AssetDatabase.SaveAssets();
        return report;
    }
}
