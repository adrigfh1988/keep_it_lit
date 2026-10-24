using System;
using UnityEditor;
using UnityEngine;

public static partial class FloorThemeBuilder
{
    // ---------------------------------------------------------------- shared low-level helpers

    private static void EnsureFolder(string path) => EditorAssetUtil.EnsureFolder(path);

    private static Transform Group(Transform parent, string name) => BuilderPrimitives.Group(parent, name);

    private static GameObject Primitive(Transform parent, PrimitiveType type, string pieceName, Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider, Quaternion? localRotation = null)
        => BuilderPrimitives.Primitive(parent, type, pieceName, localPosition, localScale, material, keepCollider, localRotation);

    private static GameObject Box(Transform parent, string pieceName, Vector3 localPosition, Vector3 size, Material material, bool keepCollider = false, Quaternion? localRotation = null)
        => BuilderPrimitives.Primitive(parent, PrimitiveType.Cube, pieceName, localPosition, size, material, keepCollider, localRotation);

    /// <summary>The built-in cylinder is 2 units tall with a 1-unit diameter at scale 1, so this converts a radius/height pair into that scale.</summary>
    private static GameObject CylinderRH(Transform parent, string pieceName, Vector3 localPosition, float radius, float height, Material material, bool keepCollider = false, Quaternion? localRotation = null)
        => BuilderPrimitives.Primitive(parent, PrimitiveType.Cylinder, pieceName, localPosition, new Vector3(radius * 2f, height * 0.5f, radius * 2f), material, keepCollider, localRotation);

    /// <summary>A cylinder spanning two local points - handy for torch handles, chains, wires, pipes at an angle.</summary>
    private static GameObject CylinderBetween(Transform parent, string pieceName, Vector3 a, Vector3 b, float radius, Material material, bool keepCollider = false)
    {
        Vector3 mid = (a + b) * 0.5f;
        float length = Mathf.Max(0.01f, Vector3.Distance(a, b));
        GameObject go = Primitive(parent, PrimitiveType.Cylinder, pieceName, mid, new Vector3(radius * 2f, length * 0.5f, radius * 2f), material, keepCollider);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
        return go;
    }

    private static GameObject Sphere(Transform parent, string pieceName, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider = false)
        => BuilderPrimitives.Primitive(parent, PrimitiveType.Sphere, pieceName, localPosition, scale, material, keepCollider);

    private static GameObject Capsule(Transform parent, string pieceName, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider = false, Quaternion? localRotation = null)
        => BuilderPrimitives.Primitive(parent, PrimitiveType.Capsule, pieceName, localPosition, scale, material, keepCollider, localRotation);

    private static Texture2D Texture(string path)
    {
        Texture2D tex = BuilderMaterials.Texture(path);
        if (tex == null) Debug.LogWarning($"FloorThemeBuilder: texture not found at {path}, leaving the material untextured there.");
        return tex;
    }

    /// <summary>Loads a normal-map texture and fixes its import type if it was not already imported as one - URP renders an un-typed normal map wrong.</summary>
    private static Texture2D NormalTexture(string path) => BuilderMaterials.NormalTexture(path);

    private static void PointLight(Transform parent, string name, Vector3 localPosition, Color color, float range, float intensity)
        => BuilderPrimitives.PointLight(parent, name, localPosition, color, range, intensity);

    /// <summary>Loaded if it already exists at folder/name.mat (so hand edits survive a rebuild), otherwise created as a URP Lit material with the given tint, optional albedo/normal textures and tiling, and optional emission.</summary>
    private static Material LoadOrCreateMat(string folder, string name, Color color, string texturePath, string normalPath, Vector2 tiling, Color? emission, float smoothness = 0.15f)
    {
        string path = $"{folder}/Materials/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Texture2D tex = !string.IsNullOrEmpty(texturePath) ? Texture(texturePath) : null;
        Texture2D normalTex = !string.IsNullOrEmpty(normalPath) ? NormalTexture(normalPath) : null;
        return BuilderMaterials.LoadOrCreateTiledMat(folder, name, color, tex, normalTex, tiling, emission, smoothness);
    }

    /// <summary>Loaded if folder/Prefabs/prefabName.prefab already exists (the freshly built temp hierarchy is discarded), otherwise saved fresh.</summary>
    private static GameObject SaveAsPrefab(GameObject root, string folder, string prefabName)
    {
        string path = $"{folder}/Prefabs/{prefabName}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(root);
            return existing;
        }

