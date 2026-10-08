using UnityEngine;

/// <summary>
/// F92: a dying lamp in the asylum. The light shimmers, now and then dips, and rarely cuts out for a tenth of a second; the lamp
/// fixture's emission follows it and so does a small buzz (pitch and volume ride the light). Put on roughly one route light
/// in four by AsylumHubBuilder. It lives under the hub's building, which is only active while the player is in the asylum,
/// so it costs nothing in the mazes. Never alerts the hunter.
/// </summary>
public class HubLampFlicker : MonoBehaviour
{
    [SerializeField] private Light lamp;
    [Tooltip("The emissive fixture mesh next to the light (optional): its glow follows the flicker.")]
    [SerializeField] private Renderer fixture;
    [Tooltip("Optional buzz source; the clip is assigned at runtime.")]
    [SerializeField] private AudioSource buzz;
    [SerializeField, Range(0f, 0.4f)] private float shimmer = 0.07f;
    [SerializeField] private Vector2 secondsBetweenDips = new Vector2(4f, 11f);
    [SerializeField, Range(0f, 1f)] private float blackoutChance = 0.2f;
    [SerializeField] private float buzzVolume = 0.18f;

    /// <summary>F96: a multiplier another system (HubDecay's flicker bursts) lays over the lamp's own flicker; 1 = none. At ~0 the buzz is muted with the light.</summary>
    [System.NonSerialized] public float External = 1f;

    private float _baseIntensity;
    private Color _baseEmission = Color.black;
    private MaterialPropertyBlock _block;
    private float _nextDip, _dipEnd, _dipLevel;
    private float _seed;

    public void Configure(Light light, Renderer fixtureRenderer, AudioSource buzzSource)
    {
        lamp = light;
        fixture = fixtureRenderer;
        buzz = buzzSource;
    }

    private void OnEnable()
    {
        if (lamp == null) lamp = GetComponent<Light>();
        if (lamp == null) { enabled = false; return; }

        _baseIntensity = lamp.intensity;
        _seed = Mathf.Abs(transform.position.x * 3.17f + transform.position.z * 1.93f);
        _nextDip = Time.time + Random.Range(secondsBetweenDips.x, secondsBetweenDips.y);
        _dipEnd = 0f;

        if (fixture != null && fixture.sharedMaterial != null && fixture.sharedMaterial.HasProperty("_EmissionColor"))
        {
            _baseEmission = fixture.sharedMaterial.GetColor("_EmissionColor");
            _block = new MaterialPropertyBlock();
        }

        if (buzz != null)
        {
            buzz.clip = AsylumAmbience.BuzzClip;
            buzz.loop = true;
            buzz.spatialBlend = 1f;
            buzz.minDistance = 1.5f;
            buzz.maxDistance = 9f;
            buzz.volume = buzzVolume;
            buzz.Play();
        }
    }

    private void OnDisable()
    {
        if (lamp != null) lamp.intensity = _baseIntensity;
        External = 1f;
        if (buzz != null) { buzz.mute = false; buzz.Stop(); }
        if (fixture != null && _block != null) fixture.SetPropertyBlock(null);
    }

    private void Update()
    {
        float t = Time.time;
        float factor = 1f + (Mathf.PerlinNoise(t * 9f, _seed) - 0.5f) * 2f * shimmer;

        if (t >= _nextDip)
        {
            _dipEnd = t + (Random.value < blackoutChance ? 0.1f : Random.Range(0.06f, 0.28f));
            _dipLevel = _dipEnd - t <= 0.1001f && Random.value < 0.5f ? 0f : Random.Range(0.15f, 0.55f);
            _nextDip = _dipEnd + Random.Range(secondsBetweenDips.x, secondsBetweenDips.y);
        }
        if (t < _dipEnd) factor *= _dipLevel;
        factor *= External;

        lamp.intensity = _baseIntensity * factor;

        if (fixture != null && _block != null)
        {
            fixture.GetPropertyBlock(_block);
            _block.SetColor("_EmissionColor", _baseEmission * factor);
            fixture.SetPropertyBlock(_block);
        }

        if (buzz != null)
        {
            buzz.pitch = 0.88f + 0.24f * Mathf.Clamp01(factor);
            buzz.volume = buzzVolume * Mathf.Clamp01(factor);
            buzz.mute = factor < 0.05f; // F96: silent while the light is out
        }
    }
}
