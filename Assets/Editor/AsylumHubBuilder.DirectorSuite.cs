using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F115 (G10): the Director's suite. The Director's office (7.5 x 6.9 m, crammed with a film-projector tableau) gets a private lounge where the
/// staff kitchen was: the 1.15 m void between the office shell's east face (x 111.03) and the kitchen shell's west face (x 112.18) is opened
/// with a 4.5 m doorway (z -62.5..-58.0, 2.9 m clear), the old furniture goes, and the office is rebuilt as a real office: desk facing the room
/// with the Director behind it and the master switch on the wall behind him. Build-missing-only, called by AddStoryLayer after AddG9 and listed in
/// StoryMissing. Spec and "as built" notes: plannings/director-office-redesign-plan.md.
///
/// Where the plan's numbers were wrong (measured 14 Oct 2026): the kitchen shell's inner face is x 112.18 (112.12 is the facing's back); the
/// removed faces run to y 12.50 (shell top), not 12.29; six decal planes sit on the two faces, not three; the kitchen shell already uses Wall_3.
/// Everything here is positioned in world metres and written into mesh assets / the prefab; the hub is at (100, 5, -50) with identity rotation.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string SuiteMarker = "F115_Suite";
    private const string SuiteMeshDir = "Assets/SourceFiles/Meshes/Asylum/";
    private const string SuiteMatDir = "Assets/SourceFiles/Materials/Asylum/";
    private const string PartitionName = "F115_Partition";
    private const string ThresholdName = "F115_Threshold";

    // The partition between the two shells (world metres).
    private const float PartOfficeX = 111.03f, PartLoungeX = 112.18f;
    private const float OpenZ0 = -62.5f, OpenZ1 = -58.0f;      // the doorway
    private const float OpenTop = 11.72f;                       // top of the doorway (soffit)
    private const float WallBottom = 8.81f, WallTop = 12.50f;   // the shells' own extent
    private const float SouthZ = -63.67f, NorthZ = -56.79f;     // the shells' inner south / north faces
    // measured -63.672 / -56.794: the partition runs 8 mm into both walls, or a hairline of sky shows at the corner
    private const float PartSouthZ = -63.68f, PartNorthZ = -56.78f;
    private static bool _suiteRebuildMeshes; // dev: regenerate the partition mesh in place (same asset, same GUID)
    private const float FloorLevel = 8.82f;

    private static readonly string[] SuiteDecals =
    {
        "Decals_2 (17)", "Decals_3 (8)", "Decals_3 (9)", // office side
        "Decals_2 (22)", "Decals_3 (24)", "Decals_3 (25)", // lounge side
    };

    // ---------------------------------------------------------------- mesh surgery

    private static string SuiteMeshPath(string name) => SuiteMeshDir + name + ".asset";

    /// <summary>A copy of `src` (every vertex array untouched) whose index buffer lacks every triangle lying in the plane x = `planeX` (world, from `t`).</summary>
    private static Mesh TrimmedCopy(Mesh src, Transform t, float xLow, float xHigh, string assetName)
    {
        string path = SuiteMeshPath(assetName);
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null && !_suiteRebuildMeshes) return existing;
        Mesh copy = Object.Instantiate(src);
        copy.name = assetName;
        Vector3[] v = src.vertices;
        int[] idx = src.triangles;
        List<int> keep = new List<int>(idx.Length);
        int dropped = 0;
        for (int i = 0; i < idx.Length; i += 3)
        {
            bool inPlane = true;
            for (int k = 0; k < 3; k++) { float wx = t.TransformPoint(v[idx[i + k]]).x; if (wx < xLow || wx > xHigh) { inPlane = false; break; } }
            if (inPlane) { dropped++; continue; }
            keep.Add(idx[i]); keep.Add(idx[i + 1]); keep.Add(idx[i + 2]);
        }
        copy.SetTriangles(keep, 0);
        copy.RecalculateBounds();
        EnsureFolderPath("Assets/SourceFiles/Meshes/Asylum");
        Debug.Log($"F115: {assetName}: dropped {dropped} triangle(s) between x={xLow:F2} and {xHigh:F2}, kept {keep.Count / 3}.");
        if (existing != null) { EditorUtility.CopySerialized(copy, existing); EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    private sealed class MeshBuilder
    {
        public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int> tris = new List<int>();
        public Vector3 origin;

        /// <summary>A quad given by four world corners (any order that goes round) and its outward normal; `uvOf` maps a world point to a uv.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, System.Func<Vector3, Vector2> uvOf)
        {
            int s = v.Count;
            foreach (Vector3 p in new[] { a, b, c, d })
            {
                v.Add(p - origin);
                n.Add(normal);
                uv.Add(uvOf(p));
            }
            // Unity: the face normal is Cross(b - a, c - a); flip when that points away from the wanted side.
            bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f;
            if (!flip) { tris.Add(s); tris.Add(s + 1); tris.Add(s + 2); tris.Add(s); tris.Add(s + 2); tris.Add(s + 3); }
            else { tris.Add(s); tris.Add(s + 2); tris.Add(s + 1); tris.Add(s); tris.Add(s + 3); tris.Add(s + 2); }
        }
    }

    /// <summary>The partition between the shells: stubs, lintel, jambs and soffit as outward faces only (plaster + the wainscot skin), uv in world metres.</summary>
    private static Mesh BuildPartitionMesh(Vector3 hubWorld)
    {
        string path = SuiteMeshPath("Asylum_DirectorSuitePartition");
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null && !_suiteRebuildMeshes) return existing;

        MeshBuilder plaster = new MeshBuilder { origin = hubWorld }, paint = new MeshBuilder { origin = hubWorld };
        float x0 = PartOfficeX, x1 = PartLoungeX, y0 = WallBottom, y1 = WallTop;
        float yw = 10.16f, ywLounge = 10.49f; // wainscot tops: the office's, the kitchen's own

        // each shell's own uv mapping continues onto its own face (measured on the shells), so the seam is invisible
        Vector2 OfficeUv(Vector3 p) => new Vector2((p.y - 8.81f) * 0.055f, (p.z - SouthZ) * 0.0552f);
        Vector2 LoungeUv(Vector3 p) => new Vector2((p.z - SouthZ) * 0.09f, 0.33f + (p.y - 8.81f) * 0.0976f);
        Vector2 OffW(Vector3 p) => new Vector2((p.z + 63.66f) * 0.0758f, 0.205f + (p.y - 8.81f) * 0.0763f);
        Vector2 LouW(Vector3 p) => new Vector2((p.z + 63.66f) * 0.0874f, 0.147f + (p.y - 8.81f) * 0.078f);
        Vector2 JambUv(Vector3 p) => new Vector2((p.y - 8.81f) * 0.055f, (p.x - x0) * 0.0552f);
        Vector2 SoffitUv(Vector3 p) => new Vector2((p.x - x0) * 0.055f, (p.z - SouthZ) * 0.0552f);

        (float za, float zb)[] stubs = { (PartSouthZ, OpenZ0), (OpenZ1, PartNorthZ) };
        foreach ((float za, float zb) in stubs)
        {
            // office-side face (normal -x) and lounge-side face (normal +x), floor to the shell top
            plaster.Quad(new Vector3(x0, y0, za), new Vector3(x0, y1, za), new Vector3(x0, y1, zb), new Vector3(x0, y0, zb), Vector3.left, OfficeUv);
            plaster.Quad(new Vector3(x1, y0, za), new Vector3(x1, y1, za), new Vector3(x1, y1, zb), new Vector3(x1, y0, zb), Vector3.right, LoungeUv);
            // the wainscot skin 2 cm proud of each face
            paint.Quad(new Vector3(x0 - 0.02f, y0, za), new Vector3(x0 - 0.02f, yw, za), new Vector3(x0 - 0.02f, yw, zb), new Vector3(x0 - 0.02f, y0, zb), Vector3.left, OffW);
            paint.Quad(new Vector3(x1 + 0.03f, y0, za), new Vector3(x1 + 0.03f, ywLounge, za), new Vector3(x1 + 0.03f, ywLounge, zb), new Vector3(x1 + 0.03f, y0, zb), Vector3.right, LouW);
        }

        // jambs: the south stub's north end faces +z, the north stub's south end faces -z; from the floor up to the soffit
        plaster.Quad(new Vector3(x0, y0, OpenZ0), new Vector3(x0, OpenTop, OpenZ0), new Vector3(x1, OpenTop, OpenZ0), new Vector3(x1, y0, OpenZ0), Vector3.forward, JambUv);
        plaster.Quad(new Vector3(x0, y0, OpenZ1), new Vector3(x0, OpenTop, OpenZ1), new Vector3(x1, OpenTop, OpenZ1), new Vector3(x1, y0, OpenZ1), Vector3.back, JambUv);
        paint.Quad(new Vector3(x0, y0, OpenZ0 + 0.02f), new Vector3(x0, yw, OpenZ0 + 0.02f), new Vector3(x1, yw, OpenZ0 + 0.02f), new Vector3(x1, y0, OpenZ0 + 0.02f), Vector3.forward, OffW);
        paint.Quad(new Vector3(x0, y0, OpenZ1 - 0.02f), new Vector3(x0, yw, OpenZ1 - 0.02f), new Vector3(x1, yw, OpenZ1 - 0.02f), new Vector3(x1, y0, OpenZ1 - 0.02f), Vector3.back, OffW);

        // the lintel: both faces from the soffit to the shell top, and the soffit itself (faces down)
        plaster.Quad(new Vector3(x0, OpenTop, OpenZ0), new Vector3(x0, y1, OpenZ0), new Vector3(x0, y1, OpenZ1), new Vector3(x0, OpenTop, OpenZ1), Vector3.left, OfficeUv);
        plaster.Quad(new Vector3(x1, OpenTop, OpenZ0), new Vector3(x1, y1, OpenZ0), new Vector3(x1, y1, OpenZ1), new Vector3(x1, OpenTop, OpenZ1), Vector3.right, LoungeUv);
        plaster.Quad(new Vector3(x0, OpenTop, OpenZ0), new Vector3(x1, OpenTop, OpenZ0), new Vector3(x1, OpenTop, OpenZ1), new Vector3(x0, OpenTop, OpenZ1), Vector3.down, SoffitUv);

        Mesh mesh = new Mesh { name = "Asylum_DirectorSuitePartition" };
        List<Vector3> verts = new List<Vector3>(plaster.v); verts.AddRange(paint.v);
        List<Vector3> norms = new List<Vector3>(plaster.n); norms.AddRange(paint.n);
        List<Vector2> uvs = new List<Vector2>(plaster.uv); uvs.AddRange(paint.uv);
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uvs); // uv2 is unused (no lightmaps) but the pack shaders' meshes carry it
        mesh.subMeshCount = 2;
        mesh.SetTriangles(plaster.tris, 0);
        List<int> paintTris = paint.tris.Select(i => i + plaster.v.Count).ToList();
        mesh.SetTriangles(paintTris, 1);
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        EnsureFolderPath("Assets/SourceFiles/Meshes/Asylum");
        if (existing != null) { EditorUtility.CopySerialized(mesh, existing); EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>The threshold: a low wood box under the doorway, opaque over the 11 cm slot between the two floors (which opens onto the room below).</summary>
    private static Mesh BuildThresholdMesh(Vector3 hubWorld)
    {
        string path = SuiteMeshPath("Asylum_DirectorSuiteThreshold");
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;
        MeshBuilder b = new MeshBuilder { origin = hubWorld };
        float x0 = PartOfficeX, x1 = PartLoungeX, z0 = OpenZ0, z1 = OpenZ1, top = FloorLevel + 0.012f, bottom = FloorLevel - 0.15f;
        Vector2 Top(Vector3 p) => new Vector2(p.x * 0.119f, p.z * 0.119f);
        Vector2 SideX(Vector3 p) => new Vector2(p.z * 0.119f, p.y * 0.119f);
        Vector2 SideZ(Vector3 p) => new Vector2(p.x * 0.119f, p.y * 0.119f);
        b.Quad(new Vector3(x0, top, z0), new Vector3(x1, top, z0), new Vector3(x1, top, z1), new Vector3(x0, top, z1), Vector3.up, Top);
        b.Quad(new Vector3(x0, bottom, z0), new Vector3(x0, top, z0), new Vector3(x0, top, z1), new Vector3(x0, bottom, z1), Vector3.left, SideX);
        b.Quad(new Vector3(x1, bottom, z0), new Vector3(x1, top, z0), new Vector3(x1, top, z1), new Vector3(x1, bottom, z1), Vector3.right, SideX);
        b.Quad(new Vector3(x0, bottom, z0), new Vector3(x0, top, z0), new Vector3(x1, top, z0), new Vector3(x1, bottom, z0), Vector3.back, SideZ);
        b.Quad(new Vector3(x0, bottom, z1), new Vector3(x0, top, z1), new Vector3(x1, top, z1), new Vector3(x1, bottom, z1), Vector3.forward, SideZ);
        b.Quad(new Vector3(x0, bottom, z0), new Vector3(x1, bottom, z0), new Vector3(x1, bottom, z1), new Vector3(x0, bottom, z1), Vector3.down, Top);
        Mesh mesh = new Mesh { name = "Asylum_DirectorSuiteThreshold" };
        mesh.SetVertices(b.v); mesh.SetNormals(b.n); mesh.SetUVs(0, b.uv); mesh.SetTriangles(b.tris, 0);
        mesh.RecalculateTangents(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// <summary>A copy of the office floor's WoodFloor whose tiling matches the office plank size on the kitchen floor's different uv density.</summary>
    private static Material LoungeFloorMaterial()
    {
        string path = SuiteMatDir + "Asylum_LoungeFloor.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Material wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Abandoned_Asylum/Materials/WoodFloor.mat");
        if (wood == null) { Debug.LogError("F115: Assets/Abandoned_Asylum/Materials/WoodFloor.mat not found."); return null; }
        EnsureFolderPath("Assets/SourceFiles/Materials/Asylum");
        Material m = new Material(wood) { name = "Asylum_LoungeFloor" };
        // office floor: 0.119 uv per metre on both axes at tiling 5; the kitchen floor: u over x 0.18/m, v over z 0.1125/m
        Vector2 scale = new Vector2(5f * 0.119f / 0.18f, 5f * 0.119f / 0.1125f);
        m.mainTextureScale = scale;
        if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", scale);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ---------------------------------------------------------------- the shells (prefab asset)

    private static bool SuiteShellDone(AsylumHub hub)
    {
        Transform b = hub.building != null ? hub.building : hub.transform.Find("Building");
        Transform floor2 = b != null ? b.Find("2ndFloor") : null;
        return floor2 != null && floor2.Find(PartitionName) != null;
    }

    /// <summary>
    /// Opens the wall in the prefab asset: the four trimmed meshes on the shells' MeshFilters and MeshColliders, the partition and the threshold
    /// as new children of 2ndFloor, the lounge floor material, the six decal planes off. One LoadPrefabContents / SaveAsPrefabAsset round trip.
    /// </summary>
    private static string OpenSuiteWall(AsylumHub hub)
    {
        Transform building = hub.building;
        Transform office = building.Find("2ndFloor/Office"), kitchen = building.Find("2ndFloor/Kitchen");
        if (office == null || kitchen == null) return "F115: the Office or Kitchen object was not found; the wall was not opened.";
        Mesh officeShell = TrimmedCopy(office.GetComponent<MeshFilter>().sharedMesh, office, PartOfficeX - 0.03f, PartOfficeX + 0.03f, "Asylum_OfficeShell_Open");
        Mesh officeFacing = TrimmedCopy(office.Find("Office_facing").GetComponent<MeshFilter>().sharedMesh, office.Find("Office_facing"), 111.00f, 111.06f, "Asylum_OfficeFacing_Open"); // the plane AND its 4 cm top cap (a ledge across the doorway at y 10.16)
        Mesh kitchenShell = TrimmedCopy(kitchen.GetComponent<MeshFilter>().sharedMesh, kitchen, PartLoungeX - 0.03f, PartLoungeX + 0.03f, "Asylum_KitchenShell_Open");
        Mesh kitchenFacing = TrimmedCopy(kitchen.Find("Kitchen_facing").GetComponent<MeshFilter>().sharedMesh, kitchen.Find("Kitchen_facing"), 112.10f, 112.22f, "Asylum_KitchenFacing_Open"); // plane + 9 cm top cap at y 10.49
        Vector3 origin = hub.transform.position;
        Mesh partition = BuildPartitionMesh(origin);
        Mesh threshold = BuildThresholdMesh(origin);
        Material loungeFloor = LoungeFloorMaterial();
        Material plaster = office.GetComponent<MeshRenderer>().sharedMaterial;                                 // Wall_3
        Material paint = office.Find("Office_facing").GetComponent<MeshRenderer>().sharedMaterial;             // Wall_4
        Material wood = office.Find("Office_floor").GetComponent<MeshRenderer>().sharedMaterial;               // WoodFloor
        StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(office.gameObject);
        AssetDatabase.SaveAssets();

        EditHubPrefab(root =>
        {
            Transform ob = root.transform.Find("Building/2ndFloor/Office"), kb = root.transform.Find("Building/2ndFloor/Kitchen");
            void Swap(Transform t, Mesh m)
            {
                t.GetComponent<MeshFilter>().sharedMesh = m;
                MeshCollider mc = t.GetComponent<MeshCollider>();
                if (mc != null) mc.sharedMesh = m;
            }
            Swap(ob, officeShell); Swap(ob.Find("Office_facing"), officeFacing);
            Swap(kb, kitchenShell); Swap(kb.Find("Kitchen_facing"), kitchenFacing);
            kb.Find("Kitchen_floor").GetComponent<MeshRenderer>().sharedMaterial = loungeFloor;
            kb.Find("Kitchen_facing").GetComponent<MeshRenderer>().sharedMaterial = paint;

            Transform floor2 = root.transform.Find("Building/2ndFloor");
            foreach (string stale in new[] { PartitionName, ThresholdName }) { Transform old = floor2.Find(stale); if (old != null) Object.DestroyImmediate(old.gameObject); }

            GameObject part = new GameObject(PartitionName);
            part.transform.SetParent(floor2, false);
            part.AddComponent<MeshFilter>().sharedMesh = partition;
            part.AddComponent<MeshRenderer>().sharedMaterials = new[] { plaster, paint };
            part.AddComponent<MeshCollider>().sharedMesh = partition;
            GameObjectUtility.SetStaticEditorFlags(part, flags);

            GameObject thr = new GameObject(ThresholdName);
            thr.transform.SetParent(floor2, false);
            thr.AddComponent<MeshFilter>().sharedMesh = threshold;
            thr.AddComponent<MeshRenderer>().sharedMaterial = wood;
            BoxCollider box = thr.AddComponent<BoxCollider>();
            box.center = new Vector3((PartOfficeX + PartLoungeX) * 0.5f, FloorLevel + 0.012f - 0.1f, (OpenZ0 + OpenZ1) * 0.5f) - origin;
            box.size = new Vector3(PartLoungeX - PartOfficeX + 0.1f, 0.2f, OpenZ1 - OpenZ0);
            GameObjectUtility.SetStaticEditorFlags(thr, flags);

            Transform decals = root.transform.Find("Building/Decals_All");
            foreach (string d in SuiteDecals) { Transform t = decals != null ? decals.Find(d) : null; if (t != null) t.gameObject.SetActive(false); }
        });
        return $"F115: wall opened (z {OpenZ0}..{OpenZ1}, soffit y {OpenTop}), threshold, lounge floor, {SuiteDecals.Length} decals off";
    }

    /// <summary>Round-trips AsylumHub.prefab through LoadPrefabContents and refreshes the scene instance (the G8/G9 precedent: structure changes go into the asset).</summary>
    private static void EditHubPrefab(System.Action<GameObject> edit)
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            edit(contents);
            PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
    }

}
