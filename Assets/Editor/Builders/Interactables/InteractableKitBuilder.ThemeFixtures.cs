using UnityEditor;
using UnityEngine;

public static partial class InteractableKitBuilder
{
    private static ThemeInteractable BuildCallBell(Transform parent, Vector3 localPosition)
    {
        Material metalMat = LoadOrCreateMat("Interactable_BellMetal", new Color(0.55f, 0.46f, 0.2f), null, 0.7f);

        GameObject root = new GameObject("CallBell");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        CylinderRH(root.transform, "Bracket", new Vector3(0f, 0.1f, 0f), 0.02f, 0.1f, metalMat, keepCollider: false);
        GameObject bell = Primitive(root.transform, PrimitiveType.Sphere, "Bell", Vector3.zero, new Vector3(0.14f, 0.12f, 0.14f), metalMat, keepCollider: false);
        SphereCollider sphere = bell.AddComponent<SphereCollider>();
        sphere.radius = 0.55f;

        return root.AddComponent<ThemeInteractable>();
    }

    private static ThemeInteractable BuildSteamValve(Transform parent, Vector3 localPosition)
    {
        Material metalMat = LoadOrCreateMat("Interactable_ValveMetal", new Color(0.35f, 0.32f, 0.3f), null, 0.55f);

        GameObject root = CylinderRH(parent, "SteamValve", localPosition, 0.03f, 0.08f, metalMat, keepCollider: true); // stem
        CylinderRH(root.transform, "Wheel", new Vector3(0f, 0.05f, 0f), 0.16f, 0.03f, metalMat, keepCollider: false);
        Primitive(root.transform, PrimitiveType.Cube, "SpokeA", new Vector3(0f, 0.05f, 0f), new Vector3(0.3f, 0.02f, 0.02f), metalMat, keepCollider: false);
        Primitive(root.transform, PrimitiveType.Cube, "SpokeB", new Vector3(0f, 0.05f, 0f), new Vector3(0.02f, 0.02f, 0.3f), metalMat, keepCollider: false);

        return root.AddComponent<ThemeInteractable>();
    }

    private static readonly string[] PackCandles = { "Items/Lights/Candle01", "Items/Lights/Candle01c", "Items/Lights/Candle01d", "Items/Lights/Candle01b" };

    /// <summary>
    /// Three pack candles side by side (0.12 m apart) and a fourth in front, their bottoms on the root's y = 0
    /// (the generator drops the root on the floor at the wall face, so the group sits 0.1 m out from the wall).
    /// One BoxCollider fits the group; the (disabled-until-lit) Light sits on the tallest wick with a tiny
    /// emissive flame bead, as the primitive candles had.
    /// </summary>
    private static ThemeInteractable BuildCandles(Transform parent, Vector3 localPosition)
    {
        foreach (string path in PackCandles)
        {
            if (HasPackPrefab(path)) continue;
            Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{path}.prefab not found - building the primitive candles instead.");
            return BuildCandlesPrimitive(parent, localPosition);
        }

        Material flameMat = LoadOrCreateMat("Interactable_CandleFlame", new Color(1f, 0.6f, 0.15f), new Color(1f, 0.6f, 0.15f) * 3f, 0.1f);

        GameObject root = new GameObject("Candles");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        Vector3[] spots = { new Vector3(-0.12f, 0f, 0.1f), new Vector3(0f, 0f, 0.1f), new Vector3(0.12f, 0f, 0.1f), new Vector3(0.02f, 0f, 0.24f) };
        float[] yaws = { 0f, 40f, -25f, 80f };
        Bounds group = default;
        Bounds tallest = default;
        bool any = false;
        for (int i = 0; i < PackCandles.Length; i++)
        {
            GameObject candle = AlchemistPack.Spawn(root.transform, PackCandles[i]);
            candle.transform.rotation = Quaternion.Euler(0f, yaws[i], 0f) * candle.transform.rotation;
            Bounds b = AlchemistPack.RenderBounds(candle);
            candle.transform.position += root.transform.TransformPoint(spots[i]) - new Vector3(b.center.x, b.min.y, b.center.z);
            b = AlchemistPack.RenderBounds(candle);

            if (!any) { group = b; tallest = b; any = true; }
            else { group.Encapsulate(b); if (b.max.y > tallest.max.y) tallest = b; }
        }

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(group.center);
        box.size = group.size;

        GameObject wick = Primitive(root.transform, PrimitiveType.Sphere, "Flame", Vector3.zero, Vector3.one * 0.02f, flameMat, keepCollider: false);
        wick.transform.position = new Vector3(tallest.center.x, tallest.max.y + 0.006f, tallest.center.z);

        Light light = wick.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.15f);
        light.range = 4f;
        light.intensity = 1f;
        light.shadows = LightShadows.None;

        ThemeInteractable candles = root.AddComponent<ThemeInteractable>();
        SerializedObject so = new SerializedObject(candles);
        so.FindProperty("indicatorLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return candles;
    }

    private static ThemeInteractable BuildCandlesPrimitive(Transform parent, Vector3 localPosition)
    {
        Material waxMat = LoadOrCreateMat("Interactable_CandleWax", new Color(0.72f, 0.66f, 0.5f), null, 0.2f);
        Material flameMat = LoadOrCreateMat("Interactable_CandleFlame", new Color(1f, 0.6f, 0.15f), new Color(1f, 0.6f, 0.15f) * 3f, 0.1f);

        GameObject root = new GameObject("Candles");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        Light flameLight = null;
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 0.08f;
            float height = 0.1f + i * 0.02f;
            GameObject stick = CylinderRH(root.transform, $"Candle{i}", new Vector3(x, height * 0.5f, 0f), 0.015f, height, waxMat, keepCollider: i == 1);
            GameObject flame = Primitive(stick.transform, PrimitiveType.Sphere, "Flame", new Vector3(0f, height * 0.5f + 0.02f, 0f), Vector3.one * 0.03f, flameMat, keepCollider: false);
            if (i == 1)
            {
                Light light = flame.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = flameMat.HasProperty("_EmissionColor") ? flameMat.GetColor("_EmissionColor") : new Color(1f, 0.6f, 0.15f);
                light.range = 4f;
                light.intensity = 1f;
                light.shadows = LightShadows.None;
                flameLight = light;
            }
        }

        ThemeInteractable candles = root.AddComponent<ThemeInteractable>();
        SerializedObject so = new SerializedObject(candles);
        so.FindProperty("indicatorLight").objectReferenceValue = flameLight;
        so.ApplyModifiedPropertiesWithoutUndo();

        return candles;
    }

    private static ThemeInteractable BuildTerminal(Transform parent, Vector3 localPosition)
    {
        Material caseMat = LoadOrCreateMat("Interactable_TerminalCase", new Color(0.18f, 0.19f, 0.2f), null, 0.4f);
        Material screenMat = LoadOrCreateMat("Interactable_TerminalScreen", new Color(0.1f, 0.5f, 0.15f), new Color(0.1f, 0.5f, 0.15f) * 1.6f, 0.2f);

        GameObject root = Primitive(parent, PrimitiveType.Cube, "Terminal", localPosition, new Vector3(0.5f, 0.7f, 0.4f), caseMat, keepCollider: true);
        Primitive(root.transform, PrimitiveType.Quad, "Screen", new Vector3(0f, 0.1f, 0.21f), new Vector3(0.32f, 0.22f, 1f), screenMat, keepCollider: false,
            localRotation: Quaternion.Euler(0f, 180f, 0f));

        return root.AddComponent<ThemeInteractable>();
    }
}
