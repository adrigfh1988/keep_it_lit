using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// The shop is a hand-editable prefab, like the Intake: Assets/Prefabs/ShopRoom.prefab is the source of truth (the
/// salesman inside it is the nested ShopSalesman.prefab). Generate saves a fresh room over it; BuildSilently and
/// <see cref="Repair"/> only place it, or save a plain scene room as the prefab. The migrations that add to an existing
/// room (contract board, door hinge, Timmy) still work on the scene instance; apply them to the prefab to keep them there.
/// </summary>
public static partial class ShopRoomBuilder
{
    public const string PrefabPath = "Assets/Prefabs/ShopRoom.prefab";

    /// <summary>Null when the scene has the ShopRoom prefab with the parts the component needs. Used by ProjectSetup.</summary>
    public static string RoomProblem()
    {
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null
                ? "missing from the scene (Build Missing Pieces places the saved prefab)"
                : "missing (floors 1-4 fall back to a plain win)";
        }
        if (!shop.IsComplete) return "incomplete";
        SerializedProperty head = new SerializedObject(shop).FindProperty("headPivot");
        if (head != null && head.objectReferenceValue == null) return "the salesman has no headPivot";
        if (!IsShopPrefabInstance(shop.gameObject)) return $"not a prefab yet (Build Missing Pieces saves it as {PrefabPath})";
        return null;
    }

    /// <summary>ProjectSetup's fix: place the saved prefab when the room is missing, or save a plain scene room as the prefab.</summary>
    public static void Repair()
    {
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null) BuildSilently();
        else if (!IsShopPrefabInstance(shop.gameObject)) SaveAsPrefab(shop.gameObject);
    }

    /// <summary>Saves <paramref name="room"/> (hand edits included) as ShopRoom.prefab and connects it in place.</summary>
    public static void SaveAsPrefab(GameObject room)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        PrefabUtility.SaveAsPrefabAssetAndConnect(room, PrefabPath, InteractionMode.AutomatedAction, out bool ok);
        if (!ok)
        {
            Debug.LogError($"Build Shop Room: could not save {PrefabPath}.", room);
            return;
        }
        EditorSceneManager.MarkSceneDirty(room.scene);
        Debug.Log($"Shop room saved as {PrefabPath}. Edit it by hand (double-click the prefab), then save the scene (Ctrl+S).", room);
    }

    private static void InstantiatePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject room = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        Undo.RegisterCreatedObjectUndo(room, "Place Shop Room");
        room.name = RoomName;
        Debug.Log($"Shop room placed from {PrefabPath}. Save the scene (Ctrl+S) to keep it.", room);
    }

    private static bool IsShopPrefabInstance(GameObject room) =>
        PrefabUtility.IsAnyPrefabInstanceRoot(room) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(room) == PrefabPath;
}
