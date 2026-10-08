using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F93 fix: every doorway the player is shown opening must read as an opening into darkness, never as the wall behind it. Build-missing-only,
/// called by AddStoryLayer (so Check Project flags it and Build Missing Pieces adds it; the hub prefab is never regenerated):
///  - <c>DoorVoid</c> under each wing door: an unlit black quad in the frame, 3 cm in front of the wall, behind the leaf (AsylumHub.WalkInRoutine
///    stops the camera 0.3 m out so the quad is always past the 0.2 m near plane);
///  - the lift car's mesh without its two half-shut door leaves (they were part of the one car mesh and read as a wall behind the shaft door);
///    the car is then open at the front: the shaft door opens onto the car interior, and from inside the car onto the hall.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string VoidName = "DoorVoid";
    private const string OpenCarMeshPath = "Assets/SourceFiles/Meshes/Asylum/Elevator_Open.asset";

    private static List<string> G8Missing(AsylumHub hub)
    {
        List<string> missing = new List<string>();
        if (hub.wingDoors != null)
        {
            foreach (WingDoor w in hub.wingDoors) if (w != null && w.transform.Find(VoidName) == null) missing.Add($"{w.name}/{VoidName}");
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

    private static void AddG8(AsylumHub hub, System.Text.StringBuilder report)
    {
        Material black = VoidMaterial();
        foreach (WingDoor w in hub.wingDoors)
        {
            if (w == null || w.transform.Find(VoidName) != null) continue;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = VoidName;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(w.transform, false);
            bool dbl = w.leafHingeB != null;
            float width = dbl ? 1.96f : 1.12f, height = 2.16f;
            quad.transform.localPosition = new Vector3(0f, height * 0.5f, 0.03f); // the wall face is at z = 0, the leaf closed at z >= 0.08
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);        // a Quad shows its face to -Z; the hall is on +Z
            quad.transform.localScale = new Vector3(width, height, 1f);
            MeshRenderer r = quad.GetComponent<MeshRenderer>();
            r.sharedMaterial = black;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            GameObjectUtility.SetStaticEditorFlags(quad, 0);
            report.AppendLine($"{w.name}: black void {width:F2} x {height:F2}");
        }

        LiftSetPiece lift = hub.GetComponentInChildren<LiftSetPiece>(true);
        MeshFilter car = lift != null ? CarFilter(lift) : null;
        if (car != null && car.sharedMesh != null && car.sharedMesh.name != "Elevator_Open") report.AppendLine(OpenTheCar(car));
        MeshFilter shaft = car != null && car.transform.parent != null ? car.transform.parent.GetComponent<MeshFilter>() : null;
        Transform basementDoor = lift != null ? new SerializedObject(lift).FindProperty("basementDoor").objectReferenceValue as Transform : null;
        if (shaft != null && basementDoor != null && shaft.sharedMesh != null && shaft.sharedMesh.name != OpenShaftMeshName) report.AppendLine(OpenTheShaft(shaft, basementDoor));
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
