using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F102: the "Hospital orderly" skin of the hunter's Timmy robot. Paints new albedo / metallic-smoothness / emission / normal
/// textures from the robot's own UV layout (Timmytexture_Albedo.png tells which island is shell, limb panel, screen or eye) and
/// writes one URP Lit material, all under Assets/SourceFiles/Characters/HunterOrderly/. The Starter Assets originals are only read -
/// the player, Stan and the hub pursuer keep theirs.
///
/// The mesh is rasterised into UV space, so every texel knows its position, normal and bone region on the robot: that is what
/// puts the dried blood on the hands and forearms, the stencilled ORDERLY 01 and the faded red cross on the chest, and keeps the
/// grime, rust and chips continuous across seams (3D noise on the mesh-space position). UV-island edges (the robot's joints and
/// panel borders) are where rust bleeds and the enamel chips. The limbs share one UV island between left and right, so nothing
/// position-dependent is painted on a limb except what is symmetric (blood by bone weight, joint rust).
///
/// Idempotent: <see cref="EnsureSkin"/> only paints when the material or a texture is missing; the menu item repaints.
/// </summary>
public static partial class HunterSkinBuilder
{
    public const string MaterialName = "Orderly";
    private const string Folder = "Assets/SourceFiles/Characters/HunterOrderly";
    private const string RobotPrefabPath = "Assets/Prefabs/PlayerRobot.prefab";
    private const string SourceAlbedoPath = "Assets/SourceFiles/TimmyRobot/Textures/Timmytexture_Albedo.png";
    private const int Size = 1024;
    private static float _chestCenterZ;

    private const byte ClsNone = 0, ClsShell = 1, ClsLimb = 2, ClsScreen = 3, ClsEye = 4, ClsMouth = 5;

    [MenuItem("LIGHTS OUT/Build/Hunter Skin (repaint)", priority = 106)]
    public static void Repaint()
    {
        Material m = Generate();
        if (m != null) Debug.Log("HunterSkinBuilder: repainted " + Folder + " - run Build > Hunter Body to put it on the scene hunter if it is not there yet.", m);
    }

