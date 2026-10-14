using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F93 fix + F112: every doorway the player is shown opening must read as an opening into darkness (the main doors: into daylight), never as
/// the wall behind it. Build-missing-only, called by AddStoryLayer (so Check Project flags it and Build Missing Pieces adds it; the hub prefab
/// is never regenerated). Per walk-through door - the five wing doors, the SHOP door and the finale's main doors (HubExitDoor) - it adds:
///  - a <see cref="DoorPassage"/> (the leaf hinges, the void, the draft, the pass colour; the main doors' leaf gets its hinge here);
///  - <c>DoorVoid</c>: an unlit quad sized to the closed leaves (+4 cm each side and on top) and placed 1.5 cm in front of the wall face found
///    by a raycast with the leaf colliders off (AsylumHub.DoorWalk stops the camera 0.3 m out so the quad is always past the 0.2 m near plane);
///  - <c>DoorDraft</c>: the dust a door pushes out when it opens.
/// And, separate from the doors:
///  - the lift car's mesh without its two half-shut door leaves (they were part of the one car mesh and read as a wall behind the shaft door);
///    the car is then open at the front: the shaft door opens onto the car interior, and from inside the car onto the hall.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string VoidName = "DoorVoid";
    private const string DraftName = "DoorDraft";
    private const string OpenCarMeshPath = "Assets/SourceFiles/Meshes/Asylum/Elevator_Open.asset";
    private static readonly Color DaylightColour = new Color(0.78f, 0.82f, 0.86f);

    /// <summary>One walk-through door of the hub (F112).</summary>
    private sealed class PassageDoor
    {
        public string Name;
        public Transform Door;
        public Transform[] Hinges;      // null while the main doors' hinge does not exist yet
        public Transform Leaf;          // the main doors' MainDoor mesh (hinge made from it)
        public bool Daylight;
    }

    private static List<PassageDoor> PassageDoors(AsylumHub hub)
    {
        List<PassageDoor> doors = new List<PassageDoor>();
        if (hub.wingDoors != null)
        {
            foreach (WingDoor w in hub.wingDoors)
            {
                if (w == null || w.leafHinge == null) continue;
                doors.Add(new PassageDoor { Name = w.name, Door = w.transform, Hinges = new[] { w.leafHinge, w.leafHingeB }.Where(h => h != null).ToArray() });
            }
        }
        if (hub.shopDoor != null && hub.shopDoor.leafHinge != null)
        {
            ShopDoor s = hub.shopDoor;
            doors.Add(new PassageDoor { Name = s.name, Door = s.transform, Hinges = new[] { s.leafHinge, s.leafHingeB }.Where(h => h != null).ToArray() });
        }
        HubExitDoor exit = hub.GetComponentInChildren<HubExitDoor>(true);
        if (exit != null)
        {
            Transform leaf = new SerializedObject(exit).FindProperty("leaf").objectReferenceValue as Transform;
            if (leaf != null)
            {
                Transform[] hinges = leaf.parent != null && leaf.parent.name == "DoorHinge" ? new[] { leaf.parent } : null;
                doors.Add(new PassageDoor { Name = exit.name, Door = exit.transform, Leaf = leaf, Hinges = hinges, Daylight = true });
            }
        }
        return doors;
    }

    /// <summary>The closed leaves' union bounds, the door's flat Into (hall to door) and its width axis.</summary>
    private static Bounds LeafBounds(PassageDoor d)
    {
        Bounds b = new Bounds(d.Door.position, Vector3.zero);
        bool any = false;
        foreach (Transform hinge in d.Hinges)
        {
            foreach (Renderer r in hinge.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
        }
        return b;
    }

    private static Vector3 IntoOf(PassageDoor d)
    {
        Vector3 into = -d.Door.forward;
        into.y = 0f;
        return into.normalized;
    }

    /// <summary>Width (along the door's right) and height of the void a door needs: the closed leaves plus 4 cm on each side and on top, the bottom sunk 3 cm into the floor.</summary>
    private static void VoidSize(PassageDoor d, out float width, out float height, out float bottom, out float lateralCentre)
    {
        Bounds b = LeafBounds(d);
        Vector3 right = Vector3.Cross(Vector3.up, IntoOf(d));
        float lo = float.MaxValue, hi = float.MinValue;
        for (int c = 0; c < 8; c++)
        {
            Vector3 corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
            float along = Vector3.Dot(corner, right);
            lo = Mathf.Min(lo, along);
            hi = Mathf.Max(hi, along);
        }
        width = hi - lo + 0.08f;
        lateralCentre = 0.5f * (lo + hi);
        bottom = b.min.y - 0.03f; // sunk 3 cm into the floor: a flush edge showed a lit line under the void
        height = b.max.y + 0.04f - bottom;
    }

    private static List<string> G8Missing(AsylumHub hub)
    {
        List<string> missing = new List<string>();
        foreach (PassageDoor d in PassageDoors(hub))
        {
            DoorPassage passage = d.Door.GetComponent<DoorPassage>();
            if (passage == null || !passage.IsComplete) missing.Add($"{d.Name}/DoorPassage");
            if (d.Hinges != null && d.Hinges.Length > 0 && hub.ContentActive)
            {
                Transform v = d.Door.Find(VoidName);
                VoidSize(d, out float w, out float h, out _, out _);
                if (v == null || Mathf.Abs(v.localScale.x - w) > 0.01f || Mathf.Abs(v.localScale.y - h) > 0.01f) missing.Add($"{d.Name}/{VoidName} size");
            }
            if (d.Door.Find(DraftName) == null) missing.Add($"{d.Name}/{DraftName}");
        }
        LiftSetPiece lift = hub.GetComponentInChildren<LiftSetPiece>(true);
        MeshFilter car = lift != null ? CarFilter(lift) : null;
        if (car != null && (car.sharedMesh == null || car.sharedMesh.name != "Elevator_Open")) missing.Add("lift car without door leaves");
        MeshFilter shaft = car != null && car.transform.parent != null ? car.transform.parent.GetComponent<MeshFilter>() : null;
        if (shaft != null && (shaft.sharedMesh == null || shaft.sharedMesh.name != OpenShaftMeshName)) missing.Add("lift shaft opened at the basement");
        return missing;
    }

    private static MeshFilter CarFilter(LiftSetPiece lift)
    {
        Transform car = new SerializedObject(lift).FindProperty("car").objectReferenceValue as Transform;
        return car != null ? car.GetComponent<MeshFilter>() : null;
    }

    /// <summary>
    /// The main doors' MainDoor leaf belongs to the hub prefab (a child of the pack's HallDownstair mesh), and a scene instance may not
    /// reparent a child that comes from its prefab - so the hinge is hung in the prefab asset itself (the leaf goes under a new DoorHinge),
    /// and the instance picks it up on the refresh. No-op when the leaf already hangs under a hinge.
    /// </summary>
    private static void EnsureMainDoorHinge(AsylumHub hub)
    {
        HubExitDoor exit = hub.GetComponentInChildren<HubExitDoor>(true);
        Transform leaf = exit != null ? new SerializedObject(exit).FindProperty("leaf").objectReferenceValue as Transform : null;
        if (leaf == null || (leaf.parent != null && leaf.parent.name == "DoorHinge")) return;

        string leafPath = AnimationUtility.CalculateTransformPath(leaf, hub.transform);
        string buildingPath = AnimationUtility.CalculateTransformPath(hub.building, hub.transform);
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform l = contents.transform.Find(leafPath);
            Transform room = contents.transform.Find(buildingPath);
            if (l == null || room == null) { Debug.LogError($"Asylum doors: the main doors' leaf ({leafPath}) was not found in {PrefabPath}."); return; }
            foreach (Transform t in l.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            Transform hinge = ShopRoom.CreateDoorHinge(room, l);
            if (hinge == null) { Debug.LogError("Asylum doors: the main doors' leaf has no renderer; no hinge."); return; }
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        AssetDatabase.Refresh();
    }

    private static void AddG8(AsylumHub hub, System.Text.StringBuilder report)
    {
        EnsureMainDoorHinge(hub);
        foreach (PassageDoor d in PassageDoors(hub)) AddPassage(hub, d, report);

        LiftSetPiece lift = hub.GetComponentInChildren<LiftSetPiece>(true);
        MeshFilter car = lift != null ? CarFilter(lift) : null;
        if (car != null && car.sharedMesh != null && car.sharedMesh.name != "Elevator_Open") report.AppendLine(OpenTheCar(car));
        MeshFilter shaft = car != null && car.transform.parent != null ? car.transform.parent.GetComponent<MeshFilter>() : null;
        Transform basementDoor = lift != null ? new SerializedObject(lift).FindProperty("basementDoor").objectReferenceValue as Transform : null;
        if (shaft != null && basementDoor != null && shaft.sharedMesh != null && shaft.sharedMesh.name != OpenShaftMeshName) report.AppendLine(OpenTheShaft(shaft, basementDoor));
    }

    /// <summary>F112: hinge (main doors), void fitted to the leaves and the wall, draft and the DoorPassage component for one door. Idempotent.</summary>
    private static void AddPassage(AsylumHub hub, PassageDoor d, System.Text.StringBuilder report)
    {
        // 1. Hinges. The main doors' hinge is made in the prefab asset by EnsureMainDoorHinge (it used to be made at runtime by HubExitDoor).
        if (d.Hinges == null || d.Hinges.Length == 0)
        {
            Debug.LogError($"Asylum doors: {d.Name} has no leaf hinge yet (the prefab refresh may still be pending): run KEEP IT LIT > Build Missing Pieces again.");
            return;
        }
        Physics.SyncTransforms();
        // 2. The void: fitted to the closed leaves, 1.5 cm in front of the wall face behind them.
        Bounds leaf = LeafBounds(d);
        Vector3 into = IntoOf(d);
        VoidSize(d, out float width, out float height, out float bottom, out float lateral);
        Vector3 right = Vector3.Cross(Vector3.up, into);
        Vector3 doorwayMid = new Vector3(leaf.center.x, bottom + 1.1f, leaf.center.z);
        Vector3 origin = doorwayMid - into * 1.0f;

        List<Collider> off = new List<Collider>();
        foreach (Transform hinge in d.Hinges)
        {
            foreach (Collider c in hinge.GetComponentsInChildren<Collider>(true)) if (c.enabled) { c.enabled = false; off.Add(c); }
        }
        Physics.SyncTransforms();
        RaycastHit wall = default;
        bool found = false;
        try
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, into, 4f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
            foreach (RaycastHit h in hits)
            {
                if (h.collider.transform.IsChildOf(d.Door)) continue;
                wall = h;
                found = true;
                break;
            }
        }
        finally
        {
            foreach (Collider c in off) c.enabled = true;
            Physics.SyncTransforms();
        }

        // The leaves are thick slabs that sit flush on the wall (their back face is within a few cm of it, sometimes inside it), so the void
        // cannot be required to sit behind the leaf's BACK face: it only has to be hidden by the closed leaf (behind its FRONT face) and in
        // front of the wall. It shows the moment the leaf swings away.
        float leafBack = float.MinValue, leafFront = float.MaxValue;
        for (int c = 0; c < 8; c++)
        {
            Vector3 corner = new Vector3((c & 1) == 0 ? leaf.min.x : leaf.max.x, (c & 2) == 0 ? leaf.min.y : leaf.max.y, (c & 4) == 0 ? leaf.min.z : leaf.max.z);
            float along = Vector3.Dot(corner, into);
            leafBack = Mathf.Max(leafBack, along);
            leafFront = Mathf.Min(leafFront, along);
        }
        // No collider wall behind it (the main doors stand in a real doorway, open to the outside): the void goes in the middle of the leaf slab,
        // 4 cm or more behind the hall face of the wall that frames it, so only the opening shows it.
        string wallName = found ? wall.collider.name : "open air";
        float wallS = found ? Vector3.Dot(wall.point, into) : 0.5f * (leafFront + leafBack) + 0.015f;
        float voidS = wallS - 0.015f;
        if (voidS < leafFront + 0.01f) voidS = wallS - 0.005f;
        if (voidS < leafFront + 0.01f)
        {
            Debug.LogError($"Asylum doors: {d.Name}: the wall face ({wallName}) is only {(wallS - leafFront) * 100f:F1} cm behind the closed leaf's front face; no room for a void. Skipped.");
            return;
        }
        float gap = wallS - voidS;
        Vector3 pos = right * lateral + into * voidS; // the three axes are orthonormal
        pos.y = bottom + height * 0.5f;

        Transform voidT = d.Door.Find(VoidName);
        MeshRenderer voidRenderer;
        if (voidT == null)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = VoidName;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(d.Door, true);
            voidT = quad.transform;
            voidRenderer = quad.GetComponent<MeshRenderer>();
            voidRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            voidRenderer.receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(quad, 0);
        }
        else voidRenderer = voidT.GetComponent<MeshRenderer>();
        voidT.position = pos;
        voidT.rotation = Quaternion.LookRotation(into, Vector3.up); // a Quad shows its face to -Z; the hall is behind this rotation's -Z
        Vector3 parentScale = d.Door.lossyScale;
        voidT.localScale = new Vector3(width / Mathf.Max(parentScale.x, 0.0001f), height / Mathf.Max(parentScale.y, 0.0001f), 1f);
        voidRenderer.sharedMaterial = d.Daylight ? DaylightVoidMaterial() : VoidMaterial();
        EditorUtility.SetDirty(voidT);

        // 3. The draft.
        ParticleSystem draft = BuildDraft(hub, d, pos, into, width, height);

        // 4. The component.
        DoorPassage passage = d.Door.GetComponent<DoorPassage>();
        if (passage == null) passage = d.Door.gameObject.AddComponent<DoorPassage>();
        SerializedObject so = new SerializedObject(passage);
        SerializedProperty hingeProp = so.FindProperty("hinges");
        hingeProp.arraySize = d.Hinges.Length;
        for (int i = 0; i < d.Hinges.Length; i++) hingeProp.GetArrayElementAtIndex(i).objectReferenceValue = d.Hinges[i];
        so.FindProperty("voidRenderer").objectReferenceValue = voidRenderer;
        so.FindProperty("draft").objectReferenceValue = draft;
        so.FindProperty("passColour").colorValue = d.Daylight ? DaylightColour : Color.black;
        so.FindProperty("openAngle").floatValue = 86f;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(passage);
        PrefabUtility.RecordPrefabInstancePropertyModifications(d.Door);
        report.AppendLine($"{d.Name}: passage, void {width:F2} x {height:F2} at {gap:F3} m from the wall ({wallName}), void {(voidS - leafBack) * 100f:F1} cm behind the leaf back face (negative = inside the leaf slab){(draft != null ? ", draft" : "")}");
    }

    /// <summary>The dust a door pushes out when it opens (child DoorDraft, not looping, not playing on awake). Null when the lift's dust material cannot be found.</summary>
    private static ParticleSystem BuildDraft(AsylumHub hub, PassageDoor d, Vector3 voidPosition, Vector3 into, float width, float height)
    {
        Transform existing = d.Door.Find(DraftName);
        ParticleSystem ps = existing != null ? existing.GetComponent<ParticleSystem>() : null;
        if (ps != null) return ps;

        LiftSetPiece lift = hub.GetComponentInChildren<LiftSetPiece>(true);
        ParticleSystem dust = lift != null ? new SerializedObject(lift).FindProperty("ceilingDust").objectReferenceValue as ParticleSystem : null;
        Material material = dust != null ? dust.GetComponent<ParticleSystemRenderer>().sharedMaterial : null;
        if (material == null) { Debug.LogError($"Asylum doors: {d.Name}: the lift's ceilingDust material was not found; no draft particles (the door still works)."); return null; }

        GameObject go = new GameObject(DraftName);
        go.transform.SetParent(d.Door, true);
        go.transform.position = voidPosition - into * 0.05f;
        go.transform.rotation = Quaternion.LookRotation(-into, Vector3.up); // +Z toward the hall
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 1.2f;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = 60;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        main.startColor = d.Daylight ? new Color(0.9f, 0.92f, 0.95f, 0.25f) : new Color(0.75f, 0.75f, 0.78f, 0.35f);
        main.gravityModifier = -0.02f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 18f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = new Vector3(0f, 0f, 0.06f);
        shape.scale = new Vector3(width, height, 0.05f);

        ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
        fade.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        return ps;
    }

    private static Material DaylightVoidMaterial()
    {
        EnsureFolderPath(MaterialFolder);
        string path = MaterialFolder + "/Asylum_DoorVoidDaylight.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Asylum_DoorVoidDaylight" };
        m.SetColor("_BaseColor", DaylightColour);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    private const string OpenShaftMeshName = "Elevator_Wall_Open";
    private const string OpenShaftMeshPath = "Assets/SourceFiles/Meshes/Asylum/Elevator_Wall_Open.asset";

    /// <summary>
    /// The pack never opened the lift shaft at the basement: its wall mesh has a solid panel behind the basement shaft door, facing into the
    /// shaft, so from inside the car the door read as plaster (the corridor already has its doorway). Copies the shaft mesh without the
    /// wall triangles across the door's frame and puts back a lintel above and a sill below the opening, with the panel's own UVs.
    /// </summary>
    private static string OpenTheShaft(MeshFilter shaft, Transform basementDoor)
    {
        Renderer doorRenderer = basementDoor.GetComponentInChildren<Renderer>();
        if (doorRenderer == null) return "lift shaft: the basement door has no renderer";
        Bounds door = doorRenderer.bounds;
        Transform tf = shaft.transform;
        Mesh src = shaft.sharedMesh;
        Vector3[] v = src.vertices;
        int[] t = src.triangles;

        // The panel: wall triangles in the door's plane, facing along the door's depth axis, inside the door's width, reaching below its top.
        List<int> keep = new List<int>();
        List<int> panelVerts = new List<int>();
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 a = tf.TransformPoint(v[t[i]]), b = tf.TransformPoint(v[t[i + 1]]), c = tf.TransformPoint(v[t[i + 2]]);
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            bool inPlane = Mathf.Abs(n.z) > 0.95f && Mathf.Abs(a.z - door.center.z) < 0.3f && Mathf.Abs(b.z - a.z) < 0.01f && Mathf.Abs(c.z - a.z) < 0.01f;
            bool inWidth = Mathf.Min(a.x, Mathf.Min(b.x, c.x)) > door.min.x - 0.02f && Mathf.Max(a.x, Mathf.Max(b.x, c.x)) < door.max.x + 0.02f;
            bool low = Mathf.Min(a.y, Mathf.Min(b.y, c.y)) < door.max.y;
            if (inPlane && inWidth && low) { panelVerts.Add(t[i]); panelVerts.Add(t[i + 1]); panelVerts.Add(t[i + 2]); continue; }
            keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
        }
        if (panelVerts.Count == 0) return "lift shaft: no wall panel behind the basement door (already open?)";

        // The panel's corners (world) with their UVs, to rebuild the strips above and below the opening.
        float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
        foreach (int vi in panelVerts) { Vector3 p = tf.TransformPoint(v[vi]); x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y); }
        Vector2[] uv = src.uv;
        Vector3[] normals = src.normals;
        Vector4[] tangents = src.tangents;
        int Corner(float x, float y)
        {
            int best = panelVerts[0];
            float bestD = float.MaxValue;
            foreach (int vi in panelVerts) { Vector3 p = tf.TransformPoint(v[vi]); float d = (p.x - x) * (p.x - x) + (p.y - y) * (p.y - y); if (d < bestD) { bestD = d; best = vi; } }
            return best;
        }
        int c00 = Corner(x0, y0), c10 = Corner(x1, y0), c01 = Corner(x0, y1), c11 = Corner(x1, y1);
        float planeZ = tf.TransformPoint(v[c00]).z;
        Vector2 Uv(float x, float y)
        {
            float u = Mathf.InverseLerp(x0, x1, x), w = Mathf.InverseLerp(y0, y1, y);
            return Vector2.Lerp(Vector2.Lerp(uv[c00], uv[c10], u), Vector2.Lerp(uv[c01], uv[c11], u), w);
        }
        // Winding taken from the removed panel, so the strips face the same way.
        int[] firstTri = { panelVerts[0], panelVerts[1], panelVerts[2] };
        Vector3 fa = tf.TransformPoint(v[firstTri[0]]), fb = tf.TransformPoint(v[firstTri[1]]), fc = tf.TransformPoint(v[firstTri[2]]);
        bool facesPlusZ = Vector3.Cross(fb - fa, fc - fa).z > 0f;

        List<Vector3> nv = new List<Vector3>(v);
        List<Vector2> nuv = new List<Vector2>(uv);
        List<Vector3> nn = new List<Vector3>(normals);
        List<Vector4> nt = tangents != null && tangents.Length == v.Length ? new List<Vector4>(tangents) : null;
        void Strip(float yLow, float yHigh)
        {
            if (yHigh - yLow < 0.01f) return;
            int start = nv.Count;
            foreach ((float x, float y) in new[] { (x0, yLow), (x1, yLow), (x0, yHigh), (x1, yHigh) })
            {
                nv.Add(tf.InverseTransformPoint(new Vector3(x, y, planeZ)));
                nuv.Add(Uv(x, y));
                nn.Add(normals[c00]);
                if (nt != null) nt.Add(tangents[c00]);
            }
            // quad 0 bottom-left, 1 bottom-right, 2 top-left, 3 top-right: (0,1,3) winds to +Z, (0,3,1) to -Z
            int[] q = facesPlusZ ? new[] { 0, 1, 3, 0, 3, 2 } : new[] { 0, 3, 1, 0, 2, 3 };
            foreach (int k in q) keep.Add(start + k);
        }
        Strip(door.max.y, y1);  // the lintel up to the ground floor
        Strip(y0, door.min.y);  // the sill down to the pit

        Mesh open = Object.Instantiate(src);
        open.name = OpenShaftMeshName;
        open.SetVertices(nv);
        open.SetUVs(0, nuv);
        open.SetNormals(nn);
        if (nt != null) open.SetTangents(nt);
        open.triangles = keep.ToArray();
        open.RecalculateBounds();
        EnsureFolderPath("Assets/SourceFiles/Meshes/Asylum");
        AssetDatabase.CreateAsset(open, OpenShaftMeshPath);
        shaft.sharedMesh = open;
        MeshCollider mc = shaft.GetComponent<MeshCollider>();
        if (mc != null) mc.sharedMesh = open;
        EditorUtility.SetDirty(shaft);
        if (mc != null) EditorUtility.SetDirty(mc);
        PrefabUtility.RecordPrefabInstancePropertyModifications(shaft);
        if (mc != null) PrefabUtility.RecordPrefabInstancePropertyModifications(mc);
        return $"lift shaft: basement panel x {x0:F2}..{x1:F2}, y {y0:F2}..{y1:F2} opened to the door ({door.min.y:F2}..{door.max.y:F2}) ({OpenShaftMeshPath})";
    }

    private static Material VoidMaterial()
    {
        EnsureFolderPath(MaterialFolder);
        string path = MaterialFolder + "/Asylum_DoorVoid.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "Asylum_DoorVoid" };
        m.SetColor("_BaseColor", Color.black);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>Copies the car mesh without the triangles of its two door leaves (thin slabs at z -1.73..-1.65, y 0.05..2.22) and puts it on the car's filter and collider.</summary>
    private static string OpenTheCar(MeshFilter car)
    {
        Mesh src = car.sharedMesh;
        Vector3[] v = src.vertices;
        int[] t = src.triangles;
        List<int> keep = new List<int>();
        int removed = 0;
        for (int i = 0; i < t.Length; i += 3)
        {
            bool leaf = true;
            for (int k = 0; k < 3; k++)
            {
                Vector3 p = v[t[i + k]];
                if (p.z < -1.735f || p.z > -1.645f || p.y < 0.04f || p.y > 2.225f) leaf = false;
            }
            if (leaf) { removed++; continue; }
            keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
        }
        if (removed == 0) return "lift car: no door leaves found in the mesh (already open?)";

        Mesh open = Object.Instantiate(src);
        open.name = "Elevator_Open";
        open.triangles = keep.ToArray();
        open.RecalculateBounds();
        EnsureFolderPath("Assets/SourceFiles/Meshes/Asylum");
        AssetDatabase.CreateAsset(open, OpenCarMeshPath);
        car.sharedMesh = open;
        MeshCollider mc = car.GetComponent<MeshCollider>();
        if (mc != null) mc.sharedMesh = open;
        EditorUtility.SetDirty(car);
        if (mc != null) EditorUtility.SetDirty(mc);
        PrefabUtility.RecordPrefabInstancePropertyModifications(car);
        if (mc != null) PrefabUtility.RecordPrefabInstancePropertyModifications(mc);
        return $"lift car: {removed / 2} door-leaf quads removed from the mesh ({OpenCarMeshPath})";
    }
}
