using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F93 (S1, floor 2): the walk from Ward A to the Boiler Room. The basement stairs are chained shut on floor 2 (a wire-mesh gate across
/// the mouth: E answers "THE GATE IS CHAINED · TAKE THE LIFT"), so the player rides the lift. The pack's lift is a real mesh: the car
/// (AsylumHub/Building/Elevator_Wall/Elevator) really moves down the shaft and carries the player with it (the CharacterController is
/// switched off around each write, like the wing-door walk-in). A closed shaft door (a clone of the pack's upper-floor door) is added at
/// ground level; it slides open when the lift is called. Half way down the car jams with a bang, its light dies, a red emergency light comes
/// on, three heavy footsteps cross the roof, a pause - and the last stretch drops, hard. The basement doors are stuck: hold E for 1.5 s to
/// force them. On floor 3 and later the gate is broken open (a toppled variant, no collider) and the lift works plainly (a smooth ride and
/// the doors open by themselves). Floor 1 and the finale: the lift is dead. Everything resets when the hub is entered or left.
/// </summary>
public class LiftSetPiece : HubSetPiece
{
    /// <summary>The floor-2 reminder that the stairs are out (said once the Boiler Room key is in hand, and again on a return from the shop).</summary>
    public const string ChainedLineKey = "hub.lift.chained";
    private const string ChainedLineFallback = "THE STAIRS TO THE BASEMENT ARE CHAINED · TAKE THE LIFT";

    /// <summary>S9: the translated reminder (the old const string was display text; callers read this now).</summary>
    public static string ChainedText => L10n.Has(ChainedLineKey) ? L10n.T(ChainedLineKey) : ChainedLineFallback;

    [Header("Parts")]
    [SerializeField] private Transform car;
    [Tooltip("Things that ride with the car (the pack's NeonLift fixture).")]
    [SerializeField] private Transform[] carParts = new Transform[0];
    [SerializeField] private Transform groundDoor;
    [SerializeField] private Transform basementDoor;
    [SerializeField] private GameObject gate;
    [SerializeField] private GameObject gateBroken;
    [SerializeField] private Light redLight;
    [SerializeField] private LiftPoint callPoint;
    [SerializeField] private LiftPoint pryPoint;

    [Header("Tuning")]
    [Tooltip("How much of a shaft door's width is left showing at the frame edge once it is open (it retracts into the side of the frame).")]
    [SerializeField, Range(0.02f, 1f)] private float doorRetracted = 0.06f;
    [SerializeField] private float jamDepth = 1.5f;

    [Header("Tremble (camera only)")]
    [Tooltip("Scales every shake, jolt and tilt of the ride. 0 = off (motion-sick players).")]
    [SerializeField, Range(0f, 2f)] private float shakeStrength = 1f;
    [Tooltip("Camera offset (m) at full shake.")]
    [SerializeField] private float shakeMaxOffset = 0.035f;
    [Tooltip("Camera pitch/roll (degrees) at full shake.")]
    [SerializeField] private float shakeMaxAngle = 1.2f;
    [Tooltip("Roll (degrees) the view keeps while the jammed car hangs off one cable.")]
    [SerializeField] private float jamList = 1.4f;
    [Tooltip("Falling dust under the car ceiling: a ParticleSystem riding with the car, emitted in bursts on the jolts. Optional.")]
    [SerializeField] private ParticleSystem ceilingDust;

    private Vector3 _redStart;
    private Vector3 _carStart, _groundDoorStart, _basementDoorStart;
    private Vector3 _groundDoorScale, _basementDoorScale;
    private float _groundDoorWidth, _basementDoorWidth;
    private Vector3[] _partStart;
    private bool _captured;
    private bool _riding, _arrived, _pryReady;
    private Coroutine _routine;
    private AudioSource _audio;
    private readonly List<Light> _killed = new List<Light>();
    private AudioClip _grind, _bang, _thud, _hum;
    private Vector3 _carried;
    private bool _lockedPlayer;

    // the tremble: rumble = steady 0..1, trauma = decaying jolts (squared into the amplitude), dip = a vertical spring, list = held roll
    private Transform _cam;
    private Vector3 _camPos;
    private Quaternion _camRot;
    private bool _shaking;
    private float _rumble, _trauma, _dip, _dipVelocity, _list, _listNow;
    private readonly List<Light> _carLights = new List<Light>();
    private float _flickerUntil;

    public bool IsComplete => car != null && groundDoor != null && basementDoor != null && gate != null && callPoint != null && pryPoint != null;

    // ---------------------------------------------------------------- IInteractable plumbing (LiftPoint)

    public string PromptFor(LiftPoint.Role role)
    {
        switch (role)
        {
            case LiftPoint.Role.Call: return L10n.T(HubState.Floor == 2 && !HubState.IsFinale ? "hub.lift.call" : "hub.lift.lift");
            case LiftPoint.Role.Gate: return L10n.T("hub.lift.gate");
            default: return L10n.T("hub.lift.force");
        }
    }

    public bool CanUse(LiftPoint.Role role)
    {
        if (_riding && role != LiftPoint.Role.Pry) return false;
        switch (role)
        {
            case LiftPoint.Role.Call: return !_arrived;
            case LiftPoint.Role.Gate: return gate != null && gate.activeInHierarchy;
            default: return _pryReady;
        }
    }

    public void Use(LiftPoint.Role role)
    {
        AsylumHub hub = Hub;
        if (hub == null) return;
        switch (role)
        {
            case LiftPoint.Role.Gate:
                hub.SubtitleLine(L10n.T("hub.lift.gate_chained"), 2.6f);
                break;

            case LiftPoint.Role.Call:
                if (_riding || _arrived) return;
                if (HubState.IsFinale || HubState.Floor < 2) { hub.SubtitleLine(L10n.T("hub.lift.does_not_move"), 2.2f); return; }
                // Floor 2: the ride is one way (the stairs are chained), and everything the floor's mission needs is upstairs - riding
                // down before the wing key is in hand would strand the player at a locked door. The lift waits for the key.
                HubMission mission = HubState.Floor == 2 ? HubMission.ForFloor(2) : null;
                if (mission != null && mission.Current != HubMission.Stage.Done)
                {
                    hub.SubtitleLine(L10n.T("hub.lift.will_not_come"), 3f);
                    return;
                }
                _routine = StartCoroutine(Ride(HubState.Floor == 2));
                break;

            case LiftPoint.Role.Pry:
                if (_pryReady) _routine = StartCoroutine(Pry());
                break;
        }
    }

    // ---------------------------------------------------------------- state

    private void Capture()
    {
        if (_captured || car == null) return;
        _captured = true;
        _carStart = car.position;
        _redStart = redLight != null ? redLight.transform.position : Vector3.zero;
        _groundDoorStart = groundDoor != null ? groundDoor.position : Vector3.zero;
        _basementDoorStart = basementDoor != null ? basementDoor.position : Vector3.zero;
        _groundDoorScale = groundDoor != null ? groundDoor.localScale : Vector3.one;
        _basementDoorScale = basementDoor != null ? basementDoor.localScale : Vector3.one;
        _groundDoorWidth = DoorWidth(groundDoor);
        _basementDoorWidth = DoorWidth(basementDoor);
        _partStart = new Vector3[carParts.Length];
        for (int i = 0; i < carParts.Length; i++) _partStart[i] = carParts[i] != null ? carParts[i].position : Vector3.zero;
    }

    protected override void OnAnyEnter(int enteredFloor)
    {
        if (!IsComplete) return;
        Capture();
        ResetState();
        bool locked = !HubState.IsFinale && enteredFloor == 2;
        bool broken = HubState.IsFinale || enteredFloor >= 3;
        gate.SetActive(locked);
        if (gateBroken != null) gateBroken.SetActive(broken);
    }

    protected override void OnBegin()
    {
        if (!IsComplete) Debug.LogError("LiftSetPiece: a part of the lift set is missing (the car, a shaft door, the gate or a call point). Run KEEP IT LIT > Build Missing Pieces. Floor 2 will use the stairs.", this);
        else if (Hub != null && Hub.MissionDone(2)) Hub.QueueSubtitleLine(ChainedText, 4f); // F100: before the key the arrival line is the order; HubMission.Complete says it once the wing is unlocked
    }

    protected override void OnEnd() { ResetState(); }

    protected override void OnDisable() { base.OnDisable(); ResetState(); }

    private void ResetState()
    {
        if (_panic) SetPanicMusic(false, 0.5f);
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        EndShake();
        SetCarLights(true);
        _carLights.Clear();
        if (ceilingDust != null) ceilingDust.Clear();
        if (_captured)
        {
            if (car != null) car.position = _carStart;
            if (redLight != null) redLight.transform.position = _redStart;
            for (int i = 0; i < carParts.Length; i++) if (carParts[i] != null) carParts[i].position = _partStart[i];
            if (groundDoor != null) { groundDoor.position = _groundDoorStart; groundDoor.localScale = _groundDoorScale; }
            if (basementDoor != null) { basementDoor.position = _basementDoorStart; basementDoor.localScale = _basementDoorScale; }
        }
        if (redLight != null) redLight.enabled = false;
        foreach (Transform part in carParts) { Renderer pr = part != null ? part.GetComponent<Renderer>() : null; if (pr != null) pr.SetPropertyBlock(null); }
        foreach (Light l in _killed) if (l != null) l.enabled = true;
        _killed.Clear();
        if (_audio != null) _audio.Stop();
        if (_lockedPlayer && Hub != null && Hub.Controller != null) Hub.Controller.MovementLocked = false;
        _lockedPlayer = false;
        if (_riding && Hub != null) Hub.SetScripted(false);
        _riding = _arrived = _pryReady = false;
        if (callPoint != null) callPoint.Bind(this);
        if (pryPoint != null) pryPoint.Bind(this);
        LiftPoint[] gates = gate != null ? gate.GetComponentsInChildren<LiftPoint>(true) : new LiftPoint[0];
        foreach (LiftPoint g in gates) g.Bind(this);
    }

    // ---------------------------------------------------------------- sounds

    private void EnsureAudio()
    {
        if (_audio != null) return;
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;
        _grind = DoorAudio.BuildClunkClip(1.0f, 95f, 0.95f);
        _bang = DoorAudio.BuildClunkClip(0.6f, 48f, 0.85f);
        _thud = DoorAudio.BuildClunkClip(0.3f, 55f, 0.8f);
        int rate = 22050, n = rate * 2;
        float[] d = new float[n];
        float low = 0f;
        uint seed = 12345;
        for (int i = 0; i < n; i++)
        {
            seed = seed * 1664525u + 1013904223u;
            float noise = ((seed >> 16) & 0xFFFF) / 32767.5f - 1f;
            low += (noise - low) * 0.02f;
            float t = i / (float)rate;
            d[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 60f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 120f * t) + low * 2.5f;
        }
        _hum = AudioClip.Create("LiftHum", n, 1, rate, false);
        _hum.SetData(d, 0);
    }

    private void Hum(bool on)
    {
        if (_audio == null) return;
        if (on) { _audio.clip = _hum; _audio.loop = true; _audio.volume = 0.22f; if (!_audio.isPlaying) _audio.Play(); }
        else _audio.Stop();
    }

    // ---------------------------------------------------------------- the ride

    /// <summary>
    /// A shaft door is one 2.3 m panel: sliding it sideways left most of the opening covered (and a plaster wall right behind the gap),
    /// so it read as shut. It retracts into the side of the frame instead - squeezed along its width toward its +right edge, which stays put -
    /// until only <see cref="doorRetracted"/> of it shows, and the whole opening is clear.
    /// </summary>
    private IEnumerator SlideDoor(Transform door, Vector3 closedPos, bool open, float seconds)
    {
        EnsureAudio();
        if (!GameAudio.Play2D(GameAudio.Library.liftGrind, 0.6f)) _audio.PlayOneShot(_grind, 0.6f); // F97: recorded clip when the slot is filled
        bool ground = door == groundDoor;
        Vector3 closedScale = ground ? _groundDoorScale : _basementDoorScale;
        float width = ground ? _groundDoorWidth : _basementDoorWidth;
        float from = closedScale.x != 0f ? door.localScale.x / closedScale.x : 1f;
        float to = open ? doorRetracted : 1f;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            SetDoorOpening(door, closedPos, closedScale, width, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds))));
            yield return null;
        }
        SetDoorOpening(door, closedPos, closedScale, width, to);
    }

    /// <summary>`fraction` of the door's width left (1 = shut), kept against its +right edge.</summary>
    private static void SetDoorOpening(Transform door, Vector3 closedPos, Vector3 closedScale, float width, float fraction)
    {
        door.localScale = new Vector3(closedScale.x * fraction, closedScale.y, closedScale.z);
        door.position = closedPos + door.right * (width * 0.5f * (1f - fraction));
    }

    /// <summary>The door panel's world width along its own right axis, measured shut (at Capture).</summary>
    private static float DoorWidth(Transform door)
    {
        if (door == null) return 0f;
        Renderer r = door.GetComponentInChildren<Renderer>();
        if (r == null) return 2.3f;
        Vector3 size = r.bounds.size, right = door.right;
        return Mathf.Abs(right.x) * size.x + Mathf.Abs(right.y) * size.y + Mathf.Abs(right.z) * size.z;
    }

    private void LockPlayer(bool on)
    {
        AsylumHub hub = Hub;
        if (hub == null) return;
        _lockedPlayer = on;
        if (hub.Controller != null) hub.Controller.MovementLocked = on;
        if (on && hub.Hud != null) { hub.Hud.SetPrompt(null); hub.Hud.SetFocusPrompt(null); }
    }

    private void SetCar(float drop)
    {
        Vector3 down = Vector3.down * drop;
        car.position = _carStart + down;
        for (int i = 0; i < carParts.Length; i++) if (carParts[i] != null) carParts[i].position = _partStart[i] + down;
        if (redLight != null) redLight.transform.position = _redStart + down;
        Hub.MovePlayerTo(_carried + down);
    }

    private IEnumerator Ride(bool jam)
    {
        AsylumHub hub = Hub;
        _riding = true;
        hub.SetScripted(true);
        LockPlayer(true);
        EnsureAudio();
        Transform player = hub.Player;
        float total = Mathf.Max(2f, groundDoor.position.y - basementDoor.position.y);
        BeginShake();

        // 1. the shaft door opens, the player steps in
        yield return SlideDoor(groundDoor, _groundDoorStart, true, 1.0f);
        Vector3 start = player.position;
        Vector3 inside = new Vector3(_carStart.x, start.y, _carStart.z);
        float t = 0f;
        while (t < 1.4f)
        {
            t += Time.deltaTime;
            hub.MovePlayerTo(Vector3.Lerp(start, inside, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.4f))));
            // look at the doors, from inside
            if (hub.Controller != null) hub.Controller.LookAt(inside + Vector3.back * 3f + Vector3.up * 1.5f, 1f - Mathf.Exp(-6f * Time.deltaTime));
            yield return null;
        }
        _carried = inside;

        // 2. the door closes behind them
        yield return SlideDoor(groundDoor, _groundDoorStart, false, 1.0f);
        yield return new WaitForSeconds(0.4f);
        Hum(true);
        Jolt(0.3f, 0.03f); // the brake lets go: the car sags on its cables
        _rumble = 0.1f;

        if (!jam)
        {
            float ride = 4.5f;
            t = 0f;
            while (t < ride)
            {
                t += Time.deltaTime;
                SetCar(total * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / ride)));
                yield return null;
            }
            SetCar(total);
            Hum(false);
            _rumble = 0f;
            Jolt(0.4f, 0.025f);
            if (!GameAudio.Play2D(GameAudio.Library.liftThud, 0.7f)) _audio.PlayOneShot(_thud, 0.7f);
            yield return SlideDoor(basementDoor, _basementDoorStart, true, 1.0f);
            yield return WalkOut();
            Finish(hub);
            yield break;
        }

        // 3. down, slowly - and the car jams
        float slow = 3.4f;
        t = 0f;
        CollectCarLights();
        float nextCatch = Random.Range(0.5f, 1.0f);
        while (t < slow)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / slow);
            SetCar(jamDepth * Mathf.Pow(k, 0.8f)); // still moving when it stops
            _rumble = Mathf.Lerp(0.1f, 0.24f, k); // the motor strains harder toward the jam
            // the cable catches now and then: a small jolt, a pinch of dust, and (more often near the jam) the car lights stutter
            if (t >= nextCatch)
            {
                nextCatch = t + Random.Range(0.45f, 1.1f) * (1f - 0.5f * k);
                Jolt(0.35f, 0.012f);
                Dust(6);
                if (Random.value < 0.4f + 0.5f * k) _flickerUntil = Time.time + Random.Range(0.05f, 0.18f);
            }
            SetCarLights(Time.time >= _flickerUntil);
            yield return null;
        }
        SetCar(jamDepth);
        SetCarLights(true); // KillCarLights only takes the lights that are on
        Hum(false);
        _rumble = 0f;
        Jolt(1f, 0.07f);
        _list = jamList; // hanging off one cable
        Dust(90);
        if (!GameAudio.Play2D(GameAudio.Library.liftBang, 1f)) _audio.PlayOneShot(_bang, 1f);
        GameAudio.Play2D(GameAudio.Library.liftJamRumble, 0.4f); // F97: the shaft shaking, under the bang (recorded clip only)
        if (hub.Torch != null) hub.Torch.Stutter(0.9f);
        KillCarLights();
        if (redLight != null) { redLight.enabled = true; redLight.intensity = 1.8f; }
        hub.SubtitleLine(L10n.T("hub.lift.stopped"), 2.2f);
        SetPanicMusic(true, 1f); // the escape track, in under a second, until they are out

        // 4. three heavy footsteps cross the roof
        yield return new WaitForSeconds(2.0f);
        Vector3 roof = car.position + Vector3.up * 3.0f;
        for (int i = 0; i < 3; i++)
        {
            if (!GameAudio.PlayAt(GameAudio.Library.liftThud, roof + Vector3.right * (i - 1) * 0.6f, 1f, 0.8f)) GameAudio.ClipAtPoint(_thud, roof + Vector3.right * (i - 1) * 0.6f, 1f);
            Jolt(0.35f, 0.03f); // its weight lands on the roof
            Dust(25);
            yield return new WaitForSeconds(1.1f);
        }
        yield return new WaitForSeconds(1.3f);

        // 5. the last stretch drops
        float fall = 0.9f;
        t = 0f;
        while (t < fall)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fall);
            SetCar(Mathf.Lerp(jamDepth, total, k * k));
            _rumble = Mathf.Lerp(0.35f, 0.8f, k);
            if (redLight != null) redLight.intensity = Random.Range(0.4f, 2.4f);
            yield return null;
        }
        SetCar(total);
        _rumble = 0f;
        _list = 0f; // it lands level
        Jolt(1f, 0.1f);
        Dust(120);
        if (!GameAudio.Play2D(GameAudio.Library.liftBang, 1f)) _audio.PlayOneShot(_bang, 1f);
        if (hub.Torch != null) hub.Torch.Stutter(1.4f);
        if (redLight != null) redLight.intensity = 1.8f;

        // 6. the doors are stuck: force them (hold E)
        hub.SubtitleLine(L10n.T("hub.lift.doors_stuck", ("key", TouchInput.Key("E", L10n.T("ui.key.use")))), 4f);
        _arrived = true;
        _pryReady = true;
        LockPlayer(false); // look and turn freely; the only way on is the doors
        hub.SetScripted(false);
        _riding = false;
        _routine = null;
    }

    private IEnumerator Pry()
    {
        _pryReady = false;
        yield return SlideDoor(basementDoor, _basementDoorStart, true, 1.3f);
        if (redLight != null) redLight.enabled = true; // it stays red down here
        yield return WalkOut();
        Finish(Hub);
        SetPanicMusic(false, 3f); // stepped out: the track fades away over 3 s, the hub music comes back
    }

    /// <summary>The car's own door leaves are part of its mesh and stay half shut, so the player is eased out through the opened shaft door.</summary>
    private IEnumerator WalkOut()
    {
        AsylumHub hub = Hub;
        LockPlayer(true);
        hub.SetScripted(true);
        Vector3 from = hub.Player.position;
        Vector3 to = new Vector3(_carStart.x, from.y, _basementDoorStart.z - 1.7f);
        // The corridor wall is ~2 m beyond the doors and looks like a shut door under the torch: as they step out, turn them down the
        // corridor (the longer open side along the doors) instead of leaving them facing that wall.
        Vector3 along = basementDoor.right;
        along.y = 0f;
        along.Normalize();
        Vector3 eye = to + Vector3.up * 1.5f;
        float plus = Physics.Raycast(eye, along, out RaycastHit hitPlus, 30f, ~0, QueryTriggerInteraction.Ignore) ? hitPlus.distance : 30f;
        float minus = Physics.Raycast(eye, -along, out RaycastHit hitMinus, 30f, ~0, QueryTriggerInteraction.Ignore) ? hitMinus.distance : 30f;
        Vector3 lookAt = eye + (plus >= minus ? along : -along) * 4f;
        float t = 0f;
        while (t < 1.5f)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.5f));
            hub.MovePlayerTo(Vector3.Lerp(from, to, k));
            if (k > 0.45f && hub.Controller != null) hub.Controller.LookAt(lookAt, 1f - Mathf.Exp(-5f * Time.deltaTime)); // once past the doors
            yield return null;
        }
        hub.MovePlayerTo(to);
        // Finish the turn so they are free to walk on facing the corridor.
        t = 0f;
        while (t < 0.6f && hub.Controller != null)
        {
            t += Time.deltaTime;
            hub.Controller.LookAt(lookAt, 1f - Mathf.Exp(-8f * Time.deltaTime));
            yield return null;
        }
    }

    private bool _panic;

    /// <summary>F93 fix: the jam brings in the same escape track the maze plays once the hatch is open (hub ambience urgency: the base music ducks under it).</summary>
    private void SetPanicMusic(bool on, float seconds)
    {
        AsylumHub hub = Hub;
        if (hub == null || hub.Ambience == null) return;
        if (!on && !_panic) return;
        _panic = on;
        DoorAudio.Note("lift panic music " + (on ? "in" : "out"));
        hub.Ambience.SetUrgency(on ? 1f : 0f, seconds);
    }

    private void Finish(AsylumHub hub)
    {
        EndShake();
        LockPlayer(false);
        hub.SetScripted(false);
        _riding = false;
        _arrived = true;
        _routine = null;
    }

    // ---------------------------------------------------------------- the tremble

    /// <summary>Remembers the camera's rest pose (a child of PlayerCameraRoot; the controller only turns the root) and starts shaking it.</summary>
    private void BeginShake()
    {
        EndShake();
        Camera cam = Hub != null ? Hub.PlayerCamera : null;
        if (cam == null) return;
        _cam = cam.transform;
        _camPos = _cam.localPosition;
        _camRot = _cam.localRotation;
        _rumble = _trauma = _dip = _dipVelocity = _list = _listNow = 0f;
        _shaking = true;
    }

    private void EndShake()
    {
        if (_shaking && _cam != null) { _cam.localPosition = _camPos; _cam.localRotation = _camRot; }
        _shaking = false;
        _cam = null;
    }

    /// <summary>A knock: `trauma` 0..1 adds to the decaying shake, `dip` (m) drops the view and springs it back with a small bounce.</summary>
    private void Jolt(float trauma, float dip)
    {
        _trauma = Mathf.Min(1f, _trauma + trauma);
        _dip = Mathf.Min(_dip, -dip);
    }

    private void Dust(int count)
    {
        if (ceilingDust != null) ceilingDust.Emit(count);
    }

    private void LateUpdate()
    {
        if (!_shaking || _cam == null) return;
        float dt = Time.deltaTime;
        _trauma = Mathf.MoveTowards(_trauma, 0f, 1.3f * dt);
        _dipVelocity += (-160f * _dip - 14f * _dipVelocity) * dt; // underdamped spring: one small bounce
        _dip += _dipVelocity * dt;
        _listNow = Mathf.MoveTowards(_listNow, _list, 3f * dt);

        float shake = shakeStrength * GameSettings.CameraShake; // S2: the Camera shake setting (0 = a still lift)
        float amp = shake * Mathf.Clamp01(_rumble + _trauma * _trauma);
        float t = Time.time * 18f;
        float n0 = Mathf.PerlinNoise(t, 0.3f) * 2f - 1f, n1 = Mathf.PerlinNoise(t, 7.1f) * 2f - 1f;
        float n2 = Mathf.PerlinNoise(t, 13.7f) * 2f - 1f, n3 = Mathf.PerlinNoise(t, 21.9f) * 2f - 1f;
        Vector3 offset = new Vector3(n0, n1, 0f) * (shakeMaxOffset * amp) + Vector3.up * (_dip * shake);
        Quaternion tilt = Quaternion.Euler(n2 * shakeMaxAngle * amp, 0f, n3 * shakeMaxAngle * 0.7f * amp + _listNow * shake);
        _cam.localPosition = _camPos + offset;
        _cam.localRotation = _camRot * tilt;
    }

    /// <summary>The hub lights inside the car right now (as KillCarLights finds them), for the stutter on the way down.</summary>
    private void CollectCarLights()
    {
        _carLights.Clear();
        Bounds b = new Bounds(car.position + Vector3.up * 1.2f, new Vector3(3.2f, 3.5f, 4.2f));
        foreach (Light l in Hub.GetComponentsInChildren<Light>(false))
            if (l != redLight && l.enabled && b.Contains(l.transform.position)) _carLights.Add(l);
    }

    /// <summary>The stutter: the car's lights and the neon fixture's glow, off or back on (only the lights CollectCarLights took).</summary>
    private void SetCarLights(bool on)
    {
        if (_carLights.Count == 0) return;
        foreach (Light l in _carLights) if (l != null && !_killed.Contains(l)) l.enabled = on;
        foreach (Transform part in carParts)
        {
            Renderer r = part != null ? part.GetComponent<Renderer>() : null;
            if (r == null) continue;
            if (on) { r.SetPropertyBlock(null); continue; }
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", Color.black);
            r.SetPropertyBlock(block);
        }
    }

    /// <summary>The car's light dies: every hub light inside the car and the neon fixture's glow.</summary>
    private void KillCarLights()
    {
        Bounds b = new Bounds(car.position + Vector3.up * 1.2f, new Vector3(3.2f, 3.5f, 4.2f));
        foreach (Light l in Hub.GetComponentsInChildren<Light>(false))
        {
            if (l == redLight || !l.enabled || !b.Contains(l.transform.position)) continue;
            l.enabled = false;
            _killed.Add(l);
        }
        foreach (Transform part in carParts)
        {
            if (part == null) continue;
            Renderer r = part.GetComponent<Renderer>();
            if (r == null) continue;
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_EmissionColor", Color.black);
            r.SetPropertyBlock(block);
        }
    }
}
