using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F117: THE LESSON. The first draft of AsylumHub/Lesson, written into the hand-edited hub prefab (<see cref="ApplyHubToPrefab"/>): chalk text laid
/// flat on the middle panel of the classroom's blackboard (Building/2ndFloor/Classroom/Board is a three-panel board whose side panels fold toward
/// the room, so the text sits on the flat middle one), a faint smudge of old writing behind it, a cold dim light on the board (the classroom had
/// none) and an E target. <see cref="HubLesson"/> fills the text at runtime from HunterMemory. Build-missing-only; nothing in the classroom is moved.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string LessonGroupName = "Lesson";
    private const string ClassroomBoardPath = "2ndFloor/Classroom/Board";

    /// <summary>ProjectSetup piece: null when there is no hub (that piece reports it) or the classroom board is complete.</summary>
    public static string LessonProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null;
        if (hub.lesson == null || !hub.lesson.IsComplete) return "missing: AsylumHub/Lesson (the chalk text on the classroom blackboard)";
        return null;
    }

    /// <summary>ProjectSetup's build: chalk text, light and E target on the classroom blackboard, applied to the prefab. Marks the scene dirty; the caller saves.</summary>
    public static void BuildLesson() => BuildLessonCore(false);

    /// <summary>Development only: tears AsylumHub/Lesson down and builds it again. Not a menu item.</summary>
    public static void RebuildLesson() => BuildLessonCore(true);

    private static void BuildLessonCore(bool force)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Asylum lesson: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        if (hub.building == null) { Debug.LogError("Asylum lesson: the hub has no Building group."); return; }
        if (!force && LessonProblem() == null) { Debug.Log("Asylum lesson: the blackboard text is already there."); return; }

        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        StringBuilder log = new StringBuilder();

        Transform board = hub.building.Find(ClassroomBoardPath);
        if (board == null) { Debug.LogError("Asylum lesson: Building/" + ClassroomBoardPath + " not found."); if (!wasActive) hub.SetContentActive(false); return; }

        Transform old = hub.transform.Find(LessonGroupName);
        if (old != null) { Object.DestroyImmediate(old.gameObject); hub.lesson = null; log.AppendLine("removed " + LessonGroupName + " (rebuilt)"); }

        // ---- the writing surface: a ray into the board's middle from the room finds the flat panel's face and normal; scans find its edges
        Bounds bb = RendBounds(board.gameObject);
        Vector3 toRoom = -board.forward; toRoom.y = 0f; toRoom.Normalize();
        Vector3 across = Vector3.Cross(Vector3.up, toRoom).normalized; // along the board, left to right as seen from the room
        Vector3 start = new Vector3(bb.center.x, bb.min.y + 1.55f, bb.center.z) + toRoom * 2.5f;
        if (!BoardHit(start, -toRoom, board, out RaycastHit hit))
        {
            Debug.LogError("Asylum lesson: no ray from the room reaches the blackboard (the classroom board has no collider?).");
            if (!wasActive) hub.SetContentActive(false);
            return;
        }
        Vector3 normal = hit.normal;
        Vector3 face = hit.point;
        ScanPanel(board, face, normal, across, out float left, out float right, out float bottom, out float top);
        float panelW = left + right, panelH = bottom + top;
        Vector3 centre = face + across * ((right - left) * 0.5f) + Vector3.up * ((top - bottom) * 0.5f);
        log.AppendLine($"blackboard middle panel: face {face:F3}, normal {normal:F2}, {panelW:F2} x {panelH:F2} m centred at {centre:F2}");

        // ---- the group, the chalk, the smudge
        GameObject groupGo = new GameObject(LessonGroupName);
        groupGo.transform.SetParent(hub.transform, false);
        groupGo.transform.position = centre;
        groupGo.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up); // TextMeshPro reads from its -Z side: it faces into the wall
        HubLesson lesson = groupGo.AddComponent<HubLesson>();

        WorldSignBuilder.EnsureFonts();
        TMP_FontAsset font = WorldSignBuilder.FontAsset(WorldSignBuilder.SignFont.Reenie);
        Material chalkMat = WorldSignBuilder.TextMaterial(WorldSignBuilder.SignFont.Reenie);
        TextMeshPro chalk = MakeChalkText(groupGo.transform, "Chalk", font, chalkMat, centre + normal * 0.004f, panelW * 0.93f, panelH * 0.9f, new Color(0.93f, 0.93f, 0.9f, 0.92f), 0f);
        TextMeshPro smudge = MakeChalkText(groupGo.transform, "Smudge", font, chalkMat, centre + normal * 0.002f + across * 0.035f + Vector3.down * 0.03f, panelW * 0.93f, panelH * 0.9f, new Color(0.8f, 0.8f, 0.78f, 0.12f), -1.6f);
        lesson.chalk = chalk;
        lesson.smudge = smudge;

        // ---- a cold, dim light on the board: the classroom has no light of its own (one unshadowed point light)
        GameObject lightGo = new GameObject("LessonLight");
        lightGo.transform.SetParent(groupGo.transform, false);
        lightGo.transform.position = centre + normal * 1.25f + Vector3.up * 0.55f;
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.72f, 0.84f, 1f);
        light.intensity = 1.1f;
        light.range = 3.4f;
        light.shadows = LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        lesson.light = light;

        // ---- the E target: a trigger just in front of the face (PlayerInteractor's cast collects triggers)
        GameObject examineGo = new GameObject("Examine");
        examineGo.transform.SetParent(groupGo.transform, false);
        examineGo.transform.SetPositionAndRotation(centre + normal * 0.2f, Quaternion.LookRotation(-normal, Vector3.up));
        BoxCollider box = examineGo.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(panelW, panelH, 0.4f);

        hub.lesson = lesson;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hub);
        lesson.Refresh();
        log.AppendLine("chalk text, smudge, LessonLight and Examine trigger built under AsylumHub/Lesson");
        log.AppendLine(Audit(hub.transform));

        if (!wasActive) hub.SetContentActive(false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        ApplyHubToPrefab(hub);
        Debug.Log("Asylum lesson (the classroom blackboard):\n" + log.ToString().Trim());
    }

    private static TextMeshPro MakeChalkText(Transform parent, string name, TMP_FontAsset font, Material material, Vector3 at, float width, float height, Color color, float rollDegrees)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        go.transform.rotation = parent.rotation * Quaternion.Euler(0f, 0f, rollDegrees);
        TextMeshPro t = go.AddComponent<TextMeshPro>();
        t.font = font;
        if (material != null) t.fontSharedMaterial = material;
        t.rectTransform.sizeDelta = new Vector2(width, height);
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.5f;
        t.fontSizeMax = 2.4f;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.alignment = TextAlignmentOptions.Center;
        t.lineSpacing = 8f;
        t.color = color;
        t.richText = true;
        t.raycastTarget = false;
        t.text = "TODAY'S LESSON";
        return t;
    }

    /// <summary>The first hit of a ray on the blackboard's own collider (the room's walls and furniture are skipped).</summary>
    private static bool BoardHit(Vector3 from, Vector3 dir, Transform board, out RaycastHit result)
    {
        foreach (RaycastHit h in Physics.RaycastAll(from, dir, 6f, ~0, QueryTriggerInteraction.Ignore).OrderBy(x => x.distance))
        {
            if (h.collider.transform.IsChildOf(board)) { result = h; return true; }
        }
        result = default;
        return false;
    }

    /// <summary>
    /// How far the flat panel the ray hit reaches from `face` to the left, right, down and up (metres, along `across` and world up): scans outward in 3 cm
    /// steps while the board still answers within a millimetre of the same plane.
    /// </summary>
    private static void ScanPanel(Transform board, Vector3 face, Vector3 normal, Vector3 across, out float left, out float right, out float bottom, out float top)
    {
        left = Scan(board, face, normal, -across, 2.6f);
        right = Scan(board, face, normal, across, 2.6f);
        bottom = Scan(board, face, normal, Vector3.down, 1.6f);
        top = Scan(board, face, normal, Vector3.up, 1.6f);
    }

    private static float Scan(Transform board, Vector3 face, Vector3 normal, Vector3 dir, float max)
    {
        float reach = 0f;
        for (float d = 0.03f; d <= max; d += 0.03f)
        {
            Vector3 p = face + dir * d + normal * 1.5f;
            if (!BoardHit(p, -normal, board, out RaycastHit h)) break;
            if (Mathf.Abs(Vector3.Dot(h.point - face, normal)) > 0.004f) break;
            reach = d;
        }
        return reach;
    }
}
