using TMPro;
using UnityEngine;

/// <summary>
/// F116: THE COMMISSARY - the shop is a room of the asylum. The hub's ground-floor storeroom (Building/1stFloor/Stock) is redecorated by
/// AsylumHubBuilder.Shop into a warm, lit shop: a counter with the salesman behind it, the contract board and his shelves. The player
/// walks in through the room's real doorway, no fade and no teleport; the counter and the board are <see cref="IInteractable"/>s
/// (<see cref="HubShopService"/>) that open the same <see cref="ShopMenu"/> the old shop room used. This component lives on AsylumHub/Shop
/// (a hand-editable part of the hub prefab; AsylumHub switches it with the other content) and drives what the builder made: the room
/// zone, the door leaf, the lamps, the sign, the music and the salesman's head.
///
/// Open exactly when the old F94 SHOP door was: a floor has been cleared (GameFlow.CurrentFloor &gt;= 2) and the finale is not running.
/// Closed, the door leaf is shut and padlocked (<see cref="HubShopDoor"/>), the lamps are off and nothing inside can be reached.
/// The old far-away ShopRoom stays in the scene only as the fallback for a scene with no usable hub.
/// </summary>
public class HubShop : MonoBehaviour
{
    [Header("Room")]
    [Tooltip("Trigger volume over the room interior. The player counts as inside when XZ is within it and the feet are within its Y range (the classroom is right above).")]
    public BoxCollider roomZone;

    [Header("Services (E)")]
    [Tooltip("The counter: E opens the shop menu.")]
    public HubShopService counter;
    [Tooltip("The contract board: E opens the contract offers.")]
    public HubShopService board;

    [Header("Salesman")]
    [Tooltip("His head bone (or a plain pivot). It turns to follow the player, within Head Yaw Limit of how his body faces.")]
    public Transform headPivot;
    public ShopkeeperAnimator shopkeeper;
    public float headTurnSpeed = 40f;
    public float headYawLimit = 70f;

    [Header("Signs")]
    [Tooltip("The sign over the counter. Its alpha wavers.")]
    public TextMeshPro counterSign;
    [Tooltip("The plaque over the door on the hall side: COMMISSARY + OPEN / CLOSED.")]
    public TMP_Text doorPlaque;

    [Header("Door (the pack's Stock/Door_V1 leaf)")]
    [Tooltip("Stock/Door_V1 (8)/DoorHinge: swung to Open Yaw while the shop is open, to Closed Yaw while it is shut.")]
    public Transform doorHinge;
    public float openYaw = 265f;
    public float closedYaw = 355f;
    [Tooltip("The closed door's E target and blocker (enabled only while closed).")]
    public HubShopDoor doorLock;

    [Header("Light")]
    [Tooltip("The room's warm lights: on while the shop is open, off while it is shut (the room is dark like the rest of the hospital).")]
    public Light[] lamps = new Light[0];

    [Header("Music")]
    [SerializeField] private float musicVolume = 0.22f;
    [Tooltip("Share of the music heard from the hall (outside the room) while the shop is open: a warm thing in the dark.")]
    [SerializeField] private float hallShare = 0.3f;

    private ShopMenu _menu;
    private float _headBaseYaw;
    private float _headYawOffset;
    private Quaternion _headBaseRotation;
    private Animator _headAnimator;
    private bool _headCaptured;

    private bool _inside;
    private float _lastGreet = -999f;
    private AudioSource _music;
    private AudioClip _musicClip;
    private bool _musicIsAsset;
    private float _musicLevel; // 0..1 of musicVolume

    /// <summary>The F94 rule, now the Commissary's: a floor has been cleared (the player may have shards and the board offers) and the finale is not running.</summary>
    public static bool OpenFor(int currentFloor) => currentFloor >= 2 && !HubState.IsFinale && !HubBreach.ShopShut; // F121: shut from the breach on

    public bool IsOpen => OpenFor(GameFlow.CurrentFloor);

    /// <summary>True once the builder has made every part the component needs.</summary>
    public bool IsComplete => roomZone != null && counter != null && board != null && doorHinge != null && (headPivot != null || shopkeeper != null);

