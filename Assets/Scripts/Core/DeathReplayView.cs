using System.Collections.Generic;
using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// F120: the death replay on the caught end screen - a grainy CCTV loop of the last ~6 s, the hunter coming for the player, filmed from a
/// ceiling corner. The two REAL bodies are re-posed from the <see cref="DeathReplayRecorder"/> window (safe: the screen behind the end panel is the
/// opaque capture blackout and every end-screen button reloads the scene, so nothing is restored); a disabled "ReplayCam" is rendered by hand at
/// 12 fps into a small RenderTexture (the ScareCctv technique), shown on a RawImage with scanlines, noise, a vignette and CAM / REC / timestamp
/// text, all generated at runtime. Nothing renders during the run; this component exists only on the end screen. Added to the end-screen root by
/// <c>GameOutcome.TryShowReplay</c>.
/// </summary>
public class DeathReplayView : MonoBehaviour
{
    private const float RenderInterval = 1f / 12f;
    private const float HoldSeconds = 0.5f;
    private const float StaticSeconds = 0.35f;
    private const float PanSpeed = 50f; // degrees per second

    private ReplayFrame[] _frames;
    private float _duration;
    private int _index;

    private AIFollower _hunter;
    private Transform _player;
    private Animator _hunterAnim;
    private Animator _playerAnim;
    private Transform _cameraRoot;
    private Flashlight _flashlight;
    private HashSet<int> _hunterParams = new HashSet<int>();
    private HashSet<int> _playerParams = new HashSet<int>();

    private Camera _cam;
    private Light _ir;
    private RenderTexture _rt;
    private Quaternion _aim;
    private float _lastRender;
    private float _nextRender;

    private RawImage _feed;
    private RawImage _noise;
    private Texture2D _scanTex;
    private Texture2D _noiseTex;
    private Texture2D _vignetteTex;
    private TextMeshProUGUI _timeText;
    private TextMeshProUGUI _recText;
    private GameObject _frameRoot;

    private enum Phase { Play, Hold, Static }
    private Phase _phase;
    private float _phaseStart;
    private bool _reachSent;
    private bool _begun;
    private int _renders;
    private Vector3 _lastChestHunter;
    private Vector3 _lastChestPlayer;

    private static readonly int ParamSpeed = Animator.StringToHash("Speed");
    private static readonly int ParamMotionSpeed = Animator.StringToHash("MotionSpeed");
    private static readonly int ParamGrounded = Animator.StringToHash("Grounded");
    private static readonly int ParamPose = Animator.StringToHash("Pose");
    private static readonly int ParamReach = Animator.StringToHash("Reach");

    /// <summary>Test read-back: the RenderTexture the feed is drawn into.</summary>
    public RenderTexture Feed => _rt;
    /// <summary>Test read-back: the replay camera.</summary>
    public Camera ReplayCamera => _cam;
    /// <summary>Test read-back: how many times the camera has been rendered.</summary>
    public int RenderCount => _renders;
    /// <summary>Test read-back: the on-screen frame.</summary>
    public RectTransform FrameRect => _frameRoot != null ? (RectTransform)_frameRoot.transform : null;

    /// <param name="frames">The window from <see cref="DeathReplayRecorder.TryGetWindow"/>, oldest first.</param>
    /// <param name="descent">The DESCENT slip uses a slightly smaller frame.</param>
    public void Begin(ReplayFrame[] frames, AIFollower hunter, Transform player, MazeGenerator maze, bool descent)
    {
        if (_begun || frames == null || frames.Length < 2 || hunter == null || player == null) return;
        _begun = true;
        _frames = frames;
        _duration = frames[frames.Length - 1].time - frames[0].time;
        _hunter = hunter;
        _player = player;

        PoseBodies();
        Vector3 camPos = PlaceCamera(maze, out int camNumber);
        BuildCamera(camPos);
        BuildOverlay(descent, camNumber);

        StartLoop();
    }

    // ---------------------------------------------------------------- bodies

