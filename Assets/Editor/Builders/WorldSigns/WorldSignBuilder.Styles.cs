using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// F109: restyles a bare TextMeshPro sign IN PLACE (the GameObject and component stay, so every serialized reference survives) - font, lit
/// material, backing and extras are added as children (local +Z = behind the text). Used by the migration and by the three room builders.
/// Spec: plannings/world-signs-plan.md D1-D8.
/// </summary>
public static partial class WorldSignBuilder
{
    public enum SignStyle { EnamelPlate, LightBox, StencilSpray, WallPaint, PaperNotice, BrassPlaque, WoodBoard, ChalkSlate, NoticeHeader }

    public sealed class SignOptions
    {
        public SignFont? font;
        public float? fontSize;        // absolute font size; null = authored size x the font's scale
        public Color? textColor;
        public float tilt;             // roll of the whole sign, degrees
        public bool battered;          // more chips, a second screw missing
        public bool hanging;           // WoodBoard / BrassPlaque on two chains + SignSway
        public float chainLength = 0.5f;
        public bool bracket = true;    // chains end in wall brackets (false: they run up into the ceiling)
        public bool darkInset;         // BrassPlaque: a dark panel inside the brass frame (status-coloured runtime text)
        public Color paint = new Color(0.86f, 0.1f, 0.07f); // StencilSpray / WallPaint
        public Vector2? fixedInner;    // text area in metres (runtime-text signs); null = measured from the text
        public string ghostKey;        // WallPaint: older painted-over words behind the sign
        public string ghostText;
        public bool deadLetter;        // SignDeadLetter on the text
        public bool repaint = true;    // WallPaint: one letter painted over and redone
        public bool curlRight = true;  // PaperNotice
        public float standoff = -1f;   // metres the sign is moved toward the viewer; -1 = the style default
        public bool noMissingScrew;
    }

    public const string BackingName = "SignBacking";
    public const string StyleMarkerName = "WorldSignStyle";

    private const float FrontZ = 0.004f;

    public static bool IsStyled(Component sign) => sign != null && (sign.transform.Find(BackingName) != null || sign.transform.Find(StyleMarkerName) != null);

    private static SignFont DefaultFont(SignStyle s)
    {
        switch (s)
        {
            case SignStyle.StencilSpray: return SignFont.Stencil;
            case SignStyle.WallPaint: return SignFont.RockSalt;
            case SignStyle.PaperNotice: return SignFont.Reenie;
            case SignStyle.ChalkSlate: return SignFont.Reenie;
            case SignStyle.WoodBoard: return SignFont.IMFell;
            case SignStyle.NoticeHeader: return SignFont.IMFell;
            default: return SignFont.Barlow;
        }
    }

    private static float FontScale(SignFont f)
    {
        switch (f)
        {
            case SignFont.Reenie: return 1.15f;
            case SignFont.RockSalt: return 0.8f;
            case SignFont.Barlow: return 1.1f;
            case SignFont.Stencil: return 0.95f;
            case SignFont.Cinzel: return 0.9f;
            default: return 1f;
        }
    }

    private static Color DefaultTextColor(SignStyle s, SignOptions o)
    {
        switch (s)
        {
            case SignStyle.EnamelPlate: return new Color(0.07f, 0.1f, 0.2f);
            case SignStyle.LightBox: return new Color(0.05f, 0.04f, 0.03f);
            case SignStyle.StencilSpray: return o.paint;
            case SignStyle.WallPaint: return o.paint;
            case SignStyle.PaperNotice: return new Color(0.1f, 0.08f, 0.1f);
            case SignStyle.BrassPlaque: return o.darkInset ? new Color(0.85f, 0.8f, 0.6f) : new Color(0.1f, 0.07f, 0.03f);
            case SignStyle.WoodBoard: return new Color(0.88f, 0.7f, 0.3f);
            case SignStyle.ChalkSlate: return new Color(0.9f, 0.9f, 0.84f);
            default: return new Color(0.86f, 0.8f, 0.62f);
        }
    }

    private static int StableHash(string s)
    {
        uint h = 2166136261u;
        foreach (char c in s ?? "") h = (h ^ c) * 16777619u;
        return (int)(h & 0x7fffffff);
    }

    // ---------------------------------------------------------------- helpers

