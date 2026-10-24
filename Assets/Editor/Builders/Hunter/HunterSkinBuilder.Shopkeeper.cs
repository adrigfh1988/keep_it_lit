using UnityEditor;
using UnityEngine;

/// <summary>
/// The shop salesman's skin: Timmy as the alchemist's brass automaton. Same UV-space rasteriser as the Orderly (the robot's
/// own islands tell shell, limb panel, screen, eye and mouth apart), painted as aged brass with tarnish and green verdigris
/// in the seams and joints, darker oiled bronze on the limb panels with rubbed-bright high spots, and warm dark glass with
/// amber eyes - the shop's sign colour - so he never reads as the hunter's bone-white Orderly or Stan's black.
/// Writes Assets/SourceFiles/Characters/Shopkeeper/Shopkeeper_* and Shopkeeper.mat. Idempotent like the Orderly.
/// </summary>
public static partial class HunterSkinBuilder
{
    public const string ShopkeeperMaterialName = "Shopkeeper";
    private const string ShopkeeperFolder = "Assets/SourceFiles/Characters/Shopkeeper";

    [MenuItem("KEEP IT LIT/Build/Shopkeeper Skin (repaint)", priority = 107)]
    public static void RepaintShopkeeper()
    {
        Material m = GenerateSkin(ShopkeeperFolder, ShopkeeperMaterialName, PaintShopkeeper, 1.6f);
        if (m != null) Debug.Log("HunterSkinBuilder: repainted " + ShopkeeperFolder + " (the shop salesman's brass skin).", m);
    }

    /// <summary>The Shopkeeper material, painting it first if it (or a texture) is missing.</summary>
    public static Material EnsureShopkeeperSkin()
    {
        string p = ShopkeeperFolder + "/" + ShopkeeperMaterialName;
        Material m = AssetDatabase.LoadAssetAtPath<Material>(p + ".mat");
        bool complete = m != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(p + "_Albedo.png") != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(p + "_MetalSmooth.png") != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(p + "_Emission.png") != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(p + "_Normal.png") != null;
        return complete ? m : GenerateSkin(ShopkeeperFolder, ShopkeeperMaterialName, PaintShopkeeper, 1.6f);
    }

    private static readonly Color Brass = new Color(0.56f, 0.42f, 0.18f);
    private static readonly Color BrightBrass = new Color(0.80f, 0.64f, 0.32f);
    private static readonly Color Tarnish = new Color(0.24f, 0.18f, 0.09f);
    private static readonly Color Bronze = new Color(0.23f, 0.14f, 0.08f);
    private static readonly Color Verdigris = new Color(0.20f, 0.40f, 0.32f);
    private static readonly Color Amber = new Color(1f, 0.62f, 0.24f);

