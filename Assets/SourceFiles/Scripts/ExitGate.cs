using UnityEngine;

/// <summary>
/// F111: one of a gate floor's two exit gates, set into the maze's outer wall by MazeGenerator.BuildExitGates (a clone of InteractableKit.escapeGate).
/// Dormant from the start of the floor (lamps dark, lever dead); MazeEscape.BeginEscape powers it; holding E on the lever (GateLever) fills
/// Progress, which never goes down, until the shutter grinds up; walking out past the wall line raises PlayerReached. The parts are found by the name
/// contract on the kit slot (PostL, Shutter, LeverBox/LeverHandle, Lamp0-2 with a Bulb and a Light, Chain, StubLight). The geometry round it (the
/// stub corridor outside, the themed fill beside and above the frame) is MazeGenerator's. Local +Z points into the maze.
/// </summary>
public class ExitGate : MonoBehaviour
{
    public enum GateState { Dormant, Powered, Opening, Open }

    private const int LampCount = 3;
    private const float LeverPullDegrees = 75f;
    private const float ShutterRise = 3.1f;
    private const float OpenSeconds = 2.5f;
    private const float FirstTouchNoise = 60f;
    private const float LampNoise = 30f;
    private const float OpenNoise = 45f;
    // The exit is the stub corridor beyond this depth outside the wall line, within this half-width of the gate's axis.
    private const float ExitDepth = 1.5f;
    private const float ExitHalfWidth = 2.2f;
    private const float ExitMaxDepth = 7f;
    // The point 1.2 m inside the wall: the wayfinding target and the hunter's noise source (the wall line itself is outside the navmesh).
    private const float InsideOffset = 1.2f;
    private const float HumVolume = 0.25f;
    private const float HumDistance = 25f;

    private static readonly Color DimColor = new Color(0.55f, 0.2f, 0.03f);
    private static readonly Color LockedColor = new Color(0.9f, 0.15f, 0.1f);

    private struct Lamp
    {
        public Renderer Bulb;
        public Light Light;
        public Material Material;
    }

    private Transform _shutter;
    private Vector3 _shutterRest;
    private Transform _handle;
    private GameObject _chain;
    private Light _stubLight;
    private float _stubIntensity = 3f;
    private readonly Lamp[] _lamps = new Lamp[LampCount];

    private MazeEscape _escape;
    private AIFollower _hunter;
    private Transform _player;
    private KeyRing _keys;
    private AudioSource _hum;
    private AudioClip _clunkClip;
    private AudioClip _grindClip;

    private Color _litColor = new Color(0.25f, 1f, 0.7f);
    private float _leverSeconds = 10f;
    private float _progress;
    private int _lampsLit;
    private bool _locked;
    private bool _touched;
    private float _openT;
    private GateState _state;

    public event System.Action<ExitGate> Opened;
    public event System.Action<ExitGate> PlayerReached;

    public GateState State => _state;

    /// <summary>0..1 of the lever. Never decreases.</summary>
    public float Progress01 => _leverSeconds > 0f ? Mathf.Clamp01(_progress / _leverSeconds) : 1f;

    /// <summary>True while the Key Hunt padlock holds the gate.</summary>
    public bool IsLocked => _locked && _state == GateState.Powered;

    public KeyRing Keys => _keys;

    /// <summary>The lever can be pulled right now: powered, not padlocked, and the run not over, frozen or in a walk-in.</summary>
    public bool CanPull => _state == GateState.Powered && !_locked && !GameOutcome.IsOver && (_escape == null || (!_escape.Frozen && !_escape.IsEntering));

    /// <summary>A point 1.2 m inside the wall at the gate's axis, at floor level: what the ping, the chevron and the distance readouts use.</summary>
    public Vector3 Position => transform.position + transform.forward * InsideOffset;

    /// <summary>The wall line at the gate's axis (floor level).</summary>
    public Vector3 WallPosition => transform.position;

