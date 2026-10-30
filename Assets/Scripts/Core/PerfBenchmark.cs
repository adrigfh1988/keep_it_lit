using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// F118: a scripted low-end benchmark. Walks a fixed route (seeded maze floors 1 and 5, a DESCENT level 12 and the Asylum hub) at three
/// quality presets and writes one CSV row per station x preset: average and 1 % low frame time. Asked for with command-line flags in a built
/// player (<c>keep_it_lit.exe -benchmark -benchmark-quit -benchmark-out C:\temp\bench.csv</c>; also -benchmark-seconds N,
/// -benchmark-presets low,medium,high, -benchmark-stations maze1,maze5,hub,descent12) or from the Editor menu KEEP IT LIT > Debug > Run Benchmark (Play)
/// (SessionState; Editor numbers are labelled "editor" and are not representative). Nothing is set up in the scene: the object is created before
/// the first scene loads and survives scene reloads. The overlay text is a dev tool and deliberately not localized.
/// </summary>
public sealed class PerfBenchmark : MonoBehaviour
{
    public const string SessionKey = "keepitlit.benchmark";
    public const string SessionSecondsKey = "keepitlit.benchmark.seconds";
    public const string SessionPresetsKey = "keepitlit.benchmark.presets";
    public const string SessionStationsKey = "keepitlit.benchmark.stations";

    private const int Seed = 4242;
    private const float WarmupSeconds = 3f;
    private const float WalkSpeed = 3.5f;
    private const string CsvHeader = "utc,app_version,unity_version,build(editor|player),platform,graphics_api,gpu,gpu_vram_mb,cpu,cpu_cores,ram_mb,resolution,preset,station,seed,frames,avg_ms,avg_fps,p99_ms,low1_fps,max_ms,load_ms";

    private sealed class Options
    {
        public float seconds = 30f;
        public string outPath;
        public string[] presets = { "low", "medium", "high" };
        public string[] stations = { "maze1", "maze5", "hub", "descent12" };
        public bool quit;
    }

    private struct Snapshot
    {
        public bool vsync, ao;
        public int frameCap;
        public GameSettings.QualityTier quality;
        public GameSettings.ShadowTier shadows;
        public GameSettings.FogTier fog;
    }

    private Options _options;
    private Snapshot _snapshot;
    private bool _snapshotTaken, _restored;
    private TextMeshProUGUI _label;
    private readonly List<float> _samples = new List<float>(4096);

    // ---------------------------------------------------------------- start

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        Options options = ReadOptions();
        if (options == null) return;

