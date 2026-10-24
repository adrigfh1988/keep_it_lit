using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F112: the iris of a door walk. A full-screen overlay that closes from the screen edges toward the centre (the doorway is always at the
/// screen centre during the walk-in, because the walk keeps its LookAt on it) and opens again at the destination. A soft-edged ring texture
/// is scaled down around the centre; a solid fill takes over for the last 20 % so no hole is left in the middle.
/// </summary>
public class ScreenIris : MonoBehaviour
{
    /// <summary>The ring's scale when open: large enough that the clear middle of the texture covers the whole screen.</summary>
    private const float OpenScale = 6.5f;

    private static ScreenIris _instance;
    private static Texture2D _ring;

    private RectTransform _ringRect;
    private RawImage _ringImage;
    private Image _fill;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _ring = null; // recreated on demand
    }

    /// <summary>The iris, shown (created on first use, reused after) in `colour`, fully open. Null when the scene has no canvas.</summary>
    public static ScreenIris Show(Color colour)
    {
        if (_instance == null)
        {
            Canvas canvas = RuntimeUi.ResolveCanvas();
            if (canvas == null) return null;
            GameObject root = RuntimeUi.CreatePanel(canvas.transform, "ScreenIris", Color.clear);
            root.GetComponent<Image>().raycastTarget = false; // the root only holds the two layers
            ScreenIris iris = root.AddComponent<ScreenIris>();

            GameObject ring = new GameObject("Ring", typeof(RectTransform));
            ring.transform.SetParent(root.transform, false);
            ring.layer = root.layer;
            iris._ringRect = (RectTransform)ring.transform;
            iris._ringRect.anchorMin = iris._ringRect.anchorMax = iris._ringRect.pivot = new Vector2(0.5f, 0.5f);
            iris._ringImage = ring.AddComponent<RawImage>();
            iris._ringImage.texture = Ring();
            iris._ringImage.raycastTarget = false;

            GameObject fill = RuntimeUi.CreatePanel(root.transform, "Fill", Color.clear);
            iris._fill = fill.GetComponent<Image>();
            iris._fill.raycastTarget = false;
            _instance = iris;
        }
        _instance.gameObject.SetActive(true);
        _instance.transform.SetAsLastSibling();
        _instance._ringImage.color = new Color(colour.r, colour.g, colour.b, 1f);
        _instance._fill.color = new Color(colour.r, colour.g, colour.b, 0f);
        _instance.SetClosure(0f);
        return _instance;
    }

    /// <summary>0 = open (nothing covered) ... 1 = fully closed (solid).</summary>
    public void SetClosure(float k)
    {
        k = Mathf.Clamp01(k);
        Canvas canvas = GetComponentInParent<Canvas>();
        Rect screen = canvas != null ? ((RectTransform)canvas.transform).rect : new Rect(0f, 0f, 1920f, 1080f);
        float big = Mathf.Max(screen.width, screen.height); // square, so the ring is round on every aspect
        float scale = Mathf.Lerp(OpenScale, 0f, k);
        _ringRect.sizeDelta = new Vector2(big, big);
        _ringRect.localScale = new Vector3(scale, scale, 1f);
        _ringImage.enabled = scale > 0.001f;
        Color c = _fill.color;
        _fill.color = new Color(c.r, c.g, c.b, Mathf.InverseLerp(0.8f, 1f, k));
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>256x256, white; alpha 0 in the middle rising to 1 at 0.24 of the square's width. The opening is made by scaling it.</summary>
    private static Texture2D Ring()
    {
        if (_ring != null) return _ring;
        const int size = 256;
        _ring = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "ScreenIrisRing",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size - 0.5f, dy = (y + 0.5f) / size - 0.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.1f, 0.24f, d));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        _ring.SetPixels32(pixels);
        _ring.Apply(false, true);
        return _ring;
    }
}
