using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// F106: the floor map, the loop puzzle's reward. A centred panel with a picture of the maze drawn from MazeGenerator.BuildMapData: the walls on
/// parchment, the doors as coloured bars (red locked, amber shut, green open), the stars, the keys, the switches in their door's colour, the open
/// hatch or lift, and the player as an arrow showing where they face. The hunter is never on it. Like LoreReadingUi it does not pause time - the
/// maze keeps moving while you read it. Locked until the loop corridor calls <see cref="Unlock"/> (instance state, so a floor reload resets it);
/// then M or the gamepad's View button opens it, M / Esc / B / a tap close it, and the pause menu offers a MAP button. It never opens in the hub,
/// the shop, the Intake or the loop itself. The picture is one reused texture and pixel buffer, redrawn when it opens and twice a second after.
/// </summary>
public class FloorMapUi : MonoBehaviour
{
    [Header("F106 - the floor map")]
    [Tooltip("Pixels per maze cell in the picture")]
    [SerializeField, Min(8)] private int cellPixels = 24;
    [Tooltip("Seconds between redraws while the map is open (the player and the doors move)")]
    [SerializeField, Min(0.1f)] private float refreshSeconds = 0.5f;
    [Tooltip("Longest side of the picture on the 1920 x 1080 canvas (px)")]
    [SerializeField, Min(200f)] private float pictureSize = 480f;
    [Tooltip("Gap between the map and the top-right corner of the 1920 x 1080 canvas (px)")]
    [SerializeField, Min(0f)] private float cornerMargin = 32f;
    [Tooltip("Seconds the 'M MAP' hint stays on the prompt line the first time the map can be opened")]
    [SerializeField, Min(1f)] private float hintSeconds = 4f;
    [SerializeField] private Color paper = new Color(0.70f, 0.66f, 0.56f);
    [SerializeField] private Color wall = new Color(0.10f, 0.08f, 0.07f);
    [SerializeField] private Color doorLocked = new Color(0.80f, 0.12f, 0.10f);
    [SerializeField] private Color doorShut = new Color(0.90f, 0.60f, 0.12f);
    [SerializeField] private Color doorOpen = new Color(0.20f, 0.70f, 0.28f);
    [SerializeField] private Color starColor = new Color(1f, 0.85f, 0.15f);
    [SerializeField] private Color keyColor = new Color(0.72f, 0.50f, 0.10f);
    [SerializeField] private Color hatchColor = new Color(0.15f, 0.80f, 0.35f);
    [SerializeField] private Color playerColor = new Color(0.95f, 0.95f, 0.98f);

    private const float PanelPadding = 20f;
    private const float FramePixels = 6f;

    private MazeGenerator _maze;
    private PlayerHud _hud;
    private GameOutcome _outcome;
    private PauseMenu _pause;

    private MazeMapData _data;
    private Texture2D _texture;
    private Color32[] _pixels;
    private Color32[] _walls;
    private GameObject _panel;
    private int _width;
    private int _height;
    private int _pad;
    private int _wallThickness;
    private float _nextRefresh;
    private int _closedFrame = -1;
    private bool _hintPending;

    /// <summary>True once the loop corridor has handed it over.</summary>
    public bool Unlocked { get; private set; }

    /// <summary>True while the panel is showing.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True on the frame it closed, so the Esc that closed it does not also pause (the same rule as the shop).</summary>
    public bool ClosedThisFrame => _closedFrame == Time.frameCount;

    /// <summary>True when the map is unlocked and the player is in a maze run it could be opened in (the pause menu shows its MAP button on this).</summary>
    public bool Available => Unlocked && GameFlow.IsRunActive && !GameOutcome.IsOver && (_outcome == null || !_outcome.IsEnding)
        && (_maze == null || _maze.Loop == null || !_maze.Loop.IsInside);

    /// <summary>Called once by MazeGenerator.SetUpAtmosphere. `maze` supplies the data; the others may be null.</summary>
    public void Configure(MazeGenerator maze, PlayerHud hud, GameOutcome outcome, PauseMenu pause)
    {
        _maze = maze;
        _hud = hud;
        _outcome = outcome;
        _pause = pause;
    }

