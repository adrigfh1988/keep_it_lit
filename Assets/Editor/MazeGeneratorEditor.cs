using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MazeGenerator))]
public class MazeGeneratorEditor : Editor
{
    private static bool _showFullSettings = true;

    private static readonly HashSet<string> HandledDebugProps = new HashSet<string>
    {
        "m_Script",
        "debugStartInMaze",
        "debugDescent",
        "debugStartInHub",
        "debugStartInShop",
        "debugStartInFinale",
        "debugFinaleAllFiles",
        "debugFloor",
        "debugFloorShape",
        "debugObjectives",
        "debugForceLoop",
        "debugForceGates",
        "superEasyMode",
        "debugFullMap",
        "debugEntities",
        "debugLivingBody",
        "debugShards",
        "debugForceTouchControls",
        "logAudioResolution"
    };

    private enum StartMode
    {
        TitleNormal = 0,
        StoryMaze = 1,
        DescentMaze = 2,
        AsylumHub = 3,
        Shop = 4,
        Finale = 5
    }

    private static readonly string[] StartModeLabels =
    {
        "Title (Normal)",
        "Story Maze",
        "DESCENT Maze",
        "Asylum Hub",
        "Shop",
        "Finale"
    };

    private static readonly string[] ShapeButtonLabels =
    {
        "Auto",
        "Rect",
        "Ring",
        "TwinWings",
        "Cross",
        "LChasm"
    };

