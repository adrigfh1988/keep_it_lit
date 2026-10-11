using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The lit room between floors: a counter, a salesman whose head follows the player, and a door to the
/// next floor. Unlike everything else in this project the room is AUTHORED in the scene, so it can be
/// selected, moved and restyled in the Editor: it is the hand-editable prefab Assets/Prefabs/ShopRoom.prefab (first
/// drafted by KEEP IT LIT > Build > Shop Room (regenerate), Assets/Editor/ShopRoomBuilder.cs); this component only drives it.
///
/// It is not a run and not an ending: see GameFlow.IsInShop. MazeGenerator finds it, hands it the
/// player/HUD/menu through Configure, and GameOutcome.FloorCleared teleports the player onto the
/// Arrival point and calls Open once the payout ledger is dismissed.
/// </summary>
public class ShopRoom : MonoBehaviour
{
    [Header("Parts (wired by the builder; drag your own in if you rebuild the room by hand)")]
    [Tooltip("Where the player stands when they arrive. Its Y rotation is the way they face.")]
    [SerializeField] private Transform arrivalPoint;
    [Tooltip("Trigger volume in front of the counter. Only its XZ footprint is used.")]
    [SerializeField] private Collider counterZone;
    [Tooltip("Trigger volume in front of the door. Only its XZ footprint is used.")]
    [SerializeField] private Collider doorZone;
    [Tooltip("F87: trigger volume in front of the contract board. Only its XZ footprint is used. Empty = no board (KEEP IT LIT > Build Missing Pieces adds it).")]
    [SerializeField] private Collider contractZone;
    [Tooltip("The salesman's head: a plain pivot, or the head bone of an animated body. It turns to follow the player, within Head Yaw Limit of how it (or the animated body) is facing here.")]
    [SerializeField] private Transform headPivot;
    [Tooltip("The sign over the counter. Its alpha wavers.")]
    [SerializeField] private TextMeshPro counterSign;
    [Tooltip("The sign over the door. Its text is set to the next floor's number on arrival.")]
    [SerializeField] private TextMeshPro doorSign;
    [Tooltip("Pivot on the door leaf's hinge edge, the leaf parented under it. Empty = found by name (Door/WoodDoor01...) and made at runtime.")]
    [SerializeField] private Transform doorHinge;

    [Header("Door")]
    [Tooltip("How far the leaf swings out when the player leaves")]
    [SerializeField] private float doorOpenAngle = 100f;
    [Tooltip("Seconds for the leaf to swing open, while the player steps up to the doorway")]
    [SerializeField] private float doorOpenSeconds = 0.8f;
    [Tooltip("Seconds to walk up to the doorway; the fade to black runs over this")]
    [SerializeField] private float doorWalkSeconds = 1.1f;

    [Header("Salesman")]
    [Tooltip("Degrees per second the head turns to track the player")]
    [SerializeField] private float headTurnSpeed = 40f;
    [Tooltip("How far either side of its authored facing the head is allowed to turn")]
    [SerializeField] private float headYawLimit = 70f;
    [Tooltip("Timmy's animations: waves on arrival and at the counter, talks while the menu is open. Optional.")]
    [SerializeField] private ShopkeeperAnimator shopkeeper;

    [Header("Ambience")]
    [SerializeField] private float humVolume = 0.22f;

    private Transform _player;
    private PlayerHud _hud;
    private ShopMenu _menu;

    private bool _open;
    private bool _inCounter;
    private bool _inDoor;
    private bool _inBoard;
    // Head tracking works two ways: a plain pivot (rotated from its authored facing) or a bone under an Animator
    // (rotated on top of that frame's animated pose). _headYawOffset is the smoothed turn in both cases.
    private float _headBaseYaw;
    private float _headYawOffset;
    private Quaternion _headBaseRotation;
    private Animator _headAnimator;

    private AudioSource _hum;
    private AudioClip _humClip;
    private bool _humIsAsset; // F97: _humClip is a library clip - never destroy it

    // The closed leaf's centre and the hinge's rest rotation, captured in Awake.
    private Vector3 _doorwayCentre;
    private Quaternion _hingeClosed;

    /// <summary>True once the room has the parts it needs to work. False = the builder has not been run.</summary>
    public bool IsComplete => arrivalPoint != null && counterZone != null && doorZone != null;

    /// <summary>F87: true once the contract board has been added to this room (ShopRoomBuilder.AddContractBoard).</summary>
    public bool HasContractBoard => contractZone != null;

