using UnityEngine;

/// <summary>
/// F106: the Visitor, the looping corridor's own creature (the hunter cannot enter it, so this is the threat inside). A dark, frozen copy of the
/// hunter's body that stands behind the player and, like Stan, only moves while nobody is looking at it. The whole state is one number, `Distance`:
/// how far behind the player it is along the corridor's path (the first leg, through the corner, to the lap trigger; behind is the other way on a lap walked back). It creeps closer while
/// unseen, falls back a little for every right decision and comes four metres nearer for every wrong one; at the catch distance it has you.
/// A plain class ticked by LoopCorridor, so it lives and stops with it.
/// </summary>
public sealed class LoopVisitor
{
    private readonly LoopCorridor _owner;
    private readonly PhantomBody _body;
    private readonly Material _material;
    private readonly Camera _camera;
    private readonly LoopCorridor.PuzzleTuning _tuning;
    private bool _shown;
    private float _creptSinceStep;

    private LoopVisitor(LoopCorridor owner, PhantomBody body, Material material, Camera camera)
    {
        _owner = owner;
        _body = body;
        _material = material;
        _camera = camera;
        _tuning = owner.Config.puzzle;
    }

    /// <summary>How far behind the player it is along the path (m). Starts at the puzzle's start distance when the trap closes.</summary>
    public float Distance { get; private set; }

    /// <summary>True when it is close enough to have the player.</summary>
    public bool HasCaught => Distance <= _tuning.visitorCatchDistance;

    /// <summary>
    /// Builds the Visitor from the hunter's own body, in the near-black silhouette material the phantoms wear. Null when there is no hunter body to copy
    /// (the puzzle then runs without it).
    /// </summary>
    public static LoopVisitor TryCreate(LoopCorridor owner, AIFollower hunter, Camera camera)
    {
        PhantomBody body = PhantomBody.TryBuild(hunter, owner.transform);
        if (body == null || camera == null) return null;

        Material dark = PhantomDirector.BuildSilhouetteMaterial(body.FirstMaterial);
        return new LoopVisitor(owner, body, dark, camera);
    }

    /// <summary>Puts it at the start distance, ready for the first lap of the puzzle.</summary>
    public void Begin()
    {
        Distance = _tuning.visitorStartDistance;
        _creptSinceStep = 0f;
    }

    /// <summary>A right decision pushes it back a little (never past the start distance); a wrong one brings it closer.</summary>
    public void Decided(bool correct)
    {
        Distance = correct
            ? Mathf.Min(Distance + _tuning.visitorReliefStep, _tuning.visitorStartDistance)
            : Distance - _tuning.visitorMistakeStep;
    }

    /// <summary>
    /// Once a frame while the player is inside: places the Visitor `Distance` metres behind them along the path (hidden while that is before the start of the
    /// corridor, so it is round the corner behind them), freezes it while it is seen and creeps it closer while it is not.
    /// </summary>
    /// <param name="pathPosition">The player's distance along the path (m).</param>
    public void Tick(float pathPosition, float deltaTime)
    {
        // Walked back, behind is further along the path, and it is round the corner once past the second leg's third cell (the mirror of the
        // first leg's start).
        LoopCorridor.Setup setup = _owner.Config;
        float pathEnd = setup.legLength + setup.trapDistance;
        bool reverse = _owner.IsReverse;
        float visitorPosition = reverse ? pathPosition + Distance : pathPosition - Distance;
        bool hidden = reverse ? visitorPosition > pathEnd : visitorPosition < 0f;
        Vector3 player = _owner.PlayerTransform.position;
        bool observed = false;

        if (hidden)
        {
            Hide();
        }
        else
        {
            Vector3 position = _owner.PointOnPath(visitorPosition);
            Vector3 facing = player - position;
            facing.y = 0f;
            if (!_shown) Present(position, facing);
            else _body.Root.transform.SetPositionAndRotation(position, facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing, Vector3.up) : _body.Root.transform.rotation);
            observed = IsObserved(position + Vector3.up * _tuning.visitorChestHeight);
        }

        if (observed) return;

        float creep = _tuning.visitorCreepSpeed * deltaTime;
        Distance -= creep;
        if (Distance > _tuning.visitorStepRange) return;

        _creptSinceStep += creep;
        if (_creptSinceStep < _tuning.visitorStepStride) return;

        _creptSinceStep = 0f;
        GameAudio.PlayAt(GameAudio.Library.timmyStep, _owner.PointOnPath(Mathf.Clamp(visitorPosition, 0f, pathEnd)), _tuning.visitorStepVolume, _tuning.visitorStepPitch);
    }

    /// <summary>The catch: it stands right behind the player, shown. Returns where its head is, which the camera should end up looking at.</summary>
    public Vector3 PlaceBehind(Transform player, Transform view)
    {
        Vector3 back = -view.forward;
        back.y = 0f;
        if (back.sqrMagnitude < 1e-4f) back = -player.forward;

        Vector3 position = player.position + back.normalized * _tuning.visitorCatchDistance;
        Vector3 facing = player.position - position;
        if (_shown) _body.Root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing, Vector3.up));
        else Present(position, facing);

        return position + Vector3.up * _tuning.visitorHeadHeight;
    }

    /// <summary>Switches the body off, for good or until the next Tick shows it.</summary>
    public void Hide()
    {
        if (!_shown) return;

        _body.Hide();
        _shown = false;
    }

    /// <summary>Releases the material the Visitor made.</summary>
    public void Release()
    {
        Hide();
        if (_material != null) Object.Destroy(_material);
    }

    private void Present(Vector3 position, Vector3 facing)
    {
        _body.Show(position, facing);
        _body.Freeze(AIFollower.Pose.Idle);
        _body.SetMaterial(_material);
        _shown = true;
    }

    /// <summary>True when the chest is inside the camera's view and nothing stands between the eye and it.</summary>
    private bool IsObserved(Vector3 chest)
    {
        Vector3 view = _camera.WorldToViewportPoint(chest);
        float margin = _tuning.visitorViewMargin;
        if (view.z <= 0f || view.x < -margin || view.x > 1f + margin || view.y < -margin || view.y > 1f + margin) return false;

        return !Physics.Linecast(_camera.transform.position, chest, ~0, QueryTriggerInteraction.Ignore);
    }
}
