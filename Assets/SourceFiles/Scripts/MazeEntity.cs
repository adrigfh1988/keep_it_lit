using System.Collections;
using StarterAssets;
using UnityEngine;

/// <summary>
/// F107 D1: base of the maze's swappable entities. MazeGenerator.BuildEntities creates them (stream +25) and SetUpAtmosphere binds each one
/// to the player systems through <see cref="Configure"/>; the base resolves the references every entity needs and shares the small helpers
/// (can-act gate, line and view tests, the pin, the camera shake, the log line). The Stalker is deliberately not one yet.
/// </summary>
public abstract class MazeEntity : MonoBehaviour
{
    /// <summary>Which roster flag this entity is.</summary>
    public abstract EntityKind Kind { get; }

    /// <summary>True if it can end the run itself. The roster allows one killer besides the hunter.</summary>
    public virtual bool IsKiller => false;

    /// <summary>The character DebugMap draws in this entity's cell.</summary>
    public virtual char DebugGlyph => '?';

    /// <summary>Late-binds the entity to the maze, the player and the hunter (everything else it needs is resolved from them). Runs once, from SetUpAtmosphere.</summary>
    public abstract void Configure(MazeGenerator maze, Transform player, AIFollower hunter);

    /// <summary>Takes the entity out of play for `dormantSeconds` and away from the player (a Second Wind's release). The default does nothing.</summary>
    public virtual void WarpAway(float dormantSeconds) { }

    protected MazeGenerator Maze { get; private set; }
    protected Transform Player { get; private set; }
    protected AIFollower Hunter { get; private set; }
    protected PlayerStealthState Stealth { get; private set; }
    protected ThirdPersonController Controller { get; private set; }
    protected Flashlight Torch { get; private set; }
    protected PlayerHud Hud { get; private set; }
    protected GameOutcome Outcome { get; private set; }
    protected MazeEscape Escape { get; private set; }
    protected Transform ViewCamera { get; private set; }

    private DreadMeter _dread;
    private readonly RaycastHit[] _hits = new RaycastHit[8];

    /// <summary>Fills the shared references. Call first thing from Configure.</summary>
    protected void BindSystems(MazeGenerator maze, Transform player, AIFollower hunter)
    {
        Maze = maze;
        Player = player;
        Hunter = hunter;
        if (player != null)
        {
            Stealth = player.GetComponent<PlayerStealthState>();
            Controller = player.GetComponent<ThirdPersonController>();
            FirstPersonRig rig = player.GetComponent<FirstPersonRig>();
            Torch = rig != null ? rig.Flashlight : null;
            ViewCamera = rig != null && rig.PlayerCamera != null ? rig.PlayerCamera.transform : player;
        }

        Hud = FindAnyObjectByType<PlayerHud>();
        Outcome = FindAnyObjectByType<GameOutcome>();
        Escape = FindAnyObjectByType<MazeEscape>();
        _dread = FindAnyObjectByType<DreadMeter>();
    }

    /// <summary>True while the run is live and nothing is ending it: no ending, no capture, no hatch walk-in.</summary>
    protected bool CanAct =>
        GameFlow.IsRunActive && !GameOutcome.IsOver
        && (Outcome == null || !Outcome.IsEnding)
        && (Escape == null || !Escape.IsEntering);

    /// <summary>A spike in the dread meter (a Lunge, a shove, a drop).</summary>
    protected void PulseDread() => _dread?.Spike();

    /// <summary>Flat (XZ) distance from `from` to the player, or infinity before binding.</summary>
    protected float FlatDistanceToPlayer(Vector3 from)
    {
        if (Player == null) return float.PositiveInfinity;
        Vector3 d = Player.position - from;
        d.y = 0f;
        return d.magnitude;
    }

    /// <summary>True if nothing solid lies between the two points once `ignore`, this entity and the player's own hierarchy are skipped (triggers never block).</summary>
    protected bool HasClearLine(Vector3 from, Vector3 to, Transform ignore)
    {
        Vector3 ray = to - from;
        float distance = ray.magnitude;
        if (distance < 0.0001f) return true;

        int count = Physics.RaycastNonAlloc(from, ray / distance, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = _hits[i].transform;
            if (IsPartOf(hit, ignore) || IsPartOf(hit, transform) || IsPartOf(hit, Player)) continue;
            return false;
        }

        return true;
    }

    /// <summary>True if `point` is inside the camera's view cone (half-angle in degrees) and within `maxDistance`, ignoring walls.</summary>
    protected bool IsInViewCone(Vector3 point, float halfAngle, float maxDistance)
    {
        if (ViewCamera == null) return false;

        Vector3 to = point - ViewCamera.position;
        float distance = to.magnitude;
        if (distance > maxDistance) return false;
        return distance < 0.01f || Vector3.Angle(ViewCamera.forward, to) <= halfAngle;
    }

    /// <summary>Plays the first slot that has a clip, as a 3D one-shot. False if none did.</summary>
    protected static bool PlayFirst(Vector3 position, float volume, float pitch, params AudioSlot[] slots)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].Has) return GameAudio.PlayAt(slots[i], position, volume, pitch);
        }

        return false;
    }

    /// <summary>One [Terror]-tagged Console line, e.g. "Listener: Dormant -> Drawn". Respects TerrorSettings.logEvents.</summary>
    protected void LogEvent(string message)
    {
        if (Maze != null) Maze.LogTerror($"{GetType().Name}: {message}", this);
        else Debug.Log($"[Terror] {GetType().Name}: {message}", this);
    }

    /// <summary>
    /// Holds the player for `seconds` with the view pulled onto `lookAt`, the torch stuttering once. Does nothing if they are hidden, already
    /// held, or the run is ending. Scaled time, so a pause holds it.
    /// </summary>
    protected IEnumerator PinPlayer(Vector3 lookAt, float seconds)
    {
        if (Controller == null || Stealth == null || Stealth.Hidden || Controller.MovementLocked || !CanAct) yield break;

        PlayerLock.Freeze(Player, true);
        if (Torch != null) Torch.Stutter(0.5f);
        LogEvent("player pinned");

        for (float t = 0f; t < seconds && CanAct; t += Time.deltaTime)
        {
            Controller.LookAt(lookAt, 1f - Mathf.Exp(-8f * Time.deltaTime));
            yield return null;
        }

        // Ending: the sequence that ended the run owns the freeze now.
        if (!GameOutcome.IsOver && (Outcome == null || !Outcome.IsEnding))
        {
            PlayerLock.Freeze(Player, false);
            LogEvent("player released");
        }
    }

    /// <summary>
    /// A short decaying yaw jitter on the view, scaled by the Camera shake setting and returned to the starting heading, so it can never turn
    /// the player. Skipped at zero shake.
    /// </summary>
    protected IEnumerator ShakeView(float seconds, float degrees)
    {
        float scale = degrees * GameSettings.CameraShake;
        if (Controller == null || scale <= 0f) yield break;

        float applied = 0f;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float envelope = 1f - t / seconds;
            float target = (Mathf.PerlinNoise(t * 30f, 0.37f) * 2f - 1f) * scale * envelope;
            Controller.AddYaw(target - applied);
            applied = target;
            yield return null;
        }

        Controller.AddYaw(-applied);
    }

    private static bool IsPartOf(Transform candidate, Transform root)
    {
        if (root == null) return false;
        while (candidate != null)
        {
            if (candidate == root) return true;
            candidate = candidate.parent;
        }

        return false;
    }
}
