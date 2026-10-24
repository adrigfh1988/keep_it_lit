using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>F109: restyles the shop signs in place on Assets/Prefabs/ShopRoom.prefab and adds the hanging name board over the counter. Build-missing-only.</summary>
public static partial class WorldSignBuilder
{
    private const string ShopPrefab = "Assets/Prefabs/ShopRoom.prefab";
    private const string ShopNameBoardName = "ShopNameBoard";

    private static (string name, SignStyle style, SignOptions options)[] ShopSigns() => new[]
    {
        // the counter sign hangs from the beams over the counter; ShopRoom pulses its text alpha at runtime (the backing stays steady)
        ("ShopSign", SignStyle.BrassPlaque, new SignOptions { font = SignFont.IMFell, hanging = true, bracket = false, chainLength = 0.6f, darkInset = true, fontSize = 3.2f }),
        // FLOOR n / ASYLUM is rewritten at runtime and AutoFit shrinks it inside the fixed slate
        ("DoorSign", SignStyle.ChalkSlate, new SignOptions { font = SignFont.IMFell, fixedInner = new Vector2(1.6f, 0.62f), fontSize = 3.0f }),
        ("ContractSign", SignStyle.NoticeHeader, new SignOptions { fontSize = 2.5f }),
    };

    public static string ShopProblem()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShopPrefab);
        if (prefab == null) return null;
        if (!FontsPresent()) return "sign fonts missing";
        int n = 0;
        foreach (var s in ShopSigns())
        {
            Transform t = prefab.transform.Find(s.name);
            if (t != null && !IsStyled(t.GetComponent<TextMeshPro>())) n++;
        }
        if (prefab.transform.Find(ShopNameBoardName) == null) n++;
        return n > 0 ? n + " shop sign(s) unstyled / name board missing" : null;
    }

    public static string RestyleShop()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ShopPrefab) == null) return "no shop prefab";
        EnsureFonts();
        GameObject root = PrefabUtility.LoadPrefabContents(ShopPrefab);
        string result;
        try
        {
            result = RestyleShopRoot(root, out int styled, out int boards);
            if (styled + boards > 0) PrefabUtility.SaveAsPrefabAsset(root, ShopPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        Debug.Log("[WorldSigns] " + result);
        return result;
    }

    /// <summary>The restyle itself, on any shop root (the prefab contents, or the scene object a regenerate has just built).</summary>
    public static string RestyleShopRoot(GameObject root, out int styled, out int boards)
    {
        styled = 0; boards = 0;
        {
            foreach (var s in ShopSigns())
            {
                Transform t = root.transform.Find(s.name);
                TextMeshPro tmp = t != null ? t.GetComponent<TextMeshPro>() : null;
                if (tmp == null) { Debug.LogWarning("[WorldSigns] shop sign missing: " + s.name); continue; }
                if (Style(tmp, s.style, s.options) != null) styled++;
            }
            if (root.transform.Find(ShopNameBoardName) == null)
            {
                GameObject go = new GameObject(ShopNameBoardName);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(0f, 2.5f, 0.8f); // hangs from the beams over the room, in front of the counter
                TextMeshPro tmp = go.AddComponent<TextMeshPro>();
                tmp.text = "APOTHECARY";
                tmp.fontSize = 2.1f;
                tmp.characterSpacing = 5f;
                SetLocalizedKey(go, "world.shop.name");
                Style(tmp, SignStyle.WoodBoard, new SignOptions { font = SignFont.Cinzel, hanging = true, bracket = false, chainLength = 0.45f, fontSize = 2.1f });
                boards++;
            }
        }
        return $"Shop signs: {styled} restyled, {boards} name board added";
    }
}
