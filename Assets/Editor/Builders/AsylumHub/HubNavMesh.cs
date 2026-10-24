using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F93 (E3): the asylum hub's own NavMesh, for the pursuer (HubStalker, G4+) and anything else that has to walk the hospital. The pack
/// doors are about 0.9-1.0 m clear, too narrow for the default humanoid agent (radius 0.5), so the hub gets a second agent type,
/// "Hub" (radius 0.3, height 1.8, climb 0.3, slope 45), created at most once (looked up by name first - NavMesh.CreateSettings()
/// adds a type every time it is called, and a previous run leaked eight of them). The surface lives on AsylumHub/Navigation, uses
/// a volume over the building with PhysicsColliders (the pack meshes are not Read/Write, so the models behind the building's mesh
/// colliders are switched to Read/Write first), is baked here in the editor and saved as Assets/SourceFiles/NavMesh/AsylumHub_NavMesh.asset.
/// It never touches the maze's runtime surface (MazeGenerator ignores any surface under an AsylumHub).
/// </summary>
public static class HubNavMesh
{
    public const string AgentName = "Hub";
    public const string DataPath = "Assets/SourceFiles/NavMesh/AsylumHub_NavMesh.asset";
    public const string NavigationName = "Navigation";

    /// <summary>The agent type id of "Hub", or -1 if there is none yet.</summary>
    public static int FindAgentType()
    {
        int count = NavMesh.GetSettingsCount();
        for (int i = 0; i < count; i++)
        {
            NavMeshBuildSettings s = NavMesh.GetSettingsByIndex(i);
            if (NavMesh.GetSettingsNameFromID(s.agentTypeID) == AgentName) return s.agentTypeID;
        }
        return -1;
    }

    /// <summary>Returns the "Hub" agent type id, creating it exactly once if it does not exist.</summary>
    public static int EnsureAgentType()
    {
        int id = FindAgentType();
        if (id != -1) return id;

        NavMesh.CreateSettings();
        Object singleton = Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings");
        SerializedObject so = new SerializedObject(singleton);
        SerializedProperty settings = so.FindProperty("m_Settings");
        SerializedProperty names = so.FindProperty("m_SettingNames");
        while (names.arraySize < settings.arraySize) names.InsertArrayElementAtIndex(names.arraySize);
        int index = settings.arraySize - 1;
        names.GetArrayElementAtIndex(index).stringValue = AgentName;
        SerializedProperty element = settings.GetArrayElementAtIndex(index);
        element.FindPropertyRelative("agentRadius").floatValue = 0.3f;
        element.FindPropertyRelative("agentHeight").floatValue = 1.8f;
        element.FindPropertyRelative("agentClimb").floatValue = 0.3f;
        element.FindPropertyRelative("agentSlope").floatValue = 45f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(singleton);
        AssetDatabase.SaveAssets();

        id = FindAgentType();
        Debug.Log($"HubNavMesh: created the \"{AgentName}\" agent type (id {id}); agent types now: {NavMesh.GetSettingsCount()}.");
        return id;
    }

