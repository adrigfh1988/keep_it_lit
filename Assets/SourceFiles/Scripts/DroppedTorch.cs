using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F120: the player's torch lying on the floor after the hunter's grab. Cloned from <c>InteractableKit.droppedTorch</c> by HunterGrab and
/// thrown with a scripted arc (no Rigidbody): the landing point is found on the NavMesh, so it can never land inside or beyond a wall and is
/// always reachable. While <see cref="Flashlight.DroppedLit"/> its child spot light shines (the same wide beam the head torch had), it is
/// registered in <see cref="LightPool"/> (Stan freezes in its pool; standing next to it lights the player), and pressing E picks it up.
/// </summary>
public class DroppedTorch : MonoBehaviour, IInteractable
{
    [Tooltip("The spot light child (disabled in the template). Flashlight.CopyBeamTo copies the live beam onto it at throw time.")]
    [SerializeField] private Light beam;
    [Tooltip("The lens: its emission follows the light.")]
    [SerializeField] private Renderer lens;

    private const float FlightSeconds = 0.55f;
    private const float SlideSeconds = 0.8f;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private Flashlight _torch;
    private HunterGrab _owner;
    private bool _taken;
    private bool _registered;
    private MaterialPropertyBlock _block;
    private Color _lensEmission = Color.white;
    private float _baseIntensity;
    private Vector3 _landing;

    /// <summary>Where the torch comes to rest (valid after Throw).</summary>
    public Vector3 Landing => _landing;

    /// <summary>
    /// Starts the throw from `from` along the flat direction `dir` for up to `distance` metres. The landing point is returned at once (the flight
    /// itself takes 0.55 s plus a 0.8 s slide).
    /// </summary>
    public Vector3 Throw(Vector3 from, Vector3 dir, float distance, Flashlight torch, HunterGrab owner)
    {
        _torch = torch;
        _owner = owner;
        dir.y = 0f;
        dir = dir.sqrMagnitude < 0.0001f ? Vector3.forward : dir.normalized;

        // Land on the navmesh: raycast along the mesh from the start so a wall shortens the throw.
        Vector3 start = from;
        if (NavMesh.SamplePosition(from, out NavMeshHit startHit, 1.5f, NavMesh.AllAreas)) start = startHit.position;
        Vector3 end = start + dir * distance;
        if (NavMesh.Raycast(start, end, out NavMeshHit wall, NavMesh.AllAreas)) end = wall.position - dir * 0.3f;
        if (NavMesh.SamplePosition(end, out NavMeshHit endHit, 1.5f, NavMesh.AllAreas)) end = endHit.position;
        else end = start;
        _landing = new Vector3(end.x, end.y + 0.04f, end.z);

        if (beam != null)
        {
            torch.CopyBeamTo(beam);
            _baseIntensity = beam.intensity;
            beam.enabled = torch.DroppedLit;
        }

        _block = new MaterialPropertyBlock();
        if (lens != null)
        {
            lens.GetPropertyBlock(_block);
            Material m = lens.sharedMaterial;
            if (m != null && m.HasProperty(EmissionColor)) _lensEmission = m.GetColor(EmissionColor);
            if (_lensEmission.maxColorComponent < 0.01f) _lensEmission = new Color(2.2f, 2.0f, 1.6f);
        }

        LightPool.Register(transform, 2.5f, () => _torch != null && _torch.DroppedLit ? 1f : 0f);
        _registered = true;

        transform.position = from;
        StartCoroutine(Fly(from, _landing));
        return _landing;
    }

    private IEnumerator Fly(Vector3 from, Vector3 to)
    {
        float spinDirection = Random.value < 0.5f ? -1f : 1f;
        float finalYaw = Random.Range(0f, 360f);
        Quaternion tumble = Random.rotation;
        float t = 0f;
        while (t < FlightSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / FlightSeconds);
            Vector3 p = Vector3.Lerp(from, to, k);
            p.y = Mathf.Lerp(from.y, to.y, k) + Mathf.Sin(k * Mathf.PI) * 0.6f;
            transform.position = p;
            transform.rotation = Quaternion.Slerp(tumble, Quaternion.Euler(0f, finalYaw, 0f), k) * Quaternion.Euler(0f, spinDirection * 900f * t, 0f);
            yield return null;
        }

        transform.position = to;
        float spin = 900f * spinDirection;
        float yaw = finalYaw;
        t = 0f;
        while (t < SlideSeconds)
        {
            t += Time.deltaTime;
            spin = Mathf.Lerp(900f * spinDirection, 0f, t / SlideSeconds);
            yaw += spin * Time.deltaTime;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f); // lying flat: local +Z (the beam axis) is horizontal
            yield return null;
        }
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    private void Update()
    {
        if (_torch == null) return;

        bool lit = _torch.DroppedLit;
        if (beam != null)
        {
            beam.enabled = lit;
            if (lit)
            {
                // The head beam's low-battery dimming and wobble (Flashlight.Update), without its focus blend.
                float charge = _torch.Charge;
                float low = Mathf.Clamp01(charge / 0.2f);
                float dim = Mathf.Lerp(0.55f, 1f, low);
                float wobble = charge < 0.2f && !FlashSafety.On ? 1f - 0.4f * (1f - low) * Mathf.PerlinNoise(Time.time * 7f, 2f) : 1f;
                beam.intensity = _baseIntensity * dim * wobble;
            }
        }

        if (lens != null)
        {
            lens.GetPropertyBlock(_block);
            _block.SetColor(EmissionColor, lit ? _lensEmission : Color.black);
            lens.SetPropertyBlock(_block);
        }
    }

    private void OnDestroy()
    {
        if (_registered) LightPool.Unregister(transform);
    }

    // ---------------------------------------------------------------- IInteractable

    string IInteractable.Prompt => L10n.T("hud.prompt.pick_up_torch");
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => !_taken && !GameOutcome.IsOver;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_taken) return;
        _taken = true;
        if (_owner != null) _owner.PickUpTorch(this);
        else Destroy(gameObject);
    }

    void IInteractable.OnFocus(bool focused) { }
}
