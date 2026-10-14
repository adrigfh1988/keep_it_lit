using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>F114 (G9), part 2: the seven scares (HubRoomScare subclasses) under AsylumHub/Life/RoomScares.</summary>
public static partial class AsylumHubBuilder
{
    // ---------------------------------------------------------------- scare plumbing

    /// <summary>Creates AsylumHub/Life/RoomScares/&lt;name&gt; at the centre of `room` with a trigger box the size of the room.</summary>
    private static GameObject NewScareHolder(AsylumHub hub, int index, Bounds room, out BoxCollider box)
    {
        GameObject go = new GameObject(ScareNames[index]);
        go.transform.SetParent(hub.life.Find(ScaresName), false);
        go.transform.position = room.center;
        box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = room.size;
        return go;
    }

    private static void SetScareCommon(SerializedObject so, int index, int mask, BoxCollider room, Transform target, HubRoomScare.LookMode look,
        float lookDot, float maxDistance, float minDistance, float hold, float armDelay)
    {
        PropString(so, "roomId", "R" + (index + 1));
        PropInt(so, "floorMask", mask);
        PropObj(so, "room", room);
        PropObj(so, "target", target);
        PropInt(so, "lookMode", (int)look);
        PropFloat(so, "lookDot", lookDot);
        PropFloat(so, "maxDistance", maxDistance);
        PropFloat(so, "minDistance", minDistance);
        PropFloat(so, "holdSeconds", hold);
        PropFloat(so, "armDelay", armDelay);
    }

    private static Transform MarkAt(Transform parent, string name, Vector3 world)
    {
        Transform t = Group(parent, name);
        t.position = world;
        return t;
    }

    // ---------------------------------------------------------------- R1: the stall that slams

    private static string BuildStallScare(AsylumHub hub)
    {
        Bounds room = FloorBox("ToiletsA_floor");
        Transform leaf = Under("Bathroom_ToiletsWall (4)", "ToiletDoor (2)");
        if (leaf == null || leaf.parent == null || leaf.parent.name != "DoorHinge") return "R1 stall door has no hinge";
        Transform hinge = leaf.parent;

        float yaw = YawOf(hinge);
        if (Mathf.Abs(yaw) < 1f)
        {
            yaw = StallYaw(leaf, hinge, 70f, out float freeRoom, out float freeStall);
            hinge.localRotation = Quaternion.Euler(0f, yaw, 0f) * hinge.localRotation;
            PrefabUtility.RecordPrefabInstancePropertyModifications(hinge);
        }
        Physics.SyncTransforms();

        GameObject go = NewScareHolder(hub, 0, room, out BoxCollider box);
        Vector3 closedCentre = hinge.position + Quaternion.Euler(0f, -yaw, 0f) * (RendererBoundsOf(leaf).center - hinge.position); // where the leaf lands when it slams
        Transform at = MarkAt(go.transform, "SlamAt", closedCentre);
        ScareStallSlam scare = go.AddComponent<ScareStallSlam>();
        SerializedObject so = new SerializedObject(scare);
        SetScareCommon(so, 0, MaskFloors135, box, at, HubRoomScare.LookMode.Away, 0.6f, 5f, 1.4f, 0f, 0.6f);
        PropObj(so, "hinge", hinge);
        PropFloat(so, "openYaw", yaw);
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{ScareNames[0]}: stall door {leaf.name} left open {yaw:F0} degrees, slams at {at.position:F2}";
    }

    // ---------------------------------------------------------------- R2: the cabinets that creak open