        return RoomPrefabLifecycle.SaveAsPrefab(root, folder, prefabName);
    }

    private static T SaveAsPrefab<T>(GameObject root, string folder, string prefabName) where T : Component
    {
        return SaveAsPrefab(root, folder, prefabName).GetComponent<T>();
    }

    // ---------------------------------------------------------------- door slats (mirrors MazeGenerator.BuildDoorSlats)

    /// <summary>Three horizontal slats with two 0.06 m gaps around eye height, same proportions as MazeGenerator's primitive locker door. Returns [bottom, middle, top] so a caller can hang extra dressing off one of them.</summary>
    private static GameObject[] BuildDoorSlats(Transform doorPivot, float doorWidth, float doorHeight, float depth, Material material)
    {
        const float gap = 0.06f;
        float bottomHeight = Mathf.Clamp(1.5f, 0.1f, doorHeight);
        float midHeight = 0.08f;
        float midStart = bottomHeight + gap;
        float midEnd = midStart + midHeight;
        float topStart = midEnd + gap;
        float topHeight = Mathf.Max(0.1f, doorHeight - topStart);

        GameObject bottom = DoorSlat(doorPivot, "Slat_Bottom", doorWidth, bottomHeight, depth, bottomHeight * 0.5f, material);
        GameObject middle = DoorSlat(doorPivot, "Slat_Middle", doorWidth, midHeight, depth, midStart + midHeight * 0.5f, material);
        GameObject top = DoorSlat(doorPivot, "Slat_Top", doorWidth, topHeight, depth, topStart + topHeight * 0.5f, material);
        return new[] { bottom, middle, top };
    }

    private static GameObject DoorSlat(Transform parent, string slatName, float width, float height, float depth, float centerY, Material material)
    {
        // Slats keep their collider: it is what stops the hunter's LOS raycast seeing through a closed door.
        return Box(parent, slatName, new Vector3(width * 0.5f, centerY, 0f), new Vector3(width, height, depth), material, keepCollider: true);
    }

    // ---------------------------------------------------------------- piece finishers

    private static WallPiece FinishWall(GameObject root, string folder)
    {
        // Default authoredLength/Height/Thickness (5, 4, 0.5) already match the canonical size every
        // wall below is built at, so there is nothing to override via SerializedObject here.
        root.AddComponent<WallPiece>();
        return SaveAsPrefab<WallPiece>(root, folder, root.name);
    }

    private static WallLamp FinishLamp(GameObject root, string folder, Light light, Renderer glass)
    {
        WallLamp lamp = root.AddComponent<WallLamp>();
        SerializedObject so = new SerializedObject(lamp);
        so.FindProperty("lightSource").objectReferenceValue = light;
        so.FindProperty("glassRenderer").objectReferenceValue = glass;
        so.ApplyModifiedPropertiesWithoutUndo();
        return SaveAsPrefab<WallLamp>(root, folder, root.name);
    }

    private static PropPiece FinishProp(GameObject root, string folder, PropPiece.MountKind mount, float depth, float weight, bool enabledInMaze = true)
    {
        PropPiece prop = root.AddComponent<PropPiece>();
        SerializedObject so = new SerializedObject(prop);
        so.FindProperty("mount").enumValueIndex = (int)mount;
        so.FindProperty("depth").floatValue = depth;
        so.FindProperty("weight").floatValue = weight;
        so.FindProperty("enabledInMaze").boolValue = enabledInMaze;
        so.ApplyModifiedPropertiesWithoutUndo();
        return SaveAsPrefab<PropPiece>(root, folder, root.name);
    }

    /// <summary>
    /// The standard locker shell every theme's locker is built from: body, plinth, trigger and anchors
    /// at the numbers the Ward table gives (every other theme's row says "Anchors as Ward" - same hinge
    /// position too, so Locker.Expose always swings the same way). buildDoorContents fills the door
    /// pivot (three slats for most themes, something else for Crypt/Hollow); extraBodyDressing adds
    /// anything else against the body (bevel strips, an LED strip, ...).
    /// </summary>
    private static Locker BuildLockerShell(string folder, string prefabName, Material bodyMat, Action<Transform> buildDoorContents, Action<GameObject> extraBodyDressing = null)
    {
        const float width = 1.1f;
        const float height = 2.1f;
        const float depth = 0.6f;

        GameObject root = new GameObject(prefabName);

        Box(root.transform, "Body", new Vector3(0f, height * 0.5f, 0.3f), new Vector3(width, height, depth), bodyMat, keepCollider: true);
        extraBodyDressing?.Invoke(root);

        GameObject doorPivot = new GameObject("Door");
        doorPivot.transform.SetParent(root.transform, false);
        doorPivot.transform.localPosition = new Vector3(-width * 0.5f, 0f, 0.62f);
        buildDoorContents(doorPivot.transform);

        Box(root.transform, "Plinth", new Vector3(0f, 0.025f, 0.3f), new Vector3(width + 0.1f, 0.05f, depth + 0.1f), bodyMat);

        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(1.4f, 2.0f, 1.2f);
        trigger.center = new Vector3(0f, 1.0f, 1.5f);

        GameObject insideAnchor = new GameObject("InsideAnchor");
        insideAnchor.transform.SetParent(root.transform, false);
        insideAnchor.transform.localPosition = new Vector3(0f, 0f, 0.3f);

        GameObject frontAnchor = new GameObject("FrontAnchor");
        frontAnchor.transform.SetParent(root.transform, false);
        frontAnchor.transform.localPosition = new Vector3(0f, 0f, 1.6f);

        Locker locker = root.AddComponent<Locker>();
        SerializedObject so = new SerializedObject(locker);
        so.FindProperty("door").objectReferenceValue = doorPivot.transform;
        so.FindProperty("insideAnchor").objectReferenceValue = insideAnchor.transform;
        so.FindProperty("frontAnchor").objectReferenceValue = frontAnchor.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        return SaveAsPrefab<Locker>(root, folder, prefabName);
    }
}