    /// <summary>Switches Read/Write on for every model that backs a MeshCollider under `building`. Returns how many importers changed.</summary>
    public static int EnableReadWrite(Transform building)
    {
        HashSet<string> paths = new HashSet<string>();
        foreach (MeshCollider mc in building.GetComponentsInChildren<MeshCollider>(true))
        {
            if (mc.sharedMesh == null) continue;
            string path = AssetDatabase.GetAssetPath(mc.sharedMesh);
            if (!string.IsNullOrEmpty(path)) paths.Add(path);
        }

        int changed = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in paths)
            {
                ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null || importer.isReadable) continue;
                importer.isReadable = true;
                importer.SaveAndReimport();
                changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        return changed;
    }

    /// <summary>
    /// Adds (or reuses) AsylumHub/Navigation with a surface for the Hub agent, bakes it with the hub content active and saves the data
    /// asset. `root` is the scene instance of the hub. Returns a one-line report.
    /// </summary>
    public static string Bake(AsylumHub hub)
    {
        int agent = EnsureAgentType();
        if (agent == -1) return "could not create the Hub agent type";

        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        int readable = EnableReadWrite(hub.building);
        Physics.SyncTransforms();

        Transform nav = hub.transform.Find(NavigationName);
        if (nav == null)
        {
            GameObject go = new GameObject(NavigationName);
            go.transform.SetParent(hub.transform, false);
            nav = go.transform;
        }
        NavMeshSurface surface = nav.GetComponent<NavMeshSurface>();
        if (surface == null) surface = nav.gameObject.AddComponent<NavMeshSurface>();

        Bounds b = new Bounds(hub.transform.position, Vector3.zero);
        foreach (Renderer r in hub.building.GetComponentsInChildren<Renderer>(true)) b.Encapsulate(r.bounds);
        b.Expand(2f);

        surface.agentTypeID = agent;
        surface.collectObjects = CollectObjects.Volume;
        surface.center = nav.InverseTransformPoint(b.center);
        surface.size = b.size;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~0;
        surface.defaultArea = 0;
        surface.overrideVoxelSize = false;
        surface.overrideTileSize = false;
        surface.minRegionArea = 0.5f;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;

        surface.BuildNavMesh();

        NavMeshData data = surface.navMeshData;
        if (data == null) return "bake produced no data";
        EnsureFolder("Assets/SourceFiles/NavMesh");
        if (AssetDatabase.LoadAssetAtPath<NavMeshData>(DataPath) != null && AssetDatabase.LoadAssetAtPath<NavMeshData>(DataPath) != data)
        {
            AssetDatabase.DeleteAsset(DataPath);
        }
        if (AssetDatabase.GetAssetPath(data) != DataPath) AssetDatabase.CreateAsset(data, DataPath);
        EditorUtility.SetDirty(surface);
        AssetDatabase.SaveAssets();

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (!wasActive) hub.SetContentActive(false);
        return $"agent {agent}, {readable} models made readable, {tri.indices.Length / 3} triangles in the whole navmesh, data {DataPath}";
    }

    /// <summary>
    /// Audit lines (each ends in OK when it passes): the agent type exists exactly once, the surface has data, and a complete path on
    /// the Hub agent leads from MainEntrance to every wing arrival.
    /// </summary>
    public static string Audit(AsylumHub hub)
    {
        StringBuilder sb = new StringBuilder();
        int named = 0;
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            if (NavMesh.GetSettingsNameFromID(NavMesh.GetSettingsByIndex(i).agentTypeID) == AgentName) named++;
        }
        int agent = FindAgentType();
        sb.AppendLine($"  navmesh agent type \"{AgentName}\": {(named == 1 ? "OK" : named == 0 ? "MISSING" : "DUPLICATED x" + named)}");
        Transform nav = hub.transform.Find(NavigationName);
        NavMeshSurface surface = nav != null ? nav.GetComponent<NavMeshSurface>() : null;
        bool hasData = surface != null && surface.navMeshData != null && AssetDatabase.GetAssetPath(surface.navMeshData) == DataPath;
        sb.AppendLine($"  navmesh surface + saved data: {(hasData ? "OK" : "MISSING (Build Missing Pieces bakes it)")}");
        if (!hasData || agent == -1 || hub.mainEntrance == null) return sb.ToString();

        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = agent, areaMask = NavMesh.AllAreas };
        bool all = true;
        if (!NavMesh.SamplePosition(hub.mainEntrance.position, out NavMeshHit start, 1.5f, filter))
        {
            sb.AppendLine("  navmesh path: MainEntrance is off the Hub navmesh");
            return sb.ToString();
        }
        NavMeshPath path = new NavMeshPath();
        foreach (WingDoor door in hub.wingDoors)
        {
            if (door == null || door.arrivalPoint == null) continue;
            bool ok = NavMesh.SamplePosition(door.arrivalPoint.position, out NavMeshHit end, 1.5f, filter) &&
                      NavMesh.CalculatePath(start.position, end.position, filter, path) && path.status == NavMeshPathStatus.PathComplete;
            all &= ok;
            sb.AppendLine($"  navmesh path entrance -> wing {door.floor}: {(ok ? PathLength(path).ToString("F0") + " m OK" : "NO COMPLETE PATH")}");
        }
        sb.AppendLine($"  navmesh paths: {(all ? "complete OK" : "BROKEN")}");
        return sb.ToString();
    }

    private static float PathLength(NavMeshPath path)
    {
        float len = 0f;
        for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return len;
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
