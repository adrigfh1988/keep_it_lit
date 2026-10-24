using UnityEngine;

/// <summary>
/// S2: scales one fog particle system by the Fog setting (Off clears and stops it, Low keeps half the particles, Normal all of them). Remembers the
/// system's own max particles / emission rate the first time it runs, so the setting can go up again. Added to each maze fog chunk by
/// MazeGenerator.BuildFog and to every hub fog emitter by AsylumHub.SetContentActive; applies on enable and on every GameSettings.Changed.
/// </summary>
[DisallowMultipleComponent]
public class FogScale : MonoBehaviour
{
    private ParticleSystem _ps;
    private int _baseMax;
    private float _baseRate;
    private bool _captured;

    /// <summary>Particles per second at the base rate scaled by the current setting (test read-back).</summary>
    public float CurrentRate => _ps != null ? _ps.emission.rateOverTime.constant : 0f;

    private void Capture()
    {
        if (_captured) return;
        _ps = GetComponent<ParticleSystem>();
        if (_ps == null) return;
        _baseMax = _ps.main.maxParticles;
        _baseRate = _ps.emission.rateOverTime.constant;
        _captured = true;
    }

    private void OnEnable()
    {
        GameSettings.Changed += Apply;
        Apply();
    }

    private void OnDisable() => GameSettings.Changed -= Apply;

    public void Apply()
    {
        Capture();
        if (!_captured) return;
        float k = GameSettings.FogFactor;
        ParticleSystem.MainModule main = _ps.main;
        ParticleSystem.EmissionModule emission = _ps.emission;
        if (k <= 0f)
        {
            emission.enabled = false;
            _ps.Clear(true);
            return;
        }
        emission.enabled = true;
        emission.rateOverTime = _baseRate * k;
        main.maxParticles = Mathf.Max(2, Mathf.RoundToInt(_baseMax * k));
    }
}