    /// <summary>Called by MazeGenerator.SetUpAtmosphere. Everything physical already exists in the scene.</summary>
    public void Configure(Transform player, PlayerHud hud, ShopMenu menu)
    {
        _player = player;
        _hud = hud;
        _menu = menu;
    }

    private void Awake()
    {
        if (headPivot != null)
        {
            _headAnimator = headPivot.GetComponentInParent<Animator>();
            _headBaseRotation = headPivot.rotation;
            // A bone's own euler yaw means nothing, so an animated head takes its facing from the body.
            _headBaseYaw = _headAnimator != null ? _headAnimator.transform.eulerAngles.y : headPivot.eulerAngles.y;
        }

        if (doorHinge == null) doorHinge = FindAndHingeDoorLeaf();
        if (doorHinge != null)
        {
            _hingeClosed = doorHinge.localRotation;
            foreach (Renderer r in doorHinge.GetComponentsInChildren<Renderer>())
            {
                if (!r.isPartOfStaticBatch) continue;
                Debug.LogWarning("ShopRoom: the door leaf is Static, so static batching froze it shut and it will not swing. Run KEEP IT LIT > Build Missing Pieces, then save the scene.", r);
                break;
            }
            Bounds leaf = RendererBounds(doorHinge);
            _doorwayCentre = leaf.size == Vector3.zero ? doorHinge.position : leaf.center;
        }

        AudioClip shopMusic = GameAudio.Library.shopMusic.First(); // F97: a recorded safe-room track instead of the mains hum
        _humIsAsset = shopMusic != null;
        _humClip = _humIsAsset ? shopMusic : BuildHumClip();
        _hum = gameObject.GetComponent<AudioSource>();
        if (_hum == null) _hum = gameObject.AddComponent<AudioSource>();
        _hum.playOnAwake = false;
        _hum.loop = true;
        _hum.spatialBlend = 0f;
        _hum.volume = Mathf.Clamp01(humVolume * (_humIsAsset ? GameAudio.Library.shopMusic.volume : 1f));
        _hum.clip = _humClip;
        if (_humIsAsset) GameSettings.RouteMusic(_hum); // S2: the recorded safe-room track is music; the synthesised mains hum stays an effect
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Teleports the player into the room, facing the way the Arrival point faces. Called at black, so the pop is never seen.</summary>
    public void ArrivePlayer(Transform player)
    {
        if (player == null || arrivalPoint == null) return;

        float yaw = arrivalPoint.eulerAngles.y;

        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.position = arrivalPoint.position;
        player.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cc != null) cc.enabled = true;

        player.GetComponent<ThirdPersonController>()?.ResetCameraRotation(yaw);

        if (doorSign != null)
        {
            if (!doorSign.enableAutoSizing) RuntimeUi.AutoFit(doorSign, 0.6f); // D14
            doorSign.text = GameFlow.ShopFromHub ? L10n.T("hud.asylum") : L10n.T("hud.floor", ("floor", GameFlow.ShopNextFloor));
        }
    }

    /// <summary>Opens the room for free movement. Called once the ledger's CONTINUE is pressed.</summary>
    public void Open()
    {
        _open = true;
        _inCounter = false;
        _inDoor = false;
        _inBoard = false;
        if (_hum != null) _hum.Play();
        // F87: after a contract was paid or failed the salesman says so instead of the usual greeting.
        if (_hud != null) _hud.ShowSubtitle(GreetingText(), 3f);
        if (shopkeeper != null) shopkeeper.Greet();
    }

    private void Update()
    {
        UpdateSignWaver();
        if (shopkeeper != null) shopkeeper.SetTalking(_open && GameFlow.IsInShop && _menu != null && _menu.IsOpen);

        if (!_open || !GameFlow.IsInShop || (_menu != null && _menu.IsOpen) || Time.timeScale <= 0f) return;

        UpdateZones();
        HandleInteract();
    }

    /// <summary>LateUpdate, so the turn lands after the Animator has written the idle pose for this frame.</summary>
    private void LateUpdate()
    {
        if (headPivot != null) UpdateHeadTracking();
    }

