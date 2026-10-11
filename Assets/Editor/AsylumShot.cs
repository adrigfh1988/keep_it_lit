using StarterAssets;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>DEV ONLY (F92 unattended run): stage the hub in Play mode and take screenshots. Never committed; deleted at the end.</summary>
public static class AsylumShot
{
    public const string Folder = "C:/Users/adria/AppData/Local/Temp/claude/D--Unity-essentials-My-project/3bcb72ed-ef04-42e7-8bbe-d9a4dca94684/scratchpad/f101/";

    /// <summary>Walks the real player CharacterController along every entrance -> wing route and reports where it ends up.</summary>
    public static string WalkTest()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        ThirdPersonController player = Object.FindAnyObjectByType<ThirdPersonController>();
        CharacterController cc = player.GetComponent<CharacterController>();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        int w = 0;
        foreach (Vector3[] path in AsylumHubBuilder.WalkRoutes(hub))
        {
            w++;
            cc.enabled = false; player.transform.position = path[0] + Vector3.up * 0.05f; cc.enabled = true; Physics.SyncTransforms();
            int idx = 1; float vy = 0f; const float dt = 0.02f; float t = 0f, stuck = 0f; Vector3 last = player.transform.position;
            while (idx < path.Length && t < 120f)
            {
                Vector3 target = path[Mathf.Min(idx + 2, path.Length - 1)];
                Vector3 to = target - player.transform.position; to.y = 0f;
                if (to.magnitude < 0.4f) { idx = Mathf.Min(idx + 3, path.Length); continue; }
                vy = cc.isGrounded ? -1f : vy - 9.81f * dt;
                cc.Move((to.normalized * 2.4f + Vector3.up * vy) * dt);
                t += dt;
                if ((player.transform.position - last).sqrMagnitude < 0.0001f) stuck += dt; else { stuck = 0f; last = player.transform.position; }
                if (stuck > 2f) break;
            }
            Vector3 end = path[path.Length - 1];
            float miss = Vector3.Distance(player.transform.position, end);
            sb.AppendLine($"route {w}: {(miss < 0.8f ? "ARRIVED" : "STUCK")} after {t:F0}s, ends {player.transform.position:F1}, target {end:F1}, miss {miss:F1}");
        }
        return sb.ToString();
    }

    /// <summary>One shot per wing door from 3.2 m in front, plus a montage named `sheet`.</summary>
    public static string WingShots(string prefix, float dist = 3.2f, float eye = 1.55f)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
        foreach (WingDoor w in hub.wingDoors)
        {
            Vector3 pos = w.transform.TransformPoint(new Vector3(0f, eye, dist));
            Vector3 look = w.transform.TransformPoint(new Vector3(0f, 1.9f, 0f));
            Shoot(pos - Vector3.up * (eye - 0.05f) + Vector3.up * 0.05f, look, prefix + w.floor);
            names.Add(prefix + w.floor);
        }
        return Montage(names.ToArray(), prefix + "sheet", 2);
    }

    /// <summary>The standard tour: 12 places a route crosses, two montages (prefix + "A", prefix + "B").</summary>
    public static string Tour(string prefix)
    {
        (string, Vector3, Vector3)[] spots =
        {
            ("entrance", new Vector3(100f, 5.07f, -62.1f), new Vector3(100f, 6.5f, -50f)),
            ("hallW", new Vector3(100f, 5.07f, -54.5f), new Vector3(88f, 6.3f, -54.5f)),
            ("hallE", new Vector3(100f, 5.07f, -54.5f), new Vector3(112f, 6.3f, -54.5f)),
            ("stairs", new Vector3(100f, 5.07f, -52f), new Vector3(100f, 7.5f, -42f)),
            ("dayroom", new Vector3(122f, 5.07f, -66f), new Vector3(122f, 5.5f, -80f)),
            ("daywing", new Vector3(120f, 5.07f, -72f), new Vector3(125.4f, 6.5f, -81.5f)),
            ("basehall", new Vector3(119f, 1.2f, -69f), new Vector3(118f, 2f, -55f)),
            ("machine", new Vector3(112.3f, 0.1f, -50.6f), new Vector3(107f, 1f, -50.6f)),
            ("morgue", new Vector3(126f, 1.2f, -53f), new Vector3(131f, 2f, -50.5f)),
            ("upperhall", new Vector3(100f, 8.82f, -54f), new Vector3(114f, 9.8f, -54f)),
            ("surgery", new Vector3(99.5f, 8.87f, -60f), new Vector3(99.5f, 9.5f, -63.5f)),
            ("office", new Vector3(108f, 8.87f, -60f), new Vector3(109f, 9.5f, -63.5f)),
        };
        string report = "";
        System.Collections.Generic.List<string> a = new System.Collections.Generic.List<string>(), b = new System.Collections.Generic.List<string>();
        for (int i = 0; i < spots.Length; i++)
        {
            string name = prefix + spots[i].Item1;
            string r = Shoot(spots[i].Item2, spots[i].Item3, name); report += spots[i].Item1 + r.Substring(r.IndexOf(" mean=")) + "; ";
            (i < 6 ? a : b).Add(name);
        }
        return Montage(a.ToArray(), prefix + "A", 2) + "\n" + Montage(b.ToArray(), prefix + "B", 2);
    }

    /// <summary>`count` shots spread along walk route `route` (0-based: wing 1..5), each looking ahead along the route; one montage.</summary>
    public static string RouteShots(string prefix, int route, int count)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        System.Collections.Generic.List<Vector3[]> routes = AsylumHubBuilder.WalkRoutes(hub);
        if (route >= routes.Count) return "no route";
        Vector3[] path = routes[route];
        System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
        for (int i = 0; i < count; i++)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt((i + 0.5f) / count * (path.Length - 1)), 0, path.Length - 1);
            int ahead = Mathf.Min(k + 14, path.Length - 1);
            Vector3 look = path[ahead] + Vector3.up * 1.4f;
            if ((look - path[k]).sqrMagnitude < 4f) look = path[k] + (path[k] - path[Mathf.Max(0, k - 14)]).normalized * 4f + Vector3.up * 1.4f;
            string n = prefix + route + "_" + i;
            Shoot(path[k] + Vector3.up * 0.05f, look, n);
            names.Add(n);
        }
        return Montage(names.ToArray(), prefix + route + "_sheet", 2);
    }

    public static string Rebuild()
    {
        AsylumHubBuilder.RegenerateSilently();
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        string s = AsylumHubBuilder.Audit(hub.transform);
        foreach (WingDoor w in hub.wingDoors) if (w != null) s += $"wing {w.floor} at {w.transform.position} arrival {w.arrivalPoint.position}\n";
        return s + AsylumHubBuilder.LastDiagnostics;
    }

    public static string Stage(bool torchOn = true, float ambientBoost = 1f)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return "no hub";
        hub.SetContentActive(true);
        Time.timeScale = 1f;
        foreach (AIFollower ai in Object.FindObjectsByType<AIFollower>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            UnityEngine.AI.NavMeshAgent a = ai.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (a != null) a.enabled = false;
            ai.enabled = false;
            ai.gameObject.SetActive(false);
        }
        foreach (Stalker s in Object.FindObjectsByType<Stalker>(FindObjectsInactive.Include, FindObjectsSortMode.None)) s.gameObject.SetActive(false);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = hub.hubAmbient * ambientBoost;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = hub.hubFogDensity;
        RenderSettings.fogColor = hub.hubFogColor;
        Flashlight f = Object.FindAnyObjectByType<Flashlight>();
        if (f != null) { f.Refill(); if (torchOn) f.SetOn(true); else f.ForceOff(); }
        return "staged";
    }

    public static string Shoot(Vector3 position, Vector3 lookAt, string name, int w = 1280, int h = 720)
    {
        ThirdPersonController player = Object.FindAnyObjectByType<ThirdPersonController>();
        if (player == null) return "no player";
        CharacterController cc = player.GetComponent<CharacterController>();
        cc.enabled = false;
        player.transform.position = position;
        cc.enabled = true;
        Physics.SyncTransforms();
        player.LookAt(lookAt, 1f);
        Camera cam = Camera.main;
        if (cam == null) return "no camera";
        // The rig camera sits at head height; orient it exactly at the look target.
        cam.transform.rotation = Quaternion.LookRotation((lookAt - cam.transform.position).normalized);
        return Render(cam, name, w, h);
    }

    /// <summary>A free camera shot (no player involved), for overviews and wall checks.</summary>
    public static string Free(Vector3 position, Vector3 lookAt, string name, float fov = 70f, int w = 1280, int h = 720)
    {
        GameObject go = new GameObject("ShotCam");
        Camera cam = go.AddComponent<Camera>();
        Camera main = Camera.main;
        if (main != null) cam.CopyFrom(main);
        cam.fieldOfView = fov;
        UniversalAdditionalCameraData data = go.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        cam.transform.position = position;
        cam.transform.rotation = Quaternion.LookRotation((lookAt - position).normalized);
        string result = Render(cam, name, w, h);
        Object.DestroyImmediate(go);
        return result;
    }

    /// <summary>Tiles several saved shots (half size each) into one PNG, to spend fewer image reads.</summary>
    public static string Montage(string[] names, string output, int columns = 2)
    {
        int cw = 640, ch = 360;
        int rows = (names.Length + columns - 1) / columns;
        Texture2D sheet = new Texture2D(cw * columns, ch * rows, TextureFormat.RGB24, false);
        for (int i = 0; i < names.Length; i++)
        {
            Texture2D t = new Texture2D(2, 2);
            t.LoadImage(System.IO.File.ReadAllBytes(Folder + names[i] + ".png"));
            RenderTexture rt = RenderTexture.GetTemporary(cw, ch);
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            Texture2D small = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, cw, ch), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            int x = (i % columns) * cw, y = (rows - 1 - i / columns) * ch;
            sheet.SetPixels(x, y, cw, ch, small.GetPixels());
            Object.DestroyImmediate(t); Object.DestroyImmediate(small);
        }
        sheet.Apply();
        System.IO.File.WriteAllBytes(Folder + output + ".png", sheet.EncodeToPNG());
        Object.DestroyImmediate(sheet);
        return Folder + output + ".png";
    }

    private static string Render(Camera cam, string name, int w, int h)
    {
        System.IO.Directory.CreateDirectory(Folder);
        RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        RenderTexture old = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        cam.targetTexture = old;
        string path = Folder + name + ".png";
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        // Mean brightness, so a "pitch black" verdict has a number behind it.
        Color32[] px = tex.GetPixels32();
        double sum = 0; int over = 0;
        for (int i = 0; i < px.Length; i += 7) { double l = (px[i].r + px[i].g + px[i].b) / 765.0; sum += l; if (px[i].r > 250 && px[i].g > 250 && px[i].b > 250) over++; }
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);
        return $"{path} mean={sum / (px.Length / 7):F3} blown={over * 7f / px.Length:P1}";
    }
}

