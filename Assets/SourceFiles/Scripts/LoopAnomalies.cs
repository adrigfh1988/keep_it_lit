using UnityEngine;

/// <summary>
/// F106: one thing that can be wrong in the looping corridor. At most one is active per lap; LoopCorridor applies it at the cut that starts
/// the lap, so the player never sees it appear, and reverts it at the next cut. Each is built from pieces MazeGenerator.BuildLoopCorridor made
/// (inactive objects, lamps), so applying one costs nothing. They only touch what is behind the landing point or round the corner.
///
/// The corridor can be walked both ways (12 Oct 2026): a lap walked forward lands in the first leg and shows its anomaly in the second; a lap
/// walked in reverse lands in the second leg and shows it in the first. The pieces that sit in a leg are built twice, one per leg at the same
/// place along it, and `reverse` picks the one round the corner ahead.
/// </summary>
public abstract class LoopAnomaly
{
    /// <summary>For the [Terror] log.</summary>
    public abstract string Name { get; }

    /// <summary>Puts the anomaly in the corridor. Called once, at the cut that starts the lap; `reverse` is the way that lap is walked.</summary>
    public abstract void Apply(bool reverse);

    /// <summary>Takes it out again (both legs' pieces). Safe to call when it was never applied.</summary>
    public abstract void Revert();

    /// <summary>Called every frame while the player is inside and the anomaly is active, for those that move or change over time.</summary>
    public virtual void Tick() { }
}

/// <summary>Anomaly 1: one lamp burns red.</summary>
public sealed class LampTintAnomaly : LoopAnomaly
{
    private readonly WallLamp _forward, _reverse;
    private readonly Color _color;

    public LampTintAnomaly(WallLamp forward, WallLamp reverse, Color color)
    {
        _forward = forward;
        _reverse = reverse;
        _color = color;
    }

    public override string Name => "a red lamp";
    public override void Apply(bool reverse) => (reverse ? _reverse : _forward).SetTint(_color);

    public override void Revert()
    {
        _forward.ClearTint();
        _reverse.ClearTint();
    }
}

/// <summary>Anomaly 2: one lamp is dead.</summary>
public sealed class LampDeadAnomaly : LoopAnomaly
{
    private readonly WallLamp _forward, _reverse;

    public LampDeadAnomaly(WallLamp forward, WallLamp reverse)
    {
        _forward = forward;
        _reverse = reverse;
    }

    public override string Name => "a dead lamp";
    public override void Apply(bool reverse) => (reverse ? _reverse : _forward).SetPowered(false);

    public override void Revert()
    {
        _forward.SetPowered(true);
        _reverse.SetPowered(true);
    }
}

/// <summary>Anomalies 3, 4 and 8: something built inactive at corridor-build time (the blood on the floor, the locker that was not there, the handprint) is switched on.</summary>
public sealed class ShowObjectAnomaly : LoopAnomaly
{
    private readonly GameObject _forward, _reverse;
    private readonly string _name;

    public ShowObjectAnomaly(GameObject forward, GameObject reverse, string anomalyName)
    {
        _forward = forward;
        _reverse = reverse;
        _name = anomalyName;
    }

    public override string Name => _name;
    public override void Apply(bool reverse) => (reverse ? _reverse : _forward).SetActive(true);

    public override void Revert()
    {
        _forward.SetActive(false);
        _reverse.SetActive(false);
    }
}
/// <summary>Anomaly 7: every corridor lamp flickers, in bursts of blinks.</summary>
public sealed class FlickerAnomaly : LoopAnomaly
{
    private const float BlinkFirst = 0.07f;
    private const float BlinkSecond = 0.09f;
    private const float BlinkGap = 0.18f;

    private readonly WallLamp[] _lamps;
    private readonly Vector2 _gap;
    private float _next = float.MaxValue;

    public FlickerAnomaly(WallLamp[] lamps, Vector2 burstGap)
    {
        _lamps = lamps;
        _gap = burstGap;
    }

    public override string Name => "every lamp flickering";
    public override void Apply(bool reverse) => _next = Time.time;
    public override void Revert() => _next = float.MaxValue;

    public override void Tick()
    {
        if (Time.time < _next) return;

        foreach (WallLamp lamp in _lamps)
        {
            lamp.Blink(BlinkFirst);
            lamp.Blink(BlinkSecond, BlinkGap);
        }

        _next = Time.time + Random.Range(_gap.x, _gap.y);
    }
}

/// <summary>Anomaly 5: someone stands at the corner ahead with their back to you. Gone the moment the beam reaches it or you walk up to it; it still counts as the lap's anomaly.</summary>
public sealed class FigureAnomaly : LoopAnomaly
{
    private readonly LoopCorridor _owner;
    private readonly Stalker _template;
    private PhantomBody _body;
    private bool _pending;
    private bool _shown;
    private float _showAt;
    private Vector3 _position, _forward; // where this lap's figure stands (the corner ahead of the way the lap is walked) and the way it faces

