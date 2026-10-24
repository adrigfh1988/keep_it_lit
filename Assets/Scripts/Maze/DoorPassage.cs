using System.Linq;
using UnityEngine;

/// <summary>
/// F112: one walk-through door of the asylum (a wing door, the finale's main doors). Lives on the same GameObject as its
/// <see cref="WingDoor"/> / <see cref="HubExitDoor"/> and holds what the shared walk (AsylumHub.DoorWalk) needs: the
/// leaf hinges, the unlit void that fills the opening behind the leaves, the draft of cold air and the swing itself. The leaves swing
/// TOWARD the hall (pulled open, never into the wall behind them) after the walk has eased the player back out of their sweep.
/// It never moves the player.
/// </summary>
public class DoorPassage : MonoBehaviour
{
    [Tooltip("Hinge pivots (made by ShopRoom.CreateDoorHinge). Left/single first.")]
    public Transform[] hinges = new Transform[0];
    [Tooltip("The unlit quad in the frame, between the wall face and the closed leaf.")]
    public Renderer voidRenderer;
    [Tooltip("Dust pushed out of the doorway when it opens. Optional.")]
    public ParticleSystem draft;
    [Tooltip("The overlay colour the walk-in fades to (black; the main doors: a cold grey-white).")]
    public Color passColour = Color.black;
    [Tooltip("How far a leaf swings toward the hall. Above 90 the free edge goes back into the wall.")]
    [Range(30f, 90f)] public float openAngle = 86f;
    [Tooltip("Set when this door's +Z points INTO the door instead of out to the hall (every hub door faces the hall with +Z).")]
    public bool intoIsForward;

    public bool IsComplete => hinges != null && hinges.Length > 0 && hinges.All(h => h != null) && voidRenderer != null;

    /// <summary>Unit, flat: from the hall into the door (= -transform.forward for every hub door).</summary>
    public Vector3 Into
    {
        get
        {
            Vector3 into = intoIsForward ? transform.forward : -transform.forward;
            into.y = 0f;
            return into.sqrMagnitude > 0.0001f ? into.normalized : Vector3.back;
        }
    }

    private bool _captured;
    private Quaternion[] _closed = new Quaternion[0];
    private float[] _signs = new float[0];
    private Bounds _closedBounds;
    private float _standOff = 1f;

    /// <summary>The middle of the opening at floor height of `y` (centre of the union of the closed leaves' renderer bounds).</summary>
    public Vector3 Doorway(float y)
    {
        EnsureCaptured();
        return new Vector3(_closedBounds.center.x, y, _closedBounds.center.z);
    }

    /// <summary>The union of the closed leaves' renderer bounds (world).</summary>
    public Bounds ClosedBounds
    {
        get { EnsureCaptured(); return _closedBounds; }
    }

    /// <summary>How far in front of the doorway the player must stand so no leaf sweeps through them: sqrt((w + 0.35)^2 - (w / 2)^2) for the widest leaf w, at least 1 m.</summary>
    public float StandOff
    {
        get { EnsureCaptured(); return _standOff; }
    }

    private void EnsureCaptured()
    {
        if (!_captured) CaptureClosed();
    }

    /// <summary>Remembers each hinge's closed localRotation, the closed leaf bounds and the swing signs. Call once, with the leaves closed, before the first swing.</summary>
    public void CaptureClosed()
    {
        if (hinges == null) hinges = new Transform[0];
        _closed = new Quaternion[hinges.Length];
        _signs = new float[hinges.Length];
        Vector3 into = Into;
        Vector3 right = Vector3.Cross(Vector3.up, into); // the door's width axis
        bool any = false;
        float widest = 0f;
        _closedBounds = new Bounds(transform.position, Vector3.zero);
        for (int i = 0; i < hinges.Length; i++)
        {
            if (hinges[i] == null) continue;
            _closed[i] = hinges[i].localRotation;
            _signs[i] = SwingSign(hinges[i], -into);
            Renderer[] renderers = hinges[i].GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Renderer r in renderers)
            {
                Bounds b = r.bounds;
                if (!any) { _closedBounds = b; any = true; }
                else _closedBounds.Encapsulate(b);
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z);
                    float along = Vector3.Dot(corner, right);
                    lo = Mathf.Min(lo, along);
                    hi = Mathf.Max(hi, along);
                }
            }
            widest = Mathf.Max(widest, hi - lo);
        }
        float w = Mathf.Max(widest, 0.5f);
        _standOff = Mathf.Max(1f, Mathf.Sqrt((w + 0.35f) * (w + 0.35f) - (w * 0.5f) * (w * 0.5f)));
        _captured = true;
    }

    /// <summary>0 = closed, 1 = `openAngle` toward the hall; the sign is per hinge (the leaves of a double door swing opposite ways in their own frames).</summary>
    public void SetOpen(float k)
    {
        EnsureCaptured();
        for (int i = 0; i < hinges.Length; i++)
        {
            if (hinges[i] == null) continue;
            hinges[i].localRotation = _closed[i] * Quaternion.Euler(0f, _signs[i] * openAngle * k, 0f);
        }
    }

    /// <summary>Back to the captured rotations.</summary>
    public void ResetClosed()
    {
        if (!_captured) return;
        for (int i = 0; i < hinges.Length; i++)
        {
            if (hinges[i] != null) hinges[i].localRotation = _closed[i];
        }
    }

    /// <summary>+1 or -1: the sign of the swing that carries the leaf's centre further along `toward`.</summary>
    public static float SwingSign(Transform hinge, Vector3 toward)
    {
        Renderer[] renderers = hinge.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return 1f;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        Vector3 offset = b.center - hinge.position;
        float plus = Vector3.Dot(Quaternion.AngleAxis(90f, hinge.up) * offset - offset, toward);
        float minus = Vector3.Dot(Quaternion.AngleAxis(-90f, hinge.up) * offset - offset, toward);
        return plus >= minus ? 1f : -1f;
    }
}