/// <summary>DEV ONLY: ASCII occupancy map of a floor rectangle.</summary>
public static class AsylumMap
{
    public static string Map(float minX, float maxX, float minZ, float maxZ, float floorY, float step = 0.5f)
    {
        Physics.SyncTransforms();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"map x {minX}..{maxX} (cols, step {step}) z {maxZ}..{minZ} (rows, north up) floorY {floorY}");
        for (float z = maxZ; z >= minZ; z -= step)
        {
            sb.Append(z.ToString("F0").PadLeft(5)).Append(' ');
            for (float x = minX; x <= maxX; x += step)
            {
                Vector3 c = new Vector3(x, floorY + 1.1f, z);
                bool floor = Physics.Raycast(new Vector3(x, floorY + 0.6f, z), Vector3.down, 1.0f, ~0, QueryTriggerInteraction.Ignore);
                bool blocked = Physics.CheckBox(c, new Vector3(step * 0.45f, 0.75f, step * 0.45f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                sb.Append(!floor && !blocked ? ' ' : blocked ? '#' : '.');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

/// <summary>DEV ONLY: lay props out in a row in empty space and photograph them.</summary>
public static class AsylumCatalog
{
    public static string Shoot(string[] paths, string name, float spacing = 3.2f, float yaw = 0f, float camDist = 12f, Vector3? camOffset = null)
    {
        Vector3 origin = new Vector3(200f, 20f, -50f);
        var root = new GameObject("CatalogTmp");
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.SetParent(root.transform);
        floor.transform.position = origin + new Vector3(paths.Length * spacing * 0.5f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(paths.Length * spacing + 6f, 1f, 8f);
        var lg = new GameObject("L"); lg.transform.SetParent(root.transform);
        var l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f; lg.transform.rotation = Quaternion.Euler(40, -150, 0);
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < paths.Length; i++)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            if (asset == null) { sb.AppendLine("missing " + paths[i]); continue; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.transform.SetParent(root.transform, true);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0) * asset.transform.rotation;
            var rs = go.GetComponentsInChildren<Renderer>();
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            go.transform.position += origin + new Vector3(i * spacing, 0, 0) - new Vector3(b.center.x, b.min.y, b.center.z);
            foreach (var li in go.GetComponentsInChildren<Light>()) li.enabled = false;
        }
        RenderSettings.ambientMode = AmbientMode.Flat;
        var oldAmb = RenderSettings.ambientLight; var oldFog = RenderSettings.fog;
        RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.55f); RenderSettings.fog = false;
        Vector3 mid = origin + new Vector3((paths.Length - 1) * spacing * 0.5f, 1.2f, 0);
        string res = AsylumShot.Free(mid + new Vector3(0, 0.5f, camDist) + (camOffset ?? Vector3.zero), mid, name, 60f, 1920, 540);
        RenderSettings.ambientLight = oldAmb; RenderSettings.fog = oldFog;
        Object.DestroyImmediate(root);
        return res + "\n" + sb;
    }
}

public static class AsylumLit
{
    /// <summary>Edit-mode shot with the hub atmosphere plus a lamp at the camera (stands in for the torch). Restores RenderSettings.</summary>
    public static string Shot(Vector3 pos, Vector3 look, string name, float lamp = 2.5f, float fov = 75f, int w = 1280, int h = 720)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        var amb = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode; var fog = RenderSettings.fog; var fd = RenderSettings.fogDensity; var fc = RenderSettings.fogColor; var fm = RenderSettings.fogMode;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = hub.hubAmbient * 1.5f;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = hub.hubFogDensity; RenderSettings.fogColor = hub.hubFogColor;
        var lg = new GameObject("ShotLamp"); lg.transform.position = pos + (look - pos).normalized * 0.2f;
        var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 13f; l.intensity = lamp; l.color = new Color(1f, 0.95f, 0.85f);
        string res = AsylumShot.Free(pos, look, name, fov, w, h);
        Object.DestroyImmediate(lg);
        RenderSettings.ambientLight = amb; RenderSettings.ambientMode = mode; RenderSettings.fog = fog; RenderSettings.fogDensity = fd; RenderSettings.fogColor = fc; RenderSettings.fogMode = fm;
        return res;
    }
}

public static class AsylumTop
{
    /// <summary>Orthographic plan view with everything above floorY+cut cut away by the near plane.</summary>
    public static string Shot(float cx, float cz, float floorY, float halfX, float halfZ, string name, float cut = 3.0f)
    {
        var amb = RenderSettings.ambientLight; var mode = RenderSettings.ambientMode; var fog = RenderSettings.fog;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.45f); RenderSettings.fog = false;
        var go = new GameObject("TopCam");
        var cam = go.AddComponent<Camera>();
        var data = go.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing = false;
        cam.orthographic = true; cam.orthographicSize = halfZ;
        cam.aspect = halfX / halfZ;
        float camY = floorY + 25f;
        go.transform.position = new Vector3(cx, camY, cz);
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.nearClipPlane = camY - (floorY + cut); cam.farClipPlane = camY - floorY + 2f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        var lg = new GameObject("TopLight"); var l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 1.2f; lg.transform.rotation = Quaternion.Euler(70, 30, 0);
        int w = Mathf.RoundToInt(halfX * 2f * 60f), h = Mathf.RoundToInt(halfZ * 2f * 60f);
        var rt = new RenderTexture(w, h, 24); cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null; cam.targetTexture = null;
        System.IO.File.WriteAllBytes(AsylumShot.Folder + name + ".png", tex.EncodeToPNG());
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go); Object.DestroyImmediate(lg);
        RenderSettings.ambientLight = amb; RenderSettings.ambientMode = mode; RenderSettings.fog = fog;
        return name;
    }
}

public static class AsylumProp
{
    /// <summary>Photographs the first dressing object named `name` from 2.6 m in front of it (native +Z side), eye height.</summary>
    public static string Shot(string name, string file, float dist = 2.6f)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        foreach (Transform t in hub.dressing.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != name || t.parent == null || t.parent.parent != hub.dressing) continue;
            var rs = t.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
            Bounds b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            Vector3 fwd = Quaternion.Euler(0, t.eulerAngles.y, 0) * Vector3.forward;
            // wall props: the open side is the one without a collider hit
            Vector3 c = b.center;
            Vector3 pos = c + fwd * dist; pos.y = b.min.y + 1.5f;
            if (Physics.Raycast(c + Vector3.up * 0.5f, fwd, dist)) pos = c - fwd * dist + Vector3.up * 1.0f; // front blocked: look from the back instead
            return AsylumLit.Shot(pos, c, file, 3f, 70f);
        }
        return "none " + name;
    }
}
