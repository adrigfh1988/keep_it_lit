using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// S9 (plan D11): adds <see cref="LocalizedText"/> to the static world-space TMP signs of the three hand-edited prefabs (AsylumHub,
/// IntakeRoom, ShopRoom) by matching each sign's current English text to a fixed text -> World-table key list. Build-missing-only and
/// idempotent: a sign that already has the component is left alone, a sign whose text matches no row (tally marks, number rows, the signs
/// code writes at runtime) is never touched. It edits the prefab ASSETS (LoadPrefabContents / SaveAsPrefabAsset); a room is never
/// regenerated, and the scene's prefab instances pick the component up from the asset. ProjectSetup piece "Localized world signs".
/// </summary>
public static class LocalizationSignsBuilder
{
    private struct Sign
    {
        public string Prefab;
        public string Text;
        public string Key;
        public Sign(string prefab, string text, string key) { Prefab = prefab; Text = text; Key = key; }
    }

    private const string Hub = "Assets/Prefabs/AsylumHub.prefab";
    private const string Intake = "Assets/Prefabs/IntakeRoom.prefab";
    private const string Shop = "Assets/Prefabs/ShopRoom.prefab";

    // The 19 static signs (plan section 1): hub 12 (admissions, 5 records slots, wristband card, 2 lower-level stencils, 3 service units),
    // Intake 5, shop 2. Text is matched after trimming and normalising line breaks.
    private static readonly Sign[] Signs =
    {
        new Sign(Hub, "KARLHEINZ ASYLUM\nADMISSIONS", "world.hub.admissions"),
        new Sign(Hub, "P3\nHILDE BRANDT", "world.hub.slot.100"),
        new Sign(Hub, "P11\nTOMAS REINER", "world.hub.slot.101"),
        new Sign(Hub, "P5\nELIAS VOGT", "world.hub.slot.102"),
        new Sign(Hub, "SUBJECT A", "world.hub.slot.103"),
        new Sign(Hub, "DIRECTOR\nPRIVATE", "world.hub.slot.104"),
        new Sign(Hub, "ELIAS VOGT\nPATIENT 5", "world.hub.wristband"),
        new Sign(Hub, "SUBJECT A\nLOWER LEVEL", "world.hub.lower_level"),
        new Sign(Hub, "SERVICE UNIT", "world.hub.service_unit"),
        new Sign(Intake, "HOLDING", "world.intake.holding"),
        new Sign(Intake, "NO ENTRY", "world.intake.no_entry"),
        new Sign(Intake, "INTAKE  ·  WARD 1", "world.intake.ward"),
        new Sign(Intake, "KEEP QUIET.\nIT HEARS EVERYTHING.", "world.intake.keep_quiet"),
        new Sign(Intake, "STAY IN YOUR BED", "world.intake.stay_in_bed"),
        new Sign(Shop, "CONTRACTS", "world.shop.contracts"),
        new Sign(Shop, "SHARDS ACCEPTED", "world.shop.shards_accepted"),
    };

    private static string Norm(string s) => (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Trim();

    private static string KeyFor(string prefab, TMP_Text t)
    {
        string text = Norm(t.text);
        foreach (Sign s in Signs) if (s.Prefab == prefab && Norm(s.Text) == text) return s.Key;
        return null;
    }

    private static IEnumerable<string> Prefabs() => Signs.Select(s => s.Prefab).Distinct();

    /// <summary>Null when every matching sign already carries a LocalizedText; otherwise what is missing.</summary>
    public static string Problem()
    {
        var missing = new List<string>();
        foreach (string path in Prefabs())
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) continue; // the room piece reports a missing prefab
            int n = 0;
            foreach (TMP_Text t in asset.GetComponentsInChildren<TMP_Text>(true))
            {
                if (KeyFor(path, t) == null) continue;
                LocalizedText lt = t.GetComponent<LocalizedText>();
                if (lt == null || string.IsNullOrEmpty(lt.Key)) n++;
            }
            if (n > 0) missing.Add($"{System.IO.Path.GetFileNameWithoutExtension(path)}: {n} sign(s)");
        }
        return missing.Count == 0 ? null : "signs without LocalizedText - " + string.Join("; ", missing);
    }

    /// <summary>Adds the missing components to the prefab assets. Returns how many signs were changed.</summary>
    public static int BuildSilently()
    {
        int changed = 0;
        foreach (string path in Prefabs())
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                int here = 0;
                foreach (TMP_Text t in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    string key = KeyFor(path, t);
                    if (key == null) continue;
                    LocalizedText lt = t.GetComponent<LocalizedText>();
                    if (lt == null) lt = t.gameObject.AddComponent<LocalizedText>();
                    else if (!string.IsNullOrEmpty(lt.Key)) continue;
                    var so = new SerializedObject(lt);
                    so.FindProperty("key").stringValue = key;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    here++;
                }
                if (here > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    changed += here;
                    Debug.Log($"[LocalizationSigns] {path}: {here} sign(s) localized.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
        if (changed > 0) AssetDatabase.SaveAssets();
        return changed;
    }

    [MenuItem("LIGHTS OUT/Localization/Localize World Signs", priority = 310)]
    public static void Menu()
    {
        int n = BuildSilently();
        EditorUtility.DisplayDialog("Localized world signs", n > 0 ? $"{n} sign(s) localized in the prefab assets." : "Nothing to do - every sign is already localized.", "OK");
    }
}
