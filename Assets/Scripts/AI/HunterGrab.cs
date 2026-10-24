using System.Collections;
using StarterAssets;
using UnityEngine;

/// <summary>
/// F120: the survivable grab. The first time the hunter catches the player on a floor it is a shove, not a death: the torch is knocked out of
/// the player's hand and lands a few metres away still lit (a <see cref="DroppedTorch"/> cloned from <c>InteractableKit.droppedTorch</c>), the
/// hunter is stunned and then searches around the torch, and the player limps (slower, no sprint) for a few seconds until they walk back and
/// press E on it. The sequence itself lives in <c>GameOutcome.Grab.cs</c> (it needs IsEnding, the fades and the escape timer); this component
/// owns the state: whether the free grab is spent, the limp, the prop. Added on the MazeGenerator object by SetUpAtmosphere, so a scene
/// reload (Retry, NEXT FLOOR, CONTINUE) creates a fresh one - "once per floor" needs no static and nothing is saved.
/// </summary>
public class HunterGrab : MonoBehaviour
{
    [Header("F120 - survivable grab")]
    [Tooltip("Seconds the hunter stands stunned after the shove")]
    [SerializeField] private float stunSeconds = 3f;
    [Tooltip("Seconds the player limps afterwards")]
    [SerializeField] private float limpSeconds = 10f;
    [Tooltip("Movement speed multiplier while limping (also no sprint)")]
    [SerializeField] private float limpSpeedScale = 0.75f;
    [Tooltip("How far (m) the torch lands from the player: min and max")]
    [SerializeField] private float throwMin = 2.2f;
    [SerializeField] private float throwMax = 4.5f;
    [Tooltip("How far (m) the shove pushes the player away from the hunter")]
    [SerializeField] private float shoveDistance = 1.1f;
    [Tooltip("How far (m) the hunter steps back when it lets go")]
    [SerializeField] private float hunterStepBack = 0.8f;

    /// <summary>
    /// F122 hook: set per floor by a difficulty rule ("no mercy") to remove the free grab. Static run state: reset at SubsystemRegistration and in
    /// GameFlow.ReturnToMenu, like RunRules.
    /// </summary>
    public static bool DisabledByRules;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => DisabledByRules = false;

    public float StunSeconds => stunSeconds;
    public float HunterStepBack => hunterStepBack;

    private Transform _player;
    private ThirdPersonController _controller;
    private PlayerStamina _stamina;
    private Flashlight _flashlight;
    private AIFollower _hunter;
    private PlayerHud _hud;
    private DroppedTorch _template;
    private TerrorSettings _terror = new TerrorSettings();

    private bool _used;
    private bool _limping;
    private float _limpLeft;
    private bool _warnedMissingTemplate;
    private DroppedTorch _prop;
    private int _side = 1;

    /// <summary>True once this floor's free grab has been spent.</summary>
    public bool Used => _used;

    /// <summary>True while the player limps.</summary>
    public bool Limping => _limping;

    /// <summary>The torch lying on the floor, or null.</summary>
    public DroppedTorch Prop => _prop;

    public void Configure(Transform player, ThirdPersonController controller, PlayerStamina stamina, Flashlight flashlight, AIFollower hunter,
        PlayerHud hud, DroppedTorch template, TerrorSettings terror)
    {
        _player = player;
        _controller = controller;
        _stamina = stamina;
        _flashlight = flashlight;
        _hunter = hunter;
        _hud = hud;
        _template = template;
        _terror = terror ?? new TerrorSettings();
    }

    /// <summary>
    /// Survivable grab / dropped torch feature has been removed: catches go directly to Second Wind or death.
    /// </summary>
    public bool CanGrab()
    {
        return false;
    }

    /// <summary>
    /// Survivable grab / dropped torch feature has been removed: catches in the hub breach go directly to Second Wind or death.
    /// </summary>
    public bool CanGrabInHub()
    {
        return false;
    }

