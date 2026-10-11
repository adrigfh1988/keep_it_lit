using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>F109: restyles the Intake room signs in place on Assets/Prefabs/IntakeRoom.prefab and adds the room name plates. Build-missing-only (a styled sign has a SignBacking / WorldSignStyle child).</summary>
public static partial class WorldSignBuilder
{
    private const string IntakePrefab = "Assets/Prefabs/IntakeRoom.prefab";

    private sealed class IntakePlan
    {
        public string name; public SignStyle style; public SignOptions options;
        public IntakePlan(string n, SignStyle s, SignOptions o) { name = n; style = s; options = o; }
    }

    private static IntakePlan[] IntakeSigns() => new[]
    {
        new IntakePlan("WardSign", SignStyle.EnamelPlate, new SignOptions { tilt = 3f }),
        new IntakePlan("HoldingSign", SignStyle.LightBox, new SignOptions { deadLetter = true }),
        new IntakePlan("NoEntrySign", SignStyle.StencilSpray, new SignOptions { paint = new Color(0.82f, 0.1f, 0.07f), fontSize = 1.55f }),
        new IntakePlan("StayInBedSign", SignStyle.WallPaint, new SignOptions { paint = new Color(0.7f, 0.1f, 0.08f), ghostText = "NO VISITORS", ghostKey = "world.ghost.no_visitors" }),
        new IntakePlan("FocusSign", SignStyle.PaperNotice, new SignOptions { curlRight = true }),
    };

    // name, text, key, position (room local), inward direction
    private static readonly (string name, string text, string key, Vector3 pos, Vector3 inward)[] IntakePlates =
    {
        ("RoomSign_Admissions_0", "ADMISSIONS", "world.intake.admissions", new Vector3(0f, 2.62f, 3.48f), Vector3.back),
        ("RoomSign_Observation_0", "OBSERVATION", "world.intake.observation", new Vector3(0f, 2.62f, 11.48f), Vector3.back),
    };

    public static string IntakeProblem()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(IntakePrefab);
        if (prefab == null) return null; // no Intake to restyle
        if (!FontsPresent()) return "sign fonts missing";
        int unstyled = 0;
        foreach (IntakePlan p in IntakeSigns())
        {
            Transform t = prefab.transform.Find(p.name);
            if (t != null && !IsStyled(t.GetComponent<TextMeshPro>())) unstyled++;
        }
        foreach (var pl in IntakePlates) if (prefab.transform.Find(pl.name) == null) unstyled++;
        return unstyled > 0 ? unstyled + " Intake sign(s) unstyled / room plate(s) missing" : null;
    }

    public static string RestyleIntake()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(IntakePrefab) == null) return "no Intake prefab";
        EnsureFonts();
        GameObject root = PrefabUtility.LoadPrefabContents(IntakePrefab);
        string result;
        try
        {
            result = RestyleIntakeRoot(root, out int styled, out int plates);
            if (styled + plates > 0) PrefabUtility.SaveAsPrefabAsset(root, IntakePrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("[WorldSigns] " + result);
        return result;
    }

    /// <summary>The restyle itself, on any Intake root (the prefab contents, or the scene object a regenerate has just built).</summary>
    public static string RestyleIntakeRoot(GameObject root, out int styled, out int plates)
    {
        var log = new System.Text.StringBuilder();
        styled = 0; plates = 0;
        {
            foreach (IntakePlan p in IntakeSigns())
            {
                Transform t = root.transform.Find(p.name);
                TextMeshPro tmp = t != null ? t.GetComponent<TextMeshPro>() : null;
                if (tmp == null) { log.AppendLine("missing sign " + p.name); continue; }
                if (Style(tmp, p.style, p.options) != null) { styled++; log.AppendLine(p.name + " -> " + p.style); }
            }
            foreach (var pl in IntakePlates)
            {
                if (root.transform.Find(pl.name) != null) continue;
                Quaternion rot = Quaternion.LookRotation(-pl.inward);
                GameObject go = new GameObject(pl.name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = pl.pos + pl.inward * 0.02f;
                go.transform.localRotation = rot;
                TextMeshPro tmp = go.AddComponent<TextMeshPro>();
                tmp.text = pl.text;
                tmp.fontSize = 1.1f;
                tmp.characterSpacing = 3f;
                SetLocalizedKey(go, pl.key);
                Style(tmp, SignStyle.EnamelPlate, new SignOptions { noMissingScrew = true });
                plates++;
            }

            // HOLDING now stutters with the others
            IntakeAmbience amb = root.GetComponent<IntakeAmbience>();
            Transform holding = root.transform.Find("HoldingSign");
            if (amb != null && holding != null)
            {
                SerializedObject so = new SerializedObject(amb);
                SerializedProperty arr = so.FindProperty("flickerSigns");
                bool has = false;
                for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == holding.GetComponent<TMP_Text>()) has = true;
                if (!has)
                {
                    arr.InsertArrayElementAtIndex(arr.arraySize);
                    arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = holding.GetComponent<TMP_Text>();
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
        return $"Intake signs: {styled} restyled, {plates} plates added\n{log}";
    }
}
