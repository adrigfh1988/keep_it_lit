using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F106/F110: the floor map. A panel in the top-right corner with a hand-inked picture of the maze built from MazeGenerator.BuildMapData: walls in
/// sepia ink on aged parchment, the doors as coloured bars (red locked with a padlock dot, amber shut, green open as a gap between two ticks) and
/// their switches as rings in the door's colour. <b>Fog of war:</b> only cells the player has walked are inked (the walls between a walked cell and
/// the next are drawn, you saw them); stars, keys, fuse boxes and the hatch are never on it, and neither is the hunter or any other entity. The
/// player is a separate arrow image turned by the body's yaw every frame. The map is locked until the player uses a Floor Plan (hold M or the
/// gamepad's View for a second, or the pause menu's MAP button) or the loop corridor hands it over (which also reveals every cell); either way it
/// is then on for the rest of that floor (instance state, so a floor reload resets it). M / View toggles it, M / Esc / B / a tap close it. Like
/// LoreReadingUi it does not pause time. It never opens in the hub, the shop, the Intake or the loop itself. The picture is one reused texture;
/// the paper and the ink are baked once into cached layers, so a redraw (when it opens, twice a second after, and when a cell is revealed) only
/// copies the ink layer and draws the doors.
/// </summary>
public class FloorMapUi : MonoBehaviour
{
    [Header("F106 - the floor map")]
    [Tooltip("Longest side of the picture on the 1920 x 1080 canvas (px). The cell size is chosen so the picture shows at scale 1 (even walls)")]
    [SerializeField, Min(200f)] private float pictureSize = 480f;
    [Tooltip("Seconds between redraws while the map is open (the doors move)")]
    [SerializeField, Min(0.1f)] private float refreshSeconds = 0.5f;
    [Tooltip("Gap between the map and the top-right corner of the 1920 x 1080 canvas (px)")]
    [SerializeField, Min(0f)] private float cornerMargin = 32f;
    [Tooltip("Seconds the 'M MAP' hint stays on the prompt line the first time the map can be opened")]
    [SerializeField, Min(1f)] private float hintSeconds = 4f;

    [Header("F110 - the Floor Plan")]
    [Tooltip("Seconds M / View must be held to use a Floor Plan")]
    [SerializeField, Min(0.2f)] private float holdSeconds = 1f;
    [Tooltip("Fade / scale-in time of the panel (unscaled seconds)")]
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.15f;

    [Header("Look")]
    [SerializeField] private Color paper = new Color(0.56f, 0.48f, 0.35f);
    [SerializeField] private Color ink = new Color(0.14f, 0.085f, 0.05f);
    [SerializeField] private Color doorLocked = new Color(0.78f, 0.13f, 0.10f);
    [SerializeField] private Color doorShut = new Color(0.93f, 0.62f, 0.10f);
    [SerializeField] private Color doorOpen = new Color(0.17f, 0.62f, 0.26f);

    private const float FramePadding = 12f;
    private const int MinCellPixels = 12;
    private const int MaxCellPixels = 48;

    private MazeGenerator _maze;
    private PlayerHud _hud;
    private GameOutcome _outcome;
    private PauseMenu _pause;

    private MazeMapData _data;
    private Texture2D _texture;
    private Color32[] _pixels;
    private Color32[] _paper;
    private Color32[] _walls;
    private byte[] _ink;
    private bool[,] _visited;
    private bool[,] _inked;
    private GameObject _panel;
    private CanvasGroup _group;
    private RectTransform _arrow;
    private int _width;
    private int _height;
    private int _pad;
    private int _cell;
    private int _wallThickness;
    private int _seed;
    private float _nextRefresh;
    private float _fade;
    private float _hold;
    private bool _holdSpent;
    private bool _dirty;
    private int _closedFrame = -1;
    private bool _hintPending;
    private bool _openPending;
    private bool _planHintShown;