    /// <summary>True while the shop menu (or the contract panel) is open.</summary>
    public bool MenuOpen => _menu != null && _menu.IsOpen;

    /// <summary>The door leaf's hinge, in the hall wall (for the hall's wayfinding: no chalk star next to it).</summary>
    public Vector3 DoorPosition => doorHinge != null ? doorHinge.position : transform.position;

    private AsylumHub Hub => AsylumHub.Active;

    /// <summary>Called by AsylumHub.Configure (MazeGenerator.SetUpAtmosphere).</summary>
    public void Configure(ShopMenu menu)
    {
        _menu = menu;
    }

    // ---------------------------------------------------------------- lifecycle

    private void Awake()
    {
        CaptureHead();

        AudioClip track = GameAudio.Library.shopMusic.First(); // F97: the recorded safe-room track instead of the synthesised hum
        _musicIsAsset = track != null;
        _musicClip = _musicIsAsset ? track : ShopRoom.BuildHumClip();
        GameObject host = counter != null ? counter.gameObject : gameObject;
        _music = host.GetComponent<AudioSource>();
        if (_music == null) _music = host.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.clip = _musicClip;
        _music.spatialBlend = 0.8f;
        _music.minDistance = 3f;
        _music.maxDistance = 14f;
        _music.rolloffMode = AudioRolloffMode.Linear;
        _music.volume = 0f;
        if (_musicIsAsset) GameSettings.RouteMusic(_music); // S2: recorded music is music; the synthesised hum stays an effect
    }

    private void OnEnable()
    {
        L10n.Changed += RefreshPlaque;
        _inside = false;
    }

    private void OnDisable()
    {
        L10n.Changed -= RefreshPlaque;
        _inside = false;
        _musicLevel = 0f;
        if (_music != null) { _music.volume = 0f; _music.Stop(); }
    }

    private void OnDestroy()
    {
        if (_musicClip != null && !_musicIsAsset) Destroy(_musicClip);
    }

    private void CaptureHead()
    {
        if (headPivot == null && shopkeeper != null)
        {
            Animator a = shopkeeper.GetComponentInChildren<Animator>();
            if (a != null && a.isHuman) headPivot = a.GetBoneTransform(HumanBodyBones.Head);
        }
        if (_headCaptured || headPivot == null) return;
        _headCaptured = true;
        _headAnimator = headPivot.GetComponentInParent<Animator>();
        _headBaseRotation = headPivot.rotation;
        // A bone's own euler yaw means nothing, so an animated head takes its facing from the body.
        _headBaseYaw = _headAnimator != null ? _headAnimator.transform.eulerAngles.y : headPivot.eulerAngles.y;
    }

    // ---------------------------------------------------------------- state

    /// <summary>Re-reads GameFlow and sets the room to open or shut. Called by AsylumHub.EnterRoutine every time the hub switches on, at black.</summary>
    public void Refresh() => Apply(IsOpen);

    /// <summary>Sets the room to the open or shut look. The builder saves the prefab in the open look (lamps on, door open, blocker off), whatever GameFlow says in the Editor.</summary>
    public void Apply(bool open)
    {

        if (doorHinge != null)
        {
            Vector3 e = doorHinge.localEulerAngles;
            e.y = open ? openYaw : closedYaw;
            doorHinge.localEulerAngles = e;
        }
        if (doorLock != null) doorLock.SetClosed(!open);
        foreach (Light lamp in lamps) { if (lamp != null) lamp.enabled = open; }
        RefreshPlaque(open);
        Physics.SyncTransforms();
        _inside = false;
    }

    private void RefreshPlaque() => RefreshPlaque(IsOpen);

    private void RefreshPlaque(bool open)
    {
        if (doorPlaque == null) return;
        doorPlaque.text = L10n.T("hub.shopdoor.plaque", ("status", L10n.T(open ? "hub.shopdoor.status_open" : "hub.shopdoor.status_closed")));
        WingDoor.FitPlaque(doorPlaque);
        doorPlaque.color = open ? new Color(1f, 0.82f, 0.5f) : new Color(0.45f, 0.4f, 0.35f);
    }

