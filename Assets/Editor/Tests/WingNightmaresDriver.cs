using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

namespace KeepItLit.Tests
{
    /// <summary>
    /// F123: a manual Play-mode smoke test of the wing nightmares, run from the CLI like <see cref="MazeTestDriver"/>:
    /// <c>unity command eval --code 'KeepItLit.Tests.WingNightmaresDriver.Start(@"C:\\some\\folder");'</c>, then read <c>Logs/WingNightmares.txt</c>.
    /// One Play session per case, entered from the OPEN scene (so unsaved InteractableKit slots are used - GameFlow.Restart would reload the scene from
    /// disk). The MazeGenerator's serialized debug fields are set before each session and restored afterwards; saves are redirected to a temp folder and the
    /// Patient Record to a throw-away key, so nothing real is read or written. Needs the Unity window in the foreground (frames do not advance otherwise).
    /// Not an NUnit test: it enters Play itself and renders PNGs into the folder it is given.
    /// </summary>
    public static class WingNightmaresDriver
    {
        private static StringBuilder _log;
        private static string _outDir;
        private static string _logPath;
        private static IEnumerator _run;
        private static readonly Stack<IEnumerator> _stack = new Stack<IEnumerator>();
        private static string _redirectDir;
        private static string _recordKey;
        private static readonly List<string> _errors = new List<string>();
        private static int _pass, _fail;

        private static void Say(string line)
        {
            _log.AppendLine(line);
            File.WriteAllText(_logPath, _log.ToString());
        }

        private static void Check(bool ok, string what)
        {
            if (ok) _pass++; else _fail++;
            Say((ok ? "PASS " : "FAIL ") + what);
        }

        public static bool IsRunning => _run != null;

        private static string _only;

        /// <summary>"only" (optional): run just the sessions whose label contains it, e.g. "floor2" or "reach".</summary>
        public static void Start(string outDir, string only = null)
        {
            _only = string.IsNullOrEmpty(only) ? null : only;
            if (_run != null) { Debug.LogWarning("WingNightmaresDriver: already running"); return; }
            _outDir = outDir;
            Directory.CreateDirectory(outDir);
            _logPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "WingNightmares.txt");
            _log = new StringBuilder();
            _pass = _fail = 0;
            Say("RUNNING");
            _redirectDir = MazeTestDriver.BeginRedirect();
            _recordKey = "kil-f123-record-" + Guid.NewGuid().ToString("N");
            _stack.Clear();
            _run = Suite();
            _stack.Push(_run);
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            try
            {
                // A nested IEnumerator yielded by a step is run to its end before its parent continues (any other yield = wait one frame).
                while (_stack.Count > 0)
                {
                    IEnumerator top = _stack.Peek();
                    if (!top.MoveNext()) { _stack.Pop(); continue; }
                    if (top.Current is IEnumerator inner) { _stack.Push(inner); continue; }
                    return;
                }
                Finish();
            }
            catch (Exception e)
            {
                Say("EXCEPTION " + e);
                _fail++;
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                Finish();
            }
        }

        private static void Finish()
        {
            EditorApplication.update -= Pump;
            _run = null;
            Say($"DONE pass={_pass} fail={_fail}");
            if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            MazeTestDriver.EndRedirect(_redirectDir);
            PlayerPrefs.DeleteKey(_recordKey);
            PlayerPrefs.DeleteKey(_recordKey + ".unreadable");
            PatientRecord.KeyOverride = null;
            if (_origLocale != null)
            {
                UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale = _origLocale;
                L10n.ClearCache();
                _origLocale = null;
            }
        }

        // ------------------------------------------------------------------ session plumbing

        private sealed class Overrides
        {
            public int Floor, Seed;
            public bool Descent;
            public readonly Dictionary<string, bool> Terror = new Dictionary<string, bool>();
        }

        private static MazeGenerator SceneMaze() => UnityEngine.Object.FindAnyObjectByType<MazeGenerator>();

        /// <summary>Sets the debug fields on the scene's MazeGenerator, enters Play, waits for the maze and the run, runs the body, leaves Play, restores the fields.</summary>
        private static IEnumerator Session(string label, Overrides o, Func<IEnumerator> body)
        {
            if (_only != null && !label.Contains(_only)) yield break;
            MazeGenerator scene = SceneMaze();
            SerializedObject so = new SerializedObject(scene);
            Dictionary<string, object> original = new Dictionary<string, object>();
            void Set(string path, object v)
            {
                SerializedProperty p = so.FindProperty(path);
                if (p == null) { Say("WARN no property " + path); return; }
                if (!original.ContainsKey(path)) original[path] = p.propertyType == SerializedPropertyType.Boolean ? (object)p.boolValue : p.intValue;
                if (v is bool b) p.boolValue = b; else p.intValue = (int)v;
            }
            Set("seed", o.Seed);
            Set("debugFloor", o.Floor);
            Set("debugStartInMaze", true);
            Set("debugDescent", o.Descent);
            foreach (KeyValuePair<string, bool> kv in o.Terror) Set("terror." + kv.Key, kv.Value);
            so.ApplyModifiedPropertiesWithoutUndo();

            Say($"--- session {label} (floor {o.Floor} seed {o.Seed}{(o.Descent ? " DESCENT" : "")}{(o.Terror.Count > 0 ? " " + string.Join(",", o.Terror.Select(t => t.Key + "=" + t.Value)) : "")})");
            _errors.Clear();
            Application.logMessageReceived += OnLog;

            EditorApplication.EnterPlaymode();
            while (!EditorApplication.isPlaying) yield return null;
            PatientRecord.KeyOverride = _recordKey;

            MazeGenerator mg = null;
            for (int frames = 0; frames < 1500; frames++)
            {
                yield return null;
                mg = SceneMaze();
                if (mg != null && Time.frameCount > 10 && mg.BuildMapData() != null && GameFlow.IsRunActive && UnityEngine.Object.FindAnyObjectByType<WingNightmareDirector>() != null) break;
                mg = null;
            }

            if (mg == null) Check(false, label + ": the maze/run did not come up in 1500 frames");
            else
            {
                for (int i = 0; i < 20; i++) yield return null;
                IEnumerator inner = body();
                while (true)
                {
                    bool more;
                    try { more = inner.MoveNext(); }
                    catch (Exception e) { Say("EXCEPTION in " + label + ": " + e); _fail++; break; }
                    if (!more) break;
                    yield return inner.Current;
                }
            }

            Application.logMessageReceived -= OnLog;
            foreach (string e in _errors.Distinct()) Say("CONSOLE " + e);
            EditorApplication.ExitPlaymode();
            while (EditorApplication.isPlaying) yield return null;
            yield return null;

            foreach (KeyValuePair<string, object> kv in original)
            {
                SerializedProperty p = so.FindProperty(kv.Key);
                if (p == null) continue;
                if (kv.Value is bool b) p.boolValue = b; else p.intValue = (int)kv.Value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            for (int i = 0; i < 5; i++) yield return null;
        }

        private static void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            if (condition.Contains("HubCache") || condition.Contains("RuntimeNavMeshBuilder") || condition.Contains("WallLamp")) return; // known pre-existing noise
            _errors.Add(type + ": " + condition.Split('\n')[0]);
        }

        // ------------------------------------------------------------------ helpers

        private static Transform Player() => UnityEngine.Object.FindAnyObjectByType<FirstPersonRig>().transform; // the rig is on the live player only

        private static void Teleport(Transform t, Vector3 pos)
        {
            CharacterController cc = t.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            t.position = pos;
            if (cc != null) cc.enabled = true;
            Physics.SyncTransforms();
        }

