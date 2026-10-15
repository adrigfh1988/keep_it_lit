using UnityEngine;

/// <summary>F120: one sample of the death replay's ring buffer - everything needed to re-pose the two real bodies and the torch.</summary>
public struct ReplayFrame
{
    public float time;          // Time.time (the run clock)
    public Vector3 hunterPos;
    public float hunterYaw;
    public float gait;          // the hunter animator's Speed (0 idle, 1 walk, 2 run)
    public float playback;      // its MotionSpeed
    public int pose;            // its Pose
    public Vector3 playerPos;
    public float playerYaw;
    public float camYaw;        // the player's camera root, world yaw (the torch aims with it, the body yaw follows the movement)
    public float camPitch;      // signed
    public bool torchOn;
    public bool hidden;
}

/// <summary>
/// F120: the death replay's recorder. A ring buffer of <see cref="ReplayFrame"/>s at 20 Hz (capacity 170 = 8.5 s) written while the run is
/// active: no physics, no allocation after Configure. <c>CaptureSequence</c> sets IsRunActive false on its first line, so recording stops at the
/// catch by itself. <see cref="DeathReplayView"/> reads the window back on the end screen. Added on the MazeGenerator object by SetUpAtmosphere.
/// </summary>
public class DeathReplayRecorder : MonoBehaviour
{
    public const int Capacity = 170;
    private const float Interval = 0.05f;
    private const float MaxJump = 3f;

    private ReplayFrame[] _buffer;
    private int _head;      // next write
    private int _count;
    private float _next;
    private bool _frozen;

    private Transform _player;
    private Transform _cameraRoot;
    private AIFollower _hunter;
    private Flashlight _flashlight;
    private PlayerStealthState _stealth;

    public int Count => _count;
    public bool Frozen => _frozen;

    public void Configure(Transform player, AIFollower hunter, Flashlight flashlight, PlayerStealthState stealth, Transform cameraRoot)
    {
        _player = player;
        _hunter = hunter;
        _flashlight = flashlight;
        _stealth = stealth;
        _cameraRoot = cameraRoot;
        if (_buffer == null) _buffer = new ReplayFrame[Capacity];
        _head = 0;
        _count = 0;
        _frozen = false;
    }

    /// <summary>Stops recording (a hunter death).</summary>
    public void Freeze() => _frozen = true;

    private void Update()
    {
        if (_buffer == null || _player == null || _hunter == null) return;
        if (!GameFlow.IsRunActive || GameOutcome.IsOver || _frozen || Time.time < _next) return;
        _next = Time.time + Interval;

        ReplayFrame f = default;
        f.time = Time.time;
        Transform h = _hunter.transform;
        f.hunterPos = h.position;
        f.hunterYaw = h.eulerAngles.y;
        f.gait = _hunter.AnimGait;
        f.playback = _hunter.AnimPlayback;
        f.pose = (int)_hunter.CurrentPose;
        f.playerPos = _player.position;
        f.playerYaw = _player.eulerAngles.y;
        if (_cameraRoot != null)
        {
            Vector3 e = _cameraRoot.eulerAngles;
            f.camYaw = e.y;
            f.camPitch = Mathf.DeltaAngle(0f, e.x);
        }
        else
        {
            f.camYaw = f.playerYaw;
        }
        f.torchOn = _flashlight != null && _flashlight.IsBeamLit;
        f.hidden = _stealth != null && _stealth.Hidden;

        _buffer[_head] = f;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    /// <summary>The i-th oldest recorded frame (0 = oldest).</summary>
    public ReplayFrame Get(int i)
    {
        int start = (_head - _count + Capacity) % Capacity;
        return _buffer[(start + i) % Capacity];
    }

    /// <summary>
    /// The replay window: the last <paramref name="maxSeconds"/> of frames, cut at the frame after the last relocation (a hunter or player jump of
    /// more than 3 m between consecutive frames: warps, lockers, teleports). Returns false when less than <paramref name="minSeconds"/> remains.
    /// </summary>
    public bool TryGetWindow(float maxSeconds, float minSeconds, out ReplayFrame[] frames)
    {
        frames = null;
        if (_buffer == null || _count < 2) return false;

        int last = _count - 1;
        float endTime = Get(last).time;
        int first = last;
        while (first > 0)
        {
            ReplayFrame a = Get(first - 1);
            ReplayFrame b = Get(first);
            if (endTime - a.time > maxSeconds) break;
            if ((a.hunterPos - b.hunterPos).sqrMagnitude > MaxJump * MaxJump || (a.playerPos - b.playerPos).sqrMagnitude > MaxJump * MaxJump) break;
            first--;
        }

        if (endTime - Get(first).time < minSeconds) return false;

        frames = new ReplayFrame[last - first + 1];
        for (int i = 0; i < frames.Length; i++) frames[i] = Get(first + i);
        return true;
    }
}
