using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Metadata;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;

/// <summary>
/// S9 (D4, D17): the localization tooling. Source of truth = the CSVs in Assets/Localization/Source (<c>UI.csv</c> = key,context,en and
/// <c>UI.es.csv</c> / <c>.fr</c> / <c>.de</c> = key,text); this class (a) makes sure the project has the Localization settings asset, the four
/// locales, the startup-selector chain and the Boot scene (<see cref="EnsureProject"/>), (b) generates / updates the String Table Collections
/// under Assets/Localization/Tables from the CSVs, idempotently (<see cref="ImportAll"/>), (c) exports / imports a per-language review CSV, and
/// (d) reports problems for the ProjectSetup piece "Localization" (<see cref="Problem"/>), including the completeness check.
/// Spec: plannings/localization-plan.md.
/// </summary>
public static class LocalizationImporter
{
    public const string Root = "Assets/Localization";
    public const string SourceDir = Root + "/Source";
    public const string TablesDir = Root + "/Tables";
    public const string ReviewDir = Root + "/Review";
    public const string LocalesDir = Root + "/Locales";
    public const string SettingsPath = Root + "/LocalizationSettings.asset";
    public const string BootScenePath = "Assets/Scenes/Boot.unity";
    public const string GameScenePath = "Assets/Scenes/GetStarted_Scene.unity";

    private static readonly string[] Codes = { "en", "es", "fr", "de" };

    // ---------------------------------------------------------------- menu

    [MenuItem("LIGHTS OUT/Localization/Import CSVs", priority = 300)]
    public static void ImportMenu()
    {
        string report = ImportAll();
        EditorUtility.DisplayDialog("Import CSVs", report, "OK");
    }

    [MenuItem("LIGHTS OUT/Localization/Set Up Project (settings, locales, Boot scene)", priority = 301)]
    public static void SetUpMenu()
    {
        EnsureProject();
        EditorUtility.DisplayDialog("Localization", "Settings, locales, startup selectors and the Boot scene are set up.\n\n" + (Problem() ?? "Check: OK"), "OK");
    }

    [MenuItem("LIGHTS OUT/Localization/Check Completeness", priority = 302)]
    public static void CheckMenu()
    {
        string r = CompletenessReport();
        Debug.Log("[Localization] " + r);
        EditorUtility.DisplayDialog("Completeness", r.Length > 1500 ? r.Substring(0, 1500) + "\n..." : r, "OK");
    }

    [MenuItem("LIGHTS OUT/Localization/Export Review CSV", priority = 310)]
    public static void ExportMenu()
    {
        EditorUtility.DisplayDialog("Export Review CSV", ExportReview(), "OK");
    }

    [MenuItem("LIGHTS OUT/Localization/Import Review CSV", priority = 311)]
    public static void ImportReviewMenu()
    {
        string report = ImportReview();
        report += "\n\n" + ImportAll();
        EditorUtility.DisplayDialog("Import Review CSV", report, "OK");
    }

    // ---------------------------------------------------------------- project setup

    /// <summary>Dialog-free: package settings asset, 4 locales, startup selector chain, missing-translation behaviour, Addressables, Boot scene, then the tables.</summary>
    public static void EnsureProject()
    {
        EnsureSettings();
        EnsureBootScene();
        ImportAll();
    }

    public static LocalizationSettings EnsureSettings()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(LocalesDir);

        LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        if (settings == null)
        {
            string[] guids = AssetDatabase.FindAssets("t:LocalizationSettings", new[] { "Assets" });
            if (guids.Length > 0) settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        LocalizationEditorSettings.ActiveLocalizationSettings = settings;

        foreach (string code in Codes)
        {
            if (LocalizationEditorSettings.GetLocale(new LocaleIdentifier(code)) != null) continue;
            Locale locale = Locale.CreateLocale(new LocaleIdentifier(code));
            if (string.IsNullOrEmpty(locale.name)) locale.name = $"{new LocaleIdentifier(code).CultureInfo?.EnglishName ?? code} ({code})";
            AssetDatabase.CreateAsset(locale, $"{LocalesDir}/{locale.name}.asset");
            LocalizationEditorSettings.AddLocale(locale, false);
        }

        // Startup chain: saved language -> system language -> English.
        var selectors = settings.GetStartupLocaleSelectors();
        bool chainOk = selectors.Count == 3 && selectors[0] is SavedLocaleSelector && selectors[1] is SystemLocaleSelector
                       && selectors[2] is SpecificLocaleSelector spec && spec.LocaleId.Code == "en";
        if (!chainOk)
        {
            selectors.Clear();
            selectors.Add(new SavedLocaleSelector());
            selectors.Add(new SystemLocaleSelector());
            selectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier("en") });
        }

        // D3: a missing translation never prints the package's "No translation found" text.
        if (settings.GetStringDatabase() is LocalizedStringDatabase db && db.MissingTranslationState != MissingTranslationBehavior.PrintWarning)
        {
            db.MissingTranslationState = MissingTranslationBehavior.PrintWarning;
        }

        // D17: Addressables content is built with the player.
        var addr = AddressableAssetSettingsDefaultObject.GetSettings(true);
        if (addr != null && addr.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
        {
            addr.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            EditorUtility.SetDirty(addr);
        }

        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }

    /// <summary>Creates Boot.unity (camera + LocalizationBoot) if missing and makes it scene 0 of the build list, GetStarted_Scene scene 1.</summary>
    public static void EnsureBootScene()
    {
        if (!File.Exists(BootScenePath))
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var cam = new GameObject("Camera");
            var c = cam.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = Color.black;
            cam.tag = "MainCamera";
            new GameObject("LocalizationBoot").AddComponent<LocalizationBoot>();
            EditorSceneManager.SaveScene(scene, BootScenePath);
            EditorSceneManager.CloseScene(scene, true);
        }

        var list = EditorBuildSettings.scenes.Where(s => s.path != BootScenePath && s.path != GameScenePath).ToList();
        list.Insert(0, new EditorBuildSettingsScene(GameScenePath, true));
        list.Insert(0, new EditorBuildSettingsScene(BootScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    // ---------------------------------------------------------------- CSV -> tables

    private sealed class Source
    {
        public string Table;
        public List<string> Keys = new List<string>();
        public Dictionary<string, string> Context = new Dictionary<string, string>();
        public Dictionary<string, Dictionary<string, string>> Text = new Dictionary<string, Dictionary<string, string>>();
    }

    private static List<Source> ReadSources()
    {
        var list = new List<Source>();
        if (!Directory.Exists(SourceDir)) return list;
        foreach (string file in Directory.GetFiles(SourceDir, "*.csv").OrderBy(f => f, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.Contains('.') || name == "Glossary") continue;
            var src = new Source { Table = name };
            var en = new Dictionary<string, string>();
            foreach (string[] r in LocalizationCsv.Parse(File.ReadAllText(file, Encoding.UTF8)).Skip(1))
            {
                if (r.Length < 3 || string.IsNullOrWhiteSpace(r[0])) continue;
                if (en.ContainsKey(r[0])) { Debug.LogWarning($"[Localization] duplicate key '{r[0]}' in {name}.csv"); continue; }
                src.Keys.Add(r[0]);
                src.Context[r[0]] = r[1];
                en[r[0]] = r[2];
            }
            src.Text["en"] = en;
            foreach (string code in Codes.Skip(1))
            {
                var map = new Dictionary<string, string>();
                string lf = Path.Combine(SourceDir, $"{name}.{code}.csv");
                if (File.Exists(lf))
                {
                    foreach (string[] r in LocalizationCsv.Parse(File.ReadAllText(lf, Encoding.UTF8)).Skip(1))
                        if (r.Length >= 2 && !string.IsNullOrWhiteSpace(r[0])) map[r[0]] = r[1];
                }
                src.Text[code] = map;
            }
            list.Add(src);
        }
        return list;
    }

    /// <summary>Creates / updates the String Table Collections from the CSVs. Idempotent. Returns a short report.</summary>
    public static string ImportAll()
    {
        EnsureSettings();
        int changes = Sync(true, out string detail);
        return changes == 0 ? "Tables already match the CSVs." + detail : $"{changes} change(s) applied." + detail;
    }

    /// <summary>Number of differences between the CSVs and the generated tables (0 = in sync). With apply=false nothing is written.</summary>
    private static int Sync(bool apply, out string detail)
    {
        int changes = 0;
        var sb = new StringBuilder();
        List<Locale> locales = Codes.Select(c => LocalizationEditorSettings.GetLocale(new LocaleIdentifier(c))).ToList();
        if (locales.Any(l => l == null)) { detail = "\n(locales missing)"; return 1; }

        foreach (Source src in ReadSources())
        {
            StringTableCollection col = LocalizationEditorSettings.GetStringTableCollection(src.Table);
            if (col == null)
            {
                changes++;
                if (!apply) { sb.Append($"\n{src.Table}: collection missing"); continue; }
                Directory.CreateDirectory(TablesDir);
                col = LocalizationEditorSettings.CreateStringTableCollection(src.Table, TablesDir, locales);
            }

            // keys no longer in the CSV
            foreach (SharedTableData.SharedTableEntry e in col.SharedData.Entries.ToList())
            {
                if (src.Keys.Contains(e.Key)) continue;
                changes++;
                if (apply) col.RemoveEntry(e.Id);
            }

            foreach (string code in Codes)
            {
                StringTable table = col.GetTable(new LocaleIdentifier(code)) as StringTable;
                if (table == null)
                {
                    changes++;
                    if (!apply) continue;
                    table = col.AddNewTable(new LocaleIdentifier(code)) as StringTable;
                }

                foreach (string key in src.Keys)
                {
                    string text = src.Text[code].TryGetValue(key, out string t) ? t : "";
                    SharedTableData.SharedTableEntry shared = col.SharedData.GetEntry(key);
                    StringTableEntry entry = shared != null ? table.GetEntry(shared.Id) : null;
                    bool smart = text.Contains("{");
                    if (shared != null && entry != null && entry.Value == text && entry.IsSmart == smart) continue;
                    if (shared == null && text.Length == 0 && code != "en") { }
                    changes++;
                    if (!apply) continue;
                    shared ??= col.SharedData.AddKey(key);
                    entry = table.GetEntry(shared.Id) ?? table.AddEntry(shared.Id, text);
                    entry.Value = text;
                    entry.IsSmart = smart;
                }

                if (apply && table != null)
                {
                    LocalizationEditorSettings.SetPreloadTableFlag(table, true);
                    EditorUtility.SetDirty(table);
                }
            }

            // context -> shared-entry comment (translators' hint)
            foreach (string key in src.Keys)
            {
                SharedTableData.SharedTableEntry shared = col.SharedData.GetEntry(key);
                string ctx = src.Context[key] ?? "";
                Comment cm = shared?.Metadata.GetMetadata<Comment>();
                if ((cm?.CommentText ?? "") == ctx) continue;
                if (shared == null) continue;
                changes++;
                if (!apply) continue;
                if (cm == null) { cm = new Comment(); shared.Metadata.AddMetadata(cm); }
                cm.CommentText = ctx;
            }

            if (apply)
            {
                EditorUtility.SetDirty(col);
                EditorUtility.SetDirty(col.SharedData);
                foreach (var tbl in col.StringTables) EditorUtility.SetDirty(tbl);
                LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(typeof(LocalizationImporter), col);
            }
            sb.Append($"\n{src.Table}: {src.Keys.Count} keys");
        }

        if (apply) AssetDatabase.SaveAssets();
        detail = sb.ToString();
        return changes;
    }

    // ---------------------------------------------------------------- completeness

    /// <summary>Every key non-empty in every locale of every collection (the skill's check). Returns the report text.</summary>
    public static string CompletenessReport()
    {
        var gaps = new List<string>();
        int checkedCount = 0;
        foreach (StringTableCollection col in LocalizationEditorSettings.GetStringTableCollections())
        {
            foreach (var key in col.SharedData.Entries)
            {
                foreach (StringTable table in col.StringTables)
                {
                    checkedCount++;
                    StringTableEntry entry = table.GetEntry(key.Id);
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
                        gaps.Add($"{col.TableCollectionName} / {table.LocaleIdentifier.Code} / {key.Key}");
                }
            }
        }
        if (checkedCount == 0) return "INCONCLUSIVE: no table entries found (no collections, or no locale tables).";
        return gaps.Count == 0
            ? $"COMPLETE: {checkedCount} entries checked, no gaps"
            : $"GAPS ({gaps.Count} of {checkedCount} checked):\n  " + string.Join("\n  ", gaps.Take(60)) + (gaps.Count > 60 ? "\n  ..." : "");
    }

    // ---------------------------------------------------------------- ProjectSetup piece

    /// <summary>Null = OK; otherwise what is wrong (ProjectSetup piece "Localization").</summary>
    public static string Problem()
    {
        var issues = new List<string>();
        if (System.Type.GetType("UnityEngine.Localization.Settings.LocalizationSettings, Unity.Localization") == null) return "package com.unity.localization is not installed";

        LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        if (settings == null) return "no active Localization Settings asset";

        foreach (string code in Codes)
            if (LocalizationEditorSettings.GetLocale(new LocaleIdentifier(code)) == null) issues.Add("locale " + code + " missing");

        var sel = settings.GetStartupLocaleSelectors();
        if (!(sel.Count == 3 && sel[0] is SavedLocaleSelector && sel[1] is SystemLocaleSelector && sel[2] is SpecificLocaleSelector))
            issues.Add("startup selector chain is not Saved -> System -> Specific(en)");
        if (settings.GetStringDatabase() is LocalizedStringDatabase db && db.MissingTranslationState != MissingTranslationBehavior.PrintWarning)
            issues.Add("missing-translation behaviour is not PrintWarning");

        var addr = AddressableAssetSettingsDefaultObject.Settings;
        if (addr == null || addr.BuildAddressablesWithPlayerBuild != AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer)
            issues.Add("Addressables 'Build Addressables on Player Build' is not on");

        if (Sync(false, out string _) != 0) issues.Add("generated tables differ from the CSVs (Import CSVs)");
        string completeness = CompletenessReport();
        if (!completeness.StartsWith("COMPLETE")) issues.Add(completeness.Split('\n')[0]);

        var scenes = EditorBuildSettings.scenes;
        if (scenes.Length < 2 || scenes[0].path != BootScenePath || !scenes[0].enabled) issues.Add("Boot scene is not first in the build list");
        else if (scenes[1].path != GameScenePath) issues.Add("GetStarted_Scene is not second in the build list");

        return issues.Count == 0 ? null : string.Join("; ", issues);
    }

    // ---------------------------------------------------------------- review CSV

    public static string ExportReview()
    {
        Directory.CreateDirectory(ReviewDir);
        var sources = ReadSources();
        var sb = new StringBuilder();
        foreach (string code in Codes.Skip(1))
        {
            var rows = new List<string[]> { new[] { "table", "key", "context", "en", "text" } };
            foreach (Source s in sources)
                foreach (string k in s.Keys)
                    rows.Add(new[] { s.Table, k, s.Context[k], s.Text["en"][k], s.Text[code].TryGetValue(k, out string t) ? t : "" });
            File.WriteAllText($"{ReviewDir}/{code}.csv", LocalizationCsv.Write(rows), new UTF8Encoding(false));
            sb.AppendLine($"{code}: {rows.Count - 1} rows -> {ReviewDir}/{code}.csv");
        }
        AssetDatabase.Refresh();
        return sb.ToString();
    }

    /// <summary>Reads Review/&lt;lang&gt;.csv back into Source/&lt;Table&gt;.&lt;lang&gt;.csv (only keys that exist in the table's English CSV).</summary>
    public static string ImportReview()
    {
        var sources = ReadSources().ToDictionary(s => s.Table);
        var sb = new StringBuilder();
        foreach (string code in Codes.Skip(1))
        {
            string file = $"{ReviewDir}/{code}.csv";
            if (!File.Exists(file)) continue;
            int n = 0;
            foreach (string[] r in LocalizationCsv.Parse(File.ReadAllText(file, Encoding.UTF8)).Skip(1))
            {
                if (r.Length < 5 || !sources.TryGetValue(r[0], out Source s) || !s.Keys.Contains(r[1])) continue;
                s.Text[code][r[1]] = r[4];
                n++;
            }
            foreach (Source s in sources.Values)
            {
                var rows = new List<string[]> { new[] { "key", "text" } };
                foreach (string k in s.Keys) rows.Add(new[] { k, s.Text[code].TryGetValue(k, out string t) ? t : "" });
                File.WriteAllText($"{SourceDir}/{s.Table}.{code}.csv", LocalizationCsv.Write(rows), new UTF8Encoding(false));
            }
            sb.AppendLine($"{code}: {n} rows read");
        }
        AssetDatabase.Refresh();
        return sb.Length == 0 ? "No review CSVs found." : sb.ToString();
    }
}