    private void UpdateZones()
    {
        if (_player == null) return;

        bool inCounter = ContainsXZ(counterZone, _player.position);
        bool inDoor = ContainsXZ(doorZone, _player.position);
        bool inBoard = !inCounter && ContainsXZ(contractZone, _player.position);
        if (inCounter == _inCounter && inDoor == _inDoor && inBoard == _inBoard) return;

        _inCounter = inCounter;
        _inDoor = inDoor;
        _inBoard = inBoard;
        if (_inCounter && shopkeeper != null) shopkeeper.Greet();
        if (_hud == null) return;

        if (_inCounter) _hud.SetPromptKey("E", "hud.prompt.talk");
        else if (_inBoard) _hud.SetPromptKey(TouchInput.Key("E", L10n.T("ui.key.use")), "hud.prompt.contracts");
        else if (_inDoor) _hud.SetPromptKey("E", "hud.prompt.descend");
        else _hud.SetPrompt(null);
    }

    private void HandleInteract()
    {
        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) pressed = true;
        if (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame) pressed = true;
#endif
        if (TouchInput.Pressed(TouchInput.TouchAction.Interact)) pressed = true;
        if (!pressed) return;

        if (_inCounter) { if (_menu != null) _menu.Open(); }
        else if (_inBoard) { if (_menu != null) _menu.OpenContracts(); }
        else if (_inDoor) Descend();
    }

    private void UpdateHeadTracking()
    {
        if (_player == null) return;

        Vector3 toPlayer = _player.position - headPivot.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) return;

        // World yaws throughout, so the room can be rotated or the salesman turned in the Editor.
        float wantedYaw = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg;
        float delta = Mathf.Clamp(Mathf.DeltaAngle(_headBaseYaw, wantedYaw), -headYawLimit, headYawLimit);
        _headYawOffset = Mathf.MoveTowards(_headYawOffset, delta, headTurnSpeed * Time.deltaTime);

        // An animated bone was just reset by the Animator; a plain pivot turns from where it was authored.
        Quaternion pose = _headAnimator != null ? headPivot.rotation : _headBaseRotation;
        headPivot.rotation = Quaternion.AngleAxis(_headYawOffset, Vector3.up) * pose;
    }

    private void UpdateSignWaver()
    {
        if (counterSign == null) return;
        float alpha = Mathf.Lerp(0.8f, 1f, Mathf.PerlinNoise(Time.time * 4f, 0.7f));
        Color c = counterSign.color;
        c.a = alpha;
        counterSign.color = c;
    }

    /// <summary>E at the door: the leaf swings out, the player walks through into the dark, then GameFlow.NextFloor().</summary>
    private void Descend()
    {
        StartCoroutine(doorHinge != null ? WalkOutRoutine() : DescendRoutine());
    }

    /// <summary>
    /// Like MazeEscape.EnterHatch: freeze, open the door, ease the player up to the doorway looking through it,
    /// then on through it while the screen fades to black. The leaf swings away from the room, so it never
    /// sweeps through the player.
    /// </summary>
    private IEnumerator WalkOutRoutine()
    {
        // GameFlow.IsInShop stays true until the reload (GameFlow.Reload clears it): clearing it here brought the
        // star counter and the in-maze HUD back for the length of the walk. _open = false already stops Update.
        _open = false;
        PlayerLock.Freeze(_player, true);
        if (_hud != null) _hud.SetPrompt(null);

        ThirdPersonController controller = _player.GetComponent<ThirdPersonController>();
        CharacterController characterController = _player.GetComponent<CharacterController>();

        // Out = from the room's centre toward the door, flat - holds however the room is turned in the Editor.
        Vector3 outward = _doorwayCentre - transform.position;
        outward.y = 0f;
        outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : -transform.forward;

        float y = _player.position.y;
        Vector3 doorway = new Vector3(_doorwayCentre.x, y, _doorwayCentre.z);
        Vector3 start = _player.position;
        Vector3 threshold = doorway - outward * 0.6f;
        // Like the asylum's doors: stop just short of the doorway (the camera near plane is 0.2 m) and be black by then. Walking on through
        // it went out of the room into empty space - the floor's grey fog, "a grey wall".
        Vector3 stop = doorway - outward * 0.3f;
        Vector3 Look(Vector3 from) => from + outward * 4f + Vector3.up * 1.4f;

        // Nothing is built outside the room: for the walk-out, empty space is black (sky and fog), restored before the hand-over.
        Camera view = Camera.main;
        CameraClearFlags clearFlags = view != null ? view.clearFlags : CameraClearFlags.Skybox;
        Color background = view != null ? view.backgroundColor : Color.black;
        Color fogColor = RenderSettings.fogColor;
        if (view != null) { view.clearFlags = CameraClearFlags.SolidColor; view.backgroundColor = Color.black; }
        RenderSettings.fogColor = Color.black;
        // The room's Backdrop (the grey panels that close it off from outside) has one right behind the door: that is the "grey wall"
        // the open door showed. Hidden for the walk-out, so the doorway is dark like every other door.
        List<Renderer> hidden = new List<Renderer>();
        Transform backdrop = transform.Find("Backdrop");
        if (backdrop != null)
        {
            foreach (Renderer r in backdrop.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || Vector3.Dot(r.bounds.center - _doorwayCentre, outward) <= 0.05f || r.bounds.SqrDistance(_doorwayCentre) > 1.5f * 1.5f) continue;
                r.enabled = false;
                hidden.Add(r);
            }
        }
        void RestoreView()
        {
            if (view != null) { view.clearFlags = clearFlags; view.backgroundColor = background; }
            RenderSettings.fogColor = fogColor;
            foreach (Renderer r in hidden) if (r != null) r.enabled = true;
        }

        GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.35f, 60f, 0.5f), _doorwayCentre, 0.8f);
        // Whichever way the swing carries the leaf's centre out of the room.
        Vector3 leafOffset = _doorwayCentre - doorHinge.position;
        float angle = Vector3.Dot(Quaternion.AngleAxis(doorOpenAngle, doorHinge.up) * leafOffset - leafOffset, outward) >= 0f ? doorOpenAngle : -doorOpenAngle;
        Quaternion opened = _hingeClosed * Quaternion.Euler(0f, angle, 0f);

        // 1. The door swings open while the player steps up to it.
        float t = 0f;
        while (t < doorOpenSeconds)
        {
            if (GameOutcome.IsOver) { RestoreView(); yield break; }

            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / doorOpenSeconds);
            doorHinge.localRotation = Quaternion.Slerp(_hingeClosed, opened, k);
            MovePlayer(characterController, Vector3.Lerp(start, threshold, k));
            if (controller != null) controller.LookAt(Look(_player.position), 1f - Mathf.Exp(-8f * Time.deltaTime));
            yield return null;
        }
        doorHinge.localRotation = opened;

        // 2. Through the doorway into the dark; the room's hum and the light go together.
        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = canvas != null ? RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>() : null;
        if (blackout != null) blackout.transform.SetAsLastSibling();
        float humStart = _hum != null ? _hum.volume : 0f;

        t = 0f;
        while (t < doorWalkSeconds)
        {
            if (GameOutcome.IsOver) { RestoreView(); yield break; }

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / doorWalkSeconds);
            // Starts at walking pace, no ease-in: it continues the step-up without a stop at the threshold.
            MovePlayer(characterController, Vector3.Lerp(threshold, stop, 1f - (1f - k) * (1f - k)));
            if (controller != null) controller.LookAt(Look(_player.position), 1f - Mathf.Exp(-8f * Time.deltaTime));
            float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.1f) / 0.9f));
            if (blackout != null) blackout.color = new Color(0f, 0f, 0f, fade);
            if (_hum != null) _hum.volume = humStart * (1f - fade);
            yield return null;
        }
        if (blackout != null) blackout.color = Color.black;

        RestoreView();
        if (_hum != null) _hum.Stop();
        if (LeaveToHub(blackout)) yield break;
        GameFlow.NextFloor();
    }

    /// <summary>
    /// F94: when the shop was reached through the hub's SHOP door, its exit goes back to the hospital (not on to the next floor) and the
    /// room is reset for a later visit. Returns false in the old flow (and for a legacy shop save), where the exit is GameFlow.NextFloor().
    /// </summary>
    private bool LeaveToHub(Image blackout)
    {
        if (!GameFlow.ShopFromHub) return false;

        _open = false;
        if (_hum != null) _hum.Stop();
        if (doorHinge != null) doorHinge.localRotation = _hingeClosed;

        AsylumHub hub = AsylumHub.Active;
        if (hub != null && hub.CanVisitShop) hub.ReturnFromShop();
        else
        {
            // The hub went away mid-visit (cannot normally happen): reload the floor we were about to play, do not skip one.
            GameFlow.ShopFromHub = false;
            GameFlow.Restart(false, 0, true);
        }
        // The hub has made its own black by now (EnterRoutine sets it before its first yield); this panel can go.
        if (blackout != null) Destroy(blackout.gameObject);
        return true;
    }

    /// <summary>A CharacterController overrides transform writes unless it is switched off around them.</summary>
    private void MovePlayer(CharacterController characterController, Vector3 position)
    {
        if (characterController != null) characterController.enabled = false;
        _player.position = position;
        if (characterController != null) characterController.enabled = true;
    }

    // ---------------------------------------------------------------- door hinge

    /// <summary>
    /// Puts a pivot on one vertical edge of the closed leaf (room-local min X) and parents the leaf under it,
    /// so turning the pivot about its up axis swings the door. Used by ShopRoomBuilder, and at runtime for a
    /// room built before the hinge existed. Returns the pivot.
    /// </summary>
    public static Transform CreateDoorHinge(Transform room, Transform leaf)
    {
        Renderer[] renderers = leaf.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return null;

        // The leaf's extent in room space (its world AABB corners brought into the room's frame).
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        foreach (Renderer r in renderers)
        {
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 local = room.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }

        GameObject hinge = new GameObject("DoorHinge");
        hinge.transform.SetParent(leaf.parent, false);
        hinge.transform.position = room.TransformPoint(new Vector3(min.x, 0.5f * (min.y + max.y), 0.5f * (min.z + max.z)));
        hinge.transform.rotation = room.rotation;
        leaf.SetParent(hinge.transform, true);
        return hinge.transform;
    }

    /// <summary>Runtime fallback for a room built before doorHinge existed: Door/WoodDoor01... gets its hinge now.</summary>
    private Transform FindAndHingeDoorLeaf()
    {
        Transform door = transform.Find("Door");
        if (door == null) return null;

        Transform existing = door.Find("DoorHinge");
        if (existing != null) return existing;

        foreach (Transform child in door)
        {
            if (child.name.StartsWith("WoodDoor01")) return CreateDoorHinge(transform, child);
        }
        return null;
    }

    private static Bounds RendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    /// <summary>The old exit, kept for a room without a door leaf: a plain fade, then GameFlow.NextFloor().</summary>
    private IEnumerator DescendRoutine()
    {
        _open = false;
        if (!GameFlow.ShopFromHub) GameFlow.IsInShop = false; // from the hub, ReturnFromShop clears it after the hub is entered
        PlayerLock.Freeze(_player, true);
        if (_hud != null) _hud.SetPrompt(null);
        if (_hum != null) _hum.Stop();

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = canvas != null ? RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>() : null;
        if (blackout != null) blackout.transform.SetAsLastSibling();

        float t = 0f;
        while (t < 0.5f)
        {
            // GameFlow.NextFloor reloads the scene regardless, but bail cleanly if an ending beat us to it.
            if (GameOutcome.IsOver) yield break;

            t += Time.deltaTime;
            if (blackout != null) blackout.color = new Color(0f, 0f, 0f, Mathf.Clamp01(t / 0.5f));
            yield return null;
        }

        if (LeaveToHub(blackout)) yield break;
        GameFlow.NextFloor();
    }

    /// <summary>S9: the contract result line (a Subtitles key) if one is waiting, else the floor's greeting.</summary>
    private static string GreetingText()
    {
        string key = ContractState.TakeSalesmanLine();
        return L10n.T(string.IsNullOrEmpty(key) ? GreetingKeyFor(GameFlow.ShopClearedFloor) : key);
    }

    private static string GreetingKeyFor(int floor)
    {
        switch (floor)
        {
            case 1: return "sub.shop.greeting.1";
            case 2: return "sub.shop.greeting.2";
            case 3: return "sub.shop.greeting.3";
            default: return "sub.shop.greeting.4";
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>XZ only: a Y-ranged test is one CharacterController skin-width away from never matching the player's feet.</summary>
    private static bool ContainsXZ(Collider zone, Vector3 point)
    {
        if (zone == null) return false;
        Bounds bounds = zone.bounds;
        return point.x >= bounds.min.x && point.x <= bounds.max.x
            && point.z >= bounds.min.z && point.z <= bounds.max.z;
    }

    /// <summary>4 s of two low sines with a slow amplitude wobble - the pattern every synthesised clip in this project follows.</summary>
    internal static AudioClip BuildHumClip()
    {
        const int sampleRate = 44100;
        const float duration = 4f;

        int sampleCount = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float wobble = 0.9f + 0.1f * Mathf.Sin(2f * Mathf.PI * 0.3f * t); // 0.8..1
            float value = 0.5f * Mathf.Sin(2f * Mathf.PI * 55f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 110f * t);
            samples[i] = value * wobble;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.5f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("ShopHum", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        if (_humClip != null && !_humIsAsset) Destroy(_humClip);
    }
}