    private static string BuildCabinetsScare(AsylumHub hub)
    {
        Bounds room = FloorBox("Toilets_floor");
        (string room, string shelf)[] shelves = { ("ToiletsWoman", "MirrorShelf (4)"), ("ToiletsWoman", "MirrorShelf (5)"), ("ToiletsMan", "MirrorShelf (6)"), ("ToiletsMan", "MirrorShelf (7)") };
        List<Transform> leaves = new List<Transform>();
        List<float> sides = new List<float>();
        List<Vector3> reachable = new List<Vector3>(), all = new List<Vector3>();
        foreach ((string roomName, string shelfName) in shelves)
        {
            Transform shelf = Under(roomName, shelfName);
            Transform caseT = shelf != null ? shelf.Find("MirrorShelf_Case") : null;
            Transform left = shelf != null ? shelf.Find("MirrorShelf_DoorL") : null;
            Transform right = shelf != null ? shelf.Find("MirrorShelf_DoorR") : null;
            if (caseT == null || left == null || right == null) return $"cabinet {roomName}/{shelfName} not found";
            leaves.Add(left); sides.Add(1f);
            leaves.Add(right); sides.Add(-1f);
            Vector3 front = Flat(-shelf.forward);
            if (Vector3.Dot(front, room.center - shelf.position) < 0f) front = -front;
            Vector3 face = RendererBoundsOf(caseT).center + front * 0.15f;
            all.Add(face);
            if (Reach(hub, new Vector3(face.x + front.x * 1.0f, room.min.y + 0.2f, face.z + front.z * 1.0f))) reachable.Add(face);
        }

        // "The middle of the cabinet row": the mean of the cabinets the player can actually walk up to (the men's half is behind a closed door).
        List<Vector3> pool = reachable.Count > 0 ? reachable : all;
        Vector3 middle = Vector3.zero;
        foreach (Vector3 p in pool) middle += p;
        middle /= pool.Count;

        GameObject go = NewScareHolder(hub, 1, room, out BoxCollider box);
        Transform target = MarkAt(go.transform, "RowMiddle", middle);
        ScareCabinetsOpen scare = go.AddComponent<ScareCabinetsOpen>();
        SerializedObject so = new SerializedObject(scare);
        SetScareCommon(so, 1, MaskFloors24, box, target, HubRoomScare.LookMode.At, 0.6f, 6f, 2f, 0.8f, 0.6f);
        PropObjects(so, "leaves", leaves.Cast<Object>().ToList());
        PropFloats(so, "sides", sides);
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{ScareNames[1]}: {leaves.Count} cabinet leaves, row middle {middle:F2} from {pool.Count} of {all.Count} cabinets ({reachable.Count} reachable)";
    }

    // ---------------------------------------------------------------- R3: lights out, a hand-print on the mirror

    private static string BuildHandprintScare(AsylumHub hub)
    {
        Bounds room = FloorBox("Bathroom_floor");
        Transform shelf = Under("Bathroom", "MirrorShelf (1)");
        Transform caseT = shelf != null ? shelf.Find("MirrorShelf_Case") : null;
        Transform left = shelf != null ? shelf.Find("MirrorShelf_DoorL") : null;
        if (caseT == null || left == null) return "bathroom middle cabinet not found";

        // The source decal: the cell's hand-print (a holder with one Quad child carrying the Handprint material).
        Transform source = hub.dressing.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "F101_SeclusionRoomA_Kit_Handprint" && t.GetComponent<Renderer>() != null);
        if (source == null) source = hub.dressing.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Quad" && t.parent != null && t.parent.name == "F101_SeclusionRoomA_Kit_Handprint");
        Material material = source != null ? source.GetComponent<Renderer>().sharedMaterial : null;
        if (material == null) return "the cell's hand-print decal (F101_SeclusionRoomA_Kit_Handprint) was not found";

        Vector3 front = Flat(-shelf.forward);
        if (Vector3.Dot(front, room.center - shelf.position) < 0f) front = -front;

        GameObject go = NewScareHolder(hub, 2, room, out BoxCollider box);
        Transform target = MarkAt(go.transform, "LookAt", RendererBoundsOf(caseT).center + front * 0.15f);

        GameObject print = GameObject.CreatePrimitive(PrimitiveType.Quad);
        print.name = "Handprint";
        Object.DestroyImmediate(print.GetComponent<Collider>());
        print.transform.SetParent(left, false);
        print.transform.position = left.TransformPoint(new Vector3(0.118f, 0f, -0.014f)); // on the left mirror, 1 cm proud of its front face
        print.transform.rotation = Quaternion.LookRotation(-front, Vector3.up);           // a Quad shows its face to -Z: the print faces the room
        print.transform.localScale = Vector3.one * 0.2f;
        MeshRenderer mr = print.GetComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        GameObjectUtility.SetStaticEditorFlags(print, 0);
        print.SetActive(false);