    /// <summary>True once the map is on for this floor (a used Floor Plan or the loop corridor's reward).</summary>
    public bool Unlocked { get; private set; }

    /// <summary>True while the panel is showing.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True on the frame it closed, so the Esc that closed it does not also pause (the same rule as the shop).</summary>
    public bool ClosedThisFrame => _closedFrame == Time.frameCount;

    /// <summary>True when the map is unlocked and the player is in a maze run it could be opened in (the pause menu shows its MAP button on this).</summary>
    public bool Available => Unlocked && InMazeRun;

    /// <summary>F110: a Floor Plan is held, the map is still locked and a maze run is going: holding M / View (or the pause menu's MAP button) uses it.</summary>
    public bool CanUsePlan => !Unlocked && InMazeRun && PlayerInventory.Count(ShopItem.FloorPlan) > 0;

    private bool InMazeRun => GameFlow.IsRunActive && !GameFlow.IsInSafeRoom && !GameOutcome.IsOver && (_outcome == null || !_outcome.IsEnding)
        && (_maze == null || _maze.Loop == null || !_maze.Loop.IsInside);

    /// <summary>Called once by MazeGenerator.SetUpAtmosphere. `maze` supplies the data; the others may be null.</summary>
    public void Configure(MazeGenerator maze, PlayerHud hud, GameOutcome outcome, PauseMenu pause)
    {
        _maze = maze;
        _hud = hud;
        _outcome = outcome;
        _pause = pause;
    }

    /// <summary>The map can be opened from now on. `announce` shows the "M MAP" hint the next time it can be opened (the loop corridor's reward).</summary>
    public void Unlock(bool announce = true)
    {
        if (Unlocked) return;

        Unlocked = true;
        _hintPending = announce;
    }

    /// <summary>F110: marks every playable cell as walked, so the whole layout (walls, doors, switches - never objectives or void cells) shows. Does not unlock by itself.</summary>
    public void RevealAll()
    {
        if (!EnsureMasks()) return;

        for (int x = 0; x < _visited.GetLength(0); x++)
        {
            for (int z = 0; z < _visited.GetLength(1); z++)
            {
                if (_maze != null && _maze.IsVoidCell(x, z)) continue;
                _visited[x, z] = true;
            }
        }
        _dirty = true;
    }

    /// <summary>Opens the map immediately if in a maze run, or on the first frame the run becomes active.</summary>
    public void OpenWhenAvailable()
    {
        if (Available && (_pause == null || !_pause.IsPaused)) Open();
        else _openPending = true;
    }

    /// <summary>F110: spends the held Floor Plan, turns the map on for this floor and opens it. Does nothing unless <see cref="CanUsePlan"/>.</summary>
    public void UsePlan()
    {
        if (!CanUsePlan || !PlayerInventory.TryConsume(ShopItem.FloorPlan)) return;

        Unlock(false);
        if (_hud != null)
        {
            _hud.PulseSlot(2);
            _hud.ShowSubtitle(L10n.T("sub.map.used"), 3.5f);
        }
        Open();
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
        ApplyFade();
    }