    private static GameObject MeshChild(Transform parent, string name, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, params Material[] mats)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterials = mats;
        r.shadowCastingMode = ShadowCastingMode.Off;
        return go;
    }

    private static GameObject Overlay(Transform parent, string name, Material mat, Vector2 size, Vector3 pos, float rollDegrees, int sortingOrder = -5)
    {
        GameObject go = MeshChild(parent, name, UnitQuad(), pos, Quaternion.Euler(0f, 0f, rollDegrees), new Vector3(size.x, size.y, 1f), mat);
        go.GetComponent<MeshRenderer>().sortingOrder = sortingOrder;
        return go;
    }

    private static void AddScrew(Transform parent, Vector2 at, Material mat, bool rustStreak, float r = 0.011f)
    {
        MeshChild(parent, "Screw", ScrewHead(r), new Vector3(at.x, at.y, FrontZ - 0.0005f), Quaternion.identity, Vector3.one, mat);
        if (rustStreak) Overlay(parent, "RustStreak", RustStreakMat, new Vector2(0.05f, 0.14f), new Vector3(at.x, at.y - 0.075f, FrontZ - 0.0015f), 0f, 2);
    }

    public static void SetLocalizedKey(GameObject go, string key)
    {
        LocalizedText lt = go.GetComponent<LocalizedText>();
        if (lt == null) lt = go.AddComponent<LocalizedText>();
        SerializedObject so = new SerializedObject(lt);
        so.FindProperty("key").stringValue = key;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    public static TextMeshPro NewText(Transform parent, string name, SignFont font, string text, float size, Color color, Vector2 rect, Vector3 localPos)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        TextMeshPro t = go.AddComponent<TextMeshPro>();
        ApplyFont(t, font);
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = rect;
        return t;
    }

    public static void ApplyFont(TMP_Text t, SignFont font)
    {
        TMP_FontAsset fa = FontAsset(font);
        Material m = TextMaterial(font);
        if (fa != null) t.font = fa;
        if (m != null) t.fontSharedMaterial = m;
    }

    public static void MakeMovable(GameObject root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
    }

    // ---------------------------------------------------------------- the one entry point

    /// <summary>Restyles <paramref name="sign"/> in place. Returns the backing/marker container (null if the sign is already styled).</summary>
    public static Transform Style(TextMeshPro sign, SignStyle style, SignOptions o = null)
    {
        if (sign == null || IsStyled(sign)) return null;
        o = o ?? new SignOptions();
        EnsureFonts();

        SignFont font = o.font ?? DefaultFont(style);
        float authored = sign.fontSize;
        ApplyFont(sign, font);
        sign.fontSize = o.fontSize ?? authored * FontScale(font);
        sign.color = o.textColor ?? DefaultTextColor(style, o);
        sign.textWrappingMode = TextWrappingModes.NoWrap;
        sign.alignment = TextAlignmentOptions.Center;
        sign.overflowMode = TextOverflowModes.Overflow;
        sign.enableAutoSizing = false;

        // ---- size the text area (and so the backing) from the text
        Vector2 inner;
        if (o.fixedInner.HasValue) inner = o.fixedInner.Value;
        else
        {
            sign.ForceMeshUpdate(true, true);
            Vector2 pref = sign.GetPreferredValues(sign.text);
            inner = new Vector2(pref.x + Mathf.Clamp(pref.y * 0.7f, 0.1f, 0.5f), pref.y + Mathf.Clamp(pref.y * 0.5f, 0.08f, 0.35f));
        }
        sign.rectTransform.sizeDelta = inner;

        float standoff = o.standoff >= 0f ? o.standoff : (style == SignStyle.LightBox ? 0.045f : o.hanging ? (o.bracket ? 0.1f : 0f) : style == SignStyle.ChalkSlate ? 0.02f : 0f);
        if (standoff > 0f) sign.transform.position -= sign.transform.forward * standoff;
        if (!Mathf.Approximately(o.tilt, 0f)) sign.transform.Rotate(0f, 0f, o.tilt, Space.Self);

        float w = inner.x, h = inner.y;
        float depth = standoff + 0.04f;
        int hash = StableHash(sign.name + sign.text);
        Transform container = null;

        switch (style)
        {
            case SignStyle.EnamelPlate: container = BuildEnamel(sign, w, h, depth, hash, o); break;
            case SignStyle.BrassPlaque: container = BuildBrass(sign, w, h, depth, hash, o); break;
            case SignStyle.LightBox: container = BuildLightBox(sign, w, h, depth); break;
            case SignStyle.StencilSpray: container = BuildSpray(sign, w, h, o, false); break;
            case SignStyle.WallPaint: container = BuildSpray(sign, w, h, o, true); break;
            case SignStyle.PaperNotice: container = BuildPaper(sign, w, h, hash, o); break;
            case SignStyle.WoodBoard: container = BuildWood(sign, w, h, depth, o); break;
            case SignStyle.ChalkSlate: container = BuildSlate(sign, w, h, depth); break;
            case SignStyle.NoticeHeader: container = BuildHeader(sign, w, h, depth); break;
        }

        if (o.deadLetter && sign.GetComponent<SignDeadLetter>() == null) sign.gameObject.AddComponent<SignDeadLetter>();
        if (!string.IsNullOrEmpty(o.ghostText)) AddGhost(sign, container, o, font);
        EditorUtility.SetDirty(sign);
        return container;
    }

    private static Transform NewContainer(TextMeshPro sign, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(sign.transform, false);
        return go.transform;
    }

    // ---------------------------------------------------------------- styles

    private static Transform BuildEnamel(TextMeshPro sign, float w, float h, float depth, int hash, SignOptions o)
    {
        w += 0.08f; h += 0.06f;
        Transform c = NewContainer(sign, BackingName);
        MeshChild(c, "Plate", BevelPlate(w, h, depth, 0.01f), new Vector3(0, 0, FrontZ), Quaternion.identity, Vector3.one, EnamelMat, EnamelEdgeMat);
        int missing = o.noMissingScrew ? -1 : hash % 4;
        int missing2 = o.battered ? (missing + 2) % 4 : -2;
        Vector2[] corners =
        {
            new Vector2(-w * 0.5f + 0.035f, h * 0.5f - 0.032f), new Vector2(w * 0.5f - 0.035f, h * 0.5f - 0.032f),
            new Vector2(w * 0.5f - 0.035f, -h * 0.5f + 0.032f), new Vector2(-w * 0.5f + 0.035f, -h * 0.5f + 0.032f),
        };
        for (int i = 0; i < 4; i++)
        {
            if (i == missing || i == missing2) continue;
            AddScrew(c, corners[i], SteelMat, i < 2 || (hash >> i & 1) == 1);
        }
        return c;
    }

    private static Transform BuildBrass(TextMeshPro sign, float w, float h, float depth, int hash, SignOptions o)
    {
        if (o.hanging) depth = 0.05f;
        float frame = o.darkInset ? 0.05f : 0.06f;
        w += frame * 1.2f; h += frame;
        Transform c = NewContainer(sign, BackingName);
        MeshChild(c, "Plate", BevelPlate(w, h, depth, 0.012f), new Vector3(0, 0, FrontZ), Quaternion.identity, Vector3.one, BrassMat, BrassEdgeMat);
        if (o.darkInset)
        {
            MeshChild(c, "Inset", BevelPlate(w - frame * 1.6f, h - frame * 1.6f, 0.012f, 0.004f), new Vector3(0, 0, FrontZ - 0.002f), Quaternion.identity, Vector3.one, SlateMat, BrassEdgeMat);
        }
        else
        {
            // a thin engraved-looking border: four strips just proud of the plate
            float fw = w - 0.07f, fh = h - 0.07f, t = 0.007f;
            MeshChild(c, "BorderTop", BevelPlate(fw, t, 0.002f, 0.001f), new Vector3(0, fh * 0.5f, FrontZ - 0.0015f), Quaternion.identity, Vector3.one, BrassEdgeMat, BrassEdgeMat);
            MeshChild(c, "BorderBottom", BevelPlate(fw, t, 0.002f, 0.001f), new Vector3(0, -fh * 0.5f, FrontZ - 0.0015f), Quaternion.identity, Vector3.one, BrassEdgeMat, BrassEdgeMat);
            MeshChild(c, "BorderLeft", BevelPlate(t, fh, 0.002f, 0.001f), new Vector3(-fw * 0.5f, 0, FrontZ - 0.0015f), Quaternion.identity, Vector3.one, BrassEdgeMat, BrassEdgeMat);
            MeshChild(c, "BorderRight", BevelPlate(t, fh, 0.002f, 0.001f), new Vector3(fw * 0.5f, 0, FrontZ - 0.0015f), Quaternion.identity, Vector3.one, BrassEdgeMat, BrassEdgeMat);
        }
        Vector2[] corners =
        {
            new Vector2(-w * 0.5f + 0.03f, h * 0.5f - 0.028f), new Vector2(w * 0.5f - 0.03f, h * 0.5f - 0.028f),
            new Vector2(w * 0.5f - 0.03f, -h * 0.5f + 0.028f), new Vector2(-w * 0.5f + 0.03f, -h * 0.5f + 0.028f),
        };
        for (int i = 0; i < 4; i++) AddScrew(c, corners[i], BrassEdgeMat, false, 0.009f);
        if (o.hanging) Hang(sign, c, w, h, o);
        return c;
    }

    private static Transform BuildLightBox(TextMeshPro sign, float w, float h, float depth)
    {
        Transform c = NewContainer(sign, BackingName);
        MeshChild(c, "Housing", BevelPlate(w + 0.12f, h + 0.12f, depth, 0.014f), new Vector3(0, 0, FrontZ + 0.012f), Quaternion.identity, Vector3.one, EnamelEdgeMat, EnamelEdgeMat);
        MeshChild(c, "Diffuser", BevelPlate(w, h, 0.01f, 0.006f), new Vector3(0, 0, FrontZ), Quaternion.identity, Vector3.one, LightPanelMat, LightPanelMat);
        return c;
    }

    private static Transform BuildSpray(TextMeshPro sign, float w, float h, SignOptions o, bool paintStyle)
    {
        Transform c = NewContainer(sign, StyleMarkerName);
        Color paint = o.textColor ?? o.paint;
        string cname = ColourName(paint);
        Overlay(c, "Overspray", OversprayMat(cname, paint), new Vector2(w * 1.45f, h * 2.1f), new Vector3(0, 0, FrontZ), 0f);
        float dripH = h * (paintStyle ? 1.0f : 0.75f);
        Overlay(c, "Drips", DripsMat(cname, paint), new Vector2(w * 0.96f, dripH), new Vector3(0, -h * 0.28f - dripH * 0.5f, FrontZ - 0.001f), 0f, -4);
        if (paintStyle && o.repaint)
        {
            GameObject patch = Overlay(c, "RepaintPatch", PatchMat, new Vector2(0.2f, 0.2f), new Vector3(0, 0, -0.003f), 0f, 5);
            TextMeshPro redo = NewText(sign.transform, "RepaintRedo", FontOf(sign), "X", sign.fontSize, Brighter(paint), new Vector2(0.5f, 0.5f), new Vector3(0, 0, -0.004f));
            redo.sortingOrder = 6;
            redo.gameObject.SetActive(false);
            patch.SetActive(false);
            SignRepaint sr = sign.gameObject.GetComponent<SignRepaint>() ?? sign.gameObject.AddComponent<SignRepaint>();
            SerializedObject so = new SerializedObject(sr);
            so.FindProperty("patch").objectReferenceValue = patch.transform;
            so.FindProperty("redo").objectReferenceValue = redo;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        return c;
    }

    private static SignFont FontOf(TextMeshPro sign)
    {
        foreach (var e in FontTable) if (sign.font != null && sign.font.name == e.asset) return e.font;
        return SignFont.Barlow;
    }

    private static Color Brighter(Color c) => new Color(Mathf.Min(1f, c.r * 1.25f + 0.05f), Mathf.Min(1f, c.g * 1.25f + 0.02f), Mathf.Min(1f, c.b * 1.25f), c.a);

    private static string ColourName(Color c)
    {
        if (c.r > 0.55f && c.g < 0.35f && c.b < 0.35f) return "Red";
        if (c.r > 0.7f && c.g > 0.7f && c.b > 0.6f) return "White";
        return "Dark";
    }

    private static Transform BuildPaper(TextMeshPro sign, float w, float h, int hash, SignOptions o)
    {
        w *= 0.92f; h *= 0.9f;
        Transform c = NewContainer(sign, BackingName);
        MeshChild(c, "Sheet", PaperSheet(w, h, o.curlRight), new Vector3(0, 0, FrontZ + 0.002f), Quaternion.identity, Vector3.one, PaperMat);
        float tw = Mathf.Clamp(w * 0.2f, 0.06f, 0.1f), th = tw * 0.55f;
        bool topLeft = true, topRight = o.curlRight ? false : true, bottomLeft = (hash & 1) == 0, bottomRight = !o.curlRight && (hash & 2) != 0;
        if (topLeft) Overlay(c, "TapeTL", TapeMat, new Vector2(tw, th), new Vector3(-w * 0.5f + 0.015f, h * 0.5f - 0.01f, FrontZ - 0.001f), -35f, 3);
        if (topRight) Overlay(c, "TapeTR", TapeMat, new Vector2(tw, th), new Vector3(w * 0.5f - 0.015f, h * 0.5f - 0.01f, FrontZ - 0.001f), 35f, 3);
        if (bottomLeft) Overlay(c, "TapeBL", TapeMat, new Vector2(tw, th), new Vector3(-w * 0.5f + 0.015f, -h * 0.5f + 0.01f, FrontZ - 0.001f), 30f, 3);
        if (bottomRight) Overlay(c, "TapeBR", TapeMat, new Vector2(tw, th), new Vector3(w * 0.5f - 0.015f, -h * 0.5f + 0.01f, FrontZ - 0.001f), -30f, 3);
        return c;
    }

    private static Transform BuildWood(TextMeshPro sign, float w, float h, float depth, SignOptions o)
    {
        w += 0.16f; h += 0.12f;
        Transform c = NewContainer(sign, BackingName);
        float d = o.hanging ? 0.05f : depth;
        MeshChild(c, "Board", BevelPlate(w, h, d, 0.016f), new Vector3(0, 0, FrontZ), Quaternion.identity, Vector3.one, WoodMat, WoodDarkMat);
        // painted/gilt inner panel line: a darker inset
        MeshChild(c, "Panel", BevelPlate(w - 0.08f, h - 0.08f, 0.006f, 0.004f), new Vector3(0, 0, FrontZ - 0.002f), Quaternion.identity, Vector3.one, WoodDarkMat, WoodDarkMat);
        if (o.hanging) Hang(sign, c, w, h, o);
        return c;
    }

    private static Transform BuildSlate(TextMeshPro sign, float w, float h, float depth)
    {
        Transform c = NewContainer(sign, BackingName);
        MeshChild(c, "Frame", BevelPlate(w + 0.16f, h + 0.16f, depth, 0.02f), new Vector3(0, 0, FrontZ + 0.01f), Quaternion.identity, Vector3.one, WoodMat, WoodDarkMat);
        MeshChild(c, "Slate", BevelPlate(w, h, 0.01f, 0.004f), new Vector3(0, 0, FrontZ), Quaternion.identity, Vector3.one, SlateMat, WoodDarkMat);
        Overlay(c, "Smudge", OversprayMat("Chalk", new Color(0.85f, 0.85f, 0.8f)), new Vector2(w * 0.9f, h * 0.9f), new Vector3(0, 0, FrontZ - 0.0015f), 4f, -2);
        return c;
    }

    private static Transform BuildHeader(TextMeshPro sign, float w, float h, float depth)
    {
        w += 0.1f; h += 0.04f;
        Transform c = NewContainer(sign, BackingName);
        MeshChild(c, "Plank", BevelPlate(w, h, depth, 0.01f), new Vector3(0, 0, FrontZ), Quaternion.identity, Vector3.one, WoodMat, WoodDarkMat);
        foreach (int sx in new[] { -1, 1 })
            AddScrew(c, new Vector2(sx * (w * 0.5f - 0.03f), 0f), SteelMat, false, 0.008f);
        return c;
    }

    private static void AddGhost(TextMeshPro sign, Transform container, SignOptions o, SignFont font)
    {
        Color g = (o.textColor ?? o.paint);
        g.a = 0.16f;
        TextMeshPro ghost = NewText(sign.transform, "GhostText", font, o.ghostText, sign.fontSize * 0.85f, g, new Vector2(sign.rectTransform.sizeDelta.x * 1.2f, sign.rectTransform.sizeDelta.y), new Vector3(0.18f, -0.14f, FrontZ - 0.001f));
        ghost.transform.localRotation = Quaternion.Euler(0f, 0f, -3f);
        ghost.sortingOrder = -6;
        if (!string.IsNullOrEmpty(o.ghostKey)) SetLocalizedKey(ghost.gameObject, o.ghostKey);
    }
}