    private bool Live
    {
        get
        {
            AsylumHub hub = Hub;
            return GameFlow.IsInHub && hub != null && hub.ContentActive && !hub.IsTransitioning && !hub.Leaving && IsOpen;
        }
    }

    private void Update()
    {
        UpdateSignWaver();
        bool live = Live;
        if (shopkeeper != null) shopkeeper.SetTalking(live && MenuOpen);
        UpdateMusic(live);
        if (MenuOpen && !IsOpen && GameFlow.IsInHub) _menu.Close(); // F121: the breach shut the Commissary under an open menu
        if (!live || Time.timeScale <= 0f) return;

        AsylumHub hub = Hub;
        if (MenuOpen && hub.PursuerAfterPlayer()) _menu.Close(); // something is coming: the menu freezes the player, so it gives way

        bool inside = Inside(hub.Player);
        if (inside == _inside) return;
        _inside = inside;
        if (!inside) return;
        hub.MarkShopVisited();

        // Walking in: one greeting (the contract result line first, else the floor's), the salesman looks up. Not again for a few seconds when the
        // player steps in and out of the doorway.
        if (Time.time - _lastGreet < 20f) return;
        _lastGreet = Time.time;
        if (hub.Hud != null) hub.Hud.ShowSubtitle(ShopRoom.GreetingText(), 3f);
        if (shopkeeper != null) shopkeeper.Greet();
    }

    /// <summary>LateUpdate, so the turn lands after the Animator has written the idle pose for this frame.</summary>
    private void LateUpdate()
    {
        if (!Live) return;
        CaptureHead();
        if (headPivot == null) return;
        AsylumHub hub = Hub;
        if (hub == null || hub.Player == null) return;
        _headYawOffset = ShopRoom.TrackHead(headPivot, _headAnimator, _headBaseRotation, _headBaseYaw, _headYawOffset, hub.Player, headTurnSpeed, headYawLimit);
    }

    /// <summary>XZ inside the room zone and the feet within its Y band - the classroom is directly above, so XZ alone would count it.</summary>
    public bool Inside(Transform player)
    {
        if (roomZone == null || player == null) return false;
        Bounds b = roomZone.bounds;
        Vector3 p = player.position;
        return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z && p.y >= b.min.y && p.y <= b.max.y;
    }

    private void UpdateSignWaver()
    {
        if (counterSign == null) return;
        Color c = counterSign.color;
        c.a = Mathf.Lerp(0.8f, 1f, Mathf.PerlinNoise(Time.time * 4f, 0.7f));
        counterSign.color = c;
    }

    private void UpdateMusic(bool live)
    {
        if (_music == null) return;
        float target = live ? (_inside ? 1f : hallShare) : 0f;
        _musicLevel = Mathf.MoveTowards(_musicLevel, target, Time.unscaledDeltaTime); // a second to fade
        _music.volume = Mathf.Clamp01(musicVolume * _musicLevel * (_musicIsAsset ? GameAudio.Library.shopMusic.volume : 1f));
        if (_musicLevel > 0.001f && !_music.isPlaying) _music.Play();
        else if (_musicLevel <= 0.001f && _music.isPlaying) _music.Stop();
    }

    // ---------------------------------------------------------------- the services

    /// <summary>E at the counter or the board. Refused (with a line) while a hub pursuer is after the player: the shop is no hiding place and the menu freezes the player.</summary>
    internal void OpenService(HubShopService.Kind kind)
    {
        AsylumHub hub = Hub;
        if (_menu == null || _menu.IsOpen || hub == null || !Live) return;
        if (hub.PursuerAfterPlayer())
        {
            if (hub.Hud != null) hub.Hud.ShowSubtitle(L10n.T("hub.not_now"), 1.6f);
            return;
        }
        if (kind == HubShopService.Kind.Counter)
        {
            if (shopkeeper != null) shopkeeper.Greet();
            _menu.Open();
        }
        else _menu.OpenContracts();
    }
}