    private void PoseBodies()
    {
        // The hunter: no agent, no AIFollower logic, no head tracking fighting the recorded pose.
        NavMeshAgent agent = _hunter.GetComponent<NavMeshAgent>();
        if (agent != null) agent.enabled = false;
        foreach (HunterGaze gaze in _hunter.GetComponentsInChildren<HunterGaze>(true)) gaze.enabled = false;
        _hunterAnim = _hunter.BodyAnimator;
        PrepareAnimator(_hunterAnim, _hunterParams);

        // The player: no controller, the body visible again, the torch beam driven by the recording.
        ThirdPersonController controller = _player.GetComponent<ThirdPersonController>();
        if (controller != null) controller.enabled = false;
        CharacterController cc = _player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        FirstPersonRig rig = _player.GetComponent<FirstPersonRig>();
        if (rig != null)
        {
            rig.ShowBodyForReplay();
            if (rig.PlayerCamera != null) rig.PlayerCamera.cullingMask = 0; // hidden behind the blackout anyway; saves the render
        }
        _playerAnim = _player.GetComponent<Animator>();
        PrepareAnimator(_playerAnim, _playerParams);
        _cameraRoot = _player.Find("PlayerCameraRoot");
        _flashlight = rig != null ? rig.Flashlight : null;
    }

    private static void PrepareAnimator(Animator animator, HashSet<int> parameters)
    {
        if (animator == null) return;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // no enabled camera sees the bodies, so culling would freeze the pose
        animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        animator.fireEvents = false; // no footstep sounds
        parameters.Clear();
        foreach (AnimatorControllerParameter p in animator.parameters) parameters.Add(p.nameHash);
    }

    // ---------------------------------------------------------------- camera

    private readonly RaycastHit[] _hits = new RaycastHit[16];

    private Vector3 PlaceCamera(MazeGenerator maze, out int number)
    {
        ReplayFrame last = _frames[_frames.Length - 1];
        float ceiling = maze != null ? maze.FloorTopY + maze.WallHeight - 0.35f : last.playerPos.y + 3.2f;
        Vector3 chest = last.playerPos + Vector3.up * 1.2f;
        Vector3 hunterThen = SampleHunter(last.time - 3f) + Vector3.up * 1.2f;

        Vector3 best = default;
        float bestDistance = -1f;

        // Candidates: the corner the hunter came from, walking back in time from 1.5 s before the catch.
        int step = 5; // 0.25 s
        int from = Mathf.Max(0, FrameAtOrBefore(last.time - 1.5f));
        for (int i = from; i >= 0; i -= step)
        {
            int ahead = Mathf.Min(_frames.Length - 1, i + step);
            Vector3 travel = _frames[ahead].hunterPos - _frames[i].hunterPos;
            travel.y = 0f;
            travel = travel.sqrMagnitude < 0.0004f ? Vector3.zero : travel.normalized;
            Vector3 p = _frames[i].hunterPos - travel * 0.6f;
            p.y = ceiling;
            float d = Vector3.Distance(p, last.playerPos);
            if (d > 12f || d <= bestDistance) continue;
            if (!ClearPoint(p) || !Sees(p, chest) || !Sees(p, hunterThen)) continue;
            best = p;
            bestDistance = d;
        }

        if (bestDistance < 0f)
        {
            // Fallback 1: above the catch point, pulled back along the hunter's arrival direction.
            Vector3 arrival = last.hunterPos - _frames[Mathf.Max(0, _frames.Length - 1 - 10)].hunterPos;
            arrival.y = 0f;
            arrival = arrival.sqrMagnitude < 0.01f ? -Vector3.forward : arrival.normalized;
            for (float pull = 2f; pull >= 0f; pull -= 0.5f)
            {
                Vector3 p = last.playerPos - arrival * pull;
                p.y = ceiling;
                if (!ClearPoint(p) || !Sees(p, chest)) continue;
                best = p;
                bestDistance = 0f;
                break;
            }
        }

        if (bestDistance < 0f)
        {
            // Fallback 2: just behind and above the hunter's last position.
            Vector3 arrival = last.hunterPos - _frames[Mathf.Max(0, _frames.Length - 1 - 10)].hunterPos;
            arrival.y = 0f;
            arrival = arrival.sqrMagnitude < 0.01f ? Vector3.back : arrival.normalized;
            best = last.hunterPos + Vector3.up * 2.2f - arrival * 1.5f;
        }

        number = Mathf.Abs(Mathf.RoundToInt(best.x * 3f) * 31 + Mathf.RoundToInt(best.z * 3f) * 17) % 9 + 1;
        return best;
    }