        GameObject go = new GameObject("PerfBenchmark");
        DontDestroyOnLoad(go);
        PerfBenchmark benchmark = go.AddComponent<PerfBenchmark>();
        benchmark._options = options;
    }

    private static Options ReadOptions()
    {
        Options o = new Options();
        bool requested = false;

#if UNITY_EDITOR
        if (UnityEditor.SessionState.GetInt(SessionKey, 0) == 1)
        {
            requested = true;
            UnityEditor.SessionState.SetInt(SessionKey, 0); // one run per request
            o.seconds = UnityEditor.SessionState.GetInt(SessionSecondsKey, 10);
            string presets = UnityEditor.SessionState.GetString(SessionPresetsKey, "");
            string stations = UnityEditor.SessionState.GetString(SessionStationsKey, "");
            if (presets.Length > 0) o.presets = presets.Split(',');
            if (stations.Length > 0) o.stations = stations.Split(',');
        }
#endif

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "-benchmark") requested = true;
            else if (a == "-benchmark-quit") o.quit = true;
            else if (a == "-benchmark-seconds" && i + 1 < args.Length && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float s)) o.seconds = Mathf.Max(1f, s);
            else if (a == "-benchmark-out" && i + 1 < args.Length) o.outPath = args[i + 1];
            else if (a == "-benchmark-presets" && i + 1 < args.Length) o.presets = args[i + 1].Split(',');
            else if (a == "-benchmark-stations" && i + 1 < args.Length) o.stations = args[i + 1].Split(',');
        }

        return requested ? o : null;
    }

    private void Start()
    {
        StartCoroutine(Run());
    }

    private void OnApplicationQuit() => Restore();

    private void OnDestroy() => Restore();

    // ---------------------------------------------------------------- settings snapshot

    private void Take()
    {
        _snapshot = new Snapshot
        {
            vsync = GameSettings.VSync, frameCap = GameSettings.FrameCap, quality = GameSettings.Quality,
            shadows = GameSettings.Shadows, ao = GameSettings.AmbientOcclusion, fog = GameSettings.Fog
        };
        _snapshotTaken = true;
    }

    /// <summary>Puts every setting the benchmark touched back (the setters persist to PlayerPrefs, so this must always run).</summary>
    private void Restore()
    {
        if (!_snapshotTaken || _restored) return;
        _restored = true;
        GameSettings.VSync = _snapshot.vsync;
        GameSettings.FrameCap = _snapshot.frameCap;
        GameSettings.Quality = _snapshot.quality;
        GameSettings.Shadows = _snapshot.shadows;
        GameSettings.AmbientOcclusion = _snapshot.ao;
        GameSettings.Fog = _snapshot.fog;
    }

    private static void ApplyPreset(string preset)
    {
        switch (preset)
        {
            case "low": GameSettings.Quality = GameSettings.QualityTier.Low; GameSettings.Shadows = GameSettings.ShadowTier.Low; GameSettings.Fog = GameSettings.FogTier.Low; GameSettings.AmbientOcclusion = false; break;
            case "medium": GameSettings.Quality = GameSettings.QualityTier.Medium; GameSettings.Shadows = GameSettings.ShadowTier.Low; GameSettings.Fog = GameSettings.FogTier.Normal; GameSettings.AmbientOcclusion = true; break;
            default: GameSettings.Quality = GameSettings.QualityTier.High; GameSettings.Shadows = GameSettings.ShadowTier.High; GameSettings.Fog = GameSettings.FogTier.Normal; GameSettings.AmbientOcclusion = true; break;
        }
    }

    // ---------------------------------------------------------------- run

    private IEnumerator Run()
    {
        // The first scene is Boot (waits for localization) in a player, GetStarted_Scene when played from the Editor.
        float guard = Time.realtimeSinceStartup + 120f;
        while (UnityEngine.Object.FindAnyObjectByType<MazeGenerator>() == null && Time.realtimeSinceStartup < guard) yield return null;
        yield return new WaitForSecondsRealtime(1f);

        BuildOverlay();
        Take();
        GameSettings.VSync = false;
        GameSettings.FrameCap = 0;

        string path = _options.outPath;
        if (string.IsNullOrEmpty(path)) path = Path.Combine(Application.persistentDataPath, "benchmarks", "benchmark-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv");
        string dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (!File.Exists(path)) File.WriteAllText(path, CsvHeader + "\n", new UTF8Encoding(false));

        int total = _options.presets.Length * _options.stations.Length, n = 0;
        foreach (string station in _options.stations)
        {
            foreach (string preset in _options.presets)
            {
                n++;
                SetLabel($"BENCHMARK  {station} {preset}  {n}/{total}");
                ApplyPreset(preset);
                string row = null;
                IEnumerator seg = RunSegment(station.Trim(), preset.Trim(), r => row = r);
                while (true)
                {
                    bool more;
                    try { more = seg.MoveNext(); }
                    catch (Exception e) { Debug.LogError("PerfBenchmark: " + station + " " + preset + " failed: " + e); row = null; break; }
                    if (!more) break;
                    yield return seg.Current;
                }
                if (row != null) File.AppendAllText(path, row + "\n", new UTF8Encoding(false));
            }
        }

        Restore();
        Debug.Log("PerfBenchmark: done, " + total + " rows -> " + path);
        SetLabel("BENCHMARK done");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        if (_options.quit) Application.Quit();
#endif
    }

    private IEnumerator RunSegment(string station, string preset, Action<string> done)
    {
        bool hub = station == "hub";
        bool descent = station.StartsWith("descent");
        int level = 1;
        if (station.StartsWith("maze")) int.TryParse(station.Substring(4), out level);
        else if (descent) int.TryParse(station.Substring(7), out level);

        MazeGenerator previous = UnityEngine.Object.FindAnyObjectByType<MazeGenerator>();
        float loadStart = Time.realtimeSinceStartup;

        if (hub)
        {
            GameFlow.IsDescent = false;
            DescentRun.ResetState();
            GameFlow.CurrentFloor = 3;
            Array.Clear(GameFlow.RolledObjectives, 0, GameFlow.RolledObjectives.Length);
            GameFlow.Restart(true, Seed, true);
        }
        else
        {
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
            GameFlow.Restart(true, Seed);
        }

        MazeGenerator mg = null;
        float deadline = Time.realtimeSinceStartup + 90f;
        while (Time.realtimeSinceStartup < deadline)
        {
            yield return null;
            mg = UnityEngine.Object.FindAnyObjectByType<MazeGenerator>();
            if (mg != null && mg != previous && mg.BuildMapData() != null) break;
            mg = null;
        }
        if (mg == null) { Debug.LogError("PerfBenchmark: " + station + " did not load"); done(null); yield break; }
        float loadMs = (Time.realtimeSinceStartup - loadStart) * 1000f;

        if (hub)
        {
            deadline = Time.realtimeSinceStartup + 40f;
            while (!GameFlow.IsInHub && Time.realtimeSinceStartup < deadline) yield return null;
            if (!GameFlow.IsInHub || AsylumHub.Active == null || !AsylumHub.Active.CanEnter)
            {
                Debug.LogWarning("PerfBenchmark: no usable hub, station skipped");
                done(Row(preset, station, 0, 0, loadMs, "skipped"));
                yield break;
            }
        }

        // Nothing may catch the camera or collect a star.
        foreach (AIFollower f in UnityEngine.Object.FindObjectsByType<AIFollower>()) f.enabled = false;
        foreach (Stalker s in UnityEngine.Object.FindObjectsByType<Stalker>()) s.enabled = false;
        foreach (MazeEntity e in UnityEngine.Object.FindObjectsByType<MazeEntity>()) e.enabled = false;

        ThirdPersonController player = UnityEngine.Object.FindAnyObjectByType<ThirdPersonController>();
        if (player == null) { Debug.LogError("PerfBenchmark: no player"); done(null); yield break; }
        PlayerLock.Freeze(player.transform, true);
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        List<Vector3> route = hub ? HubRoute(player.transform.position) : MazeRoute(mg);
        if (route.Count < 2) { Debug.LogWarning("PerfBenchmark: no route for " + station); route = new List<Vector3> { player.transform.position, player.transform.position + Vector3.forward }; }

        UnityEngine.Random.InitState(Seed);
        _samples.Clear();
        float t0 = Time.realtimeSinceStartup, walkDist = 0f;
        float total = PathLength(route);
        float length = Mathf.Max(0.5f, total);
        float segmentEnd = WarmupSeconds + _options.seconds;
        float clock = 0f;
        while (clock < segmentEnd)
        {
            yield return null;
            float dt = Time.unscaledDeltaTime;
            clock += dt;
            if (clock > WarmupSeconds) _samples.Add(dt);

            walkDist += WalkSpeed * dt;
            float d = Mathf.PingPong(walkDist, length);
            Vector3 pos = PointAlong(route, d, out Vector3 dir);
            player.transform.position = pos;
            Vector3 face = walkDist % (2f * length) > length ? -dir : dir;
            if (face.sqrMagnitude > 0.0001f) player.LookAt(pos + face.normalized * 6f + Vector3.up * 1.6f, 1f);
        }

        done(Row(preset, station, _samples.Count, 0, loadMs, null));
    }

    // ---------------------------------------------------------------- routes

    private static List<Vector3> MazeRoute(MazeGenerator mg)
    {
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = mg.MazeAgentTypeId, areaMask = NavMesh.AllAreas };
        return Corners(mg.StartCellCenter, mg.FarthestCellCenter(), filter);
    }

    private static List<Vector3> HubRoute(Vector3 from)
    {
        AsylumHub hub = AsylumHub.Active;
        int type = 0;
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            NavMeshBuildSettings s = NavMesh.GetSettingsByIndex(i);
            if (NavMesh.GetSettingsNameFromID(s.agentTypeID) == "Hub") type = s.agentTypeID;
        }
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = type, areaMask = NavMesh.AllAreas };

        List<Vector3> route = new List<Vector3>();
        Vector3 at = from;
        List<Vector3> stops = new List<Vector3>();
        if (hub.mainEntrance != null) stops.Add(hub.mainEntrance.position);
        foreach (WingDoor door in hub.wingDoors) if (door != null && door.arrivalPoint != null) stops.Add(door.arrivalPoint.position);
        foreach (Vector3 stop in stops)
        {
            List<Vector3> leg = Corners(at, stop, filter);
            if (leg.Count < 2) continue;
            if (route.Count > 0) leg.RemoveAt(0);
            route.AddRange(leg);
            at = leg[leg.Count - 1];
        }
        return route;
    }

    private static List<Vector3> Corners(Vector3 a, Vector3 b, NavMeshQueryFilter filter)
    {
        List<Vector3> result = new List<Vector3>();
        if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 4f, filter) || !NavMesh.SamplePosition(b, out NavMeshHit hb, 4f, filter)) return result;
        NavMeshPath path = new NavMeshPath();
        if (NavMesh.CalculatePath(ha.position, hb.position, filter, path) && path.status == NavMeshPathStatus.PathComplete) result.AddRange(path.corners);
        return result;
    }

    private static float PathLength(List<Vector3> route)
    {
        float len = 0f;
        for (int i = 1; i < route.Count; i++) len += Vector3.Distance(route[i - 1], route[i]);
        return len;
    }

    private static Vector3 PointAlong(List<Vector3> route, float distance, out Vector3 direction)
    {
        direction = Vector3.forward;
        for (int i = 1; i < route.Count; i++)
        {
            float seg = Vector3.Distance(route[i - 1], route[i]);
            direction = (route[i] - route[i - 1]);
            direction.y = 0f;
            if (distance <= seg || i == route.Count - 1) return Vector3.Lerp(route[i - 1], route[i], seg > 0.0001f ? Mathf.Clamp01(distance / seg) : 1f);
            distance -= seg;
        }
        return route[route.Count - 1];
    }

    // ---------------------------------------------------------------- output

    private string Row(string preset, string station, int frames, int unused, float loadMs, string skipped)
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        double avg = 0, p99 = 0, max = 0;
        if (frames > 0)
        {
            List<float> sorted = new List<float>(_samples);
            sorted.Sort();
            double sum = 0;
            foreach (float s in sorted) sum += s;
            avg = sum / sorted.Count * 1000.0;
            p99 = sorted[Mathf.Min(sorted.Count - 1, Mathf.CeilToInt(sorted.Count * 0.99f) - 1)] * 1000.0;
            max = sorted[sorted.Count - 1] * 1000.0;
        }