public static partial class WorldSignBuilder
{
    /// <summary>Two chains from the top corners of a board; SignSway swings the whole sign about the chain anchor. Not static (D5).</summary>
    private static void Hang(TextMeshPro sign, Transform c, float w, float h, SignOptions o)
    {
        float cx = w * 0.5f - 0.08f, chain = o.chainLength;
        float standoff = o.standoff >= 0f ? o.standoff : (o.bracket ? 0.1f : 0f);
        for (int s = -1; s <= 1; s += 2)
        {
            MeshChild(c, "Chain", BevelPlate(0.018f, chain, 0.018f, 0.004f), new Vector3(s * cx, h * 0.5f + chain * 0.5f, FrontZ + 0.01f), Quaternion.identity, Vector3.one, SteelMat, SteelMat);
            if (o.bracket) MeshChild(c, "Bracket", BevelPlate(0.035f, 0.035f, standoff + 0.05f, 0.006f), new Vector3(s * cx, h * 0.5f + chain, FrontZ), Quaternion.identity, Vector3.one, SteelMat, SteelMat);
            AddScrew(c, new Vector2(s * cx, h * 0.5f - 0.03f), SteelMat, false, 0.008f);
        }
        SignSway sway = sign.gameObject.AddComponent<SignSway>();
        sway.PivotLocal = new Vector3(0f, h * 0.5f + chain, FrontZ + 0.02f);
        MakeMovable(sign.gameObject);
    }
}