    private bool ClearPoint(Vector3 p) => !Physics.CheckSphere(p, 0.2f, ~0, QueryTriggerInteraction.Ignore);

    /// <summary>A ray from <paramref name="from"/> to <paramref name="to"/> that hits nothing but the two bodies.</summary>
    private bool Sees(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float distance = dir.magnitude;
        if (distance < 0.01f) return true;
        int count = Physics.RaycastNonAlloc(from, dir / distance, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
        Transform playerRoot = _player.root;
        Transform hunterRoot = _hunter.transform.root;
        for (int i = 0; i < count; i++)
        {
            Transform t = _hits[i].collider.transform;
            if (t.IsChildOf(playerRoot) || t.IsChildOf(hunterRoot)) continue;
            return false;
        }
        return true;
    }

    private int FrameAtOrBefore(float time)
    {
        int result = 0;
        for (int i = 0; i < _frames.Length; i++)
        {
            if (_frames[i].time <= time) result = i; else break;
        }
        return result;
    }

    private Vector3 SampleHunter(float time)
    {
        int i = FrameAtOrBefore(time);
        return _frames[i].hunterPos;
    }

    private void BuildCamera(Vector3 position)
    {
        GameObject go = new GameObject("ReplayCam");
        go.transform.position = position;
        _cam = go.AddComponent<Camera>();
        _cam.enabled = false; // rendered by hand
        _cam.fieldOfView = 75f;
        _cam.nearClipPlane = 0.05f;
        _cam.farClipPlane = 30f;
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = Color.black;
        _cam.allowHDR = false;
        _cam.allowMSAA = false;
        var data = _cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
        }

        _rt = new RenderTexture(256, 144, 16, RenderTextureFormat.Default) { name = "ReplayFeed", antiAliasing = 1, filterMode = FilterMode.Point };
        _rt.Create();
        _cam.targetTexture = _rt;

        // The "IR illuminator": the maze is black.
        GameObject lightGo = new GameObject("IR");
        lightGo.transform.SetParent(go.transform, false);
        _ir = lightGo.AddComponent<Light>();
        _ir.type = LightType.Spot;
        _ir.range = 18f;
        _ir.spotAngle = 110f;
        _ir.intensity = 4f;
        _ir.color = new Color(0.80f, 0.95f, 0.85f);
        _ir.shadows = LightShadows.None;

        Vector3 target = Chest(_frames[0].hunterPos, _frames[0].playerPos);
        Vector3 to = target - position;
        _aim = to.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(to);
        _cam.transform.rotation = _aim;
    }

    private static Vector3 Chest(Vector3 hunter, Vector3 player) => (hunter + player) * 0.5f + Vector3.up * 1.2f;

    // ---------------------------------------------------------------- overlay

