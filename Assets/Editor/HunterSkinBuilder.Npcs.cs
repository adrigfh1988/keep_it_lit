using UnityEditor;
using UnityEngine;

/// <summary>
/// The hub robots' skins: the six NPCs and the patients Hilde and Elias are Timmy robots, each in its own paint so they never read as one another,
/// the hunter's bone-white Orderly, the pursuer's dulled Timmy, Stan's black or the shop's brass. One parametrised painter
/// (<see cref="PaintNpc"/>) over the same UV-space rasteriser as the Orderly, with a palette per NPC: enamel shell, limb
/// panels, how worn (grime, rust, chips), and the colour of the eyes and the screen glow - the strongest tell in a dark hub, so
/// no two share a hue and none is red (hunter), amber (shopkeeper) or white (Stan).
/// Writes Assets/SourceFiles/Characters/HubNpcs/&lt;Who&gt;_* and &lt;Who&gt;.mat. Idempotent like the Orderly.
/// </summary>
public static partial class HunterSkinBuilder
{
    private const string NpcFolder = "Assets/SourceFiles/Characters/HubNpcs";

    private sealed class NpcPalette
    {
        public Color shell, limb, eyes;
        public float grime, rust, chips, smooth = 0.3f, metallic = 0.08f, eyeGlow = 1f;
    }

    /// <summary>Every painted hub robot: the six NPCs plus the patients Hilde and Elias.</summary>
    private static readonly string[] NpcSkinNames = { "Agnes", "Bruno", "Josef", "Marta", "Weiss", "Clerk", "Hilde", "Elias" };

    private static NpcPalette PaletteFor(string who)
    {
        switch (who)
        {
            // the nurse: powder-blue enamel, kept clean, soft cyan eyes
            case "Agnes": return new NpcPalette { shell = new Color(0.56f, 0.66f, 0.74f), limb = new Color(0.66f, 0.66f, 0.63f), eyes = new Color(0.25f, 0.9f, 0.95f), grime = 0.35f, rust = 0.15f, chips = 0.2f, smooth = 0.42f };
            // the kitchen hand: mustard enamel, charcoal limbs, greasy, yellow eyes
            case "Bruno": return new NpcPalette { shell = new Color(0.6f, 0.45f, 0.13f), limb = new Color(0.12f, 0.12f, 0.13f), eyes = new Color(1f, 0.86f, 0.2f), grime = 1f, rust = 0.5f, chips = 0.6f, smooth = 0.35f };
            // the bedridden one: faded sage, heavy rust, dim violet eyes
            case "Josef": return new NpcPalette { shell = new Color(0.4f, 0.47f, 0.37f), limb = new Color(0.33f, 0.34f, 0.29f), eyes = new Color(0.62f, 0.38f, 1f), grime = 0.8f, rust = 1f, chips = 0.8f, eyeGlow = 0.55f };
            // the child: dusty rose with cream limbs, chipped, pink eyes
            case "Marta": return new NpcPalette { shell = new Color(0.68f, 0.42f, 0.47f), limb = new Color(0.68f, 0.62f, 0.52f), eyes = new Color(1f, 0.38f, 0.78f), grime = 0.5f, rust = 0.3f, chips = 0.9f };
            // the doctor: polished gunmetal, navy limbs, cold blue eyes
            case "Weiss": return new NpcPalette { shell = new Color(0.2f, 0.22f, 0.25f), limb = new Color(0.07f, 0.09f, 0.15f), eyes = new Color(0.25f, 0.45f, 1f), grime = 0.25f, rust = 0.1f, chips = 0.3f, smooth = 0.55f, metallic = 0.6f };
            // the patient Hilde: washed-out lavender-grey, worn, teal eyes
            case "Hilde": return new NpcPalette { shell = new Color(0.55f, 0.5f, 0.6f), limb = new Color(0.5f, 0.49f, 0.5f), eyes = new Color(0.1f, 0.85f, 0.65f), grime = 0.6f, rust = 0.4f, chips = 0.6f };
            // the patient Elias: pale bone-blue, the most neglected of them, faint straw-coloured eyes
            case "Elias": return new NpcPalette { shell = new Color(0.6f, 0.64f, 0.66f), limb = new Color(0.44f, 0.45f, 0.45f), eyes = new Color(0.9f, 0.86f, 0.55f), grime = 0.9f, rust = 0.8f, chips = 0.85f, eyeGlow = 0.45f };
            // the clerk: khaki drab, brown limbs, dusty, phosphor-green eyes
            default: return new NpcPalette { shell = new Color(0.44f, 0.42f, 0.29f), limb = new Color(0.24f, 0.18f, 0.12f), eyes = new Color(0.25f, 1f, 0.4f), grime = 0.7f, rust = 0.35f, chips = 0.45f };
        }
    }

    [MenuItem("LIGHTS OUT/Build/Hub NPC Skins (repaint)", priority = 108)]
    public static void RepaintNpcSkins()
    {
        foreach (string who in NpcSkinNames) GenerateNpcSkin(who);
        Debug.Log("HunterSkinBuilder: repainted the hub NPC skins in " + NpcFolder + ".");
    }

    /// <summary>The NPC's material, painting it first if it (or a texture) is missing.</summary>
    public static Material EnsureNpcSkin(HubNpc.Who who) => EnsureNpcSkin(who.ToString());