    /// <summary>Hides the map.</summary>
    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        _closedFrame = Time.frameCount;
    }

    private void Update()
    {
        TrackVisited();
        UpdateFade();

        if (_hintPending && Available && _hud != null)
        {
            _hintPending = false;
            _hud.ShowHintPrompt(GameAction.Map, L10n.T("ui.pause.map"), "hud.prompt.map", hintSeconds);
        }

        if (_openPending && Available && (_pause == null || !_pause.IsPaused))
        {
            _openPending = false;
            Open();
        }

        if (CanUsePlan && !_planHintShown && _hud != null)
        {
            _planHintShown = true;
            _hud.ShowHintPrompt(GameAction.Map, L10n.T("ui.pause.map"), "hud.prompt.use_floor_plan", hintSeconds);
        }

        if (IsOpen && (!Available || (_pause != null && _pause.IsPaused)))
        {
            Close();
            return;
        }

        if (IsOpen)
        {
            if (ClosePressed()) Close();
            else
            {
                if (_dirty || Time.unscaledTime >= _nextRefresh) Refresh();
                UpdateArrow();
            }
        }
        else if (Unlocked)
        {
            if (OpenPressed()) Open();
        }
        else
        {
            UpdateHold();
        }
    }

    /// <summary>Hold M / View for <see cref="holdSeconds"/> with a plan in hand: the plan is used. A tap does nothing, so it cannot be wasted by accident.</summary>
    private void UpdateHold()
    {
        bool holding = CanUsePlan && HoldDown() && (_pause == null || !_pause.IsPaused);
        if (!holding)
        {
            _hold = 0f;
            _holdSpent = false;
            return;
        }
        if (_holdSpent) return;

        _hold += Time.unscaledDeltaTime;
        if (_hold >= holdSeconds)
        {
            _holdSpent = true;
            UsePlan();
        }
        else if (_hud != null)
        {
            _hud.ShowHintPrompt(GameAction.Map, L10n.T("ui.pause.map"), "hud.prompt.use_floor_plan", 0.3f, Mathf.RoundToInt(_hold / holdSeconds * 100f));
        }
    }

    private static bool HoldDown() => GameInput.Held(GameAction.Map);

    private static bool OpenPressed() => GameInput.Pressed(GameAction.Map);

    // F119: M again, Esc / pad B (MenuCancel, which also ORs the touch Pause button) closes it. Esc is also the Pause action, so MenuCancel alone covers it.
    private static bool ClosePressed() => GameInput.Pressed(GameAction.Map) || GameInput.Pressed(GameAction.MenuCancel);

    private void OnDestroy()
    {
        if (_texture != null) Destroy(_texture);
    }

    // ---------------------------------------------------------------- fog of war

    private bool EnsureMasks()
    {
        if (_maze == null) return false;

        int w = _maze.GridWidth;
        int h = _maze.GridHeight;
        if (w <= 0 || h <= 0) return false;
        if (_visited == null || _visited.GetLength(0) != w || _visited.GetLength(1) != h)
        {
            _visited = new bool[w, h];
            _inked = new bool[w, h];
        }
        return true;
    }

    /// <summary>Marks the player's cell as walked, every frame of a run (so a plan used later shows what was already walked).</summary>
    private void TrackVisited()
    {
        if (!GameFlow.IsRunActive || _maze == null || !EnsureMasks()) return;
        if (!_maze.TryGetPlayerOnMap(out Vector2 position, out _)) return;

        int x = Mathf.Clamp(Mathf.FloorToInt(position.x), 0, _visited.GetLength(0) - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(position.y), 0, _visited.GetLength(1) - 1);
        if (_visited[x, z]) return;

        _visited[x, z] = true;
        _dirty = true;
    }

    /// <summary>Inks every walked cell that is not on the cached ink layer yet.</summary>
    private void SyncInk()
    {
        for (int x = 0; x < _visited.GetLength(0); x++)
        {
            for (int z = 0; z < _visited.GetLength(1); z++)
            {
                if (!_visited[x, z] || _inked[x, z]) continue;
                _inked[x, z] = true;
                InkCell(x, z);
            }
        }
    }

    private bool Revealed(Vector2Int cell)
    {
        return cell.x >= 0 && cell.y >= 0 && cell.x < _visited.GetLength(0) && cell.y < _visited.GetLength(1) && _visited[cell.x, cell.y];
    }

    // ---------------------------------------------------------------- the panel

    /// <summary>The panel, its picture, the player's arrow and a tap-to-close button, sized from the maze. False when there is no canvas or no maze yet.</summary>
    private bool BuildPanel()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        _data = _maze != null ? _maze.BuildMapData(_data) : null;
        if (canvas == null || _data == null || !EnsureMasks()) return false;

        // Pixels per cell chosen so the whole picture is at most pictureSize canvas pixels and shows at scale 1.
        int longest = Mathf.Max(_data.Width, _data.Height);
        _cell = Mathf.Clamp(Mathf.FloorToInt(pictureSize / (longest + 1)), MinCellPixels, MaxCellPixels) / 2 * 2;
        _pad = _cell / 2;
        _wallThickness = Mathf.Max(2, _cell / 9);
        _width = _data.Width * _cell + _pad * 2;
        _height = _data.Height * _cell + _pad * 2;
        _seed = _data.Seed;
        _pixels = new Color32[_width * _height];
        _paper = new Color32[_width * _height];
        _walls = new Color32[_width * _height];
        _ink = new byte[_width * _height];
        for (int x = 0; x < _inked.GetLength(0); x++)
        {
            for (int z = 0; z < _inked.GetLength(1); z++) _inked[x, z] = false;
        }
        _texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false) { name = "FloorMap", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        PaintPaper();
        System.Array.Copy(_paper, _walls, _walls.Length);

        Vector2 picture = new Vector2(_width, _height);

        _panel = RuntimeUi.CreatePanel(canvas.transform, "FloorMapPanel", new Color(0.045f, 0.035f, 0.025f, 0.94f));
        // Top-right corner, out of the way of the HUD counter (top-left) and the prompts (bottom).
        RectTransform panelRect = _panel.GetComponent<RectTransform>();
        RuntimeUi.Place(panelRect, new Vector2(1f, 1f), Vector2.zero, picture + Vector2.one * FramePadding * 2f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-cornerMargin, -cornerMargin);
        _group = _panel.AddComponent<CanvasGroup>();
        Button tap = _panel.AddComponent<Button>();
        tap.targetGraphic = _panel.GetComponent<Image>();
        tap.transition = Selectable.Transition.None;
        tap.onClick.AddListener(Close);

        // A thin brass rim between the dark frame and the paper.
        GameObject rim = new GameObject("Rim", typeof(RectTransform), typeof(Image));
        rim.transform.SetParent(_panel.transform, false);
        RuntimeUi.Place(rim.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), Vector2.zero, picture + Vector2.one * 4f);
        Image rimImage = rim.GetComponent<Image>();
        rimImage.color = new Color(0.36f, 0.26f, 0.14f, 1f);
        rimImage.raycastTarget = false;

        GameObject map = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
        map.transform.SetParent(_panel.transform, false);
        RuntimeUi.Place(map.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), Vector2.zero, picture);
        RawImage image = map.GetComponent<RawImage>();
        image.texture = _texture;
        image.raycastTarget = false;

        GameObject arrow = new GameObject("Player", typeof(RectTransform), typeof(Image));
        arrow.transform.SetParent(map.transform, false);
        _arrow = arrow.GetComponent<RectTransform>();
        _arrow.anchorMin = Vector2.zero;
        _arrow.anchorMax = Vector2.zero;
        _arrow.pivot = new Vector2(0.5f, 0.5f);
        float arrowSize = Mathf.Max(20f, _cell * 0.95f);
        _arrow.sizeDelta = new Vector2(arrowSize, arrowSize);
        Image arrowImage = arrow.GetComponent<Image>();
        arrowImage.sprite = BuildArrowSprite();
        arrowImage.raycastTarget = false;

        _panel.SetActive(false);
        return true;
    }

    /// <summary>Redraws the picture from a fresh snapshot: the cached ink, then the doors and switches of the walked cells.</summary>
    private void Refresh()
    {
        _nextRefresh = Time.unscaledTime + refreshSeconds;
        _dirty = false;
        _data = _maze.BuildMapData(_data);
        SyncInk();
        System.Array.Copy(_walls, _pixels, _pixels.Length);
        DrawDoors();
        DrawButtons();
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
    }

    /// <summary>The player's arrow follows the body every frame, so the picture itself needs no redraw for movement.</summary>
    private void UpdateArrow()
    {
        if (_arrow == null) return;

        bool on = _maze != null && _maze.TryGetPlayerOnMap(out Vector2 position, out float yaw);
        if (_arrow.gameObject.activeSelf != on) _arrow.gameObject.SetActive(on);
        if (!on) return;

        _maze.TryGetPlayerOnMap(out position, out yaw);
        _arrow.anchoredPosition = new Vector2(_pad + position.x * _cell, _pad + position.y * _cell);
        _arrow.localRotation = Quaternion.Euler(0f, 0f, -yaw);
    }

    private void UpdateFade()
    {
        if (_panel == null) return;

        float target = IsOpen ? 1f : 0f;
        if (!Mathf.Approximately(_fade, target)) _fade = Mathf.MoveTowards(_fade, target, Time.unscaledDeltaTime / fadeSeconds);
        ApplyFade();
        if (!IsOpen && _fade <= 0f && _panel.activeSelf) _panel.SetActive(false);
    }

    private void ApplyFade()
    {
        if (_group == null) return;

        float eased = _fade * _fade * (3f - 2f * _fade);
        _group.alpha = eased;
        _panel.transform.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);
    }

    // ---------------------------------------------------------------- the paper

    private static float Hash01(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private static float ValueNoise(float x, float y, int salt)
    {
        int xi = Mathf.FloorToInt(x);
        int yi = Mathf.FloorToInt(y);
        float fx = x - xi;
        float fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Hash01(xi, yi, salt);
        float b = Hash01(xi + 1, yi, salt);
        float c = Hash01(xi, yi + 1, salt);
        float d = Hash01(xi + 1, yi + 1, salt);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    /// <summary>
    /// Aged parchment, generated once per panel: mottling at two scales, short fibres, a few stains, burnt darker edges and a faint dashed pencil grid
    /// over the extent of the floor (so the size of the floor reads without showing a single wall).
    /// </summary>
    private void PaintPaper()
    {
        int salt = _seed * 31 + 7;
        float edgeSpan = Mathf.Max(20f, _cell * 1.1f);
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                float coarse = ValueNoise(x * 0.022f, y * 0.022f, salt);
                float fine = ValueNoise(x * 0.13f, y * 0.13f, salt + 1);
                float grain = Hash01(x, y, salt + 2);
                float tone = 0.80f + coarse * 0.32f + (fine - 0.5f) * 0.14f + (grain - 0.5f) * 0.05f;

                int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(_width - 1 - x, _height - 1 - y));
                float burn = Mathf.SmoothStep(0f, 1f, edge / edgeSpan);
                // Rough the edge so it burns unevenly.
                burn = Mathf.Clamp01(burn + (ValueNoise(x * 0.09f, y * 0.09f, salt + 3) - 0.5f) * 0.35f);
                tone *= 0.50f + 0.50f * burn;

                Color c = new Color(paper.r * tone, paper.g * tone, paper.b * tone * (0.85f + 0.15f * burn));
                _paper[y * _width + x] = c;
            }
        }

        System.Random rng = new System.Random(salt);
        // Fibres.
        int fibres = _width * _height / 1400;
        for (int i = 0; i < fibres; i++)
        {
            float fx = (float)rng.NextDouble() * _width;
            float fy = (float)rng.NextDouble() * _height;
            float angle = (float)rng.NextDouble() * Mathf.PI;
            int length = 5 + rng.Next(10);
            float shade = 0.90f + (float)rng.NextDouble() * 0.05f;
            for (int s = 0; s < length; s++)
            {
                Multiply((int)(fx + Mathf.Cos(angle) * s), (int)(fy + Mathf.Sin(angle) * s), shade);
            }
        }

        // Stains: soft, uneven blotches.
        int stains = 2 + rng.Next(3);
        for (int i = 0; i < stains; i++)
        {
            float cx = (float)rng.NextDouble() * _width;
            float cy = (float)rng.NextDouble() * _height;
            float radius = 14f + (float)rng.NextDouble() * 26f;
            float strength = 0.10f + (float)rng.NextDouble() * 0.10f;
            for (int y = Mathf.Max(0, (int)(cy - radius * 1.4f)); y < Mathf.Min(_height, (int)(cy + radius * 1.4f)); y++)
            {
                for (int x = Mathf.Max(0, (int)(cx - radius * 1.4f)); x < Mathf.Min(_width, (int)(cx + radius * 1.4f)); x++)
                {
                    float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                    d += (ValueNoise(x * 0.12f, y * 0.12f, salt + 5) - 0.5f) * 0.7f;
                    if (d >= 1f) continue;
                    // A darker ring at the rim, like dried tea.
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(d - 0.85f) * 6f);
                    Multiply(x, y, 1f - strength * (0.35f + ring * 0.9f) * (1f - d * 0.5f));
                }
            }
        }

        // Pencil grid, dashed.
        for (int i = 0; i <= _data.Width; i++)
        {
            int gx = _pad + i * _cell;
            for (int y = _pad; y <= _pad + _data.Height * _cell; y++)
            {
                if ((y & 7) < 3) Multiply(gx, y, 0.86f);
            }
        }
        for (int j = 0; j <= _data.Height; j++)
        {
            int gy = _pad + j * _cell;
            for (int x = _pad; x <= _pad + _data.Width * _cell; x++)
            {
                if ((x & 7) < 3) Multiply(x, gy, 0.86f);
            }
        }
    }

    private void Multiply(int x, int y, float factor)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height) return;

        int i = y * _width + x;
        Color32 p = _paper[i];
        _paper[i] = new Color32((byte)(p.r * factor), (byte)(p.g * factor), (byte)(p.b * factor), 255);
    }

    // ---------------------------------------------------------------- the ink

    /// <summary>Raises the ink coverage of a pixel to at least `alpha` and recomputes the cached picture there (so redrawing a shared wall changes nothing).</summary>
    private void Cover(int x, int y, float alpha)
    {
        if (x < 0 || y < 0 || x >= _width || y >= _height || alpha <= 0f) return;

        int i = y * _width + x;
        byte b = (byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
        if (b <= _ink[i]) return;

        _ink[i] = b;
        float tone = 0.85f + Hash01(x, y, 91) * 0.35f;
        Color32 p = _paper[i];
        float a = b / 255f;
        _walls[i] = new Color32(
            (byte)Mathf.Lerp(p.r, Mathf.Clamp01(ink.r * tone) * 255f, a),
            (byte)Mathf.Lerp(p.g, Mathf.Clamp01(ink.g * tone) * 255f, a),
            (byte)Mathf.Lerp(p.b, Mathf.Clamp01(ink.b * tone) * 255f, a),
            255);
    }

    /// <summary>Draws the four walls of a walked cell, and a dot on each corner.</summary>
    private void InkCell(int x, int z)
    {
        for (int dir = 0; dir < 4; dir++)
        {
            if (_data.IsWallClosed(x, z, dir)) InkWall(x, z, dir);
        }

        int left = _pad + x * _cell;
        int bottom = _pad + z * _cell;
        Dot(left, bottom);
        Dot(left + _cell, bottom);
        Dot(left, bottom + _cell);
        Dot(left + _cell, bottom + _cell);
    }

    private void Dot(int cx, int cy)
    {
        float radius = _wallThickness * 0.7f + 0.3f;
        int r = Mathf.CeilToInt(radius) + 1;
        for (int y = cy - r; y <= cy + r; y++)
        {
            for (int x = cx - r; x <= cx + r; x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                Cover(x, y, Mathf.Clamp01(radius + 0.5f - d));
            }
        }
    }

    /// <summary>One wall: a line with a wobbling centre and a thickness that now and then swells a pixel, soft at the edges - keyed on absolute pixels so both cells sharing it draw the same line.</summary>
    private void InkWall(int x, int z, int dir)
    {
        int left = _pad + x * _cell;
        int bottom = _pad + z * _cell;
        bool horizontal = dir == 0 || dir == 2;
        int line = dir == 0 ? bottom + _cell : dir == 2 ? bottom : dir == 1 ? left + _cell : left;
        int start = horizontal ? left : bottom;

        for (int t = 0; t <= _cell; t++)
        {
            int along = start + t;
            int wobble = Mathf.RoundToInt((ValueNoise(along * 0.11f, line * 0.37f + 3f, 17) - 0.5f) * 2.2f);
            int thickness = _wallThickness + (Hash01(along / 6, line, 23) > 0.8f ? 1 : 0);
            int first = line + wobble - thickness / 2;
            for (int k = -1; k <= thickness; k++)
            {
                float a = k < 0 || k >= thickness ? 0.38f : 1f;
                int across = first + k;
                if (horizontal) Cover(along, across, a);
                else Cover(across, along, a);
            }
        }
    }

    // ---------------------------------------------------------------- doors and switches

    private void DrawDoors()
    {
        foreach (MazeMapData.DoorInfo door in _data.Doors)
        {
            if (!Revealed(door.CellA) && !Revealed(door.CellB)) continue;
            DrawDoor(door);
        }
    }

    private Color32 DoorColor(MazeMapData.DoorInfo door)
    {
        return !door.Unlocked ? doorLocked : door.Open ? doorOpen : doorShut;
    }

    /// <summary>Locked: a red bar with a padlock dot. Shut: a solid amber bar. Open: no bar, a green tick at each end of the gap.</summary>
    private void DrawDoor(MazeMapData.DoorInfo door)
    {
        int left = _pad + door.CellA.x * _cell;
        int bottom = _pad + door.CellA.y * _cell;
        bool horizontal = door.Dir == 0 || door.Dir == 2;
        int line = door.Dir == 0 ? bottom + _cell : door.Dir == 2 ? bottom : door.Dir == 1 ? left + _cell : left;
        int start = (horizontal ? left : bottom) + _cell / 6;
        int end = (horizontal ? left : bottom) + _cell - _cell / 6;
        int t = _wallThickness + 1;
        Color32 colour = DoorColor(door);
        Color32 outline = ink;

        if (door.Open && door.Unlocked)
        {
            int span = t + 3;
            Bar(horizontal, start, start + 2, line - span, line + span + 1, outline);
            Bar(horizontal, end - 2, end, line - span, line + span + 1, outline);
            Bar(horizontal, start, start + 2, line - span + 1, line + span, colour);
            Bar(horizontal, end - 2, end, line - span + 1, line + span, colour);
            return;
        }

        Bar(horizontal, start - 1, end + 1, line - t / 2 - 1, line - t / 2 + t + 1, outline);
        Bar(horizontal, start, end, line - t / 2, line - t / 2 + t, colour);
        if (!door.Unlocked)
        {
            int mid = (start + end) / 2;
            Bar(horizontal, mid - 4, mid + 5, line - 4, line + 5, outline);
            Bar(horizontal, mid - 3, mid + 4, line - 3, line + 4, colour);
            Bar(horizontal, mid - 1, mid + 2, line - 1, line + 2, outline);
        }
    }

    /// <summary>Fills [a0, a1) along the wall and [c0, c1) across it, whichever way the wall runs.</summary>
    private void Bar(bool horizontal, int a0, int a1, int c0, int c1, Color32 colour)
    {
        if (horizontal) Rect(a0, c0, a1, c1, colour);
        else Rect(c0, a0, c1, a1, colour);
    }

    /// <summary>A switch is a ring in the colour of the door it opens (grey when the map does not know it), in a walked cell.</summary>
    private void DrawButtons()
    {
        foreach (MazeMapData.ButtonInfo button in _data.Buttons)
        {
            if (!Revealed(button.Cell)) continue;

            Color32 colour = new Color32(120, 112, 100, 255);
            foreach (MazeMapData.DoorInfo door in _data.Doors)
            {
                if (door.Id == button.DoorId) colour = DoorColor(door);
            }

            Vector2 centre = new Vector2(_pad + (button.Cell.x + 0.5f) * _cell - _cell * 0.25f, _pad + (button.Cell.y + 0.5f) * _cell - _cell * 0.25f);
            Ring(centre, _cell * 0.17f + 1.5f, _cell * 0.17f - 0.5f, ink);
            Ring(centre, _cell * 0.17f + 0.5f, _cell * 0.17f - 1.5f, colour);
        }
    }

    private void Ring(Vector2 centre, float outer, float inner, Color32 colour)
    {
        int x0 = Mathf.Max(Mathf.FloorToInt(centre.x - outer), 0);
        int x1 = Mathf.Min(Mathf.CeilToInt(centre.x + outer), _width - 1);
        int y0 = Mathf.Max(Mathf.FloorToInt(centre.y - outer), 0);
        int y1 = Mathf.Min(Mathf.CeilToInt(centre.y + outer), _height - 1);
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                float d = Mathf.Sqrt((x - centre.x) * (x - centre.x) + (y - centre.y) * (y - centre.y));
                if (d <= outer && d >= inner) _pixels[y * _width + x] = colour;
            }
        }
    }

    /// <summary>Fills the pixels in [x0, x1) x [y0, y1) of the live picture, clipped to it.</summary>
    private void Rect(int x0, int y0, int x1, int y1, Color32 colour)
    {
        x0 = Mathf.Max(x0, 0);
        y0 = Mathf.Max(y0, 0);
        x1 = Mathf.Min(x1, _width);
        y1 = Mathf.Min(y1, _height);
        for (int y = y0; y < y1; y++)
        {
            int row = y * _width;
            for (int x = x0; x < x1; x++) _pixels[row + x] = colour;
        }
    }

    // ---------------------------------------------------------------- the arrow

    private static readonly Vector2[] ArrowShape =
    {
        new Vector2(0f, 1f), new Vector2(0.72f, -0.85f), new Vector2(0f, -0.38f), new Vector2(-0.72f, -0.85f)
    };

    /// <summary>A small dark arrowhead with a pale outline, tip up (the image is turned by the player's yaw), supersampled for soft edges.</summary>
    private Sprite BuildArrowSprite()
    {
        const int size = 48;
        Color32[] pixels = new Color32[size * size];
        Color32 pale = new Color32(235, 225, 200, 255);
        Color32 dark = new Color32(34, 22, 14, 255);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float outer = 0f;
                float inner = 0f;
                for (int sy = 0; sy < 3; sy++)
                {
                    for (int sx = 0; sx < 3; sx++)
                    {
                        float u = ((x + (sx + 0.5f) / 3f) / size - 0.5f) * 2.1f;
                        float v = ((y + (sy + 0.5f) / 3f) / size - 0.5f) * 2.1f;
                        if (InShape(new Vector2(u, v), 1f)) outer += 1f / 9f;
                        if (InShape(new Vector2(u, v), 0.58f)) inner += 1f / 9f;
                    }
                }
                Color c = Color.Lerp(new Color32(0, 0, 0, 0), pale, outer);
                c = Color.Lerp(c, dark, inner);
                c.a = outer;
                pixels[y * size + x] = c;
            }
        }

        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "FloorMapArrow", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        texture.SetPixels32(pixels);
        texture.Apply(false);
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
    }

    /// <summary>Even-odd point-in-polygon test of the arrow outline scaled by `scale` about its middle.</summary>
    private static bool InShape(Vector2 p, float scale)
    {
        Vector2 middle = new Vector2(0f, -0.05f);
        bool inside = false;
        for (int i = 0, j = ArrowShape.Length - 1; i < ArrowShape.Length; j = i++)
        {
            Vector2 a = middle + (ArrowShape[i] - middle) * scale;
            Vector2 b = middle + (ArrowShape[j] - middle) * scale;
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }
}
