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
    [SerializeField] private float doorSlide = 1.3f;
    [SerializeField] private float jamDepth = 1.5f;

    private Vector3 _redStart;
    private Vector3 _carStart, _groundDoorStart, _basementDoorStart;
    private Vector3[] _partStart;
    private bool _captured;
    private bool _riding, _arrived, _pryReady;
    private Coroutine _routine;
    private AudioSource _audio;
    private readonly List<Light> _killed = new List<Light>();
    private AudioClip _grind, _bang, _thud, _hum;
    private Vector3 _carried;
    private bool _lockedPlayer;

    public bool IsComplete => car != null && groundDoor != null && basementDoor != null && gate != null && callPoint != null && pryPoint != null;

    // ---------------------------------------------------------------- IInteractable plumbing (LiftPoint)

    public string PromptFor(LiftPoint.Role role)
    {
        switch (role)
        {
            case LiftPoint.Role.Call: return HubState.Floor == 2 && !HubState.IsFinale ? "CALL THE LIFT" : "THE LIFT";
            case LiftPoint.Role.Gate: return "THE GATE";
            default: return "FORCE THE DOORS";
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
                hub.SubtitleLine("THE GATE IS CHAINED · TAKE THE LIFT", 2.6f);
                break;

            case LiftPoint.Role.Call:
                if (_riding || _arrived) return;
                if (HubState.IsFinale || HubState.Floor < 2) { hub.SubtitleLine("THE LIFT DOES NOT MOVE", 2.2f); return; }
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
        if (!IsComplete) Debug.LogError("LiftSetPiece: a part of the lift set is missing (the car, a shaft door, the gate or a call point). Run LIGHTS OUT > Build Missing Pieces. Floor 2 will use the stairs.", this);
        else if (Hub != null) Hub.SubtitleLine("THE STAIRS TO THE BASEMENT ARE CHAINED · TAKE THE LIFT", 4f);
    }

    protected override void OnEnd() { ResetState(); }

    protected override void OnDisable() { base.OnDisable(); ResetState(); }

    private void ResetState()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        if (_captured)
        {
            if (car != null) car.position = _carStart;
            if (redLight != null) redLight.transform.position = _redStart;
            for (int i = 0; i < carParts.Length; i++) if (carParts[i] != null) carParts[i].position = _partStart[i];
            if (groundDoor != null) groundDoor.position = _groundDoorStart;
            if (basementDoor != null) basementDoor.position = _basementDoorStart;
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

    private IEnumerator SlideDoor(Transform door, Vector3 closedPos, bool open, float seconds)
    {
        EnsureAudio();
        _audio.PlayOneShot(_grind, 0.6f);
        Vector3 from = door.position;
        Vector3 to = open ? closedPos + door.right * doorSlide : closedPos;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            door.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        door.position = to;
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
            _audio.PlayOneShot(_thud, 0.7f);
            yield return SlideDoor(basementDoor, _basementDoorStart, true, 1.0f);
            yield return WalkOut();
            Finish(hub);
            yield break;
        }

        // 3. down, slowly - and the car jams
        float slow = 3.4f;
        t = 0f;
        while (t < slow)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / slow);
            SetCar(jamDepth * Mathf.Pow(k, 0.8f)); // still moving when it stops
            yield return null;
        }
        SetCar(jamDepth);
        Hum(false);
        _audio.PlayOneShot(_bang, 1f);
        if (hub.Torch != null) hub.Torch.Stutter(0.9f);
        KillCarLights();
        if (redLight != null) { redLight.enabled = true; redLight.intensity = 1.8f; }
        hub.SubtitleLine("THE LIFT STOPPED", 2.2f);

        // 4. three heavy footsteps cross the roof
        yield return new WaitForSeconds(2.0f);
        Vector3 roof = car.position + Vector3.up * 3.0f;
        for (int i = 0; i < 3; i++)
        {
            AudioSource.PlayClipAtPoint(_thud, roof + Vector3.right * (i - 1) * 0.6f, 1f);
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
            if (redLight != null) redLight.intensity = Random.Range(0.4f, 2.4f);
            yield return null;
        }
        SetCar(total);
        _audio.PlayOneShot(_bang, 1f);
        if (hub.Torch != null) hub.Torch.Stutter(1.4f);
        if (redLight != null) redLight.intensity = 1.8f;

        // 6. the doors are stuck: force them (hold E)
        hub.SubtitleLine("THE DOORS ARE STUCK · HOLD E TO FORCE THEM", 4f);
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
    }

    /// <summary>The car's own door leaves are part of its mesh and stay half shut, so the player is eased out through the opened shaft door.</summary>
    private IEnumerator WalkOut()
    {
        AsylumHub hub = Hub;
        LockPlayer(true);
        hub.SetScripted(true);
        Vector3 from = hub.Player.position;
        Vector3 to = new Vector3(_carStart.x, from.y, basementDoor.position.z - 1.7f);
        float t = 0f;
        while (t < 1.5f)
        {
            t += Time.deltaTime;
            hub.MovePlayerTo(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.5f))));
            yield return null;
        }
        hub.MovePlayerTo(to);
    }

    private void Finish(AsylumHub hub)
    {
        LockPlayer(false);
        hub.SetScripted(false);
        _riding = false;
        _arrived = true;
        _routine = null;
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