    /// <summary>The skin by name (an NPC or a patient), painting it first if it (or a texture) is missing.</summary>
    public static Material EnsureNpcSkin(string who)
    {
        string p = NpcFolder + "/" + who;
        Material m = AssetDatabase.LoadAssetAtPath<Material>(p + ".mat");
        bool complete = m != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(p + "_Albedo.png") != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(p + "_MetalSmooth.png") != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(p + "_Emission.png") != null
            && AssetDatabase.LoadAssetAtPath<Texture2D>(p + "_Normal.png") != null;
        return complete ? m : GenerateNpcSkin(who);
    }

    private static Material GenerateNpcSkin(string who)
    {
        NpcPalette pal = PaletteFor(who);
        return GenerateSkin(NpcFolder, who,
            (Buffers b, int i, int x, int y, Vector4 chest, out Color a, out Color m, out Color e, out float h) => PaintNpc(b, i, x, y, pal, out a, out m, out e, out h),
            1.3f);
    }

    private static void PaintNpc(Buffers b, int i, int x, int y, NpcPalette p, out Color albedo, out Color metal, out Color emission, out float height)
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
            // dark glass with a faint glow of the eye colour behind it and some smeared dust
            float u = x / (float)Size, v = y / (float)Size;
            float smear = Fbm(new Vector3(u * 16f, v * 34f, 5f), 3);
            float rim = Mathf.Exp(-d / 6f);
            albedo = Color.Lerp(new Color(0.02f, 0.022f, 0.025f), new Color(0.07f, 0.07f, 0.07f), Smooth(0.55f, 0.88f, smear) * (0.3f + 0.4f * p.grime));
            metal = new Color(0.1f, 0f, 0f, Mathf.Clamp01(0.9f - 0.35f * Smooth(0.6f, 0.9f, smear) * p.grime - 0.35f * rim));
            float scan = 0.5f + 0.5f * Mathf.Sin(y * 0.9f);
            emission = p.eyes * (0.02f + 0.012f * scan) * (1f - rim) * p.eyeGlow;
            return;
        }
        if (cls == ClsEye)
        {
            albedo = p.eyes * 0.05f;
            metal = new Color(0f, 0f, 0f, 0.9f);
            emission = p.eyes * p.eyeGlow * (0.75f + 0.25f * Fbm(new Vector3(x * 0.05f, y * 0.05f, 9f), 2));
            return;
        }
        if (cls == ClsMouth)
        {
            albedo = p.eyes * 0.04f;
            metal = new Color(0f, 0f, 0f, 0.6f);
            emission = p.eyes * p.eyeGlow * 0.3f * (0.7f + 0.3f * Fbm(new Vector3(x * 0.2f, y * 0.2f, 4f), 2));
            return;
        }

        bool limb = cls == ClsLimb;
        Color col = limb ? p.limb : p.shell;
        col *= Mathf.Lerp(0.88f, 1.08f, Fbm(m * 3f, 3));
        float metallic = limb ? Mathf.Max(p.metallic, 0.3f) : p.metallic, smooth = p.smooth;

        // grime: crevices, undersides, near the floor, vertical run-off
        float low = 1f - Smooth(0f, 0.35f, m.y);
        float under = Mathf.Clamp01(-n.y);
        float dirtNoise = Fbm(m * 20f, 3);
        float streak = Fbm(new Vector3(m.x * 70f, m.y * 5f, m.z * 70f), 3);
        float grime = Mathf.Clamp01(0.85f * edge + 0.45f * under + 0.5f * low * dirtNoise + 0.35f * Smooth(0.58f, 0.8f, streak) * (0.4f + edgeWide)) * p.grime;
        col = Color.Lerp(col, new Color(0.07f, 0.055f, 0.035f), Mathf.Clamp01(grime * (0.45f + 0.8f * dirtNoise)) * 0.8f);
        smooth = Mathf.Lerp(smooth, 0.12f, grime);

        // rust bleeding from the joints and panel edges
        float rustField = edgeWide * (0.45f + 0.9f * Fbm(new Vector3(m.x * 14f, m.y * 5f, m.z * 14f) + new Vector3(0f, 4f, 0f), 3));
        float rustMask = Smooth(0.5f, 0.8f, rustField) * p.rust;
        col = Color.Lerp(col, Rust * Mathf.Lerp(0.6f, 1.15f, Fbm(m * 30f, 2)), rustMask * 0.65f);
        smooth = Mathf.Lerp(smooth, 0.1f, rustMask);
        metallic = Mathf.Lerp(metallic, 0.05f, rustMask);

        // chips: enamel gone to bare dark steel, mostly along edges; fewer on a cared-for body
        float chipNoise = Fbm(m * 40f + new Vector3(7f, 1f, 4f), 3);
        float threshold = Mathf.Lerp(0.86f, 0.78f, edge) + (1f - p.chips) * 0.08f;
        float chip = Smooth(threshold, threshold + 0.035f, chipNoise);
        float halo = Smooth(threshold - 0.08f, threshold, chipNoise) * (1f - chip) * p.rust;
        col = Color.Lerp(col, Rust * 0.9f, halo * 0.6f);
        col = Color.Lerp(col, Steel * Mathf.Lerp(0.8f, 1.5f, Fbm(m * 60f, 2)), chip);
        metallic = Mathf.Lerp(metallic, 0.65f, chip);
        smooth = Mathf.Lerp(smooth, 0.38f, chip);
        height = -0.9f * chip - 0.25f * halo + 0.06f * Fbm(m * 90f, 2);

        albedo = new Color(Mathf.Clamp01(col.r), Mathf.Clamp01(col.g), Mathf.Clamp01(col.b), 1f);
        metal = new Color(Mathf.Clamp01(metallic), 0f, 0f, Mathf.Clamp01(smooth));
    }
}