    private static void PaintShopkeeper(Buffers b, int i, int x, int y, Vector4 chest, out Color albedo, out Color metal, out Color emission, out float height)
    {
        emission = Color.black;
        height = 0f;
        byte cls = b.covered[i] ? b.cls[i] : ClsShell;
        Vector3 m = b.covered[i] ? b.pos[i] / 0.3f : new Vector3(x * 0.01f, y * 0.01f, 3.7f); // ~metres on the robot
        Vector3 n = b.covered[i] ? b.nrm[i] : Vector3.forward;
        float d = b.covered[i] ? b.dist[i] : 0f;
        float edge = Mathf.Exp(-d / 8f);
        float edgeWide = Mathf.Exp(-d / 16f);

        if (cls == ClsScreen)
        {
            // warm smoked glass with a faint glow behind it and a little smeared dust; no cracks - he is looked after
            float u = x / (float)Size, v = y / (float)Size;
            float smear = Fbm(new Vector3(u * 16f, v * 34f, 5f), 3);
            float rim = Mathf.Exp(-d / 6f);
            Color glass = Color.Lerp(new Color(0.035f, 0.022f, 0.012f), new Color(0.09f, 0.075f, 0.05f), Smooth(0.55f, 0.88f, smear) * 0.5f);
            albedo = Color.Lerp(glass, Tarnish * 0.6f, rim * 0.6f);
            metal = new Color(0.1f, 0f, 0f, Mathf.Clamp01(0.9f - 0.3f * Smooth(0.6f, 0.9f, smear) - 0.35f * rim));
            float scan = 0.5f + 0.5f * Mathf.Sin(y * 0.9f);
            emission = Amber * (0.025f + 0.015f * scan) * (1f - rim);
            return;
        }
        if (cls == ClsEye)
        {
            albedo = new Color(0.06f, 0.035f, 0.01f);
            metal = new Color(0f, 0f, 0f, 0.9f);
            emission = Amber * (0.8f + 0.2f * Fbm(new Vector3(x * 0.05f, y * 0.05f, 9f), 2));
            return;
        }
        if (cls == ClsMouth)
        {
            albedo = new Color(0.05f, 0.03f, 0.01f);
            metal = new Color(0f, 0f, 0f, 0.6f);
            emission = Amber * 0.35f * (0.7f + 0.3f * Fbm(new Vector3(x * 0.2f, y * 0.2f, 4f), 2));
            return;
        }

        bool limb = cls == ClsLimb;
        Color col = limb ? Bronze : Brass;
        float metallic = limb ? 0.75f : 0.85f, smooth = limb ? 0.38f : 0.5f;

        // uneven tone: brass is never one colour
        float mottle = Fbm(m * 3f, 3);
        col *= Mathf.Lerp(0.85f, 1.1f, mottle);

        // tarnish: broad, darker patches where nobody polishes - undersides, near the floor, the backs of things
        float under = Mathf.Clamp01(-n.y);
        float low = 1f - Smooth(0f, 0.4f, m.y);
        float tarnishField = 0.5f * Fbm(m * 6f + new Vector3(2f, 9f, 4f), 3) + 0.35f * under + 0.25f * low + 0.2f * Mathf.Clamp01(n.z);
        float tarnish = Smooth(0.45f, 0.8f, tarnishField);
        col = Color.Lerp(col, limb ? Bronze * 0.7f : Tarnish, tarnish * 0.75f);
        smooth = Mathf.Lerp(smooth, 0.25f, tarnish);

        // rubbed bright: the high, often-touched spots on the shell catch the lamps
        float rub = Smooth(0.62f, 0.78f, Fbm(m * 5f + new Vector3(7f, 1f, 3f), 3)) * Mathf.Clamp01(n.y * 0.5f + 0.6f) * (1f - edge) * (1f - tarnish);
        col = Color.Lerp(col, limb ? Brass : BrightBrass, rub * 0.7f);
        smooth = Mathf.Lerp(smooth, 0.72f, rub);

        // verdigris: crusts in the joints and panel seams, creeping out in streaks
        float vNoise = Fbm(new Vector3(m.x * 18f, m.y * 7f, m.z * 18f) + new Vector3(5f, 0f, 2f), 3);
        float verdField = 0.9f * edge + 0.45f * edgeWide * vNoise + 0.2f * under * vNoise;
        float verd = Smooth(0.45f, 0.75f, verdField);
        Color verdCol = Verdigris * Mathf.Lerp(0.7f, 1.2f, Fbm(m * 35f, 2));
        col = Color.Lerp(col, verdCol, verd * 0.85f);
        metallic = Mathf.Lerp(metallic, 0.05f, verd);
        smooth = Mathf.Lerp(smooth, 0.12f, verd);

        // fine pitting and a crust that stands slightly proud of the metal
        float pit = Smooth(0.78f, 0.84f, Fbm(m * 70f, 2));
        col = Color.Lerp(col, col * 0.6f, pit * 0.5f);
        height = 0.45f * verd * Fbm(m * 80f, 2) - 0.35f * pit + 0.05f * Fbm(m * 110f, 2);

        albedo = new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b), 1f);
        metal = new Color(Mathf.Clamp01(metallic), 0f, 0f, Mathf.Clamp01(smooth));
    }
}