        ScareLightsHandprint scare = go.AddComponent<ScareLightsHandprint>();
        SerializedObject so = new SerializedObject(scare);
        SetScareCommon(so, 2, MaskFloors235, box, target, HubRoomScare.LookMode.At, 0.6f, 3f, 0f, 0f, 0.6f);
        PropObj(so, "handprint", print);
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{ScareNames[2]}: hand-print on {left.name} of {shelf.name}, look target {target.position:F2}";
    }

    // ---------------------------------------------------------------- screens: the television (R4) and the CCTV monitor (R6)

    /// <summary>
    /// The flat front face of a mesh object that looks toward `toward` (world): the triangles whose world normal is within ~30 degrees of an
    /// axis of the object and turned toward `toward`, in the plane that holds the most of their area (planes bucketed to `planeTolerance`, so a stand, a
    /// knob or a rounded rim is left out). Returns a centre (on the plane), a rotation that makes a Quad's visible side face outward (up stays up) and the
    /// rectangle's size.
    /// </summary>
    private static bool FrontFace(Transform t, Vector3 toward, float planeTolerance, float ignoreBelow, out Vector3 center, out Quaternion rotation, out float width, out float height)
    {
        center = t.position; rotation = Quaternion.identity; width = height = 0f;
        MeshFilter filter = t.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null || !filter.sharedMesh.isReadable) return false;
        Vector3[] verts = filter.sharedMesh.vertices;
        int[] tris = filter.sharedMesh.triangles;
        Vector3[] world = verts.Select(v => t.TransformPoint(v)).ToArray();

        float bestArea = 0f;
        Vector3 bestNormal = Vector3.zero;
        foreach (Vector3 axis in new[] { t.right, -t.right, t.forward, -t.forward })
        {
            if (Vector3.Dot(axis.normalized, toward) < 0.3f) continue;
            float area = 0f;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 cross = Vector3.Cross(world[tris[i + 1]] - world[tris[i]], world[tris[i + 2]] - world[tris[i]]);
                if (Vector3.Dot(cross.normalized, axis.normalized) > 0.85f) area += cross.magnitude * 0.5f;
            }
            if (area > bestArea) { bestArea = area; bestNormal = axis.normalized; }
        }
        if (bestArea < 0.01f) return false;

        // The face is the plane that holds the most front-facing area (bucketed to `planeTolerance`): a bezel, not a knob or a stand.
        float lowest = t.GetComponent<Renderer>() != null ? t.GetComponent<Renderer>().bounds.min.y : float.MinValue;
        Dictionary<int, float> areaByPlane = new Dictionary<int, float>();
        List<(Vector3 a, Vector3 b, Vector3 c, int bucket)> faces = new List<(Vector3, Vector3, Vector3, int)>();
        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = world[tris[i]], b = world[tris[i + 1]], c = world[tris[i + 2]];
            if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < lowest + ignoreBelow) continue; // a stand
            Vector3 cross = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(cross.normalized, bestNormal) <= 0.85f) continue;
            int bucket = Mathf.RoundToInt((Vector3.Dot(a, bestNormal) + Vector3.Dot(b, bestNormal) + Vector3.Dot(c, bestNormal)) / 3f / planeTolerance);
            faces.Add((a, b, c, bucket));
            areaByPlane[bucket] = (areaByPlane.TryGetValue(bucket, out float held) ? held : 0f) + cross.magnitude * 0.5f;
        }
        if (faces.Count == 0) return false;
        int bestBucket = areaByPlane.OrderByDescending(kv => kv.Value).First().Key;
        List<Vector3> points = new List<Vector3>();
        float plane = bestBucket * planeTolerance;
        foreach (var f in faces)
        {
            if (Mathf.Abs(f.bucket - bestBucket) > 1) continue;
            points.Add(f.a); points.Add(f.b); points.Add(f.c);
        }
        rotation = Quaternion.LookRotation(-bestNormal, Vector3.up);
        Quaternion inverse = Quaternion.Inverse(rotation);
        Vector3 anchor = points[0];
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        foreach (Vector3 p in points)
        {
            Vector3 l = inverse * (p - anchor);
            minX = Mathf.Min(minX, l.x); maxX = Mathf.Max(maxX, l.x);
            minY = Mathf.Min(minY, l.y); maxY = Mathf.Max(maxY, l.y);
        }
        Vector3 mid = anchor + rotation * new Vector3(0.5f * (minX + maxX), 0.5f * (minY + maxY), 0f);
        center = mid + bestNormal * (plane - Vector3.Dot(mid, bestNormal));
        width = maxX - minX;
        height = maxY - minY;
        return true;
    }

    /// <summary>
    /// A Quad fitted to the front face of `set`, 1 mm proud, as a child of `parent`. `padSide` is the bezel at each side as a fraction of the face width,
    /// `padTop` / `padBottom` the bezel above / below as fractions of its height (a set has a thicker chin than a brow). Null if no face is found.
    /// </summary>
    private static GameObject ScreenQuad(Transform parent, string name, Transform set, Vector3 toward, float planeTolerance, float ignoreBelow, float padSide, float padTop, float padBottom, Material material)
    {
        if (!FrontFace(set, toward, planeTolerance, ignoreBelow, out Vector3 center, out Quaternion rotation, out float width, out float height)) return null;
        center += rotation * Vector3.up * (height * (padBottom - padTop) * 0.5f);
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(parent, true);
        quad.transform.rotation = rotation;
        float bulge = BulgeInFront(set, rotation, center, width * (1f - 2f * padSide), height * (1f - padTop - padBottom));
        quad.transform.position = center + rotation * Vector3.back * (bulge + 0.002f); // Vector3.back is -Z: toward the viewer; clear of a CRT's curved glass
        quad.transform.localScale = new Vector3(width * (1f - 2f * padSide), height * (1f - padTop - padBottom), 1f);
        MeshRenderer mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        GameObjectUtility.SetStaticEditorFlags(quad, 0);
        return quad;
    }

    /// <summary>How far (m) the mesh of `set` stands in front of the plane through `center` (facing `rotation`'s visible side) inside the w x h rectangle: a CRT's glass bulges.</summary>
    private static float BulgeInFront(Transform set, Quaternion rotation, Vector3 center, float w, float h)
    {
        MeshFilter filter = set.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null) return 0f;
        Quaternion inverse = Quaternion.Inverse(rotation);
        float front = 0f;
        foreach (Vector3 v in filter.sharedMesh.vertices)
        {
            Vector3 l = inverse * (set.TransformPoint(v) - center);
            if (Mathf.Abs(l.x) > w * 0.5f || Mathf.Abs(l.y) > h * 0.5f) continue;
            front = Mathf.Max(front, -l.z); // the visible side is -Z
        }
        return front;
    }

    private static Texture2D TvNoiseTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(TvNoisePath);
        if (existing != null) return existing;
        EnsureFolderPath(System.IO.Path.GetDirectoryName(TvNoisePath).Replace('\\', '/'));
        Texture2D tex = new Texture2D(64, 48, TextureFormat.RGBA32, false);
        System.Random rng = new System.Random(114);
        for (int y = 0; y < 48; y++)
        {
            float band = 0.85f + 0.15f * Mathf.Sin(y * 0.9f); // faint horizontal banding
            for (int x = 0; x < 64; x++)
            {
                float v = Mathf.Clamp01((0.15f + 0.85f * (float)rng.NextDouble()) * band);
                tex.SetPixel(x, y, new Color(v, v, v, 1f));
            }
        }
        tex.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), TvNoisePath), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(TvNoisePath, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(TvNoisePath);
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 64;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TvNoisePath);
    }

    private static Material UnlitMaterial(string name, Color colour, Texture map)
    {
        EnsureFolderPath(MaterialFolder);
        string path = $"{MaterialFolder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetColor("_BaseColor", colour);
        if (map != null) m.SetTexture("_BaseMap", map);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------------------------------------------------------------- R4: the television

    private static string BuildTelevisionScare(AsylumHub hub)
    {
        Bounds room = FloorBox("Kitchen_floor");
        Transform tv = Under("Kitchen", "Television (1)");
        if (tv == null) return "kitchen television not found";
        Vector3 tvAt = tv.GetComponent<Renderer>().bounds.center;
        Material material = UnlitMaterial("Asylum_TvStatic", new Color(0.55f, 0.6f, 0.65f), TvNoiseTexture());

        GameObject go = NewScareHolder(hub, 3, room, out BoxCollider box);
        GameObject screen = ScreenQuad(go.transform, "Screen", tv, Flat(room.center - tvAt), 0.01f, 0f, 0.105f, 0.096f, 0.165f, material);
        if (screen == null) { Object.DestroyImmediate(go); return "no flat front face on the television"; }
        AudioSource hiss = screen.AddComponent<AudioSource>();
        hiss.playOnAwake = false;
        hiss.loop = true;
        hiss.spatialBlend = 1f;
        hiss.minDistance = 1f;
        hiss.maxDistance = 9f;
        hiss.rolloffMode = AudioRolloffMode.Linear;
        hiss.volume = 0.25f;
        screen.GetComponent<MeshRenderer>().enabled = false; // the scare switches it on when the hub is entered on an armed floor

        ScareTelevision scare = go.AddComponent<ScareTelevision>();
        SerializedObject so = new SerializedObject(scare);
        SetScareCommon(so, 3, MaskFloors134, box, screen.transform, HubRoomScare.LookMode.At, 0.85f, 20f, 0f, 1.5f, 0.6f);
        PropObj(so, "screen", screen.GetComponent<MeshRenderer>());
        PropObj(so, "hiss", hiss);
        PropFloat(so, "nearDistance", 2.2f);
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{ScareNames[3]}: static screen {screen.transform.localScale.x:F2} x {screen.transform.localScale.y:F2} at {screen.transform.position:F2}";
    }

    // ---------------------------------------------------------------- R5: the chair that scrapes back

    private static string BuildChairScare(AsylumHub hub)
    {
        Bounds room = FloorBox("VisitRoom_floor");
        Transform[] chairs = hub.dressing.GetComponentsInChildren<Transform>(true).Where(t => t.name == "F101_VisitRoom_ChairWhite").ToArray();
        Transform table = hub.dressing.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "F101_VisitRoom_TableWhite");
        if (chairs.Length == 0 || table == null) return "the visiting room's white chairs / table were not found";

        Transform doorFrame = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Door_Frame (1)" && t.parent != null && t.parent.name == "GuardRoom");
        Vector3 doorway = doorFrame != null ? doorFrame.position : room.center;
        Transform chair = chairs.OrderByDescending(c => Vector3.Distance(RendererBoundsOf(c).center, doorway)).First();
        Bounds cb = RendererBoundsOf(chair);
        Vector3 back = Flat(cb.center - RendererBoundsOf(table).center);

        GameObject go = NewScareHolder(hub, 4, room, out BoxCollider box);
        ScareChairScrape scare = go.AddComponent<ScareChairScrape>();
        SerializedObject so = new SerializedObject(scare);
        SetScareCommon(so, 4, MaskFloors124, box, chair, HubRoomScare.LookMode.Away, 0.6f, 5f, 1.2f, 0f, 1.2f);
        PropObj(so, "chair", chair);
        so.FindProperty("slideLocal").vector3Value = LocalOffset(chair, back, 0.35f);
        PropFloat(so, "turnDegrees", 18f);
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{ScareNames[4]}: chair at {cb.center:F2} slides back along world {back} (away from the table)";
    }

    // ---------------------------------------------------------------- R6: the CCTV

    private static string BuildCctvScare(AsylumHub hub)
    {
        int layer = LayerMask.NameToLayer(ScareCctv.LayerName);
        if (layer < 0) return "no CctvOnly layer";
        Bounds room = FloorBox("GuardRoom_floor");
        Transform monitor = Under("GuardRoom", "PC_Monitor (1)");
        Transform chairOffice = Under("GuardRoom", "ChairOffice");
        Transform door = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Door_V1 (2)");
        Transform body = hub.life.Find("Pursuer") != null ? hub.life.Find("Pursuer").Find("Body_PlayerRobot") : null;
        if (monitor == null || door == null || body == null) return "monitor / guard room door / pursuer body not found";
        Vector3 monitorAt = monitor.GetComponent<Renderer>().bounds.center;
        Vector3 toChair = chairOffice != null ? Flat(chairOffice.GetComponent<Renderer>().bounds.center - monitorAt) : Flat(room.center - monitorAt);

        GameObject go = NewScareHolder(hub, 5, room, out BoxCollider box);
        Material screenMaterial = UnlitMaterial("Asylum_CctvScreen", new Color(0.75f, 0.95f, 0.8f), null);
        GameObject screen = ScreenQuad(go.transform, "CctvScreen", monitor, toChair, 0.01f, 0.07f, 0.093f, 0.078f, 0.13f, screenMaterial);
        if (screen == null) { Object.DestroyImmediate(go); return "no flat front face on the monitor"; }

        // The doorway and the corridor side (the side of the door away from the room).
        float floorY = door.position.y;
        Vector3 out_ = Flat(door.position - room.center);
        out_ = Mathf.Abs(out_.x) > Mathf.Abs(out_.z) ? new Vector3(Mathf.Sign(out_.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(out_.z));
        Vector3 side = Vector3.Cross(Vector3.up, out_);
        Vector3 doorway = new Vector3(door.position.x, floorY, door.position.z);
        // 4 m out from the door and a little to the side with more room, 2.6 m up, looking at the doorway.
        float roomLeft = Physics.Raycast(doorway + out_ * 3f + Vector3.up * 2f, side, out RaycastHit hl, 6f, ~0, QueryTriggerInteraction.Ignore) ? hl.distance : 6f;
        float roomRight = Physics.Raycast(doorway + out_ * 3f + Vector3.up * 2f, -side, out RaycastHit hr, 6f, ~0, QueryTriggerInteraction.Ignore) ? hr.distance : 6f;
        Vector3 camAt = doorway + out_ * 3.8f + (roomLeft >= roomRight ? side : -side) * 1.6f + Vector3.up * 2.6f;
        GameObject camGo = new GameObject("CctvCamera");
        camGo.transform.SetParent(go.transform, true);
        camGo.transform.position = camAt;
        camGo.transform.rotation = Quaternion.LookRotation((doorway + Vector3.up * 1.1f) - camAt, Vector3.up);
        Camera cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 25f;
        cam.cullingMask = ~0; // everything, including CctvOnly
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.03f, 0.025f);
        cam.allowHDR = false;
        cam.allowMSAA = false;
        UniversalAdditionalCameraData data = camGo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.renderShadows = false;

        // The figure: the pursuer's body on the CctvOnly layer, in the doorway on the corridor side, facing into the room. Inactive.
        GameObject figureRoot = new GameObject("CctvFigure");
        figureRoot.transform.SetParent(go.transform, true);
        figureRoot.transform.position = doorway + out_ * 0.15f;
        figureRoot.transform.rotation = Quaternion.LookRotation(-out_, Vector3.up);
        GameObject clone = Object.Instantiate(body.gameObject, figureRoot.transform);
        clone.name = "Body";
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        foreach (Collider c in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = layer;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        }
        figureRoot.layer = layer;

        GameObject lightGo = new GameObject("CctvFill");
        lightGo.transform.SetParent(go.transform, true);
        lightGo.transform.position = doorway + out_ * 1.0f + Vector3.up * 1.7f;
        lightGo.layer = layer;
        Light fill = lightGo.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.range = 4f;
        fill.intensity = 1.5f;
        fill.color = new Color(0.85f, 0.92f, 1f);
        fill.shadows = LightShadows.None;
        fill.cullingMask = 1 << layer;
        fill.enabled = false;
        figureRoot.SetActive(false);

        ScareCctv scare = go.AddComponent<ScareCctv>();
        SerializedObject so = new SerializedObject(scare);
        SetScareCommon(so, 5, MaskFloors245, box, screen.transform, HubRoomScare.LookMode.At, 0.8f, 2.5f, 0f, 1.0f, 0.6f);
        PropObj(so, "screen", screen.GetComponent<MeshRenderer>());
        PropObj(so, "cctvCamera", cam);
        PropObj(so, "figure", figureRoot);
        PropObj(so, "fill", fill);
        PropObj(so, "staticTexture", TvNoiseTexture());
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{ScareNames[5]}: monitor screen {screen.transform.localScale.x:F2} x {screen.transform.localScale.y:F2} at {screen.transform.position:F2}, camera at {camAt:F2}, figure at {figureRoot.transform.position:F2} (layer {layer})";
    }

    // ---------------------------------------------------------------- R7: the cell door

    private static string BuildCellDoorScare(AsylumHub hub)
    {
        Bounds room = FloorBox("SeclusionRoom_floor");
        Transform leaf = Under("Door_V3 (1)", "Door");
        Transform mattress = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "F101_SeclusionRoomA_Mattress")
            ?? hub.dressing.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "F101_SeclusionRoomA_Mattress");
        if (leaf == null || leaf.parent == null || leaf.parent.name != "DoorHinge" || mattress == null) return "cell A door hinge / mattress not found";
        Transform hinge = leaf.parent;
        float yaw = YawOf(hinge);
        if (Mathf.Abs(yaw) < 1f) return "cell A door was not opened";

        // The cell: found from the mattress by raycasting to its four walls.
        Bounds mb = RendererBoundsOf(mattress);
        Vector3 from = new Vector3(mb.center.x, mb.max.y + 1.0f, mb.center.z);
        float west = Ray(from, Vector3.left), east = Ray(from, Vector3.right), south = Ray(from, Vector3.back), north = Ray(from, Vector3.forward);
        float x0 = from.x - west + 0.05f, x1 = from.x + east - 0.05f, z0 = from.z - south + 0.05f, z1 = from.z + north - 0.05f;
        float floorY = room.min.y + 0.15f;
        Bounds cellBounds = new Bounds(new Vector3(0.5f * (x0 + x1), floorY + 1.4f, 0.5f * (z0 + z1)), new Vector3(x1 - x0, 3.2f, z1 - z0));

        GameObject go = NewScareHolder(hub, 6, room, out BoxCollider box);
        GameObject cellGo = new GameObject("Cell");
        cellGo.transform.SetParent(go.transform, false);
        cellGo.transform.position = cellBounds.center;
        BoxCollider cellBox = cellGo.AddComponent<BoxCollider>();
        cellBox.isTrigger = true;
        cellBox.size = cellBounds.size;

        // The padded wall opposite the door: the door is on one X wall, so the whisper comes from the other.
        Vector3 doorAt = RendererBoundsOf(leaf).center;
        bool doorOnWest = Mathf.Abs(doorAt.x - x0) < Mathf.Abs(doorAt.x - x1);
        Transform whisper = MarkAt(go.transform, "WhisperFrom", new Vector3(doorOnWest ? x1 : x0, floorY + 1.4f, from.z));

        ScareCellDoor scare = go.AddComponent<ScareCellDoor>();
        SerializedObject so = new SerializedObject(scare);
        SetScareCommon(so, 6, MaskFloors135, box, null, HubRoomScare.LookMode.Any, 0.6f, 6f, 0f, 0f, 0.6f);
        PropObj(so, "hinge", hinge);
        PropFloat(so, "openYaw", yaw);
        PropObj(so, "cell", cellBox);
        PropFloat(so, "leafWidth", 1.06f);
        PropObj(so, "whisperFrom", whisper);
        so.ApplyModifiedPropertiesWithoutUndo();
        return $"{ScareNames[6]}: cell door left open {yaw:F0} degrees, cell x {x0:F2}..{x1:F2} z {z0:F2}..{z1:F2}";
    }

    private static float Ray(Vector3 from, Vector3 dir)
    {
        return Physics.Raycast(from, dir, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 3f;
    }
}