        /// <summary>Turns the player AND the first-person camera to look along a flat direction (the camera yaw lives in ThirdPersonController, not in the body).</summary>
        private static void Face(Transform player, Vector3 direction, float pitch = 0f)
        {
            direction.y = 0f;
            float yaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;
            StarterAssets.ThirdPersonController c = player.GetComponent<StarterAssets.ThirdPersonController>();
            typeof(StarterAssets.ThirdPersonController).GetField("_cinemachineTargetYaw", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c, yaw);
            typeof(StarterAssets.ThirdPersonController).GetField("_cinemachineTargetPitch", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(c, pitch);
            player.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private static void WarpHunter(AIFollower hunter, Vector3 pos)
        {
            NavMeshAgent agent = hunter.GetComponent<NavMeshAgent>();
            if (NavMesh.SamplePosition(pos, out NavMeshHit hit, 3f, NavMesh.AllAreas)) pos = hit.position;
            if (agent != null && agent.enabled) agent.Warp(pos); else hunter.transform.position = pos;
        }

        private static readonly List<string> _subs = new List<string>();

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                string s = Subtitle();
                if (!string.IsNullOrEmpty(s) && (_subs.Count == 0 || _subs[_subs.Count - 1] != s)) _subs.Add(s);
                yield return null;
            }
        }

        /// <summary>True when this subtitle text showed since the last ClearSubs (polled every frame inside Wait).</summary>
        private static bool Saw(string text) => _subs.Contains(text) || Subtitle() == text;

        private static void ClearSubs() => _subs.Clear();

