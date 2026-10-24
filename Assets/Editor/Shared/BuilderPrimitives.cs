using UnityEngine;

/// <summary>
/// Shared primitive geometry and light creation helpers used across KEEP IT LIT editor builders.
/// </summary>
public static class BuilderPrimitives
{
    public static Transform Group(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    public static GameObject Primitive(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 localScale,
        Material material, bool keepCollider = false, Quaternion? localRotation = null)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation ?? Quaternion.identity;
        go.transform.localScale = localScale;
        if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;

        if (!keepCollider)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }
        return go;
    }

    public static GameObject Box(Transform parent, string name, Vector3 localPosition, Vector3 size, Material material, bool keepCollider = false)
    {
        return Primitive(parent, PrimitiveType.Cube, name, localPosition, size, material, keepCollider);
    }

    /// <summary>The built-in cylinder is 2 units tall with a 1-unit diameter at scale 1, so this converts a radius/height pair into that scale.</summary>
    public static GameObject CylinderRH(Transform parent, string name, Vector3 localPosition, float radius, float height, Material material, bool keepCollider = false)
    {
        return Primitive(parent, PrimitiveType.Cylinder, name, localPosition, new Vector3(radius * 2f, height * 0.5f, radius * 2f), material, keepCollider);
    }

    public static GameObject CylinderBetween(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material material)
    {
        Vector3 delta = b - a;
        float length = delta.magnitude;
        Quaternion rot = length > 0.0001f ? Quaternion.FromToRotation(Vector3.up, delta / length) : Quaternion.identity;
        return Primitive(parent, PrimitiveType.Cylinder, name, (a + b) * 0.5f, new Vector3(radius * 2f, length * 0.5f, radius * 2f), material, localRotation: rot);
    }

    public static GameObject Sphere(Transform parent, string name, Vector3 localPosition, float radius, Material material)
    {
        return Primitive(parent, PrimitiveType.Sphere, name, localPosition, Vector3.one * (radius * 2f), material);
    }

    public static GameObject Capsule(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material, Quaternion? localRotation = null)
    {
        return Primitive(parent, PrimitiveType.Capsule, name, localPosition, localScale, material, localRotation: localRotation);
    }

    public static Light PointLight(Transform parent, string name, Vector3 localPosition, Color color, float range, float intensity)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        return light;
    }
}