    /// <summary>The loop corridor's reward: the map can be opened from now on, and the next time it can be, the hint says how.</summary>
    public void Unlock()
    {
        if (Unlocked) return;

        Unlocked = true;
        _hintPending = true;
    }

    /// <summary>Shows the map. Does nothing unless it is unlocked and a maze run is going.</summary>
    public void Open()
    {
        if (IsOpen || !Available || (_pause != null && _pause.IsPaused)) return;

        if (_panel == null && !BuildPanel()) return;

        IsOpen = true;
        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();
        Refresh();
    }

    /// <summary>Hides the map.</summary>
    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        _closedFrame = Time.frameCount;
        if (_panel != null) _panel.SetActive(false);
    }

    private void Update()
    {
        if (_hintPending && Available && _hud != null)
        {
            _hintPending = false;
            _hud.ShowHintPrompt(TouchInput.Key("M", L10n.T("ui.pause.map")), "hud.prompt.map", hintSeconds);
        }

        if (IsOpen && (!Available || (_pause != null && _pause.IsPaused)))
        {
            Close();
            return;
        }

        if (IsOpen)
        {
            if (ClosePressed()) Close();
            else if (Time.unscaledTime >= _nextRefresh) Refresh();
        }
        else if (OpenPressed())
        {
            Open();
        }
    }

    private static bool OpenPressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) return true;
        if (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame) return true;
#endif
        return false;
    }

    private static bool ClosePressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && (Keyboard.current.mKey.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame)) return true;
        if (Gamepad.current != null && (Gamepad.current.selectButton.wasPressedThisFrame || Gamepad.current.buttonEast.wasPressedThisFrame)) return true;