    /// <summary>Called by MazeGenerator.BuildExitGates once the template is cloned and placed.</summary>
    public void Define(float leverSeconds, Color litColor)
    {
        _leverSeconds = Mathf.Max(1f, leverSeconds);
        _litColor = litColor;

        _shutter = transform.Find("Shutter");
        if (_shutter != null) _shutterRest = _shutter.localPosition;
        _handle = transform.Find("LeverBox/LeverHandle");
        Transform chain = transform.Find("Chain");
        _chain = chain != null ? chain.gameObject : null;
        Transform stub = transform.Find("StubLight");
        _stubLight = stub != null ? stub.GetComponent<Light>() : null;

        for (int i = 0; i < LampCount; i++)
        {
            Transform lamp = transform.Find("Lamp" + i);
            if (lamp == null) continue;
            Transform bulb = lamp.Find("Bulb");
            _lamps[i].Bulb = bulb != null ? bulb.GetComponent<Renderer>() : null;
            _lamps[i].Light = lamp.GetComponentInChildren<Light>(true);
            if (_lamps[i].Bulb != null)
            {
                _lamps[i].Material = _lamps[i].Bulb.material; // an instance, so lighting one lamp does not light the template's
                _lamps[i].Material.EnableKeyword("_EMISSION");
            }
        }

        if (_chain != null) _chain.SetActive(false);
        if (_stubLight != null) { _stubIntensity = _stubLight.intensity; _stubLight.enabled = false; _stubLight.color = litColor; }
        SetLeverVisual();
        ApplyLamps();
    }

    /// <summary>MazeEscape: the profile's lever time, passed on after the gate was built.</summary>
    public void SetLeverSeconds(float seconds) { _leverSeconds = Mathf.Max(1f, seconds); }

    /// <summary>MazeEscape: who to tell (the hunter hears the lever), who walks out, which keys are missing, and the escape that can freeze the run.</summary>
    public void Bind(MazeEscape escape, AIFollower hunter, Transform player, KeyRing keys)
    {
        _escape = escape;
        _hunter = hunter;
        _player = player;
        _keys = keys;
    }

    /// <summary>The lever box's focus collider (the object MazeGenerator puts the GateLever on is its parent).</summary>
    public Transform LeverBox => transform.Find("LeverBox");

    /// <summary>The escape has begun: power comes on (a clunk and a hum), the lamps glow dim, and a Key Hunt floor's gates are chained.</summary>
    public void Power(bool locked)
    {
        if (_state != GateState.Dormant) return;
        _state = GateState.Powered;
        _locked = locked;
        PlayClunk(1f);
        StartHum();
        if (_stubLight != null) { _stubLight.enabled = true; _stubLight.intensity = _stubIntensity * 0.2f; } // a small light over the gate
        ApplyLamps();
    }

    /// <summary>The padlock came off or went on (KeyRing.Changed).</summary>
    public void SetLocked(bool locked)
    {
        if (_locked == locked) return;
        _locked = locked;
        if (!locked) PlayClunk(0.9f);
        ApplyLamps();
    }

    /// <summary>One frame of E held on the lever (GateLever.HoldTick). Fills the progress and never takes any back.</summary>
    public void Pull(float dt)
    {
        if (!CanPull) return;

        if (!_touched)
        {
            _touched = true;
            _hunter?.HearNoise(Position, FirstTouchNoise);
        }

        _progress = Mathf.Min(_leverSeconds, _progress + dt);
        SetLeverVisual();

        int lit = Mathf.Min(LampCount, Mathf.FloorToInt(Progress01 * LampCount + 0.0001f));
        while (_lampsLit < lit)
        {
            _lampsLit++;
            PlayClunk(1f);
            _hunter?.HearNoise(Position, LampNoise);
            ApplyLamps();
        }

        if (_progress >= _leverSeconds) BeginOpening();
    }

    /// <summary>MazeEscape.Stop: the hum ends with the run.</summary>
    public void Silence()
    {
        if (_hum != null) _hum.Stop();
    }

    /// <summary>The flat spot 1.5 m past the exit line, on the gate's axis, at height `y`: where the walk-out eases the player to.</summary>
    public Vector3 ExitEasePoint(float y)
    {
        Vector3 p = transform.position - transform.forward * (ExitDepth + 1.5f);
        p.y = y;
        return p;
    }

    /// <summary>Where the walk-out looks: down the stairwell.</summary>
    public Vector3 StairLookPoint => transform.position - transform.forward * (ExitDepth + 2.2f) + Vector3.down * 1.5f;