    public FigureAnomaly(LoopCorridor owner, Stalker template)
    {
        _owner = owner;
        _template = template;
    }

    public override string Name => "someone at the corner";

    public override void Apply(bool reverse)
    {
        _pending = true;
        _position = reverse ? _owner.Config.figurePositionReverse : _owner.Config.figurePosition;
        _forward = reverse ? _owner.Config.figureForwardReverse : _owner.Config.figureForward;
        _showAt = Time.time + _owner.Config.tuning.figureDelay;
    }

    public override void Revert()
    {
        _pending = false;
        Hide();
    }

    public override void Tick()
    {
        if (_pending && Time.time >= _showAt) Show();
        if (!_shown) return;

        LoopCorridor.Setup setup = _owner.Config;
        Vector3 chest = _position + Vector3.up * setup.tuning.figureTestHeight;
        bool close = Vector3.Distance(_owner.PlayerTransform.position, _position) < setup.tuning.figureVanishDistance;
        if (close || BeamReaches(chest)) Hide();
    }

    private void Show()
    {
        _pending = false;
        if (_body == null) _body = PhantomBody.TryBuild(_template, _owner.transform);
        if (_body == null) return;

        _body.Show(_position, _forward);
        _body.Freeze(AIFollower.Pose.Idle);
        _shown = true;
    }

    private void Hide()
    {
        if (!_shown) return;

        _body.Hide();
        _shown = false;
    }

    private bool BeamReaches(Vector3 point)
    {
        Flashlight torch = _owner.Torch;
        if (torch == null || !torch.IsBeamLit) return false;

        LoopCorridor.Tuning tuning = _owner.Config.tuning;
        Vector3 toPoint = point - torch.BeamOrigin;
        return toPoint.magnitude < torch.Range * tuning.figureBeamRangeFraction
            && Vector3.Angle(torch.BeamForward, toPoint) < torch.OuterAngle * tuning.figureBeamAngleFraction;
    }
}

/// <summary>Anomaly 6: footsteps behind you. A 3D source follows a few metres behind the player for a few seconds, stepping as they walk, with their own footstep clips pitched down to the hunter's. Purely audio.</summary>
public sealed class FootstepsAnomaly : LoopAnomaly
{
    private readonly LoopCorridor _owner;
    private readonly AudioClip[] _clips;
    private AudioSource _source;
    private float _until;
    private float _walked;
    private Vector3 _lastPlayerPosition;

    public FootstepsAnomaly(LoopCorridor owner, AudioClip[] clips)
    {
        _owner = owner;
        _clips = clips;
    }

    public override string Name => "footsteps behind you";

    public override void Apply(bool reverse)
    {
        LoopCorridor.PuzzleTuning puzzle = _owner.Config.puzzle;
        if (_source == null)
        {
            GameObject holder = new GameObject("Loop_Footsteps");
            holder.transform.SetParent(_owner.transform, false);
            _source = holder.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 1.5f;
            _source.maxDistance = puzzle.footstepsMaxDistance;
            GameSettings.RouteEffect(_source);
        }

        _until = Time.time + puzzle.footstepsSeconds;
        _walked = puzzle.footstepsStride * 0.5f; // half a stride, so the first step does not land on the cut
        _lastPlayerPosition = _owner.PlayerTransform.position;
        Follow();
    }

    public override void Revert()
    {
        _until = 0f;
        if (_source != null) _source.Stop();
    }

    public override void Tick()
    {
        if (Time.time >= _until) return;

        Follow();
        LoopCorridor.PuzzleTuning puzzle = _owner.Config.puzzle;
        Vector3 position = _owner.PlayerTransform.position;
        _walked += Vector3.Distance(position, _lastPlayerPosition);
        _lastPlayerPosition = position;
        if (_walked < puzzle.footstepsStride) return;

        _walked = 0f;
        _source.pitch = Random.Range(puzzle.footstepsPitch.x, puzzle.footstepsPitch.y);
        _source.PlayOneShot(_clips[Random.Range(0, _clips.Length)], puzzle.footstepsVolume);
    }

    /// <summary>Keeps the source a few metres behind the player's view, on the floor.</summary>
    private void Follow()
    {
        Transform view = _owner.CameraTransform != null ? _owner.CameraTransform : _owner.PlayerTransform;
        Vector3 back = -view.forward;
        back.y = 0f;
        if (back.sqrMagnitude < 1e-4f) back = -view.right;

        Vector3 position = _owner.PlayerTransform.position + back.normalized * _owner.Config.puzzle.footstepsBehind;
        _source.transform.position = position;
    }
}