    private void BuildOverlay(bool descent, int camNumber)
    {
        Vector2 size = descent ? new Vector2(448f, 252f) : new Vector2(512f, 288f);
        Vector2 centre = descent ? new Vector2(-290f, 236f) : new Vector2(-316f, 254f);

        GameObject border = new GameObject("ReplayFrame");
        border.transform.SetParent(transform, false);
        border.layer = gameObject.layer;
        Image borderImage = border.AddComponent<Image>();
        borderImage.color = new Color(0.03f, 0.04f, 0.04f, 1f);
        borderImage.raycastTarget = false;
        RectTransform br = borderImage.rectTransform;
        br.anchorMin = br.anchorMax = new Vector2(1f, 0f);
        br.pivot = new Vector2(0.5f, 0.5f);
        br.anchoredPosition = centre;
        br.sizeDelta = size + new Vector2(12f, 12f);
        _frameRoot = border;

        _feed = MakeRaw(border.transform, "Feed", size);
        _feed.texture = _rt;
        _feed.color = new Color(0.78f, 0.92f, 0.82f);

        _scanTex = new Texture2D(1, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "ReplayScan" };
        _scanTex.SetPixels(new[] { new Color(0, 0, 0, 0.35f), new Color(0, 0, 0, 0.35f), new Color(0, 0, 0, 0), new Color(0, 0, 0, 0) });
        _scanTex.Apply(false);
        RawImage scan = MakeRaw(border.transform, "Scanlines", size);
        scan.texture = _scanTex;
        scan.uvRect = new Rect(0f, 0f, 1f, size.y / 4f);

        _noiseTex = new Texture2D(128, 128, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat, name = "ReplayNoise" };
        Color32[] pixels = new Color32[128 * 128];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte g = (byte)Random.Range(90, 255);
            pixels[i] = new Color32(g, g, g, 255);
        }
        _noiseTex.SetPixels32(pixels);
        _noiseTex.Apply(false);
        _noise = MakeRaw(border.transform, "Noise", size);
        _noise.texture = _noiseTex;
        _noise.color = new Color(1f, 1f, 1f, 0.10f);