    /// <summary>The Orderly material, painting it first if it (or a texture) is missing.</summary>
    public static Material EnsureSkin()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(Folder + "/" + MaterialName + ".mat");
        bool complete = m != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Orderly_Albedo.png") != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Orderly_MetalSmooth.png") != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Orderly_Emission.png") != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Orderly_Normal.png") != null;
        return complete ? m : Generate();
    }

    // ---------------------------------------------------------------- generation

    private class Buffers
    {
        public byte[] cls = new byte[Size * Size];
        public bool[] covered = new bool[Size * Size];
        public Vector3[] pos = new Vector3[Size * Size];
        public Vector3[] nrm = new Vector3[Size * Size];
        public float[] blood = new float[Size * Size];
        public float[] torso = new float[Size * Size];
        public float[] foot = new float[Size * Size];
        public float[] dist = new float[Size * Size];
    }

    /// <summary>Paints one texel; <see cref="Paint"/> is the Orderly, HunterSkinBuilder.Shopkeeper.cs has the shop's brass.</summary>
    private delegate void Painter(Buffers b, int i, int x, int y, Vector4 chest, out Color albedo, out Color metal, out Color emission, out float height);

    private static Material Generate() => GenerateSkin(Folder, MaterialName, Paint, 1.4f);

    /// <summary>Rasterises the robot into UV space, runs <paramref name="paint"/> on every texel and writes {name}_Albedo /
    /// _MetalSmooth / _Emission / _Normal plus {name}.mat (URP Lit) into <paramref name="folder"/>.</summary>
    private static Material GenerateSkin(string folder, string name, Painter paint, float emissionIntensity)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
        SkinnedMeshRenderer smr = prefab != null ? prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        if (smr == null || smr.sharedMesh == null)
        {
            Debug.LogError("HunterSkinBuilder: the robot mesh was not found in " + RobotPrefabPath);
            return null;
        }
        Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!source.LoadImage(File.ReadAllBytes(SourceAlbedoPath)))
        {
            Debug.LogError("HunterSkinBuilder: could not read " + SourceAlbedoPath);
            return null;
        }
        if (source.width != Size || source.height != Size)
        {
            Debug.LogError("HunterSkinBuilder: expected a " + Size + " px source albedo, got " + source.width + "x" + source.height);
            return null;
        }
        Color32[] src = source.GetPixels32();
        Object.DestroyImmediate(source);

        Buffers b = Rasterise(smr, src, out Vector4 chest);
        ComputeEdgeDistance(b);

        Color[] albedo = new Color[Size * Size];
        Color[] metal = new Color[Size * Size];   // R metallic, A smoothness
        Color[] emission = new Color[Size * Size];
        float[] height = new float[Size * Size];

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x;
                paint(b, i, x, y, chest, out albedo[i], out metal[i], out emission[i], out height[i]);
            }
        }

        Dilate(b, albedo, metal, emission, height);
        Color[] normal = HeightToNormal(height);

        EnsureFolderPath(folder);

        Texture2D tAlbedo = WriteTexture(folder, name + "_Albedo", albedo, true, false);
        Texture2D tMetal = WriteTexture(folder, name + "_MetalSmooth", metal, false, false);
        Texture2D tEmission = WriteTexture(folder, name + "_Emission", emission, true, false);
        Texture2D tNormal = WriteTexture(folder, name + "_Normal", normal, false, true);

        string matPath = folder + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetTexture("_BaseMap", tAlbedo);
        mat.SetTexture("_MainTex", tAlbedo);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetTexture("_MetallicGlossMap", tMetal);
        mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        mat.SetFloat("_Metallic", 1f);
        mat.SetFloat("_Smoothness", 1f);
        mat.SetFloat("_SmoothnessTextureChannel", 0f); // metallic map alpha
        mat.SetTexture("_BumpMap", tNormal);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetFloat("_BumpScale", 1f);
        mat.SetTexture("_EmissionMap", tEmission);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(emissionIntensity, emissionIntensity, emissionIntensity, 1f));
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return mat;
    }

    // ---------------------------------------------------------------- mesh -> UV space

    private static Buffers Rasterise(SkinnedMeshRenderer smr, Color32[] src, out Vector4 chest)
    {
        Mesh mesh = smr.sharedMesh;
        Vector3[] verts = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uv = mesh.uv;
        int[] tris = mesh.triangles;
        BoneWeight[] weights = mesh.boneWeights;
        Transform[] bones = smr.bones;

        float[] vBlood = new float[verts.Length], vTorso = new float[verts.Length], vFoot = new float[verts.Length];
        float minX = 99f, maxX = -99f, minY = 99f, maxY = -99f, minZ = 99f, maxZ = -99f;
        for (int i = 0; i < verts.Length; i++)
        {
            string bone = bones[weights[i].boneIndex0].name;
            if (bone.Contains("Forearm")) vBlood[i] = 0.85f;
            else if (bone.Contains("Hand") || bone.Contains("Thumb") || bone.Contains("Index") || bone.Contains("Middle") || bone.Contains("Pinky")) vBlood[i] = 1f;
            else if (bone.EndsWith("Arm")) vBlood[i] = 0.2f;
            bool torso = bone == "Spine1" || bone == "Spine2" || bone == "Hips";
            if (torso) vTorso[i] = 1f;
            if (bone.Contains("Foot") || bone.Contains("Toes")) vFoot[i] = 1f;
            if (torso)
            {
                minX = Mathf.Min(minX, verts[i].x); maxX = Mathf.Max(maxX, verts[i].x);
                minY = Mathf.Min(minY, verts[i].y); maxY = Mathf.Max(maxY, verts[i].y);
                minZ = Mathf.Min(minZ, verts[i].z); maxZ = Mathf.Max(maxZ, verts[i].z);
            }
        }
        chest = new Vector4(minX, minY, maxX, maxY);
        _chestCenterZ = (minZ + maxZ) * 0.5f;
        Debug.Log($"HunterSkinBuilder: chest front plane x {minX:F3}..{maxX:F3}, y {minY:F3}..{maxY:F3} (mesh units).");

        Buffers buf = new Buffers();
        for (int t = 0; t < tris.Length; t += 3)
        {
            int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
            Vector2 a = uv[ia] * Size, bb = uv[ib] * Size, c = uv[ic] * Size;
            float area = (bb.x - a.x) * (c.y - a.y) - (c.x - a.x) * (bb.y - a.y);
            if (Mathf.Abs(area) < 1e-4f) continue;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(bb.x, c.x)) - 1f));
            int x1 = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(bb.x, c.x)) + 1f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(bb.y, c.y)) - 1f));
            int y1 = Mathf.Min(Size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(bb.y, c.y)) + 1f));
            if (x1 - x0 > Size / 2 || y1 - y0 > Size / 2) continue; // wrapped UVs
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float px = x + 0.5f, py = y + 0.5f;
                    float w1 = ((px - a.x) * (c.y - a.y) - (c.x - a.x) * (py - a.y)) / area;
                    float w2 = ((bb.x - a.x) * (py - a.y) - (px - a.x) * (bb.y - a.y)) / area;
                    float w0 = 1f - w1 - w2;
                    const float tol = -0.03f;
                    if (w0 < tol || w1 < tol || w2 < tol) continue;
                    int idx = y * Size + x;
                    buf.covered[idx] = true;
                    buf.pos[idx] = verts[ia] * w0 + verts[ib] * w1 + verts[ic] * w2;
                    buf.nrm[idx] = (normals[ia] * w0 + normals[ib] * w1 + normals[ic] * w2).normalized;
                    buf.blood[idx] = vBlood[ia] * w0 + vBlood[ib] * w1 + vBlood[ic] * w2;
                    buf.torso[idx] = vTorso[ia] * w0 + vTorso[ib] * w1 + vTorso[ic] * w2;
                    buf.foot[idx] = vFoot[ia] * w0 + vFoot[ib] * w1 + vFoot[ic] * w2;
                    buf.cls[idx] = Classify(src[idx], x, y);
                }
            }
        }
        return buf;
    }

    /// <summary>Which kind of surface a texel of the original texture is: its colour plus its place in the sheet.</summary>
    private static byte Classify(Color32 c, int x, int y)
    {
        float lum = (c.r + c.g + c.b) / (3f * 255f);
        if (lum > 0.93f) return y > 0.59f * Size ? ClsMouth : ClsEye; // the white island: a mouth bar above, an eye circle below
        if (lum < 0.55f)
        {
            // The head's screen island sits in the middle of the sheet; the limb panel island is the top-right one.
            bool screen = x > 0.54f * Size && x < 0.74f * Size && y > 0.45f * Size && y < 0.75f * Size;
            return screen ? ClsScreen : ClsLimb;
        }
        return ClsShell;
    }

    /// <summary>Distance (texels) from each covered texel to the nearest island edge or surface-class change.</summary>
    private static void ComputeEdgeDistance(Buffers b)
    {
        float[] d = b.dist;
        const float big = 1000f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x;
                if (!b.covered[i]) { d[i] = 0f; continue; }
                bool edge = false;
                for (int k = 0; k < 4 && !edge; k++)
                {
                    int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0), ny = y + (k == 2 ? -1 : k == 3 ? 1 : 0);
                    if (nx < 0 || ny < 0 || nx >= Size || ny >= Size) { edge = true; break; }
                    int j = ny * Size + nx;
                    if (!b.covered[j] || b.cls[j] != b.cls[i]) edge = true;
                }
                d[i] = edge ? 0f : big;
            }
        }
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int i = y * Size + x; float v = d[i];
                if (x > 0) v = Mathf.Min(v, d[i - 1] + 1f);
                if (y > 0) v = Mathf.Min(v, d[i - Size] + 1f);
                if (x > 0 && y > 0) v = Mathf.Min(v, d[i - Size - 1] + 1.414f);
                if (x < Size - 1 && y > 0) v = Mathf.Min(v, d[i - Size + 1] + 1.414f);
                d[i] = v;
            }
        for (int y = Size - 1; y >= 0; y--)
            for (int x = Size - 1; x >= 0; x--)
            {
                int i = y * Size + x; float v = d[i];
                if (x < Size - 1) v = Mathf.Min(v, d[i + 1] + 1f);
                if (y < Size - 1) v = Mathf.Min(v, d[i + Size] + 1f);
                if (x < Size - 1 && y < Size - 1) v = Mathf.Min(v, d[i + Size + 1] + 1.414f);
                if (x > 0 && y < Size - 1) v = Mathf.Min(v, d[i + Size - 1] + 1.414f);
                d[i] = v;
            }
    }

    // ---------------------------------------------------------------- painting

    private static readonly Color Bone = new Color(0.73f, 0.71f, 0.64f);
    private static readonly Color LimbBone = new Color(0.54f, 0.52f, 0.46f);
    private static readonly Color Steel = new Color(0.11f, 0.11f, 0.12f);
    private static readonly Color Rust = new Color(0.30f, 0.13f, 0.06f);
    private static readonly Color Blood = new Color(0.20f, 0.025f, 0.02f);
    private static readonly Color Ink = new Color(0.06f, 0.06f, 0.07f);
    private static readonly Color Cross = new Color(0.46f, 0.05f, 0.04f);

    private static void Paint(Buffers b, int i, int x, int y, Vector4 chest, out Color albedo, out Color metal, out Color emission, out float height)
    {
        emission = Color.black;
        height = 0f;
        byte cls = b.covered[i] ? b.cls[i] : ClsShell;
        Vector3 m = b.covered[i] ? b.pos[i] / 0.3f : new Vector3(x * 0.01f, y * 0.01f, 3.7f); // ~metres on the robot
        Vector3 n = b.covered[i] ? b.nrm[i] : Vector3.forward;
        float d = b.covered[i] ? b.dist[i] : 0f;
        float edge = Mathf.Exp(-d / 9f);
        float edgeWide = Mathf.Exp(-d / 15f);

        if (cls == ClsScreen)
        {
            PaintScreen(x, y, d, out albedo, out metal, out height);
            return;
        }
        if (cls == ClsMouth)
        {
            albedo = new Color(0.02f, 0.0f, 0.0f);
            metal = new Color(0f, 0f, 0f, 0.15f);
            emission = new Color(0.22f, 0.012f, 0.008f) * (0.5f + 0.5f * Fbm(new Vector3(x * 0.2f, y * 0.2f, 4f), 2));
            return;
        }
        if (cls == ClsEye)
        {
            albedo = new Color(0.03f, 0.0f, 0.0f);
            metal = new Color(0f, 0f, 0f, 0.9f);
            float glow = 0.75f + 0.25f * Fbm(new Vector3(x * 0.05f, y * 0.05f, 9f), 2);
            emission = new Color(1f, 0.05f, 0.03f) * glow;
            return;
        }

        bool limb = cls == ClsLimb;
        Color col = limb ? LimbBone : Bone;
        float mottle = Fbm(m * 3f, 3);
        col *= Mathf.Lerp(0.88f, 1.08f, mottle);
        col = Color.Lerp(col, col * new Color(1.03f, 0.99f, 0.88f), Fbm(m * 7f + new Vector3(13f, 5f, 2f), 2));
        float metallic = 0.08f, smooth = 0.28f;

        // grime: crevices, undersides, the dirt a robot picks up near the floor, vertical run-off
        float low = 1f - Smooth(0f, 0.35f, m.y);
        float under = Mathf.Clamp01(-n.y);
        float dirtNoise = Fbm(m * 20f, 3);
        float streak = Fbm(new Vector3(m.x * 70f, m.y * 5f, m.z * 70f), 3);
        float grime = Mathf.Clamp01(0.85f * edge + 0.45f * under + 0.5f * low * dirtNoise + 0.35f * Smooth(0.58f, 0.8f, streak) * (0.4f + edgeWide));
        col = Color.Lerp(col, new Color(0.07f, 0.055f, 0.035f), Mathf.Clamp01(grime * (0.45f + 0.8f * dirtNoise)) * 0.85f);
        smooth = Mathf.Lerp(smooth, 0.12f, grime);

        // dried blood by bone region (hands, forearms) plus a few spatters on the torso
        float bloodWeight = b.covered[i] ? b.blood[i] : 0f;
        float bn = Fbm(m * 14f + new Vector3(3f, 8f, 1f), 3);
        float bloodAmount = bloodWeight * Smooth(0.30f, 0.75f, bn);
        if (b.covered[i] && b.torso[i] > 0.5f) bloodAmount = Mathf.Max(bloodAmount, 0.54f * Smooth(0.80f, 0.83f, Fbm(m * 42f, 2)));
        float bloodMask = Smooth(0.2f, 0.34f, bloodAmount);
        // runs: darker, glossier where the blood has pooled
        Color bloodCol = Color.Lerp(Blood, new Color(0.34f, 0.05f, 0.03f), Mathf.Clamp01(bn * 1.4f - 0.2f));
        col = Color.Lerp(col, bloodCol, bloodMask * 0.92f);
        smooth = Mathf.Lerp(smooth, 0.38f, bloodMask);
        metallic = Mathf.Lerp(metallic, 0f, bloodMask);

        // rust bleeding from the joints and panel edges, run downwards by the stretched noise
        float rustField = edgeWide * (0.45f + 0.9f * Fbm(new Vector3(m.x * 14f, m.y * 5f, m.z * 14f) + new Vector3(0f, 4f, 0f), 3));
        float rustMask = Smooth(0.5f, 0.8f, rustField);
        Color rustCol = Rust * Mathf.Lerp(0.6f, 1.15f, Fbm(m * 30f, 2));
        col = Color.Lerp(col, rustCol, rustMask * 0.65f * (1f - bloodMask));
        smooth = Mathf.Lerp(smooth, 0.1f, rustMask);
        metallic = Mathf.Lerp(metallic, 0.05f, rustMask);

        // chips: enamel gone, bare dark steel with a rust halo, mostly along edges
        float chipNoise = Fbm(m * 40f + new Vector3(7f, 1f, 4f), 3);
        float threshold = Mathf.Lerp(0.86f, 0.78f, edge);
        float chip = Smooth(threshold, threshold + 0.035f, chipNoise);
        float halo = Smooth(threshold - 0.08f, threshold, chipNoise) * (1f - chip);
        col = Color.Lerp(col, Rust * 0.9f, halo * 0.7f);
        col = Color.Lerp(col, Steel * Mathf.Lerp(0.8f, 1.5f, Fbm(m * 60f, 2)), chip);
        metallic = Mathf.Lerp(metallic, 0.65f, chip);
        smooth = Mathf.Lerp(smooth, 0.38f, chip);
        height = -0.9f * chip - 0.25f * halo + 0.06f * Fbm(m * 90f, 2);

        albedo = new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b), 1f);
        metal = new Color(Mathf.Clamp01(metallic), 0f, 0f, Mathf.Clamp01(smooth));
    }

    private static void PaintScreen(int x, int y, float d, out Color albedo, out Color metal, out float height)
    {
        float u = x / (float)Size, v = y / (float)Size;
        Color col = new Color(0.014f, 0.02f, 0.024f);
        // a faint tube-glow gradient and smeared dirt on the glass
        float smear = Fbm(new Vector3(u * 18f, v * 40f, 2f), 3);
        col = Color.Lerp(col, new Color(0.075f, 0.08f, 0.07f), Smooth(0.5f, 0.85f, smear) * 0.6f);
        float rim = Mathf.Exp(-d / 6f);
        col = Color.Lerp(col, new Color(0.05f, 0.04f, 0.03f), rim * 0.7f);
        float smooth = 0.86f - 0.35f * Smooth(0.55f, 0.9f, smear) - 0.3f * rim;

        // cracks radiate from an impact point left of centre (Worley edges, fading out with distance)
        Vector2 impact = new Vector2(0.60f, 0.58f);
        float r = Vector2.Distance(new Vector2(u, v), impact);
        float reach = Mathf.Exp(-r * r / 0.0018f);
        float crack = 0f;
        for (int o = 0; o < 2; o++)
        {
            float cellScale = o == 0 ? 11f : 22f;
            crack = Mathf.Max(crack, (1f - Smooth(0f, o == 0 ? 0.05f : 0.06f, Worley(u * cellScale, v * cellScale))) * (o == 0 ? 1f : 0.4f));
        }
        crack *= Mathf.Clamp01(reach * 1.6f);
        // two long splits across the glass
        float split1 = 1f - Smooth(0f, 0.0035f, Mathf.Abs((v - 0.58f) - 0.6f * (u - 0.60f) + 0.015f * Mathf.Sin(u * 90f)));
        float split2 = 1f - Smooth(0f, 0.0030f, Mathf.Abs((v - 0.60f) + 1.8f * (u - 0.66f) + 0.01f * Mathf.Sin(v * 110f)));
        crack = Mathf.Clamp01(Mathf.Max(crack, Mathf.Max(split1, split2) * 0.9f));
        col = Color.Lerp(col, new Color(0.50f, 0.55f, 0.56f), crack * 0.55f);
        smooth = Mathf.Lerp(smooth, 0.3f, crack);
        height = -crack * 0.7f;
        albedo = col;
        metal = new Color(0.1f, 0f, 0f, Mathf.Clamp01(smooth));
    }


    private static float TextCoverage(float lx, float ly, string text, float unit)
    {
        float totalW = (text.Length * 6f - 1f) * unit;
        float u = (lx + totalW * 0.5f) / unit;      // glyph columns
        float v = (ly + 3.5f * unit) / unit;        // 0..7 rows, bottom up
        if (u < 0f || u >= text.Length * 6f - 1f || v < 0f || v >= 7f) return 0f;
        int ch = Mathf.FloorToInt(u / 6f);
        float cu = u - ch * 6f;
        if (cu >= 5f) return 0f;
        string[] g = Glyph(text[ch]);
        if (g == null) return 0f;
        int col = Mathf.FloorToInt(cu), row = 6 - Mathf.FloorToInt(v);
        return g[row][col] == '#' ? 1f : 0f;
    }

    private static string[] Glyph(char c)
    {
        switch (c)
        {
            case 'O': return new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." };
            case 'R': return new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" };
            case 'D': return new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." };
            case 'E': return new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" };
            case 'L': return new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" };
            case 'Y': return new[] { "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#.." };
            case '0': return new[] { ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###." };
            case '1': return new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." };
            default: return null;
        }
    }

    // ---------------------------------------------------------------- chest stencil (a curved decal, not part of the texture)

    private const string StencilTexturePath = Folder + "/Orderly_Stencil.png";
    private const string StencilMaterialPath = Folder + "/Orderly_Stencil.mat";
    private const string StencilMeshPath = Folder + "/Orderly_ChestStencil.asset";
    public const string StencilChildName = "ChestStencil";

    /// <summary>
    /// The ORDERLY 01 stencil and the faded red cross. They cannot live in the main texture: the robot's torso UVs are mirrored (the
    /// left and right halves share texels), so any lettering painted there reads backwards on one side. Instead a thin curved strip
    /// that hugs the front of the torso (rays cast at the robot's own torso triangles, lifted 3 mm) carries an alpha-clipped stencil
    /// texture and is parented to the bone that skins most of the torso, so it moves with the body. Mesh, texture and material are
    /// assets under the skin folder; the child is replaced on every call.
    /// </summary>
    public static void AttachChestStencil(GameObject body)
    {
        SkinnedMeshRenderer smr = body.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr == null || smr.sharedMesh == null || smr.bones == null || smr.bones.Length == 0)
        {
            Debug.LogWarning("HunterSkinBuilder: no skinned mesh on the hunter body, chest stencil skipped.", body);
            return;
        }
        Mesh src = smr.sharedMesh;
        Vector3[] verts = src.vertices;
        Vector3[] normals = src.normals;
        int[] tris = src.triangles;
        BoneWeight[] weights = src.boneWeights;
        Matrix4x4[] bindposes = src.bindposes;
        Transform[] bones = smr.bones;

        bool[] torso = new bool[verts.Length];
        Dictionary<int, int> boneCounts = new Dictionary<int, int>();
        float minX = 99f, maxX = -99f, minY = 99f, maxY = -99f, minZ = 99f, maxZ = -99f;
        for (int i = 0; i < verts.Length; i++)
        {
            string bone = bones[weights[i].boneIndex0].name;
            if (bone != "Spine1" && bone != "Spine2" && bone != "Hips") continue;
            torso[i] = true;
            int bi = weights[i].boneIndex0;
            boneCounts[bi] = (boneCounts.TryGetValue(bi, out int c) ? c : 0) + 1;
            minX = Mathf.Min(minX, verts[i].x); maxX = Mathf.Max(maxX, verts[i].x);
            minY = Mathf.Min(minY, verts[i].y); maxY = Mathf.Max(maxY, verts[i].y);
            minZ = Mathf.Min(minZ, verts[i].z); maxZ = Mathf.Max(maxZ, verts[i].z);
        }
        if (boneCounts.Count == 0) return;
        int boneIndex = -1, best = 0;
        foreach (KeyValuePair<int, int> kv in boneCounts) if (kv.Value > best) { best = kv.Value; boneIndex = kv.Key; }
        Transform parentBone = bones[boneIndex];

        float width = maxX - minX, height = maxY - minY;
        Vector3 axis = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
        float radius = width * 0.5f;
        const float halfAngle = 0.95f;      // radians either side of straight ahead (about 54 degrees)
        float arc = 2f * halfAngle * radius;
        float y0 = minY + height * 0.10f, y1 = minY + height * 0.92f;
        const float lift = 0.0005f;         // mesh units; ~3 mm on the robot

        const int nx = 32, ny = 18;
        Vector3[] pos = new Vector3[(nx + 1) * (ny + 1)];
        Vector3[] nrm = new Vector3[pos.Length];
        Vector2[] uv = new Vector2[pos.Length];
        Matrix4x4 bind = bindposes[boneIndex];
        for (int j = 0; j <= ny; j++)
        {
            float y = Mathf.Lerp(y0, y1, j / (float)ny);
            for (int i = 0; i <= nx; i++)
            {
                float phi = Mathf.Lerp(-halfAngle, halfAngle, i / (float)nx);
                Vector3 dir = new Vector3(Mathf.Sin(phi), 0f, Mathf.Cos(phi));
                Vector3 origin = new Vector3(axis.x, y, axis.z);
                if (!RayTorso(origin, dir, verts, normals, tris, torso, out float hitT, out Vector3 hitN))
                {
                    hitT = radius; hitN = dir; // no hit (should not happen on a closed torso): fall back to the bounding cylinder
                }
                Vector3 p = origin + dir * hitT + hitN * lift;
                int k = j * (nx + 1) + i;
                pos[k] = bind.MultiplyPoint3x4(p);          // mesh space -> the bone's own space at bind pose
                nrm[k] = bind.MultiplyVector(hitN).normalized;
                uv[k] = new Vector2(1f - i / (float)nx, j / (float)ny); // +x is on the viewer's left from the front, so u runs the other way
            }
        }
        int[] idx = new int[nx * ny * 6];
        int n = 0;
        for (int j = 0; j < ny; j++)
        {
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                idx[n++] = a; idx[n++] = c; idx[n++] = b;
                idx[n++] = b; idx[n++] = c; idx[n++] = d;
            }
        }

        EnsureFolderPath(Folder);
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(StencilMeshPath);
        if (mesh == null)
        {
            mesh = new Mesh { name = "Orderly_ChestStencil" };
            AssetDatabase.CreateAsset(mesh, StencilMeshPath);
        }
        mesh.Clear();
        mesh.vertices = pos;
        mesh.normals = nrm;
        mesh.uv = uv;
        mesh.triangles = idx;
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        EditorUtility.SetDirty(mesh);

        Material mat = EnsureStencilMaterial(arc / width, (y1 - y0) / width);

        Transform old = body.transform.Find(StencilChildName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        foreach (Transform bone in bones)
        {
            Transform stale = bone != null ? bone.Find(StencilChildName) : null;
            if (stale != null) Object.DestroyImmediate(stale.gameObject);
        }
        GameObject go = new GameObject(StencilChildName);
        go.transform.SetParent(parentBone, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = true;
        Debug.Log($"HunterSkinBuilder: chest stencil attached to {parentBone.name} (torso {width:F3} x {height:F3} mesh units, strip {arc / width:F2} x {(y1 - y0) / width:F2} widths).", go);
    }

    private static bool RayTorso(Vector3 origin, Vector3 dir, Vector3[] verts, Vector3[] normals, int[] tris, bool[] torso, out float hitT, out Vector3 hitNormal)
    {
        hitT = float.MaxValue;
        hitNormal = Vector3.forward;
        bool hit = false;
        for (int t = 0; t < tris.Length; t += 3)
        {
            int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
            if (!torso[ia] || !torso[ib] || !torso[ic]) continue;
            Vector3 e1 = verts[ib] - verts[ia], e2 = verts[ic] - verts[ia];
            Vector3 pv = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, pv);
            if (Mathf.Abs(det) < 1e-12f) continue;
            float inv = 1f / det;
            Vector3 tv = origin - verts[ia];
            float u = Vector3.Dot(tv, pv) * inv;
            if (u < 0f || u > 1f) continue;
            Vector3 qv = Vector3.Cross(tv, e1);
            float v = Vector3.Dot(dir, qv) * inv;
            if (v < 0f || u + v > 1f) continue;
            float dist = Vector3.Dot(e2, qv) * inv;
            if (dist <= 0f || dist >= hitT) continue;
            hitT = dist;
            hitNormal = (normals[ia] * (1f - u - v) + normals[ib] * u + normals[ic] * v).normalized;
            hit = true;
        }
        return hit;
    }

    /// <summary>The stencil texture (alpha = paint) laid out for a strip `aspect` widths wide and `heightUnits` widths tall, and the material around it.</summary>
    private static Material EnsureStencilMaterial(float aspect, float heightUnits)
    {
        const int texW = 768;
        int texH = Mathf.Max(64, Mathf.RoundToInt(texW * heightUnits / aspect));
        Color[] pixels = new Color[texW * texH];
        // everything below is in "torso widths": the strip spans aspect x heightUnits of them
        for (int py = 0; py < texH; py++)
        {
            for (int px = 0; px < texW; px++)
            {
                float coverage = 0f;
                bool isCross = false;
                const int ss = 3; // 3x3 supersampling keeps the 5x7 glyph edges clean
                for (int sy = 0; sy < ss; sy++)
                {
                    for (int sx = 0; sx < ss; sx++)
                    {
                        float lx = ((px + (sx + 0.5f) / ss) / texW - 0.5f) * aspect;
                        float ly = (py + (sy + 0.5f) / ss) / texH * heightUnits;
                        float c = StencilAt(lx, ly, heightUnits, out bool cross);
                        coverage += c;
                        if (c > 0f && cross) isCross = true;
                    }
                }
                coverage /= ss * ss;
                // worn: the paint is patchy and flaked, more so on the red cross
                float wear = Smooth(0.28f, 0.6f, Fbm(new Vector3(px * 0.045f, py * 0.045f, isCross ? 3f : 8f), 3) + 0.2f * Fbm(new Vector3(px * 0.2f, py * 0.2f, 1f), 2));
                float alpha = coverage * Mathf.Lerp(0.55f, 1f, wear);
                Color paint = isCross ? Cross * Mathf.Lerp(0.8f, 1.1f, wear) : Ink;
                pixels[py * texW + px] = new Color(paint.r, paint.g, paint.b, alpha);
            }
        }

        Texture2D t = new Texture2D(texW, texH, TextureFormat.RGBA32, false, false);
        t.SetPixels(pixels);
        t.Apply();
        File.WriteAllBytes(StencilTexturePath, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(StencilTexturePath, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(StencilTexturePath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(StencilTexturePath);

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(StencilMaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Orderly_Stencil" };
            AssetDatabase.CreateAsset(mat, StencilMaterialPath);
        }
        mat.SetTexture("_BaseMap", tex);
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 0f);          // opaque queue, alpha-clipped: no sorting against the body, nothing to blend
        mat.SetFloat("_AlphaClip", 1f);
        mat.SetFloat("_Cutoff", 0.4f);
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.SetFloat("_Cull", 0f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", 0.3f);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>The stencil artwork at (lx, ly): lx in torso widths from the strip's centre, ly in torso widths up from its bottom.</summary>
    private static float StencilAt(float lx, float ly, float heightUnits, out bool isCross)
    {
        isCross = false;
        // red cross, upper chest
        const float arm = 0.05f, halfLen = 0.17f;
        float crossY = 0.72f * heightUnits;
        float dx = Mathf.Abs(lx), dy = Mathf.Abs(ly - crossY);
        if ((dx < arm && dy < halfLen) || (dy < arm && dx < halfLen)) { isCross = true; return 1f; }

        // ORDERLY on one line, 01 below it, 5x7 glyphs
        const string line1 = "ORDERLY", line2 = "01";
        float unit1 = 0.74f / (line1.Length * 6f - 1f);       // torso widths per glyph column
        float unit2 = unit1 * 1.7f;
        float a = TextCoverage(lx, ly - 0.42f * heightUnits, line1, unit1);
        if (a > 0f) return a;
        return TextCoverage(lx, ly - 0.2f * heightUnits, line2, unit2);
    }

    private static void EnsureFolderPath(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    // ---------------------------------------------------------------- post

    /// <summary>Spreads painted texels a few pixels outward so bilinear filtering and mips never pull in the uncovered background.</summary>
    private static void Dilate(Buffers b, Color[] albedo, Color[] metal, Color[] emission, float[] height)
    {
        bool[] done = (bool[])b.covered.Clone();
        for (int pass = 0; pass < 6; pass++)
        {
            bool[] next = (bool[])done.Clone();
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    int i = y * Size + x;
                    if (done[i]) continue;
                    Color a = Color.black, mt = Color.black, em = Color.black; float h = 0f; int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= Size || ny >= Size) continue;
                            int j = ny * Size + nx;
                            if (!done[j]) continue;
                            a += albedo[j]; mt += metal[j]; em += emission[j]; h += height[j]; n++;
                        }
                    }
                    if (n == 0) continue;
                    albedo[i] = a / n; metal[i] = mt / n; emission[i] = em / n; height[i] = h / n;
                    next[i] = true;
                }
            }
            done = next;
        }
    }

    private static Color[] HeightToNormal(float[] h)
    {
        Color[] result = new Color[Size * Size];
        const float strength = 2.2f;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                float hl = h[y * Size + Mathf.Max(0, x - 1)], hr = h[y * Size + Mathf.Min(Size - 1, x + 1)];
                float hd = h[Mathf.Max(0, y - 1) * Size + x], hu = h[Mathf.Min(Size - 1, y + 1) * Size + x];
                Vector3 n = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
                result[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        }
        return result;
    }

    private static Texture2D WriteTexture(string folder, string name, Color[] pixels, bool sRgb, bool normalMap)
    {
        Texture2D t = new Texture2D(Size, Size, TextureFormat.RGBA32, false, !sRgb);
        t.SetPixels(pixels);
        t.Apply();
        byte[] png = t.EncodeToPNG();
        Object.DestroyImmediate(t);
        string path = folder + "/" + name + ".png";
        File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = sRgb;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = true;
        importer.isReadable = false;
        importer.maxTextureSize = Size;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------------------------------------------------------- noise

    /// <summary>GLSL-style smoothstep (Mathf.SmoothStep is a different, from/to interpolation).</summary>
    private static float Smooth(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + z * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFFu) / 16777215f;
        }
    }

    private static float Noise(float x, float y, float z)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
        float fx = x - xi, fy = y - yi, fz = z - zi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy); fz = fz * fz * (3f - 2f * fz);
        float c00 = Mathf.Lerp(Hash(xi, yi, zi), Hash(xi + 1, yi, zi), fx);
        float c10 = Mathf.Lerp(Hash(xi, yi + 1, zi), Hash(xi + 1, yi + 1, zi), fx);
        float c01 = Mathf.Lerp(Hash(xi, yi, zi + 1), Hash(xi + 1, yi, zi + 1), fx);
        float c11 = Mathf.Lerp(Hash(xi, yi + 1, zi + 1), Hash(xi + 1, yi + 1, zi + 1), fx);
        return Mathf.Lerp(Mathf.Lerp(c00, c10, fy), Mathf.Lerp(c01, c11, fy), fz);
    }

    private static float Fbm(Vector3 p, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Noise(p.x, p.y, p.z);
            norm += amp;
            p *= 2.03f; amp *= 0.5f;
        }
        return sum / norm;
    }

    /// <summary>Worley F2 - F1 in 2D: near zero along the borders between cells, which makes a crack network.</summary>
    private static float Worley(float x, float y)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float f1 = 9f, f2 = 9f;
        for (int j = -1; j <= 1; j++)
        {
            for (int i = -1; i <= 1; i++)
            {
                int cx = xi + i, cy = yi + j;
                float px = cx + Hash(cx, cy, 11), py = cy + Hash(cx, cy, 29);
                float dd = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (dd < f1) { f2 = f1; f1 = dd; } else if (dd < f2) f2 = dd;
            }
        }
        return f2 - f1;
    }
}