    private static readonly MazeGenerator.DebugFloorShape[] ShapeButtonValues =
    {
        MazeGenerator.DebugFloorShape.Auto,
        MazeGenerator.DebugFloorShape.Rectangle,
        MazeGenerator.DebugFloorShape.Ring,
        MazeGenerator.DebugFloorShape.TwinWings,
        MazeGenerator.DebugFloorShape.Cross,
        MazeGenerator.DebugFloorShape.LChasm
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        MazeGenerator mg = (MazeGenerator)target;

        SerializedProperty pSeed = serializedObject.FindProperty("seed");
        SerializedProperty pStartMaze = serializedObject.FindProperty("debugStartInMaze");
        SerializedProperty pDescent = serializedObject.FindProperty("debugDescent");
        SerializedProperty pStartHub = serializedObject.FindProperty("debugStartInHub");
        SerializedProperty pStartShop = serializedObject.FindProperty("debugStartInShop");
        SerializedProperty pStartFinale = serializedObject.FindProperty("debugStartInFinale");
        SerializedProperty pFinaleFiles = serializedObject.FindProperty("debugFinaleAllFiles");
        SerializedProperty pFloor = serializedObject.FindProperty("debugFloor");
        SerializedProperty pShape = serializedObject.FindProperty("debugFloorShape");
        SerializedProperty pObjectives = serializedObject.FindProperty("debugObjectives");
        SerializedProperty pForceLoop = serializedObject.FindProperty("debugForceLoop");
        SerializedProperty pForceGates = serializedObject.FindProperty("debugForceGates");
        SerializedProperty pSuperEasy = serializedObject.FindProperty("superEasyMode");
        SerializedProperty pFullMap = serializedObject.FindProperty("debugFullMap");
        SerializedProperty pEntities = serializedObject.FindProperty("debugEntities");
        SerializedProperty pLivingBody = serializedObject.FindProperty("debugLivingBody");
        SerializedProperty pShards = serializedObject.FindProperty("debugShards");
        SerializedProperty pTouch = serializedObject.FindProperty("debugForceTouchControls");
        SerializedProperty pLogAudio = serializedObject.FindProperty("logAudioResolution");

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Header + Reset All button
        EditorGUILayout.BeginHorizontal();
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        EditorGUILayout.LabelField("DEBUG & QUICK TEST", titleStyle);

        List<string> activeFlags = GetActiveFlagList(
            pSeed, pStartMaze, pDescent, pStartHub, pStartShop, pStartFinale,
            pFloor, pShape, pObjectives, pForceLoop, pForceGates,
            pSuperEasy, pFullMap, pEntities, pShards, pTouch, pLogAudio);

        GUI.enabled = activeFlags.Count > 0;
        Color prevBg = GUI.backgroundColor;
        if (activeFlags.Count > 0) GUI.backgroundColor = new Color(1f, 0.75f, 0.3f);
        if (GUILayout.Button("Reset All Debug Flags", GUILayout.Width(155f), GUILayout.Height(20f)))
        {
            ResetAllDebugFlags(
                pSeed, pStartMaze, pDescent, pStartHub, pStartShop, pStartFinale, pFinaleFiles,
                pFloor, pShape, pObjectives, pForceLoop, pForceGates,
                pSuperEasy, pFullMap, pEntities, pLivingBody, pShards, pTouch, pLogAudio);
        }
        GUI.backgroundColor = prevBg;
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (activeFlags.Count > 0)
        {
            EditorGUILayout.HelpBox("Active overrides: " + string.Join(", ", activeFlags) + " (reset before committing scene)", MessageType.Warning);
        }

        EditorGUILayout.Space(3f);

        // 1. Start Mode Selector
        EditorGUILayout.LabelField("1. Where to Start (on Play)", EditorStyles.miniBoldLabel);
        StartMode currentMode = GetStartMode(pStartMaze, pDescent, pStartHub, pStartShop, pStartFinale);
        int pickedMode = GUILayout.Toolbar((int)currentMode, StartModeLabels, GUILayout.Height(22f));
        if (pickedMode != (int)currentMode)
        {
            SetStartMode((StartMode)pickedMode, pStartMaze, pDescent, pStartHub, pStartShop, pStartFinale, pFloor);
        }

        if ((StartMode)pickedMode == StartMode.Finale)
        {
            EditorGUILayout.PropertyField(pFinaleFiles, new GUIContent("All 5 Case Files Read (Enable Ending B)"));
        }

        EditorGUILayout.Space(4f);

        // 2. Floor / Depth & Seed
        EditorGUILayout.LabelField("2. Floor / Depth & Seed", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        bool isDescentMode = pDescent.boolValue || (Application.isPlaying && GameFlow.IsDescent);
        string floorLabel = isDescentMode ? "DESCENT Depth" : "Story Floor (0=1)";
        EditorGUIUtility.labelWidth = 110f;
        pFloor.intValue = Mathf.Max(0, EditorGUILayout.IntField(floorLabel, pFloor.intValue, GUILayout.Width(165f)));
        EditorGUIUtility.labelWidth = 0f;

        int[] quickFloors = isDescentMode ? new[] { 1, 2, 3, 4, 5, 8, 12 } : new[] { 1, 2, 3, 4, 5 };
        foreach (int f in quickFloors)
        {
            bool selected = pFloor.intValue == f || (!isDescentMode && f == 1 && pFloor.intValue == 0);
            GUI.backgroundColor = selected ? new Color(0.55f, 0.85f, 1f) : prevBg;
            if (GUILayout.Button(isDescentMode ? $"-{f}" : $"{f}", EditorStyles.miniButton, GUILayout.Width(28f)))
            {
                pFloor.intValue = f;
            }
        }
        GUI.backgroundColor = prevBg;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUIUtility.labelWidth = 110f;
        pSeed.intValue = EditorGUILayout.IntField("Fixed Seed (0=Rng)", pSeed.intValue, GUILayout.Width(210f));
        EditorGUIUtility.labelWidth = 0f;
        if (GUILayout.Button("0 (Random)", EditorStyles.miniButton, GUILayout.Width(75f)))
        {
            pSeed.intValue = 0;
        }
        if (GUILayout.Button("Roll Fixed", EditorStyles.miniButton, GUILayout.Width(75f)))
        {
            pSeed.intValue = Random.Range(10000, 9999999);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4f);

        // 3. Floor Shape (Void Mask)
        EditorGUILayout.LabelField("3. Floor Footprint / Void Shape (F124)", EditorStyles.miniBoldLabel);
        int currentShapeIdx = 0;
        for (int i = 0; i < ShapeButtonValues.Length; i++)
        {
            if (pShape.intValue == (int)ShapeButtonValues[i])
            {
                currentShapeIdx = i;
                break;
            }
        }
        int newShapeIdx = GUILayout.Toolbar(currentShapeIdx, ShapeButtonLabels, GUILayout.Height(22f));
        if (newShapeIdx != currentShapeIdx)
        {
            pShape.intValue = (int)ShapeButtonValues[newShapeIdx];
        }

        EditorGUILayout.Space(4f);

        // 4. Quick Toggles & Overrides
        EditorGUILayout.LabelField("4. Mechanics, Entities & Cheats", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        DrawToggleButton(pSuperEasy, "Super Easy", "Pack stars/keys near start, no locked doors");
        bool prevFullMap = pFullMap.boolValue;
        DrawToggleButton(pFullMap, "Full Map", "Unlock and reveal the entire floor map (all non-void cells, walls, doors and switches, like clearing the loop) and open it automatically");
        if (Application.isPlaying && pFullMap.boolValue != prevFullMap)
        {
            FloorMapUi mapUi = FindAnyObjectByType<FloorMapUi>();
            if (mapUi != null)
            {
                if (pFullMap.boolValue)
                {
                    mapUi.Unlock();
                    mapUi.RevealAll();
                    mapUi.OpenWhenAvailable();
                }
                else
                {
                    mapUi.Close();
                }
            }
        }
        DrawToggleButton(pForceLoop, "Force Loop", "Build the F105/F106 looping corridor on any themed floor");
        DrawToggleButton(pForceGates, "Force Gates", "Use F111 outer-wall exit gates instead of hatch");
        DrawToggleButton(pTouch, "Touch UI", "Force on-screen mobile controls");
        DrawToggleButton(pLogAudio, "Log Audio", "Log audio clip resolution to Console");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUIUtility.labelWidth = 85f;
        EditorGUILayout.PropertyField(pObjectives, new GUIContent("Objectives"));
        EditorGUILayout.PropertyField(pEntities, new GUIContent("Entities"));
        EditorGUIUtility.labelWidth = 0f;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        EditorGUIUtility.labelWidth = 85f;
        if ((pEntities.intValue & (int)EntityKind.LivingBody) != 0)
        {
            EditorGUILayout.PropertyField(pLivingBody, new GUIContent("Living Body"));
        }
        EditorGUILayout.PropertyField(pShards, new GUIContent("Give Shards"));
        EditorGUIUtility.labelWidth = 0f;
        EditorGUILayout.EndHorizontal();

        // 5. Live Play-Mode Controls
        if (Application.isPlaying)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            string liveMode = GameFlow.IsDescent ? $"DESCENT Level -{DescentRun.Depth}" : $"Story Floor {GameFlow.CurrentFloor}";
            EditorGUILayout.LabelField(
                $"LIVE RUN:  {liveMode}   |   Shape: {mg.ActiveShape} ({mg.PlayableCellCount} cells, {mg.GridWidth}x{mg.GridHeight})   |   Seed: {mg.UsedSeed}",
                EditorStyles.miniBoldLabel);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.55f, 0.95f, 0.65f);
            if (GUILayout.Button("Rebuild Maze (New Seed)", GUILayout.Height(24f)))
            {
                serializedObject.ApplyModifiedProperties();
                TriggerLiveRebuild(mg, pDescent.boolValue, pFloor.intValue, Random.Range(10000, int.MaxValue));
            }
            GUI.backgroundColor = new Color(0.65f, 0.85f, 1f);
            if (GUILayout.Button("Rebuild (Same Seed)", GUILayout.Height(24f)))
            {
                serializedObject.ApplyModifiedProperties();
                TriggerLiveRebuild(mg, pDescent.boolValue, pFloor.intValue, mg.UsedSeed);
            }
            if (GUILayout.Button("Next Floor / Depth +1", GUILayout.Height(24f)))
            {
                int next = (GameFlow.IsDescent ? DescentRun.Depth : GameFlow.CurrentFloor) + 1;
                pFloor.intValue = next;
                serializedObject.ApplyModifiedProperties();
                TriggerLiveRebuild(mg, GameFlow.IsDescent || pDescent.boolValue, next, Random.Range(10000, int.MaxValue));
            }
            GUI.backgroundColor = prevBg;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Print Maze Map (Clipboard & Logs/MazeMap.txt)"))
            {
                string map = mg.DebugMap();
                EditorGUIUtility.systemCopyBuffer = map;
                string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "MazeMap.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, map);
                Debug.Log($"MazeMap written to {path} and copied to clipboard:\n{map}", mg);
            }
            if (GUILayout.Button("Unlock & Open Floor Map (M)"))
            {
                FloorMapUi mapUi = FindAnyObjectByType<FloorMapUi>();
                if (mapUi != null)
                {
                    mapUi.Unlock();
                    mapUi.RevealAll();
                    mapUi.OpenWhenAvailable();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(6f);
        _showFullSettings = EditorGUILayout.Foldout(_showFullSettings, "MazeGenerator Configuration (Geometry, Materials, Themes, Audio, Terror)", true, EditorStyles.foldoutHeader);
        if (_showFullSettings)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (HandledDebugProps.Contains(iterator.name)) continue;
                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void TriggerLiveRebuild(MazeGenerator mg, bool descent, int floorOrDepth, int newSeed)
    {
        if (descent)
        {
            if (!GameFlow.IsDescent) DescentRun.Begin();
            GameFlow.IsDescent = true;
            DescentRun.Depth = Mathf.Max(1, floorOrDepth);
            GameFlow.CurrentFloor = Mathf.Min(DescentRun.Depth, FloorProfile.FinalFloor);
        }
        else
        {
            GameFlow.IsDescent = false;
            DescentRun.ResetState();
            GameFlow.CurrentFloor = Mathf.Clamp(floorOrDepth > 0 ? floorOrDepth : 1, 1, FloorProfile.FinalFloor);
        }
        GameFlow.Restart(true, newSeed);
    }

    private static void DrawToggleButton(SerializedProperty prop, string label, string tooltip)
    {
        Color prev = GUI.backgroundColor;
        if (prop.boolValue) GUI.backgroundColor = new Color(1f, 0.8f, 0.35f);
        bool next = GUILayout.Toggle(prop.boolValue, new GUIContent(label, tooltip), EditorStyles.miniButton);
        if (next != prop.boolValue) prop.boolValue = next;
        GUI.backgroundColor = prev;
    }

    private static StartMode GetStartMode(
        SerializedProperty pStartMaze, SerializedProperty pDescent,
        SerializedProperty pStartHub, SerializedProperty pStartShop, SerializedProperty pStartFinale)
    {
        if (pStartShop.boolValue) return StartMode.Shop;
        if (pStartFinale.boolValue) return StartMode.Finale;
        if (pStartHub.boolValue) return StartMode.AsylumHub;
        if (pStartMaze.boolValue) return pDescent.boolValue ? StartMode.DescentMaze : StartMode.StoryMaze;
        return StartMode.TitleNormal;
    }

    private static void SetStartMode(
        StartMode mode,
        SerializedProperty pStartMaze, SerializedProperty pDescent,
        SerializedProperty pStartHub, SerializedProperty pStartShop, SerializedProperty pStartFinale,
        SerializedProperty pFloor)
    {
        pStartMaze.boolValue = mode == StartMode.StoryMaze || mode == StartMode.DescentMaze;
        pDescent.boolValue = mode == StartMode.DescentMaze;
        pStartHub.boolValue = mode == StartMode.AsylumHub;
        pStartShop.boolValue = mode == StartMode.Shop;
        pStartFinale.boolValue = mode == StartMode.Finale;

        if (mode == StartMode.DescentMaze && pFloor.intValue < 2)
        {
            pFloor.intValue = 2; // Depth 2+ rolls non-rectangular void footprints by default
        }
    }

    private static List<string> GetActiveFlagList(
        SerializedProperty pSeed,
        SerializedProperty pStartMaze, SerializedProperty pDescent,
        SerializedProperty pStartHub, SerializedProperty pStartShop, SerializedProperty pStartFinale,
        SerializedProperty pFloor, SerializedProperty pShape, SerializedProperty pObjectives,
        SerializedProperty pForceLoop, SerializedProperty pForceGates,
        SerializedProperty pSuperEasy, SerializedProperty pFullMap, SerializedProperty pEntities,
        SerializedProperty pShards, SerializedProperty pTouch, SerializedProperty pLogAudio)
    {
        List<string> list = new List<string>();
        if (pSeed.intValue != 0) list.Add($"seed={pSeed.intValue}");
        if (pStartMaze.boolValue) list.Add("StartInMaze");
        if (pDescent.boolValue) list.Add("DESCENT");
        if (pStartHub.boolValue) list.Add("StartInHub");
        if (pStartShop.boolValue) list.Add("StartInShop");
        if (pStartFinale.boolValue) list.Add("StartInFinale");
        if (pFloor.intValue != 0) list.Add($"Floor={pFloor.intValue}");
        if (pShape.intValue != (int)MazeGenerator.DebugFloorShape.Auto) list.Add($"Shape={(MazeGenerator.DebugFloorShape)pShape.intValue}");
        if (pObjectives.intValue != 0) list.Add($"Objectives={(FloorProfile.Objective)pObjectives.intValue}");
        if (pForceLoop.boolValue) list.Add("ForceLoop");
        if (pForceGates.boolValue) list.Add("ForceGates");
        if (pSuperEasy.boolValue) list.Add("SuperEasy");
        if (pFullMap.boolValue) list.Add("FullMap");
        if (pEntities.intValue != 0) list.Add($"Entities={(EntityKind)pEntities.intValue}");
        if (pShards.intValue != 0) list.Add($"Shards={pShards.intValue}");
        if (pTouch.boolValue) list.Add("ForceTouch");
        if (pLogAudio.boolValue) list.Add("LogAudio");
        return list;
    }

    private static void ResetAllDebugFlags(
        SerializedProperty pSeed,
        SerializedProperty pStartMaze, SerializedProperty pDescent,
        SerializedProperty pStartHub, SerializedProperty pStartShop, SerializedProperty pStartFinale,
        SerializedProperty pFinaleFiles,
        SerializedProperty pFloor, SerializedProperty pShape, SerializedProperty pObjectives,
        SerializedProperty pForceLoop, SerializedProperty pForceGates,
        SerializedProperty pSuperEasy, SerializedProperty pFullMap, SerializedProperty pEntities, SerializedProperty pLivingBody,
        SerializedProperty pShards, SerializedProperty pTouch, SerializedProperty pLogAudio)
    {
        pSeed.intValue = 0;
        pStartMaze.boolValue = false;
        pDescent.boolValue = false;
        pStartHub.boolValue = false;
        pStartShop.boolValue = false;
        pStartFinale.boolValue = false;
        pFinaleFiles.boolValue = true;
        pFloor.intValue = 0;
        pShape.intValue = (int)MazeGenerator.DebugFloorShape.Auto;
        pObjectives.intValue = 0;
        pForceLoop.boolValue = false;
        pForceGates.boolValue = false;
        pSuperEasy.boolValue = false;
        pFullMap.boolValue = false;
        pEntities.intValue = 0;
        pLivingBody.intValue = 0;
        pShards.intValue = 0;
        pTouch.boolValue = false;
        pLogAudio.boolValue = false;
    }
}
