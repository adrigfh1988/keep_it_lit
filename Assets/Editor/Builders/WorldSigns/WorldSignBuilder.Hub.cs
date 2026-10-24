using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F109: restyles the asylum hub signs in place on the scene hub instance (the wing and shop plaques, the admissions sign, the records board,
/// the stencils, the tallies, the wristband) and then places the room name plates (AsylumHubBuilder.AddRoomPlates). Build-missing-only; the
/// result is applied to Assets/Prefabs/AsylumHub.prefab through AsylumHubBuilder.ApplyHubToPrefab. Never regenerates the hub.
/// </summary>
public static partial class WorldSignBuilder
{
    private static AsylumHub FindHub() => Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);

    /// <summary>Text-only restyle: the font family and lit material, no backing (the sign sits on something that already is its surface).</summary>
    private static bool TextOnly(TextMeshPro t, SignFont font, float sizeScale = 1f, Color? color = null)
    {
        if (t == null || IsStyled(t)) return false;
        ApplyFont(t, font);
        t.fontSize *= sizeScale;
        if (color.HasValue) t.color = color.Value;
        GameObject marker = new GameObject(StyleMarkerName);
        marker.transform.SetParent(t.transform, false);
        EditorUtility.SetDirty(t);
        return true;
    }

    private static void DestroyBacking(Transform parent, string name)
    {
        Transform back = parent != null ? parent.Find(name) : null;
        if (back != null) Object.DestroyImmediate(back.gameObject);
    }

    private static string HubPath(Transform t, Transform root)
    {
        string p = t.name;
        for (Transform c = t.parent; c != null && c != root; c = c.parent) p = c.name + "/" + p;
        return p;
    }

    public static string HubProblem()
    {
        AsylumHub hub = FindHub();
        if (hub == null) return null;
        if (!FontsPresent()) return "sign fonts missing";
        int unstyled = 0;
        foreach (TextMeshPro t in hub.GetComponentsInChildren<TextMeshPro>(true))
        {
            if (t.name.StartsWith("RoomSign_") || t.transform.parent != null && t.transform.parent.name == "RoomSign_") continue;
            if (IsHubSignTarget(t, hub) && !IsStyled(t)) unstyled++;
        }
        string plates = AsylumHubBuilder.PlatesProblem(hub);
        if (unstyled == 0 && plates == null) return null;
        return (unstyled > 0 ? unstyled + " hub sign(s) unstyled" : "") + (unstyled > 0 && plates != null ? ", " : "") + (plates ?? "");
    }

    private static bool IsHubSignTarget(TextMeshPro t, AsylumHub hub)
    {
        string n = t.name, parent = t.transform.parent != null ? t.transform.parent.name : "";
        if (n == "SignText" && parent == "Entrance") return true;
        if (n == "Plaque") return true;
        if (n == "Header" && parent == "RecordsBoard") return true;
        if (n == "Label" && (parent.StartsWith("Slot_") || parent == "Wristband")) return true;
        if (n == "Tally" || n == "TallyMarks" || n == "Tallies" || n == "TalliesScratch") return true;
        if (n == "Stencil") return true;
        return false;
    }

    public static string RestyleHub()
    {
        AsylumHub hub = FindHub();
        if (hub == null) return "no hub in the scene";
        string result = RestyleHubInstance(hub, true, true);
        Debug.Log("[WorldSigns] " + result);
        return result;
    }

    /// <summary>Restyles the signs of a scene hub; optionally places the room plates (needs the Hub NavMesh) and applies the result to the prefab.</summary>
    public static string RestyleHubInstance(AsylumHub hub, bool placePlates, bool applyToPrefab)
    {
        EnsureFonts();
        var log = new StringBuilder();
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        int plaques = 0, texts = 0, stencils = 0, headers = 0, entrance = 0;

        foreach (TextMeshPro t in hub.GetComponentsInChildren<TextMeshPro>(true))
        {
            if (!IsHubSignTarget(t, hub) || IsStyled(t)) continue;
            string n = t.name, parent = t.transform.parent != null ? t.transform.parent.name : "";
            if (n == "SignText" && parent == "Entrance")
            {
                DestroyBacking(t.transform.parent, "Sign"); // the old plain slab behind it
                Style(t, SignStyle.EnamelPlate, new SignOptions { fontSize = 2.0f, noMissingScrew = true });
                entrance++;
            }
            else if (n == "Plaque")
            {
                DestroyBacking(t.transform.parent, "PlaqueBack");
                Style(t, SignStyle.BrassPlaque, new SignOptions { darkInset = true, fixedInner = new Vector2(2.1f, 0.6f), fontSize = 1.7f });
                plaques++;
            }
            else if (n == "Header")
            {
                Style(t, SignStyle.EnamelPlate, new SignOptions { fixedInner = new Vector2(1.9f, 0.3f), fontSize = 1.6f, noMissingScrew = true });
                headers++;
            }
            else if (n == "Label")
            {
                if (TextOnly(t, SignFont.Barlow, 1f)) texts++;
            }
            else if (n == "Stencil")
            {
                Color c = t.color;
                Style(t, SignStyle.StencilSpray, new SignOptions { textColor = c, paint = c });
                stencils++;
            }
            else
            {
                if (TextOnly(t, SignFont.Reenie, 1.1f)) texts++; // tallies: scratched-in numbers and marks
            }
        }
        log.AppendLine($"hub signs: entrance {entrance}, plaques {plaques}, header {headers}, text-only {texts}, stencils {stencils}");
        if (placePlates) log.Append(AsylumHubBuilder.AddRoomPlates(hub));

        if (!wasActive) hub.SetContentActive(false);
        if (applyToPrefab) AsylumHubBuilder.ApplyHubToPrefab(hub);
        return log.ToString();
    }
}
