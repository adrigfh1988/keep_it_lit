using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KeepItLit.Tests
{
    /// <summary>
    /// F118: loads one maze per (floor|depth, seed) inside a single Play session and runs MazeGenerator.CheckReachability on each. Shared by the
    /// [UnityTest] (MazeReachabilityTests) and by <see cref="StartManual"/> (a pump on EditorApplication.update, so it also runs from the CLI
    /// while a scene is dirty, which would make the Test Runner stop on its save prompt).
    /// </summary>
    public static class MazeTestDriver
    {
        public const string RedirectKey = "keepitlit.test.redirectsaves";
        public const string DirKey = "keepitlit.test.savedir";

        public sealed class Result
        {
            public int Cases, GateFloors, Lockdown, Blackout, KeyHunt, Doors, Keys, Fuses, Buttons, Gates, Stars, SqueezeNotes;
            public readonly List<string> Failures = new List<string>();
            public readonly List<string> Squeeze = new List<string>();
            public double Seconds;

            public string Coverage() =>
                $"cases={Cases} gateFloors={GateFloors} lockdown={Lockdown} blackout={Blackout} keyHunt={KeyHunt} stars={Stars} doors={Doors} keys={Keys} fuses={Fuses} buttons={Buttons} gates={Gates} squeezeNavNotes={SqueezeNotes} seconds={Seconds:0}";
        }

        /// <summary>Runs after SubsystemRegistration and before the first scene loads: points the save store at a temp folder when a test asked, so not even the title screen reads the developer's real save.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RedirectSaves()
        {
            if (SessionState.GetBool(RedirectKey, false)) SaveSystem.DirectoryOverride = SessionState.GetString(DirKey, null);
        }

        public static string BeginRedirect()
        {
            string dir = Path.Combine(Path.GetTempPath(), "kil-maze-tests-" + Guid.NewGuid().ToString("N"));
            SessionState.SetString(DirKey, dir);
            SessionState.SetBool(RedirectKey, true);
            return dir;
        }

        public static void EndRedirect(string dir)
        {
            SessionState.SetBool(RedirectKey, false);
            SaveSystem.DirectoryOverride = null;
            try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
        }

        /// <summary>Names of MazeGenerator debug fields that are not at their default (the test would not measure a normal run).</summary>
        public static string NonDefaultDebugField(MazeGenerator mg)
        {
            SerializedObject so = new SerializedObject(mg);
            string[] bools = { "superEasyMode", "debugForceGates", "debugForceLoop", "debugDescent", "debugStartInMaze", "debugStartInHub", "debugStartInShop", "debugStartInIntake" };
            foreach (string name in bools)
            {
                SerializedProperty p = so.FindProperty(name);
                if (p != null && p.propertyType == SerializedPropertyType.Boolean && p.boolValue) return name;
            }
            SerializedProperty obj = so.FindProperty("debugObjectives");
            if (obj != null && obj.propertyType == SerializedPropertyType.Enum && obj.intValue != 0) return "debugObjectives";
            return null;
        }

        public static IEnumerator Run(bool descent, Result r)
        {
            double t0 = EditorApplication.timeSinceStartup;
            int[] seeds = descent ? MazeTestMatrix.DescentSeeds : MazeTestMatrix.StorySeeds;
            int[] levels = descent ? MazeTestMatrix.DescentDepths : MazeTestMatrix.StoryFloors;

            foreach (int level in levels)
            {
                foreach (int seed in seeds)
                {
                    string label = descent ? $"DESCENT depth {level} seed {seed}" : $"floor {level} seed {seed}";
                    MazeGenerator previous = UnityEngine.Object.FindAnyObjectByType<MazeGenerator>();

                    if (descent)
                    {
                        DescentRun.Begin();
                        DescentRun.Depth = level;
                        GameFlow.IsDescent = true;
                        GameFlow.CurrentFloor = Mathf.Min(level, FloorProfile.FinalFloor);
                    }
                    else
                    {
                        GameFlow.IsDescent = false;
                        DescentRun.ResetState();
                        GameFlow.CurrentFloor = level;
                    }
                    Array.Clear(GameFlow.RolledObjectives, 0, GameFlow.RolledObjectives.Length);
                    GameFlow.Restart(true, seed);

                    MazeGenerator mg = null;
                    int frames = 0;
                    while (frames++ < 900)
                    {
                        yield return null;
                        mg = UnityEngine.Object.FindAnyObjectByType<MazeGenerator>();
                        if (mg != null && mg != previous && mg.BuildMapData() != null) break;
                        mg = null;
                    }
                    if (mg == null) { r.Failures.Add($"{label}: maze did not finish building within 900 frames"); continue; }
                    yield return null;
                    yield return null;
                    mg = UnityEngine.Object.FindAnyObjectByType<MazeGenerator>();

                    if (mg.UsedSeed != seed) { r.Failures.Add($"{label}: UsedSeed {mg.UsedSeed} != {seed}"); continue; }

                    ReachabilityReport rep = mg.CheckReachability();
                    r.Cases++;
                    r.Stars += rep.Stars; r.Doors += rep.Doors; r.Keys += rep.Keys; r.Fuses += rep.FuseBoxes; r.Buttons += rep.Buttons; r.Gates += rep.GateCount;
                    if (rep.GateCount > 0) r.GateFloors++;
                    int flags = GameFlow.CurrentFloor < GameFlow.RolledObjectives.Length ? GameFlow.RolledObjectives[GameFlow.CurrentFloor] : 0;
                    if ((flags & 1) != 0) r.Lockdown++;
                    if ((flags & 2) != 0) r.Blackout++;
                    if ((flags & 4) != 0) r.KeyHunt++;
                    if (rep.Missing.Count > 0) r.Failures.Add($"{label}: DebugMap follows" + System.Environment.NewLine + mg.DebugMap());
                    foreach (string m in rep.Missing) r.Failures.Add($"{label}: {m} (stars={rep.Stars} keys={rep.Keys} fuses={rep.FuseBoxes} buttons={rep.Buttons} gates={rep.GateCount})");
                    foreach (string m in rep.NavMissing) { r.SqueezeNotes++; r.Squeeze.Add($"{label}: {m}"); }
                }
            }

            r.Seconds = EditorApplication.timeSinceStartup - t0;
        }

        // ---------------------------------------------------------------- manual (CLI) run

        private static IEnumerator _manual;
        private static Result _manualResult;
        private static string _manualDir;
        private static bool _manualDescent;
        private static int _manualStage;
        private static string _manualOut;

        /// <summary>eval: KeepItLit.Tests.MazeTestDriver.StartManual(false) (story) or (true) (DESCENT). Writes Logs/MazeTest-story.txt / -descent.txt.</summary>
        public static void StartManual(bool descent)
        {
            if (_manualStage != 0) { Debug.LogWarning("MazeTestDriver: a manual run is already in progress"); return; }
            _manualDescent = descent;
            _manualOut = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", descent ? "MazeTest-descent.txt" : "MazeTest-story.txt");
            File.WriteAllText(_manualOut, "RUNNING\n");
            _manualDir = BeginRedirect();
            _manualResult = new Result();
            _manualStage = 1;
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            try
            {
                switch (_manualStage)
                {
                    case 1:
                        EditorApplication.EnterPlaymode();
                        _manualStage = 2;
                        break;
                    case 2:
                        if (EditorApplication.isPlaying && Time.frameCount > 5) { _manual = Run(_manualDescent, _manualResult); _manualStage = 3; }
                        break;
                    case 3:
                        if (!_manual.MoveNext()) _manualStage = 4;
                        break;
                    case 4:
                        Finish();
                        EditorApplication.ExitPlaymode();
                        _manualStage = 5;
                        break;
                    case 5:
                        if (!EditorApplication.isPlaying) { EndRedirect(_manualDir); EditorApplication.update -= Pump; _manualStage = 0; }
                        break;
                }
            }
            catch (Exception e)
            {
                _manualResult.Failures.Add("EXCEPTION " + e);
                Finish();
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                EndRedirect(_manualDir);
                EditorApplication.update -= Pump;
                _manualStage = 0;
            }
        }

        private static void Finish()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine((_manualResult.Failures.Count == 0 ? "PASS " : "FAIL ") + (_manualDescent ? "descent " : "story ") + _manualResult.Coverage());
            foreach (string f in _manualResult.Failures) sb.AppendLine("UNREACHABLE/FAIL " + f);
            foreach (string f in _manualResult.Squeeze) sb.AppendLine("squeeze-note " + f);
            File.WriteAllText(_manualOut, sb.ToString());
        }
    }
}