        private static string Subtitle()
        {
            TextMeshProUGUI t = UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include).FirstOrDefault(x => x.name == "DreadSubtitle");
            return t != null ? t.text : "";
        }

        private static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).GetValue(o);

        private static string Pose(Vector3 v) => $"({v.x:0.0},{v.z:0.0})";

        /// <summary>A PNG from a temporary camera (a copy of the player's), lit by a temporary fill light unless `fill` is false.</summary>
        private static void Shot(string name, Vector3 position, Vector3 lookAt, bool fill = true, float fov = 65f, float fillRange = 9f)
        {
            Camera playerCam = UnityEngine.Object.FindAnyObjectByType<FirstPersonRig>().PlayerCamera;
            GameObject go = new GameObject("F123Cam");
            Camera cam = go.AddComponent<Camera>();
            cam.CopyFrom(playerCam);
            cam.fieldOfView = fov;
            cam.enabled = false;
            UniversalAdditionalCameraData data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(lookAt - position, Vector3.up));
            if (fill)
            {
                GameObject lightGo = new GameObject("Fill");
                lightGo.transform.SetParent(go.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 0.3f, -0.4f);
                Light l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = fillRange;
                l.intensity = 3f;
                l.color = new Color(1f, 0.93f, 0.82f);
                l.shadows = LightShadows.None;
            }

            RenderTexture rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(_outDir, name + ".png"), tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            UnityEngine.Object.Destroy(go);
        }

        /// <summary>The cells from the start toward the farthest reachable cell, `count` of them, as a walking path.</summary>
        private static List<Vector2Int> WalkPath(MazeGenerator mg, int count)
        {
            int[,] steps = mg.StepsFrom(mg.CellCenter(0, 0));
            Vector2Int target = Vector2Int.zero;
            int best = -1;
            for (int x = 0; x < mg.GridWidth; x++)
                for (int z = 0; z < mg.GridHeight; z++)
                    if (steps[x, z] >= 0 && steps[x, z] <= count - 1 && steps[x, z] > best) { best = steps[x, z]; target = new Vector2Int(x, z); }
            List<Vector2Int> path = new List<Vector2Int> { target };
            Vector2Int c = target;
            while (steps[c.x, c.y] > 0)
            {
                for (int d = 0; d < 4; d++)
                {
                    if (mg.IsClosedWall(c, d)) continue;
                    Vector2Int n = MazeGenerator.NeighbourOf(c, d);
                    if (n.x < 0 || n.y < 0 || n.x >= mg.GridWidth || n.y >= mg.GridHeight || steps[n.x, n.y] != steps[c.x, c.y] - 1) continue;
                    c = n;
                    path.Add(c);
                    break;
                }
            }
            path.Reverse();
            return path;
        }

        private static string Layout(MazeGenerator mg)
        {
            StringBuilder sb = new StringBuilder();
            for (int x = 0; x < mg.GridWidth; x++)
                for (int z = 0; z < mg.GridHeight; z++)
                    for (int d = 0; d < 4; d++) sb.Append(mg.IsClosedWall(new Vector2Int(x, z), d) ? '1' : '0');
            MazeMapData data = mg.BuildMapData();
            sb.Append("|stars");
            foreach (Vector2Int s in data.Stars.OrderBy(v => v.x * 100 + v.y)) sb.Append(' ').Append(s);
            sb.Append("|keys");
            foreach (MazeMapData.KeyInfo k in data.Keys) sb.Append(' ').Append(k.Cell);
            sb.Append("|buttons");
            foreach (MazeMapData.ButtonInfo b in data.Buttons) sb.Append(' ').Append(b.Cell);
            return sb.ToString();
        }

        private static string BankSignature(MazeGenerator mg)
        {
            StringBuilder sb = new StringBuilder();
            foreach (MorgueDrawerBank bank in mg.DrawerBanks)
            {
                sb.Append(bank.name).Append(':').Append(bank.OpenTray).Append(':');
                foreach (TextMeshPro t in bank.GetComponentsInChildren<TextMeshPro>(true)) sb.Append(t.text.Replace("\n", "/")).Append(',');
                sb.Append(';');
            }
            foreach (NightmareAmpoule a in mg.Ampoules) sb.Append(a.name).Append(Pose(a.transform.position)).Append(';');
            foreach (GameObject n in mg.DirectorNotices) sb.Append(n.name).Append(Pose(n.transform.position)).Append(';');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the suite

        private static IEnumerator Suite()
        {
            // ---- floor 1
            yield return Session("floor1", new Overrides { Floor = 1, Seed = 101 }, Floor1);

            // ---- floor 2
            yield return Session("floor2", new Overrides { Floor = 2, Seed = 202 }, Floor2);

            // ---- floor 3: flags on (twice, for determinism) and off
            string sigA = null, sigB = null, layoutOn = null, layoutOff = null;
            yield return Session("floor3", new Overrides { Floor = 3, Seed = 303 }, () => Floor3(s => sigA = s, s => layoutOn = s));
            yield return Session("floor3-again", new Overrides { Floor = 3, Seed = 303 }, () => Capture(m => { sigB = BankSignature(m); }));
            Check(sigA != null && sigA == sigB, "floor 3 same seed: the same banks, tags and trays twice (" + (sigA ?? "null") + ")");
            Overrides off3 = new Overrides { Floor = 3, Seed = 303 };
            off3.Terror["morgueDrawers"] = false;
            yield return Session("floor3-off", off3, () => Capture(m => { layoutOff = Layout(m); Check(m.DrawerBanks.Count == 0, "floor 3 flag off: no banks"); }));
            Check(layoutOn != null && layoutOn == layoutOff, "floor 3: walls, stars, keys and buttons identical with the drawers on and off (A10 bait stars intact)");

            // ---- floor 4
            string sig4 = null, layout4On = null, layout4Off = null;
            yield return Session("floor4", new Overrides { Floor = 4, Seed = 404 }, () => Floor4(s => layout4On = s));
            Overrides off4 = new Overrides { Floor = 4, Seed = 404 };
            off4.Terror["ampoules"] = false;
            yield return Session("floor4-off", off4, () => Capture(m => { layout4Off = Layout(m); Check(m.Ampoules.Count == 0, "floor 4 flag off: no ampoules"); }));
            Check(layout4On != null && layout4On == layout4Off, "floor 4: layout identical with the ampoules on and off");
            _ = sig4;

            // ---- floor 5
            string layout5On = null, layout5Off = null;
            yield return Session("floor5", new Overrides { Floor = 5, Seed = 606 }, () => Floor5(s => layout5On = s));
            Overrides off5 = new Overrides { Floor = 5, Seed = 606 };
            off5.Terror["houseRules"] = false;
            yield return Session("floor5-off", off5, () => Capture(m => { layout5Off = Layout(m); Check(m.DirectorNotices.Count == 0, "floor 5 flag off: no notices"); }));
            Check(layout5On != null && layout5On == layout5Off, "floor 5: layout identical with the notices on and off");

            // ---- the shipped languages: measure, screenshot the notice, the slip and the loss screen
            foreach (string code in new[] { "fr", "de", "en" })
            {
                string c = code;
                yield return Session("lang-" + c, new Overrides { Floor = 5, Seed = 606 }, () => Languages(c));
            }

            yield return Session("inv505", new Overrides { Floor = 5, Seed = 505 }, Inv505);

            // ---- DESCENT: depth 3 and 5 are untouched
            foreach (int depth in new[] { 3, 5 })
            {
                yield return Session("descent" + depth, new Overrides { Floor = depth, Seed = 101, Descent = true }, () => Descent(depth));
            }

            // ---- reachability with the in-memory scene (the F123 passes really run): floors 1-5 x the matrix seeds
            foreach (int floor in new[] { 3, 4, 5, 1, 2 })
            {
                foreach (int seed in MazeTestMatrix.StorySeeds)
                {
                    int f = floor, s = seed;
                    yield return Session($"reach f{f} s{s}", new Overrides { Floor = f, Seed = s }, () => Reach(f, s));
                }
            }
        }

        private static UnityEngine.Localization.Locale _origLocale;

        private static IEnumerator Languages(string code)
        {
            MazeGenerator mg = SceneMaze();
            if (_origLocale == null) _origLocale = UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale;
            UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale = UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales.GetLocale(code);
            L10n.ClearCache();
            yield return Wait(0.6f);
            Check(UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale.Identifier.Code == code, "lang " + code + ": the locale is selected");
            Canvas canvas = RuntimeUi.ResolveCanvas();

            // Raw keys must never show: every new key resolves.
            int missing = 0;
            string[] keys = { "sub.nightmare.hilde.seen.0", "sub.nightmare.hilde.seen.1", "sub.nightmare.tomas.knocks_two", "sub.nightmare.tomas.knocks_three", "sub.nightmare.elias.empty.0", "sub.nightmare.elias.empty.1", "sub.nightmare.elias.empty.2",
                "sub.nightmare.elias.five", "sub.nightmare.elias.yours", "sub.nightmare.elias.your_things", "sub.nightmare.weiss.dose.0", "sub.nightmare.weiss.dose.1", "sub.nightmare.weiss.dose.2", "sub.nightmare.weiss.wears_off",
                "sub.nightmare.director.welcome", "sub.nightmare.director.warn.run", "sub.nightmare.director.warn.hide", "sub.nightmare.director.warn.throw", "sub.nightmare.director.report.run", "sub.nightmare.director.report.hide", "sub.nightmare.director.report.throw",
                "hud.prompt.drawer", "hud.prompt.ampoule", "world.nightmare.drawer.elias", "world.nightmare.drawer.yours", "world.nightmare.toetag", "world.nightmare.director.rules",
                "ui.ledger.slip_subtitle.1", "ui.ledger.slip_subtitle.2", "ui.ledger.slip_subtitle.3", "ui.ledger.slip_subtitle.4", "ui.end.wing.1", "ui.end.wing.2", "ui.end.wing.3", "ui.end.wing.4", "ui.end.wing.5", "ui.end.floor_cleared_body", "ui.ledger.subtitle" };
            int longest = 0; string longestKey = "";
            foreach (string k in keys)
            {
                string v = L10n.T(k, ("mark", WingNightmares.PatientMark()));
                if (v == k || string.IsNullOrEmpty(v)) { missing++; Say("   MISSING " + k); }
                if (k.StartsWith("sub.") && v.Length > longest) { longest = v.Length; longestKey = k; }
                if (k.StartsWith("sub.") && v.Length > 120) Say($"   TOO LONG ({v.Length}) {k}");
            }
            Check(missing == 0, $"lang {code}: all {keys.Length} new keys resolve (longest subtitle {longest} chars: {longestKey})");

            // Slip subtitle: one line in 1400 px at 34 pt with auto-size down to 0.7 -> must fit in 2000 px at full size.
            for (int floor = 1; floor <= 4; floor++)
            {
                TextMeshProUGUI t = RuntimeUi.CreateText(canvas.transform, "measure", WingNightmares.SlipSubtitle(floor), 34f, Color.white);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                float w = t.GetPreferredValues(t.text).x;
                Check(w <= 2000f, $"lang {code}: slip subtitle {floor} needs {w:0} px at 34 pt (fits down to 70 %: {w <= 2000f}) '{t.text}'");
                UnityEngine.Object.Destroy(t.gameObject);
            }
            // Loss screen: cause line + wing line wrapped in 1300 x 200 at 32 pt.
            for (int floor = 1; floor <= 5; floor++)
            {
                string combo = L10n.T("ui.end.out_of_time_body", ("stars", 3), ("metres", 120)) + "\n" + WingNightmares.LossLine(floor);
                TextMeshProUGUI t = RuntimeUi.CreateText(canvas.transform, "measure", combo, 32f, Color.white);
                t.textWrappingMode = TextWrappingModes.Normal;
                Vector2 size = t.GetPreferredValues(combo, 1300f, 0f);
                Check(size.y <= 200f, $"lang {code}: loss text floor {floor} wraps to {size.y:0} px high in the 1300 x 200 box");
                UnityEngine.Object.Destroy(t.gameObject);
            }
            // Banner names.
            for (int floor = 1; floor <= 5; floor++)
            {
                string banner = L10n.T("sub.banner.floor_theme", ("floor", floor), ("theme", WingNightmares.BannerName(floor)));
                Say("   banner floor " + floor + ": " + banner);
            }

            // The notice, in this language.
            foreach (GameObject n in new List<GameObject>(mg.DirectorNotices)) UnityEngine.Object.Destroy(n);
            yield return null;
            MethodInfo place = typeof(MazeGenerator).GetMethod("PlaceHouseRules", BindingFlags.NonPublic | BindingFlags.Instance);
            int dir = 0;
            for (int d = 0; d < 4; d++) if (mg.IsClosedWall(Vector2Int.zero, d)) { dir = d; break; }
            place.Invoke(mg, new object[] { Vector2Int.zero, dir, true });
            GameObject notice = mg.DirectorNotices[mg.DirectorNotices.Count - 1];
            Vector3 p = notice.transform.position;
            Shot("lang_" + code + "_notice", p - notice.transform.forward * 2.0f + Vector3.up * 0.0f, p, fov: 55f, fillRange: 7f);

            // The slip, for floors 1 and 3.
            GameOutcome outcome = UnityEngine.Object.FindAnyObjectByType<GameOutcome>();
            MethodInfo build = typeof(GameOutcome).GetMethod("BuildLedger", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (int cleared in new[] { 1, 3 })
            {
                int oldFloor = GameFlow.CurrentFloor;
                GameFlow.CurrentFloor = cleared;
                PlayerWallet.ResetCampaign();
                Payout payout = PlayerWallet.ComputeFloorClear(cleared, 3, 41.5f);
                GameObject panel = RuntimeUi.CreatePanel(canvas.transform, "TestSlip", Color.black);
                panel.transform.SetAsLastSibling();
                build.Invoke(outcome, new object[] { panel.transform, payout, true });
                yield return Wait(0.4f);
                ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, $"lang_{code}_slip{cleared}.png"));
                yield return Wait(0.6f);
                UnityEngine.Object.Destroy(panel);
                GameFlow.CurrentFloor = oldFloor;
                PlayerWallet.ResetCampaign();
                yield return null;
            }

            yield return LossScreen(5, "lang_" + code + "_loss5");
        }

        /// <summary>Why floor 5 seed 505 reports targets unreachable: structural (walls) or lock logic?</summary>
        private static IEnumerator Inv505()
        {
            MazeGenerator mg = SceneMaze();
            int w = mg.GridWidth, h = mg.GridHeight;
            int[,] steps = mg.StepsFrom(mg.CellCenter(0, 0));
            int unreachable = 0;
            List<Vector2Int> cut = new List<Vector2Int>();
            for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) if (steps[x, z] < 0) { unreachable++; cut.Add(new Vector2Int(x, z)); }
            Say($"seed 505 floor 5: with EVERY door open {unreachable} of {w * h} cells are unreachable from the start {string.Join(" ", cut)}");
            MazeMapData data = mg.BuildMapData();
            Say("loop entrance cell opening (L): " + mg.DebugMap().Split('\n').FirstOrDefault(l => l.StartsWith("L loop")));
            Say("gates: " + string.Join(" | ", mg.DebugMap().Split('\n').Where(l => l.StartsWith("G exit"))));
            ReachabilityReport rep = mg.CheckReachability();
            Say("grid+navmesh report: missing [" + string.Join("; ", rep.Missing) + "] cellsBeforeButtons " + rep.CellsBeforeButtons + " after " + rep.CellsReached);
            // Is each missing target reachable by walking if the locked doors were open? (steps >= 0 means yes)
            foreach (string m in rep.Missing) Say("   " + m);
            _ = data;
            yield break;
        }

        private static IEnumerator Capture(Action<MazeGenerator> action)
        {
            action(SceneMaze());
            yield break;
        }

        private static IEnumerator Reach(int floor, int seed)
        {
            MazeGenerator mg = SceneMaze();
            ReachabilityReport rep = mg.CheckReachability();
            bool ok = rep.Missing.Count == 0;
            Say($"{(ok ? "PASS" : "FAIL")} reach floor {floor} seed {seed}: targets {rep.Targets}, missing [{string.Join("; ", rep.Missing)}], banks {mg.DrawerBanks.Count} ampoules {mg.Ampoules.Count} notices {mg.DirectorNotices.Count}");
            if (ok) _pass++; else _fail++;
            yield break;
        }

        // ------------------------------------------------------------------ floor 1: Hilde

        private static IEnumerator Floor1()
        {
            MazeGenerator mg = SceneMaze();
            WingNightmareDirector dir = UnityEngine.Object.FindAnyObjectByType<WingNightmareDirector>();
            Transform player = Player();
            AIFollower hunter = UnityEngine.Object.FindAnyObjectByType<AIFollower>();
            Check(dir.Floor == 1, "floor 1: the director is on floor 1");
            int doorsBefore = UnityEngine.Object.FindObjectsByType<MazeDoor>().Length;
            string mapBefore = mg.DebugMap();
            int manualBefore = mapBefore.Split('\n').Count(l => l.StartsWith("door ") && l.Contains("Manual"));

            hunter.enabled = false; // the walk must not end in a capture; the bash test below switches it back on
            // Walk ~14 cells so they count as visited, then leave the run clock past 75 s and wait for the natural spawn.
            List<Vector2Int> path = WalkPath(mg, 14);
            foreach (Vector2Int c in path)
            {
                Teleport(player, mg.CellCenter(c.x, c.y) + Vector3.up * 0.1f);
                yield return Wait(0.6f);
            }
            GameFlow.RunStartTime -= 80f;
            Say($"walked {path.Count} cells; the player stands at {Pose(player.position)}; waiting for the natural door (min age 20 s)");

            float giveUp = Time.realtimeSinceStartup + 60f;
            while (dir.ExtraDoorsState.Count == 0 && Time.realtimeSinceStartup < giveUp) yield return null;
            Check(dir.ExtraDoorsState.Count == 1, $"floor 1: a door appeared by itself ({dir.ExtraDoorsState.Count})");
            if (dir.ExtraDoorsState.Count == 0)
            {
                Say("   explain: " + dir.ExtraDoorsState.Explain());
                WorldShiftDirector ws = UnityEngine.Object.FindAnyObjectByType<WorldShiftDirector>();
                Say($"   shift: enabled {ws.enabled} nextSample {Field<float>(ws, "_nextSample"):0.0} now {Time.time:0.0} visitedAt {Field<IDictionary>(ws, "_visitedAt").Count} live {GameFlow.IsRunActive} over {GameOutcome.IsOver} timeScale {Time.timeScale} player {Pose(player.position)} cell {mg.TryWorldToCell(player.position, out Vector2Int pc)} {pc}");
                yield break;
            }

            MazeDoor door = dir.ExtraDoorsState.Doors[0];
            int doorsAfter = UnityEngine.Object.FindObjectsByType<MazeDoor>().Length;
            Check(doorsAfter == doorsBefore + 1, $"floor 1: MazeDoor count {doorsBefore} -> {doorsAfter}");
            string[] chalk = door.GetComponentsInChildren<TextMeshPro>(true).Select(t => t.text).ToArray();
            Check(chalk.Length == 2 && chalk.All(t => t == "41"), "floor 1: chalk 41 on both faces (" + string.Join(",", chalk) + ")");
            Check(((IInteractable)door).CanInteract, "floor 1: the new door is a free Manual door");
            string mapAfter = mg.DebugMap();
            int manualAfter = mapAfter.Split('\n').Count(l => l.StartsWith("door ") && l.Contains("Manual"));
            Check(manualAfter == manualBefore + 1, $"floor 1: DebugMap lists the door ({manualBefore} -> {manualAfter} Manual doors)");
            Check(mg.BuildMapData().Doors.Count == manualAfter + mapAfter.Split('\n').Count(l => l.StartsWith("door ") && !l.Contains("Manual")), "floor 1: the floor map snapshot has the door");
            Say("door at " + Pose(door.transform.position) + " " + door.name + "; player " + Pose(player.position) + "; hunter " + Pose(hunter.transform.position));

            // Look at it from a couple of metres on one side, then the other face.
            Vector3 centre = door.transform.position;
            Vector3 normal = door.transform.forward;
            Shot("f1_door_front", centre + normal * 3.2f + Vector3.up * 1.6f, centre + Vector3.up * 1.6f);
            Shot("f1_door_back", centre - normal * 3.2f + Vector3.up * 1.6f, centre + Vector3.up * 1.6f);

            // Seen check: stand in view within 12 m. The subtitle should read THERE WAS NO DOOR HERE.
            ClearSubs();
            Teleport(player, centre + normal * 4.5f);
            Face(player, -normal);
            yield return Wait(1.5f);
            Check(Saw(L10n.T("sub.nightmare.hilde.seen.0")), "floor 1: the first sight says '" + L10n.T("sub.nightmare.hilde.seen.0") + "' (saw: " + string.Join(" | ", _subs) + ")");
            Check(Field<List<bool>>(dir.ExtraDoorsState, "_seen")[0], "floor 1: the door is marked seen (once)");

            // E opens it.
            ((IInteractable)door).Interact(null);
            yield return Wait(1.2f);
            Check(Field<bool>(door, "_open"), "floor 1: E opens the door");
            Shot("f1_door_open", centre + normal * 3.2f + Vector3.up * 1.6f, centre + Vector3.up * 1.6f);
            ((IInteractable)door).Interact(null);
            yield return Wait(1.0f);
            Check(!Field<bool>(door, "_open"), "floor 1: E closes it again");

            // A second door, then a third.
            bool second = dir.ExtraDoorsState.DebugFire(true);
            Check(second && dir.ExtraDoorsState.Count == 2, "floor 1: DebugFire(true) places a second door");
            if (second)
            {
                string[] c2 = dir.ExtraDoorsState.Doors[1].GetComponentsInChildren<TextMeshPro>(true).Select(t => t.text).ToArray();
                Check(c2.All(t => t == "42"), "floor 1: the second door reads 42 (" + string.Join(",", c2) + ")");
            }
            bool third = dir.ExtraDoorsState.DebugFire(true);
            Say("third DebugFire(true): " + third + " (the natural Tick caps at 2; DebugFire ignores the cap by design but TooCloseToPlacedDoors still applies)");

            hunter.enabled = true;
            // The hunter bashes through it: put the hunter on one side, send it to the other side of the (closed) door.
            MazeDoor target = door;
            Vector3 side = target.transform.position + normal * 3.5f;
            Vector3 other = target.transform.position - normal * 3.5f;
            WarpHunter(hunter, side);
            Teleport(player, other + (-normal) * 6f);
            yield return Wait(0.5f);
            hunter.ForceLastKnownPosition(other, true);
            float t0 = Time.realtimeSinceStartup;
            bool opened = false;
            while (Time.realtimeSinceStartup - t0 < 25f)
            {
                if (Field<bool>(target, "_open")) { opened = true; break; }
                yield return null;
            }
            Check(opened, "floor 1: the hunter bashed the extra door open within 25 s (hunter at " + Pose(hunter.transform.position) + ")");
        }

        // ------------------------------------------------------------------ floor 2: Tomas

        private static IEnumerator Floor2()
        {
            MazeGenerator mg = SceneMaze();
            WingNightmareDirector dir = UnityEngine.Object.FindAnyObjectByType<WingNightmareDirector>();
            Transform player = Player();
            AIFollower hunter = UnityEngine.Object.FindAnyObjectByType<AIFollower>();
            PipeKnocks knocks = dir.Knocks;
            Check(dir.Floor == 2, "floor 2: the director is on floor 2");

            // Near: the hunter 2 steps from the player.
            int[,] steps = mg.StepsFrom(player.position);
            Vector2Int nearCell = Vector2Int.zero, farCell = Vector2Int.zero;
            int farBest = -1;
            for (int x = 0; x < mg.GridWidth; x++)
                for (int z = 0; z < mg.GridHeight; z++)
                {
                    if (steps[x, z] == 2 && nearCell == Vector2Int.zero) nearCell = new Vector2Int(x, z);
                    if (steps[x, z] > farBest) { farBest = steps[x, z]; farCell = new Vector2Int(x, z); }
                }
            Say($"near cell {nearCell} (2 steps), far cell {farCell} ({farBest} steps)");

            // The first group of the floor is always honest: near -> 3, far -> 2.
            WarpHunter(hunter, mg.CellCenter(nearCell.x, nearCell.y));
            yield return Wait(0.3f);
            knocks.DebugKnockNow();
            yield return Wait(2.2f);
            PipeKnocks.Group g = knocks.History[knocks.History.Count - 1];
            Check(g.Shown == 3 && g.Truth == 3 && !g.Lie, $"floor 2: hunter 2 steps away -> three knocks (shown {g.Shown}, truth {g.Truth}, steps {g.HunterSteps})");
            Check(Subtitle() == L10n.T("sub.nightmare.tomas.knocks_three"), "floor 2: subtitle '" + Subtitle() + "'");

            WarpHunter(hunter, mg.CellCenter(farCell.x, farCell.y));
            yield return Wait(0.3f);
            knocks.DebugKnockNow();
            yield return Wait(2.0f);
            g = knocks.History[knocks.History.Count - 1];
            Check(g.Truth == 2 && g.HunterSteps >= 6, $"floor 2: hunter {g.HunterSteps} steps away -> truth two (shown {g.Shown}, lie {g.Lie})");
            Check(Subtitle() == L10n.T(g.Shown == 3 ? "sub.nightmare.tomas.knocks_three" : "sub.nightmare.tomas.knocks_two"), "floor 2: subtitle follows the sound: '" + Subtitle() + "'");

            // Rates: speed the taps up and play many groups. Cues on (default).
            FieldInfo gap = typeof(PipeKnocks).GetField("tapGap", BindingFlags.NonPublic | BindingFlags.Instance);
            float oldGap = (float)gap.GetValue(knocks);
            gap.SetValue(knocks, 0.01f);
            knocks.History.Clear();
            FieldInfo lastLie = typeof(PipeKnocks).GetField("_lastWasLie", BindingFlags.NonPublic | BindingFlags.Instance);
            lastLie.SetValue(knocks, false);
            yield return PlayGroups(knocks, 240);
            Analyse("cues ON (UnreliableCues true, lyingKnocks true)", knocks, expectLies: true);

            // F122 HONEST CUES: the global switch is false -> every knock truthful.
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.HonestCues, Oath.None);
            Check(!CampaignRules.UnreliableCues, "floor 2: HONEST CUES sets CampaignRules.UnreliableCues false");
            knocks.History.Clear();
            lastLie.SetValue(knocks, false);
            yield return PlayGroups(knocks, 160);
            Analyse("HONEST CUES assist (CampaignRules.UnreliableCues false)", knocks, expectLies: false);
            CampaignRules.ResetCampaign();

            // F103 switch: terror.lyingKnocks false with the global true -> truthful too.
            TerrorSettings terror = Field<TerrorSettings>(knocks, "_terror");
            bool oldLying = terror.lyingKnocks;
            terror.lyingKnocks = false;
            knocks.History.Clear();
            lastLie.SetValue(knocks, false);
            yield return PlayGroups(knocks, 160);
            Analyse("TerrorSettings.lyingKnocks false", knocks, expectLies: false);
            terror.lyingKnocks = oldLying;
            gap.SetValue(knocks, oldGap);

            // The held breath / loop duck keeps it silent: force the natural Tick path with the bed ducked.
            HorrorAudioDirector audio = UnityEngine.Object.FindAnyObjectByType<HorrorAudioDirector>();
            audio.Duck(0.3f, 4f);
            Check(audio.IsDucked, "floor 2: HorrorAudioDirector.IsDucked while ducked");
            FieldInfo next = typeof(PipeKnocks).GetField("_next", BindingFlags.NonPublic | BindingFlags.Instance);
            next.SetValue(knocks, Time.time - 1f);
            int before = knocks.History.Count;
            yield return Wait(1.0f);
            Check(knocks.History.Count == before, "floor 2: no knock group while the bed is ducked (held breath stays silent)");
            audio.Duck(1f, 1f); // Duck holds until released: this is what the end of a held breath does
            float duckGuard = Time.realtimeSinceStartup + 25f;
            while (audio.IsDucked && Time.realtimeSinceStartup < duckGuard) yield return null;
            Say($"   the duck ended after {25f - (duckGuard - Time.realtimeSinceStartup):0.0} s of waiting (IsDucked {audio.IsDucked})");
            next.SetValue(knocks, Time.time - 1f);
            yield return Wait(1.5f);
            Say($"   after the duck: ducked {audio.IsDucked} chasing {hunter.IsChasing} captured {hunter.IsCaptured} playing {knocks.IsPlaying} next {Field<float>(knocks, "_next"):0.0} now {Time.time:0.0} loopInside {(mg.Loop != null && mg.Loop.IsInside)}");
            Check(knocks.History.Count > before, "floor 2: the natural Tick plays a group again once the duck ends");

            // Where does the sound come from? Never toward the hunter.
            WarpHunter(hunter, mg.CellCenter(nearCell.x, nearCell.y));
            yield return Wait(0.3f);
            AudioSource src = dir.GetComponentsInChildren<AudioSource>().First(a => a.name == "PipeKnock");
            int toward = 0;
            for (int i = 0; i < 40; i++)
            {
                MethodInfo pick = typeof(PipeKnocks).GetMethod("PickSource", BindingFlags.NonPublic | BindingFlags.Instance);
                Vector3 p = (Vector3)pick.Invoke(knocks, null);
                Vector3 a = p - player.position; a.y = 0f;
                Vector3 h = hunter.transform.position - player.position; h.y = 0f;
                if (h.sqrMagnitude > 1f && a.sqrMagnitude > 0.01f && Vector3.Angle(a, h) < 50f) toward++;
            }
            Check(toward == 0, $"floor 2: 40 source picks, {toward} toward the hunter (<50 deg)");
            _ = src;
        }

        private static IEnumerator PlayGroups(PipeKnocks knocks, int count)
        {
            for (int i = 0; i < count; i++)
            {
                knocks.DebugKnockNow();
                float guard = Time.realtimeSinceStartup + 2f;
                while (knocks.IsPlaying && Time.realtimeSinceStartup < guard) yield return null;
            }
        }

        private static void Analyse(string label, PipeKnocks knocks, bool expectLies)
        {
            int lies = knocks.History.Count(x => x.Lie);
            int twice = 0;
            for (int i = 1; i < knocks.History.Count; i++) if (knocks.History[i].Lie && knocks.History[i - 1].Lie) twice++;
            int near = knocks.History.Count(x => x.Truth == 3);
            float rate = knocks.History.Count > 0 ? lies / (float)knocks.History.Count : 0f;
            Say($"knocks [{label}]: {knocks.History.Count} groups, {lies} lies ({rate:P0}), {twice} lies twice in a row, {near} near-truths");
            if (expectLies) Check(lies > 0 && rate > 0.12f && rate < 0.33f && twice == 0, $"floor 2: with cues on about a fifth to a quarter lie, never two in a row ({rate:P0})");
            else Check(lies == 0, $"floor 2: {label} -> zero lies");
        }

        // ------------------------------------------------------------------ floor 3: Elias

        private static IEnumerator Floor3(Action<string> sig, Action<string> layout)
        {
            MazeGenerator mg = SceneMaze();
            WingNightmareDirector dir = UnityEngine.Object.FindAnyObjectByType<WingNightmareDirector>();
            Transform player = Player();
            AIFollower hunter = UnityEngine.Object.FindAnyObjectByType<AIFollower>();
            Check(dir.Floor == 3, "floor 3: the director is on floor 3");
            sig(BankSignature(mg));
            layout(Layout(mg));
            IReadOnlyList<MorgueDrawerBank> banks = mg.DrawerBanks;
            Check(banks.Count >= 2, $"floor 3: {banks.Count} drawer banks");
            Check(banks.Any(b => b.BankRole == MorgueDrawerBank.Role.Yours) && banks.Any(b => b.BankRole == MorgueDrawerBank.Role.Five), "floor 3: one bank is YOURS and one is FIVE");
            Say(mg.DebugMap());

            // Case file 102 stays on its wall: a LoreNote named PatientFile_102 that is NOT under a bank.
            LoreNote file = UnityEngine.Object.FindObjectsByType<LoreNote>(FindObjectsInactive.Include).FirstOrDefault(n => n.name == "PatientFile_102");
            Check(file != null && file.gameObject.activeInHierarchy && file.GetComponentInParent<MorgueDrawerBank>() == null, "floor 3: case file 102 is on a wall, not in a drawer (" + (file != null ? Pose(file.transform.position) : "missing") + ")");

            int noises = 0;
            Action<Vector3, float> onNoise = (p, r) => noises++;
            MazeNoise.Emitted += onNoise;
            int batteriesBefore = PlayerInventory.Count(ShopItem.SpareBattery);
            int index = 0;
            foreach (MorgueDrawerBank bank in banks)
            {
                Vector3 pos = bank.transform.position;
                Vector3 front = bank.transform.forward;
                Vector3 standAt = pos + front * 3.4f;
                Teleport(player, standAt + Vector3.up * 0.1f);
                Face(player, -front);
                yield return Wait(0.5f);

                string tagText = string.Join(" | ", bank.GetComponentsInChildren<TextMeshPro>(true).Where(t => t.name == "Tag").Select(t => t.text));
                Say($"bank {bank.name} at {Pose(pos)} tags [{tagText}] tray {bank.OpenTray}");
                Shot($"f3_bank{index}_closed", pos + front * 3.6f + Vector3.up * 1.5f, pos + front * 0.6f + Vector3.up * 1.0f, fillRange: 8f);

                // The interactor's probe: a spherecast from the player's camera finds the bank.
                Camera cam = UnityEngine.Object.FindAnyObjectByType<FirstPersonRig>().PlayerCamera;
                Vector3 aimFrom = cam.transform.position;
                Vector3 aimAt = pos + front * 0.3f + Vector3.up * 1.0f;
                RaycastHit[] hits = Physics.SphereCastAll(aimFrom, 0.15f, (aimAt - aimFrom).normalized, 6f, ~0, QueryTriggerInteraction.Collide);
                bool found = hits.Any(h => h.collider.GetComponentInParent<IInteractable>() is MorgueDrawerBank);
                Check(found, $"floor 3: bank {index}: a probe from the player's eye finds the IInteractable");

                IInteractable it = bank;
                Check(it.CanInteract && it.HoldSeconds > 0f && !string.IsNullOrEmpty(it.Prompt), $"floor 3: bank {index}: prompt '{it.Prompt}', hold {it.HoldSeconds}");
                int noisesBefore = noises;
                int batt = PlayerInventory.Count(ShopItem.SpareBattery);
                it.Interact(null);
                yield return Wait(1.6f);
                Check(noises > noisesBefore, $"floor 3: bank {index} opening made a noise the hunter can hear");
                Check(!it.CanInteract, $"floor 3: bank {index} cannot be opened twice");
                Transform tray = bank.GetComponentsInChildren<Transform>(true).First(t => t.name == "Drawer_" + bank.OpenTray);
                Check(Mathf.Abs(Vector3.Dot(tray.position - bank.transform.TransformPoint(0, tray.localPosition.y, 0), front)) > 0.2f, $"floor 3: bank {index}: tray {bank.OpenTray} rolled out ({tray.localPosition})");
                string sub = Subtitle();
                Say($"   subtitle: {sub}");
                if (bank.BankRole == MorgueDrawerBank.Role.Yours)
                {
                    Transform body = bank.GetComponentsInChildren<Transform>(true).First(t => t.name == "YourBody");
                    Check(body.gameObject.activeSelf && body.GetComponentInParent<MorgueDrawerBank>() == bank, "floor 3: YOURS shows the robot on the tray");
                    Check(sub.Contains(L10n.T("sub.nightmare.elias.yours")) || sub == L10n.T("sub.nightmare.elias.your_things"), "floor 3: YOURS subtitle");
                    int battNow = PlayerInventory.Count(ShopItem.SpareBattery);
                    Check(battNow == batt + 1 || !PlayerInventory.CanHoldMore(ShopItem.SpareBattery), $"floor 3: YOURS grants a spare battery when there is room ({batt} -> {battNow})");
                    Shot($"f3_bank{index}_yours_open", pos + front * 3.4f + Vector3.up * 1.8f, tray.position + front * 0.2f + Vector3.up * 0.2f, fov: 55f, fillRange: 8f);
                    Vector3 trayTop = tray.position - front * 0.55f + Vector3.up * 0.25f;
                    Shot($"f3_bank{index}_yours_eye", trayTop + front * 1.5f + Vector3.up * 0.55f, trayTop, fov: 55f, fillRange: 7f);
                }
                else if (bank.BankRole == MorgueDrawerBank.Role.Five)
                {
                    Transform tag = bank.GetComponentsInChildren<Transform>(true).First(t => t.name == "ToeTag");
                    Check(tag.gameObject.activeSelf, "floor 3: FIVE shows the toe tag on the tray");
                    Check(sub == L10n.T("sub.nightmare.elias.five"), "floor 3: FIVE subtitle '" + sub + "'");
                    Shot($"f3_bank{index}_five_open", pos + front * 3.4f + Vector3.up * 1.8f, tray.position + front * 0.2f + Vector3.up * 0.2f, fov: 55f, fillRange: 8f);
                    Shot($"f3_bank{index}_five_eye", tag.position + front * 1.6f + Vector3.up * 0.55f, tag.position, fov: 55f, fillRange: 7f);
                    Shot($"f3_bank{index}_five_tag", tag.position + front * 0.8f + Vector3.up * 0.7f, tag.position, fov: 40f, fillRange: 6f);
                }
                else
                {
                    Check(L10n.Pool("sub.nightmare.elias.empty").Contains(sub), "floor 3: an empty drawer says one of the empty lines");
                    Shot($"f3_bank{index}_empty_open", pos + front * 3.4f + Vector3.up * 1.8f, tray.position + front * 0.2f + Vector3.up * 0.2f, fov: 55f, fillRange: 8f);
                }
                index++;
            }
            MazeNoise.Emitted -= onNoise;
            _ = batteriesBefore;
            _ = hunter;

            // A star left in a dead end in front of a bank is still collectable (A10): list stars sharing a bank's cell.
            MazeMapData data = mg.BuildMapData();
            List<Vector2Int> bankCells = banks.Select(b => { mg.TryWorldToCell(b.transform.position + b.transform.forward * 1f, out Vector2Int c); return c; }).ToList();
            Say("stars " + string.Join(" ", data.Stars) + " ; bank cells " + string.Join(" ", bankCells));

            // The end screen for a loss on this floor.
            yield return LossScreen(3, "f3_loss_en");
        }

        // ------------------------------------------------------------------ floor 4: Dr. Weiss

        private static IEnumerator Floor4(Action<string> layout)
        {
            MazeGenerator mg = SceneMaze();
            WingNightmareDirector dir = UnityEngine.Object.FindAnyObjectByType<WingNightmareDirector>();
            Transform player = Player();
            AIFollower hunter = UnityEngine.Object.FindAnyObjectByType<AIFollower>();
            PlayerStamina stamina = player.GetComponent<PlayerStamina>();
            DreadMeter dread = UnityEngine.Object.FindAnyObjectByType<DreadMeter>();
            HorrorAudioDirector audio = UnityEngine.Object.FindAnyObjectByType<HorrorAudioDirector>();
            Check(dir.Floor == 4, "floor 4: the director is on floor 4");
            layout(Layout(mg));
            IReadOnlyList<NightmareAmpoule> ampoules = mg.Ampoules;
            Check(ampoules.Count == 3, $"floor 4: {ampoules.Count} ampoules");
            Say(mg.DebugMap());

            // None in or beside a lamp cell.
            List<Vector2Int> lampCells = Field<List<Vector2Int>>(mg, "_lampCells");
            int bad = 0;
            foreach (NightmareAmpoule a in ampoules)
            {
                mg.TryWorldToCell(a.transform.position, out Vector2Int c);
                bool inLamp = lampCells.Contains(c);
                bool nextToLamp = false;
                for (int d = 0; d < 4; d++) if (!mg.IsClosedWall(c, d) && lampCells.Contains(MazeGenerator.NeighbourOf(c, d))) nextToLamp = true;
                Say($"ampoule {a.name} cell {c} inLamp {inLamp} nextToLamp {nextToLamp}");
                if (inLamp || nextToLamp) bad++;
            }
            Check(bad == 0, $"floor 4: no ampoule in a lamp cell or beside one ({bad} bad)");

            int starsTotal = GameManager.StarsTotal;
            FieldInfo mutedField = typeof(HorrorAudioDirector).GetField("_heartbeatMutedUntil", BindingFlags.NonPublic | BindingFlags.Instance);

            for (int i = 0; i < ampoules.Count; i++)
            {
                NightmareAmpoule a = ampoules[i];
                Vector3 pos = a.transform.position;
                if (i == 0)
                {
                    // A render in the dark with only the torch, then with a fill light.
                    mg.TryWorldToCell(pos, out Vector2Int ac);
                    Vector3 away = mg.CellCenter(ac.x, ac.y) - pos;
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.right;
                    Teleport(player, pos + away * 3.0f + Vector3.up * 0.1f);
                    Face(player, -away);
                    yield return Wait(0.6f);
                    Shot("f4_ampoule_torch", pos + away * 3.0f + Vector3.up * 1.4f, pos, fill: false, fov: 60f);
                    Shot("f4_ampoule_fill", pos + away * 1.3f + Vector3.up * 0.8f, pos + Vector3.up * 0.05f, fov: 45f, fillRange: 6f);
                    Shot("f4_ampoule_fill_far", pos + away * 3.0f + Vector3.up * 1.5f, pos + Vector3.up * 0.05f, fov: 60f, fillRange: 7f);
                }

                // The hunter close, the dread meter pushed up, the legs tired.
                WarpHunter(hunter, pos + new Vector3(2f, 0f, 0f));
                Teleport(player, pos + new Vector3(0f, 0.1f, 1.0f));
                typeof(PlayerStamina).GetProperty("Fraction").SetValue(stamina, 0.2f);
                typeof(PlayerStamina).GetProperty("Exhausted").SetValue(stamina, true);
                yield return Wait(0.5f);
                float valueBefore = dread != null ? dread.Value : 0f;

                IInteractable it = a;
                Check(it.CanInteract && it.HoldSeconds == 0f, $"floor 4: ampoule {i}: prompt '{it.Prompt}'");
                it.Interact(null);
                yield return Wait(0.2f);
                Check(stamina.Fraction >= 0.99f && !stamina.Exhausted, $"floor 4: ampoule {i}: stamina restored ({stamina.Fraction:0.00})");
                Check(dread == null || dread.IsNumb, $"floor 4: ampoule {i}: DreadMeter.IsNumb");
                float muted = (float)mutedField.GetValue(audio);
                Check(muted > Time.time + 15f, $"floor 4: ampoule {i}: the heartbeat is muted for {muted - Time.time:0.0} s");
                Check(Subtitle() == L10n.T("sub.nightmare.weiss.dose." + i), $"floor 4: ampoule {i}: subtitle '{Subtitle()}'");
                Check(a.IsTaken && !((IInteractable)a).CanInteract, $"floor 4: ampoule {i}: taken, gone");
                Check(GameManager.StarsTotal == starsTotal, "floor 4: the star total is unchanged (" + GameManager.StarsTotal + ")");
                yield return Wait(2.0f);
                if (dread != null) Check(dread.Value < 0.05f || dread.Value < valueBefore, $"floor 4: ampoule {i}: dread {valueBefore:0.00} -> {dread.Value:0.00} with the hunter 2 m away");
                if (i == 0) Say("heartbeat/dread sampled; ampoule 0 numb timer started");
            }

            // Wears off after ~20 s from the last dose.
            float waited = 0f;
            while (dread != null && dread.IsNumb && waited < 30f) { waited += Time.deltaTime; yield return null; }
            yield return Wait(0.6f);
            Check(dread == null || !dread.IsNumb, "floor 4: the numbness ends (waited " + waited.ToString("0.0") + " s after the last dose)");
            Check(Subtitle() == L10n.T("sub.nightmare.weiss.wears_off"), "floor 4: wears-off subtitle '" + Subtitle() + "'");
            Check((float)mutedField.GetValue(audio) <= Time.time + 0.1f, "floor 4: the heartbeat is allowed again");

            yield return LossScreen(4, "f4_loss_en");
        }

        // ------------------------------------------------------------------ floor 5: the Director

        private static IEnumerator Floor5(Action<string> layout)
        {
            MazeGenerator mg = SceneMaze();
            WingNightmareDirector dir = UnityEngine.Object.FindAnyObjectByType<WingNightmareDirector>();
            Transform player = Player();
            AIFollower hunter = UnityEngine.Object.FindAnyObjectByType<AIFollower>();
            Check(dir.Floor == 5, "floor 5: the director is on floor 5");
            layout(Layout(mg));
            Check(mg.DirectorNotices.Count == 3, $"floor 5: {mg.DirectorNotices.Count} house-rules notices");
            Say(mg.DebugMap());

            GameObject first = mg.DirectorNotices[0];
            mg.TryWorldToCell(first.transform.position, out Vector2Int firstCell);
            Check(firstCell == Vector2Int.zero || (Mathf.Abs(firstCell.x) + Mathf.Abs(firstCell.y)) == 1, "floor 5: the first notice is on the start cell's wall (" + firstCell + ")");

            int n = 0;
            foreach (GameObject notice in mg.DirectorNotices)
            {
                Vector3 toWall = notice.transform.forward;
                Vector3 p = notice.transform.position;
                TextMeshPro text = notice.GetComponentInChildren<TextMeshPro>();
                Say($"notice {notice.name} at {Pose(p)} text lines {text.text.Split('\n').Length}: {text.text.Replace("\n", " / ")}");
                Shot($"f5_notice{n}", p - toWall * 2.4f + Vector3.up * 0.1f, p, fov: 55f, fillRange: 7f);
                if (n == 0) Shot("f5_notice0_torch", p - toWall * 2.4f + Vector3.up * 0.1f, p, fill: false, fov: 55f);
                n++;
            }

            // The PA: welcome at 10 s of run time.
            string last = null;
            GameFlow.RunStartTime = Time.time - 11f;
            yield return Wait(0.8f);
            Check(Subtitle() == L10n.T("sub.nightmare.director.welcome"), "floor 5: the welcome line at 10 s ('" + Subtitle() + "')");
            HouseRules rules = dir.Rules;

            // Throw: the ThrowController.Thrown event is subscribed.
            ThrowController tc = player.GetComponent<ThrowController>();
            FieldInfo ev = typeof(ThrowController).GetField("Thrown", BindingFlags.NonPublic | BindingFlags.Instance);
            Action thrown = (Action)ev.GetValue(tc);
            Check(thrown != null, "floor 5: ThrowController.Thrown has a subscriber");
            WarpHunter(hunter, mg.CellCenter(mg.GridWidth - 1, mg.GridHeight - 1)); // far, not chasing
            yield return Wait(0.3f);
            thrown?.Invoke();
            yield return Wait(0.3f);
            last = Subtitle();
            Check(rules.Breaches(HouseRules.Rule.Throw) == 1 && last == L10n.T("sub.nightmare.director.warn.throw", ("mark", WingNightmares.PatientMark())), "floor 5: the first throw is a warning, no report: '" + last + "'");
            Vector3 lkBefore = hunter.LastKnownPosition;
            thrown?.Invoke();
            Check(rules.Breaches(HouseRules.Rule.Throw) == 1, "floor 5: a second throw inside the cooldowns is ignored");
            Check((hunter.LastKnownPosition - lkBefore).sqrMagnitude < 0.01f, "floor 5: the warning did not ping the hunter");

            // A later breach reports and sends the orderly (forced, to skip the 20/45 s cooldowns).
            Teleport(player, mg.CellCenter(3, 2) + Vector3.up * 0.1f);
            yield return Wait(0.3f);
            rules.DebugBreach(HouseRules.Rule.Throw);
            yield return Wait(0.3f);
            last = Subtitle();
            Check(rules.Breaches(HouseRules.Rule.Throw) == 2 && last == L10n.T("sub.nightmare.director.report.throw", ("mark", WingNightmares.PatientMark())), "floor 5: the second breach is a report: '" + last + "'");
            Check((hunter.LastKnownPosition - player.position).magnitude < 1.5f || hunter.IsBackingOff, $"floor 5: the orderly was sent to the player ({Pose(hunter.LastKnownPosition)} vs {Pose(player.position)}, backoff {hunter.IsBackingOff})");
            rules.DebugBreach(HouseRules.Rule.Run);
            yield return Wait(0.3f);
            Check(Subtitle() == L10n.T("sub.nightmare.director.warn.run", ("mark", WingNightmares.PatientMark())), "floor 5: a first run breach is a warning: '" + Subtitle() + "'");
            rules.DebugBreach(HouseRules.Rule.Hide);
            yield return Wait(0.3f);
            Check(Subtitle() == L10n.T("sub.nightmare.director.warn.hide", ("mark", WingNightmares.PatientMark())), "floor 5: a first hide breach is a warning: '" + Subtitle() + "'");
            rules.DebugBreach(HouseRules.Rule.Hide);
            yield return Wait(0.3f);
            Check(Subtitle() == L10n.T("sub.nightmare.director.report.hide", ("mark", WingNightmares.PatientMark())), "floor 5: a later hide breach is a report: '" + Subtitle() + "'");

            yield return LossScreen(5, "f5_loss_en");
        }

        // ------------------------------------------------------------------ DESCENT

        private static IEnumerator Descent(int depth)
        {
            MazeGenerator mg = SceneMaze();
            WingNightmareDirector dir = UnityEngine.Object.FindAnyObjectByType<WingNightmareDirector>();
            Check(GameFlow.IsDescent, $"descent {depth}: GameFlow.IsDescent");
            Check(dir != null && dir.Floor == 0, $"descent {depth}: the wing director is idle (floor {(dir != null ? dir.Floor : -1)})");
            Check(mg.DrawerBanks.Count == 0 && mg.Ampoules.Count == 0 && mg.DirectorNotices.Count == 0, $"descent {depth}: no banks, ampoules or notices");
            string title = (string)typeof(MazeGenerator).GetMethod("FloorTitleText", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(mg, null);
            Check(title == L10n.T("hud.descent.level", ("n", DescentRun.Depth)), $"descent {depth}: the banner is '{title}'");
            Check(WingNightmares.SlipSubtitle(depth) == L10n.T("ui.ledger.slip_subtitle") && WingNightmares.LossLine(depth) == null && WingNightmares.BannerName(depth) == null, $"descent {depth}: the generic slip line, no loss line, no wing banner");
            yield break;
        }

        // ------------------------------------------------------------------ the loss screen and the slip in the shipped languages

        private static IEnumerator LossScreen(int floor, string name)
        {
            GameOutcome outcome = UnityEngine.Object.FindAnyObjectByType<GameOutcome>();
            outcome.Lose(LoseReason.OutOfTime);
            yield return Wait(4.5f);
            TextMeshProUGUI sub = UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Exclude).FirstOrDefault(t => t.name == "Subtitle" && t.transform.parent != null && t.transform.parent.name == "EndScreen");
            Say($"loss screen floor {floor}: subtitle = {(sub != null ? sub.text.Replace("\n", " // ") : "(none)")}");
            Check(sub != null && sub.text.Contains(L10n.T("ui.end.wing." + floor)), $"floor {floor}: the loss screen carries the wing line");
            if (sub != null)
            {
                yield return Wait(0.2f);
                Say($"   subtitle preferred width {sub.preferredWidth:0}, rect {sub.rectTransform.rect.width:0}x{sub.rectTransform.rect.height:0}, size {sub.fontSize:0.0}");
                ScreenCapture.CaptureScreenshot(Path.Combine(_outDir, name + ".png"));
                yield return Wait(0.5f);
            }
        }
    }
}