    /// <summary>True when `world` is in the stub corridor beyond the exit line.</summary>
    public bool IsInExit(Vector3 world)
    {
        Vector3 local = transform.InverseTransformPoint(world);
        return local.z <= -ExitDepth && local.z >= -ExitMaxDepth && Mathf.Abs(local.x) <= ExitHalfWidth;
    }

    private void BeginOpening()
    {
        _state = GateState.Opening;
        _openT = 0f;
        if (!GameAudio.PlayAt(GameAudio.Library.shutterRoll, transform.position, 1f))
        {
            if (_grindClip == null) _grindClip = DoorAudio.BuildCreakClip(OpenSeconds, 55f, 5);
            GameAudio.ClipAtPoint(_grindClip, transform.position, 1f);
        }
        _hunter?.HearNoise(Position, OpenNoise);
        if (_stubLight != null) { _stubLight.enabled = true; _stubLight.intensity = _stubIntensity; }
        if (_chain != null) _chain.SetActive(false);
        ApplyLamps();
        Opened?.Invoke(this);
    }

    private void Update()
    {
        if (_state == GateState.Opening)
        {
            _openT += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_openT / OpenSeconds));
            if (_shutter != null) _shutter.localPosition = _shutterRest + Vector3.up * (ShutterRise * k);
            if (_openT >= OpenSeconds) _state = GateState.Open;
        }

        if ((_state == GateState.Opening || _state == GateState.Open) && _player != null && IsInExit(_player.position))
        {
            PlayerReached?.Invoke(this);
        }
    }

    private void SetLeverVisual()
    {
        if (_handle != null) _handle.localRotation = Quaternion.Euler(LeverPullDegrees * Progress01, 0f, 0f);
    }

    /// <summary>Lamp look by state: dark while dormant, dim red-amber once powered, red when chained, glowing in the lit colour as the lever fills.</summary>
    private void ApplyLamps()
    {
        for (int i = 0; i < LampCount; i++)
        {
            Color color;
            float strength;
            bool light;
            if (_state == GateState.Dormant) { color = Color.black; strength = 0f; light = false; }
            else if (_locked && _state == GateState.Powered) { color = LockedColor; strength = 2.5f; light = true; }
            else if (i < _lampsLit) { color = _litColor; strength = 3.5f; light = true; }
            else { color = DimColor; strength = 1.2f; light = false; }

            if (_lamps[i].Material != null) _lamps[i].Material.SetColor("_EmissionColor", color * strength);
            if (_lamps[i].Light != null)
            {
                _lamps[i].Light.color = color;
                _lamps[i].Light.enabled = light;
            }
        }

        if (_chain != null) _chain.SetActive(_locked && _state == GateState.Powered);
    }

    private void PlayClunk(float volume)
    {
        if (GameAudio.PlayAt(GameAudio.Library.fuseClunk, transform.position + Vector3.up * 1.3f, volume)) return;
        if (_clunkClip == null) _clunkClip = DoorAudio.BuildClunkClip(0.28f, 95f, 0.45f);
        GameAudio.ClipAtPoint(_clunkClip, transform.position + Vector3.up * 1.3f, volume);
    }

    private void StartHum()
    {
        if (_hum == null)
        {
            _hum = gameObject.AddComponent<AudioSource>();
            _hum.clip = BuildHumClip();
            _hum.loop = true;
            _hum.playOnAwake = false;
            _hum.spatialBlend = 1f;
            _hum.rolloffMode = AudioRolloffMode.Linear;
            _hum.minDistance = 2f;
            _hum.maxDistance = HumDistance;
            _hum.volume = HumVolume;
            GameSettings.RouteEffect(_hum);
        }
        _hum.Play();
    }

    /// <summary>A one-second mains hum (100 Hz and its first harmonics, whole cycles so the loop has no seam).</summary>
    private static AudioClip BuildHumClip()
    {
        const int sampleRate = 22050;
        float[] samples = new float[sampleRate];
        for (int i = 0; i < samples.Length; i++)
        {
            float t = i / (float)sampleRate;
            samples[i] = 0.6f * Mathf.Sin(2f * Mathf.PI * 100f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 200f * t) + 0.1f * Mathf.Sin(2f * Mathf.PI * 300f * t);
        }

        AudioClip clip = AudioClip.Create("GateHum", samples.Length, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