    /// <summary>Spends the free grab.</summary>
    public void MarkUsed() => _used = true;

    /// <summary>
    /// The shove: drops the torch (spawning the prop, which lands 2.2 to 4.5 m away from the hunter side of the player) and pushes the player
    /// away from the hunter. Returns the point the torch will land on.
    /// </summary>
    public Vector3 Shove() => Shove(_hunter.transform.position, true);

    /// <summary>F121: the shove from any captor at `hunterPos` (the hub breach's HubStalker); `spend` false leaves this floor's free grab alone.</summary>
    public Vector3 Shove(Vector3 hunterPos, bool spend)
    {
        Vector3 playerPos = _player.position;
        Vector3 away = playerPos - hunterPos;
        away.y = 0f;
        away = away.sqrMagnitude < 0.0001f ? _player.forward : away.normalized;

        // Away from the hunter, rotated off the shove path by 35..80 degrees, alternating sides.
        _side = -_side;
        Vector3 throwDir = Quaternion.AngleAxis(_side * Random.Range(35f, 80f), Vector3.up) * away;
        Vector3 from = _flashlight.BeamOrigin;

        _flashlight.Drop();
        _prop = Instantiate(_template, from, Quaternion.identity);
        _prop.gameObject.name = "DroppedTorch";
        _prop.gameObject.SetActive(true);
        Vector3 landing = _prop.Throw(from, throwDir, Random.Range(throwMin, throwMax), _flashlight, this);

        StartCoroutine(Push(away));
        if (spend) _used = true;
        return landing;
    }

    /// <summary>The shove moves the player over 0.2 s through the CharacterController, so walls stop it.</summary>
    private IEnumerator Push(Vector3 direction)
    {
        CharacterController cc = _player != null ? _player.GetComponent<CharacterController>() : null;
        if (cc == null) yield break;

        float duration = 0.2f;
        float t = 0f;
        while (t < duration && cc != null && cc.enabled)
        {
            float dt = Time.deltaTime;
            t += dt;
            cc.Move(direction * (shoveDistance / duration * dt));
            yield return null;
        }
    }

    /// <summary>The limp: slower and no sprint for limpSeconds (scaled time).</summary>
    public void BeginLimp()
    {
        if (_limping) return;
        _limping = true;
        _limpLeft = limpSeconds;
        if (_controller != null) _controller.SpeedMultiplier *= limpSpeedScale; // multiply/divide, never assign (SqueezeZone's rule)
        if (_stamina != null) _stamina.LimpBlocked = true;
        GameAudio.Play2D(GameAudio.Library.lockerBreath, 0.35f, 0.9f); // a heavy breath; silent if the slot is empty
    }

    public void EndLimp()
    {
        if (!_limping) return;
        _limping = false;
        if (_controller != null) _controller.SpeedMultiplier /= limpSpeedScale;
        if (_stamina != null) _stamina.LimpBlocked = false;
    }

    private void Update()
    {
        if (!_limping) return;
        _limpLeft -= Time.deltaTime;
        if (_limpLeft <= 0f) EndLimp();
    }

    /// <summary>The player pressed E on the prop.</summary>
    public void PickUpTorch(DroppedTorch prop)
    {
        if (_flashlight != null) _flashlight.PickUp();
        if (_hud != null) _hud.ShowSubtitle(L10n.T("sub.grab.torch_back"), 1.6f);
        if (prop != null) Destroy(prop.gameObject);
        if (prop == _prop) _prop = null;
    }

    /// <summary>
    /// A same-scene transition (the floor cleared into the hub, the old shop room, the descent clear): the limp ends and the torch is back in
    /// hand, off, with the prop gone.
    /// </summary>
    public void EndForTransition()
    {
        EndLimp();
        if (_flashlight != null) _flashlight.RecoverDropped();
        if (_prop != null) Destroy(_prop.gameObject);
        _prop = null;
    }

    private void OnDestroy()
    {
        EndLimp();
    }
}