        _vignetteTex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "ReplayVignette" };
        Color32[] v = new Color32[64 * 64];
        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                float r = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.55f) / 0.75f);
                v[y * 64 + x] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(r * r) * 210f));
            }
        }
        _vignetteTex.SetPixels32(v);
        _vignetteTex.Apply(false);
        RawImage vignette = MakeRaw(border.transform, "Vignette", size);
        vignette.texture = _vignetteTex;

        Color text = new Color(0.8f, 0.95f, 0.85f, 0.95f);
        TextMeshProUGUI cam = RuntimeUi.CreateText(border.transform, "Cam", L10n.T("ui.replay.cam", ("n", camNumber.ToString("00"))), 22f, text);
        cam.alignment = TextAlignmentOptions.TopLeft;
        RuntimeUi.Place(cam.rectTransform, new Vector2(0f, 1f), new Vector2(110f, -22f), new Vector2(200f, 30f));
        _recText = RuntimeUi.CreateText(border.transform, "Rec", "● " + L10n.T("ui.replay.rec"), 22f, new Color(0.95f, 0.15f, 0.1f));
        _recText.alignment = TextAlignmentOptions.TopRight;
        RuntimeUi.Place(_recText.rectTransform, new Vector2(1f, 1f), new Vector2(-90f, -22f), new Vector2(160f, 30f));
        _timeText = RuntimeUi.CreateText(border.transform, "Time", "03:00:00", 20f, text);
        _timeText.alignment = TextAlignmentOptions.BottomLeft;
        RuntimeUi.Place(_timeText.rectTransform, new Vector2(0f, 0f), new Vector2(90f, 20f), new Vector2(160f, 28f));

        TextMeshProUGUI caption = RuntimeUi.CreateText(transform, "ReplayCaption", L10n.T(CaptionBehind() ? "ui.replay.caption_behind" : "ui.replay.caption"), 26f, new Color(0.85f, 0.2f, 0.15f));
        caption.characterSpacing = 4f;
        RuntimeUi.AutoFit(caption, 0.6f);
        caption.alignment = TextAlignmentOptions.Center;
        RectTransform cr = caption.rectTransform;
        cr.anchorMin = cr.anchorMax = new Vector2(1f, 0f);
        cr.pivot = new Vector2(0.5f, 0.5f);
        cr.anchoredPosition = new Vector2(centre.x, centre.y - size.y * 0.5f - 34f);
        cr.sizeDelta = new Vector2(size.x + 40f, 40f);
    }

    private static RawImage MakeRaw(Transform parent, string name, Vector2 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;
        RawImage raw = go.AddComponent<RawImage>();
        raw.raycastTarget = false;
        RectTransform r = raw.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = Vector2.zero;
        r.sizeDelta = size;
        return raw;
    }

    /// <summary>"IT WAS BEHIND YOU THE WHOLE TIME" when, over the last 3 s, the hunter was more than 100 degrees off the player's facing for 60 % of the frames.</summary>
    private bool CaptionBehind()
    {
        float end = _frames[_frames.Length - 1].time;
        int total = 0, behind = 0;
        for (int i = _frames.Length - 1; i >= 0 && end - _frames[i].time <= 3f; i--)
        {
            Vector3 to = _frames[i].hunterPos - _frames[i].playerPos;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) continue;
            Vector3 facing = Quaternion.Euler(0f, _frames[i].camYaw, 0f) * Vector3.forward;
            total++;
            if (Vector3.Angle(facing, to) > 100f) behind++;
        }
        return total > 0 && behind >= 0.6f * total;
    }

    // ---------------------------------------------------------------- loop

    private void StartLoop()
    {
        _phase = Phase.Play;
        _phaseStart = Time.unscaledTime;
        _index = 0;
        _reachSent = false;
        _nextRender = 0f;
        if (_hunterAnim != null && _hunterParams.Contains(ParamReach)) _hunterAnim.ResetTrigger(ParamReach);
    }

    private void Update()
    {
        if (!_begun) return;

        float t = Time.unscaledTime - _phaseStart;
        float lt;
        switch (_phase)
        {
            case Phase.Play:
                lt = Mathf.Min(t, _duration);
                if (t >= _duration) { _phase = Phase.Hold; _phaseStart = Time.unscaledTime; }
                break;
            case Phase.Hold:
                lt = _duration;
                if (t >= HoldSeconds) EnterStatic();
                break;
            default:
                lt = 0f;
                if (t >= StaticSeconds) StartLoop();
                break;
        }

        ApplyPose(lt);

        // Chrome: the REC dot blinks at 1 Hz (slow enough for Reduce flashing), the noise swells during the static gap.
        if (_recText != null)
        {
            bool on = FlashSafety.On || ((int)Time.unscaledTime) % 2 == 0;
            _recText.alpha = on ? 1f : 0.15f;
        }
        if (_noise != null)
        {
            Color c = _noise.color;
            c.a = _phase == Phase.Static ? 0.9f : 0.10f;
            _noise.color = c;
        }
    }

    private void EnterStatic()
    {
        _phase = Phase.Static;
        _phaseStart = Time.unscaledTime;
        // Back to the bind pose inside the static gap, so the loop does not start mid-Reach.
        if (_hunterAnim != null) _hunterAnim.Rebind();
        if (_playerAnim != null) _playerAnim.Rebind();
        _reachSent = false;
        _index = 0;
    }

    private void ApplyPose(float lt)
    {
        float time = _frames[0].time + lt;
        while (_index < _frames.Length - 2 && _frames[_index + 1].time <= time) _index++;
        ReplayFrame a = _frames[_index];
        ReplayFrame b = _frames[Mathf.Min(_index + 1, _frames.Length - 1)];
        float span = b.time - a.time;
        float k = span > 0.0001f ? Mathf.Clamp01((time - a.time) / span) : 0f;

        Vector3 hunterPos = Vector3.Lerp(a.hunterPos, b.hunterPos, k);
        Vector3 playerPos = Vector3.Lerp(a.playerPos, b.playerPos, k);
        _hunter.transform.SetPositionAndRotation(hunterPos, Quaternion.Euler(0f, Mathf.LerpAngle(a.hunterYaw, b.hunterYaw, k), 0f));
        _player.SetPositionAndRotation(playerPos, Quaternion.Euler(0f, Mathf.LerpAngle(a.playerYaw, b.playerYaw, k), 0f));

        if (_cameraRoot != null)
        {
            _cameraRoot.rotation = Quaternion.Euler(Mathf.Lerp(a.camPitch, b.camPitch, k), Mathf.LerpAngle(a.camYaw, b.camYaw, k), 0f);
        }
        if (_flashlight != null) _flashlight.SetReplayBeam((k < 0.5f ? a : b).torchOn);

        if (_hunterAnim != null)
        {
            if (_hunterParams.Contains(ParamSpeed)) _hunterAnim.SetFloat(ParamSpeed, Mathf.Lerp(a.gait, b.gait, k));
            if (_hunterParams.Contains(ParamMotionSpeed)) _hunterAnim.SetFloat(ParamMotionSpeed, Mathf.Lerp(a.playback, b.playback, k));
            if (_hunterParams.Contains(ParamGrounded)) _hunterAnim.SetBool(ParamGrounded, true);
            if (_hunterParams.Contains(ParamPose)) _hunterAnim.SetInteger(ParamPose, a.pose);
            if (!_reachSent && lt >= _duration - 0.15f && _hunterParams.Contains(ParamReach))
            {
                _reachSent = true;
                _hunterAnim.SetTrigger(ParamReach);
            }
        }

        if (_playerAnim != null)
        {
            Vector3 flat = b.playerPos - a.playerPos;
            flat.y = 0f;
            float speed = span > 0.0001f ? flat.magnitude / span : 0f;
            if (_playerParams.Contains(ParamSpeed)) _playerAnim.SetFloat(ParamSpeed, speed);
            if (_playerParams.Contains(ParamMotionSpeed)) _playerAnim.SetFloat(ParamMotionSpeed, 1f);
            if (_playerParams.Contains(ParamGrounded)) _playerAnim.SetBool(ParamGrounded, true);
        }

        _lastChestHunter = hunterPos;
        _lastChestPlayer = playerPos;

        if (_timeText != null)
        {
            int seconds = Mathf.Max(0, Mathf.FloorToInt(time - GameFlow.RunStartTime)) + 3 * 3600;
            _timeText.text = $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
        }
    }

    private void LateUpdate()
    {
        // After the Animator evaluated the pose set in Update.
        if (!_begun || _cam == null || Time.unscaledTime < _nextRender) return;
        float dt = Mathf.Clamp(Time.unscaledTime - _lastRender, RenderInterval, 0.25f);
        _lastRender = Time.unscaledTime;
        _nextRender = Time.unscaledTime + RenderInterval;

        // A PTZ camera: turns toward the midpoint of the two bodies at a limited rate.
        Vector3 to = Chest(_lastChestHunter, _lastChestPlayer) - _cam.transform.position;
        if (to.sqrMagnitude > 0.0001f)
        {
            _aim = Quaternion.RotateTowards(_aim, Quaternion.LookRotation(to), PanSpeed * dt);
            _cam.transform.rotation = _aim;
        }

        if (_noise != null) _noise.uvRect = new Rect(Random.value, Random.value, 1f, 1f);

        // 3 % of frames are held for a dropped-frame feel.
        if (Random.value < 0.03f) return;

        bool fog = RenderSettings.fog;
        RenderSettings.fog = false; // the world fog is for the player's eyes; the IR camera sees what its light reaches
        _cam.Render();
        RenderSettings.fog = fog;
        _renders++;
    }

    private void OnDestroy()
    {
        if (_cam != null)
        {
            _cam.targetTexture = null;
            Destroy(_cam.gameObject);
        }
        if (_rt != null) { _rt.Release(); Destroy(_rt); }
        if (_scanTex != null) Destroy(_scanTex);
        if (_noiseTex != null) Destroy(_noiseTex);
        if (_vignetteTex != null) Destroy(_vignetteTex);
    }
}
