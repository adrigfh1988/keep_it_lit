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