#if UNITY_EDITOR
        const string build = "editor";
#else
        const string build = "player";
#endif
        string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";
        string preset2 = skipped != null ? preset + " (skipped)" : preset;
        return string.Join(",",
            DateTime.UtcNow.ToString("o", c), Csv(Application.version), Csv(Application.unityVersion), build, Application.platform.ToString(),
            Csv(SystemInfo.graphicsDeviceType.ToString()), Csv(SystemInfo.graphicsDeviceName), SystemInfo.graphicsMemorySize.ToString(c),
            Csv(SystemInfo.processorType), SystemInfo.processorCount.ToString(c), SystemInfo.systemMemorySize.ToString(c),
            Screen.width + "x" + Screen.height, preset2, station, Seed.ToString(c), frames.ToString(c),
            avg.ToString("0.00", c), (avg > 0 ? 1000.0 / avg : 0).ToString("0.0", c), p99.ToString("0.00", c), (p99 > 0 ? 1000.0 / p99 : 0).ToString("0.0", c),
            max.ToString("0.00", c), loadMs.ToString("0", c));
    }

    // ---------------------------------------------------------------- overlay

    private void BuildOverlay()
    {
        GameObject canvasObject = new GameObject("BenchmarkOverlay");
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        _label = RuntimeUi.CreateText(canvasObject.transform, "Label", "BENCHMARK", 26f, Color.yellow);
        _label.alignment = TextAlignmentOptions.TopLeft;
        RuntimeUi.Place(_label.rectTransform, new Vector2(0f, 1f), new Vector2(480f, -30f), new Vector2(900f, 40f));
    }

    private void SetLabel(string text)
    {
        if (_label != null) _label.text = text;
    }
}