#endif
        return TouchInput.Pressed(TouchInput.TouchAction.Pause);
    }

    private void OnDestroy()
    {
        if (_texture != null) Destroy(_texture);
    }

    // ---------------------------------------------------------------- the panel

    /// <summary>The panel, its picture and a tap-to-close button, sized from the maze. False when there is no canvas or no maze yet.</summary>
    private bool BuildPanel()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        _data = _maze != null ? _maze.BuildMapData(_data) : null;
        if (canvas == null || _data == null) return false;

        _pad = cellPixels / 2;
        _wallThickness = Mathf.Max(2, cellPixels / 8);
        _width = _data.Width * cellPixels + _pad * 2;
        _height = _data.Height * cellPixels + _pad * 2;
        _pixels = new Color32[_width * _height];
        _walls = new Color32[_width * _height];
        _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false) { name = "FloorMap", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        DrawWalls();

        float scale = pictureSize / Mathf.Max(_width, _height);
        Vector2 picture = new Vector2(_width * scale, _height * scale);

        _panel = RuntimeUi.CreatePanel(canvas.transform, "FloorMapPanel", new Color(0.02f, 0.02f, 0.02f, 0.88f));
        // Top-right corner, out of the way of the HUD counter (top-left) and the prompts (bottom).
        RectTransform panelRect = _panel.GetComponent<RectTransform>();
        RuntimeUi.Place(panelRect, new Vector2(1f, 1f), Vector2.zero, picture + Vector2.one * PanelPadding);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-cornerMargin, -cornerMargin);
        Button tap = _panel.AddComponent<Button>();
        tap.targetGraphic = _panel.GetComponent<Image>();
        tap.transition = Selectable.Transition.None;
        tap.onClick.AddListener(Close);

        GameObject map = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
        map.transform.SetParent(_panel.transform, false);
        RuntimeUi.Place(map.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), Vector2.zero, picture);
        RawImage image = map.GetComponent<RawImage>();
        image.texture = _texture;
        image.raycastTarget = false;

        _panel.SetActive(false);
        return true;
    }

    /// <summary>Redraws the picture from a fresh snapshot, reusing the buffers.</summary>
    private void Refresh()
    {
        _nextRefresh = Time.unscaledTime + refreshSeconds;
        _data = _maze.BuildMapData(_data);
        System.Array.Copy(_walls, _pixels, _pixels.Length);
        DrawDoors();
        DrawHatch();
        DrawStars();
        DrawKeys();
        DrawButtons();
        DrawPlayer();
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
    }

    // ---------------------------------------------------------------- drawing

    private void DrawWalls()
    {
        Color32 paperColor = paper;
        for (int i = 0; i < _walls.Length; i++) _walls[i] = paperColor;

        Color32 frame = wall;
        for (int x = 0; x < _data.Width; x++)
        {
            for (int z = 0; z < _data.Height; z++)
            {
                if (_data.IsWallClosed(x, z, 0)) EdgeBar(_walls, x, z, 0, 0, _wallThickness, frame);
                if (_data.IsWallClosed(x, z, 1)) EdgeBar(_walls, x, z, 1, 0, _wallThickness, frame);
                if (_data.IsWallClosed(x, z, 2)) EdgeBar(_walls, x, z, 2, 0, _wallThickness, frame);
                if (_data.IsWallClosed(x, z, 3)) EdgeBar(_walls, x, z, 3, 0, _wallThickness, frame);
            }
        }
    }

    private void DrawDoors()
    {
        foreach (MazeMapData.DoorInfo door in _data.Doors)
        {
            EdgeBar(_pixels, door.CellA.x, door.CellA.y, door.Dir, cellPixels / 6, _wallThickness + 2, DoorColor(door));
        }
    }

    private Color32 DoorColor(MazeMapData.DoorInfo door)
    {
        return !door.Unlocked ? doorLocked : door.Open ? doorOpen : doorShut;
    }

    private void DrawHatch()
    {
        if (_data.HatchOpen) Disc(CellCentre(_data.HatchCell), cellPixels * 0.38f, hatchColor);
    }

    private void DrawStars()
    {
        foreach (Vector2Int cell in _data.Stars) Diamond(CellCentre(cell), cellPixels * 0.3f, starColor);
    }

    private void DrawKeys()
    {
        foreach (MazeMapData.KeyInfo key in _data.Keys)
        {
            if (!key.Taken) Square(CellCentre(key.Cell) + new Vector2(cellPixels * 0.22f, -cellPixels * 0.22f), cellPixels * 0.14f, keyColor);
        }
    }

    /// <summary>A switch is a small square in the colour of the door it opens (grey when the map does not know it).</summary>
    private void DrawButtons()
    {
        foreach (MazeMapData.ButtonInfo button in _data.Buttons)
        {
            Color32 colour = new Color32(120, 120, 120, 255);
            foreach (MazeMapData.DoorInfo door in _data.Doors)
            {
                if (door.Id == button.DoorId) colour = DoorColor(door);
            }

            Vector2 centre = CellCentre(button.Cell) + new Vector2(-cellPixels * 0.22f, -cellPixels * 0.22f);
            Square(centre, cellPixels * 0.14f + 1f, wall);
            Square(centre, cellPixels * 0.14f - 1f, colour);
        }
    }

    /// <summary>The player: a pale arrow on a dark one, pointing the way they face (north is up).</summary>
    private void DrawPlayer()
    {
        if (!_data.HasPlayer) return;

        Vector2 centre = new Vector2(_pad + _data.PlayerPosition.x * cellPixels, _pad + _data.PlayerPosition.y * cellPixels);
        float yaw = _data.PlayerYaw * Mathf.Deg2Rad;
        Vector2 forward = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
        Arrow(centre, forward, cellPixels * 0.55f, wall);
        Arrow(centre, forward, cellPixels * 0.42f, playerColor);
    }

    private Vector2 CellCentre(Vector2Int cell)
    {
        return new Vector2(_pad + (cell.x + 0.5f) * cellPixels, _pad + (cell.y + 0.5f) * cellPixels);
    }

    /// <summary>A bar along side `dir` (0 north, 1 east, 2 south, 3 west) of a cell, `inset` pixels short at each end.</summary>
    private void EdgeBar(Color32[] target, int x, int z, int dir, int inset, int thickness, Color32 colour)
    {
        int left = _pad + x * cellPixels;
        int bottom = _pad + z * cellPixels;
        int half = thickness / 2;
        switch (dir)
        {
            case 0: Rect(target, left + inset - half, bottom + cellPixels - half, left + cellPixels - inset + half, bottom + cellPixels + thickness - half, colour); break;
            case 2: Rect(target, left + inset - half, bottom - half, left + cellPixels - inset + half, bottom + thickness - half, colour); break;
            case 1: Rect(target, left + cellPixels - half, bottom + inset - half, left + cellPixels + thickness - half, bottom + cellPixels - inset + half, colour); break;
            default: Rect(target, left - half, bottom + inset - half, left + thickness - half, bottom + cellPixels - inset + half, colour); break;
        }
    }

    /// <summary>Fills the pixels in [x0, x1) x [y0, y1), clipped to the picture.</summary>
    private void Rect(Color32[] target, int x0, int y0, int x1, int y1, Color32 colour)
    {
        x0 = Mathf.Max(x0, 0);
        y0 = Mathf.Max(y0, 0);
        x1 = Mathf.Min(x1, _width);
        y1 = Mathf.Min(y1, _height);
        for (int y = y0; y < y1; y++)
        {
            int row = y * _width;
            for (int x = x0; x < x1; x++) target[row + x] = colour;
        }
    }

    private void Square(Vector2 centre, float radius, Color32 colour)
    {
        Rect(_pixels, Mathf.RoundToInt(centre.x - radius), Mathf.RoundToInt(centre.y - radius), Mathf.RoundToInt(centre.x + radius), Mathf.RoundToInt(centre.y + radius), colour);
    }

    private void Disc(Vector2 centre, float radius, Color32 colour)
    {
        Stamp(Glyph.Disc, centre, radius, colour, default, default, default);
    }

    private void Diamond(Vector2 centre, float radius, Color32 colour)
    {
        Stamp(Glyph.Diamond, centre, radius, colour, default, default, default);
    }

    /// <summary>A triangle with its tip `size` ahead of `centre` along `forward` and its base `size` * 0.6 behind.</summary>
    private void Arrow(Vector2 centre, Vector2 forward, float size, Color32 colour)
    {
        Vector2 side = new Vector2(-forward.y, forward.x);
        Vector2 tip = centre + forward * size;
        Vector2 left = centre - forward * (size * 0.6f) + side * (size * 0.7f);
        Vector2 right = centre - forward * (size * 0.6f) - side * (size * 0.7f);
        Stamp(Glyph.Triangle, centre, size * 1.2f, colour, tip, left, right);
    }

    private enum Glyph
    {
        Disc,
        Diamond,
        Triangle
    }

    /// <summary>Fills the pixels around `centre` (within `radius`) that fall inside the glyph; a triangle uses its three corners.</summary>
    private void Stamp(Glyph glyph, Vector2 centre, float radius, Color32 colour, Vector2 a, Vector2 b, Vector2 c)
    {
        int x0 = Mathf.Max(Mathf.FloorToInt(centre.x - radius), 0);
        int x1 = Mathf.Min(Mathf.CeilToInt(centre.x + radius), _width - 1);
        int y0 = Mathf.Max(Mathf.FloorToInt(centre.y - radius), 0);
        int y1 = Mathf.Min(Mathf.CeilToInt(centre.y + radius), _height - 1);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float dx = x - centre.x;
                float dy = y - centre.y;
                bool inside = glyph == Glyph.Disc ? dx * dx + dy * dy <= radius * radius
                    : glyph == Glyph.Diamond ? Mathf.Abs(dx) + Mathf.Abs(dy) <= radius
                    : InTriangle(new Vector2(x, y), a, b, c);
                if (inside) _pixels[y * _width + x] = colour;
            }
        }
    }

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b);
        float d2 = Cross(p, b, c);
        float d3 = Cross(p, c, a);
        bool negative = d1 < 0f || d2 < 0f || d3 < 0f;
        bool positive = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(negative && positive);
    }

    private static float Cross(Vector2 p, Vector2 a, Vector2 b)
    {
        return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
