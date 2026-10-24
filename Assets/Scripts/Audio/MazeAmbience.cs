using UnityEngine;

/// <summary>
/// The maze's environmental one-shots (10 Oct 2026): wind gusting through the corridors and, more rarely, something far off -
/// each from a random direction at a distance, muffled as if through walls. Clips come from GameAudioLibrary.mazeWind / mazeFar
/// (the Scary Sound FX pack); an empty slot is simply silent. Only during a maze run. Added by MazeGenerator.SetUpAtmosphere.
/// </summary>
public class MazeAmbience : MonoBehaviour
{
    [Header("Wind")]
    [SerializeField] private Vector2 windInterval = new Vector2(15f, 35f);
    [SerializeField] private Vector2 windDistance = new Vector2(10f, 18f);
    [SerializeField, Range(0f, 1f)] private float windVolume = 0.35f;
    [SerializeField] private float windLowPassHz = 2500f;

    [Header("Far events")]
    [SerializeField] private Vector2 farInterval = new Vector2(45f, 90f);
    [SerializeField] private Vector2 farDistance = new Vector2(18f, 28f);
    [SerializeField, Range(0f, 1f)] private float farVolume = 0.45f;
    [SerializeField] private float farLowPassHz = 1600f;

    [Header("F104 B11 - duck")]
    [Tooltip("Below this duck gain no wind or far event is spawned at all (the held breath is silence, not quiet)")]
    [SerializeField] private float silentBelowGain = 0.05f;

    private float _nextWind, _nextFar;
    private float _duckGain = 1f;
    private float _duckTarget = 1f;
    private float _duckRate;

    /// <summary>
    /// F104 B11: eases the one-shots' volume to `gain` over `seconds` (0 for a held breath, then back to 1). Anything already
    /// playing finishes at the volume it was spawned with; new ones are scaled by the gain, and none spawn while it is near zero.
    /// </summary>
    public void SetDuck(float gain, float seconds)
    {
        _duckTarget = Mathf.Clamp01(gain);
        _duckRate = seconds > 0.0001f ? Mathf.Abs(_duckTarget - _duckGain) / seconds : float.MaxValue;
    }

    private void Start()
    {
        _nextWind = Time.time + Random.Range(windInterval.x * 0.3f, windInterval.y * 0.5f);
        _nextFar = Time.time + Random.Range(farInterval.x * 0.5f, farInterval.y);
    }

    private void Update()
    {
        _duckGain = Mathf.MoveTowards(_duckGain, _duckTarget, _duckRate * Time.deltaTime);
        if (!GameFlow.IsRunActive || GameOutcome.IsOver) return;
        if (Time.time >= _nextWind)
        {
            _nextWind = Time.time + Random.Range(windInterval.x, windInterval.y);
            if (_duckGain >= silentBelowGain) PlayAround(GameAudio.Library.mazeWind, windDistance, windVolume * _duckGain, windLowPassHz);
        }
        if (Time.time >= _nextFar)
        {
            _nextFar = Time.time + Random.Range(farInterval.x, farInterval.y);
            if (_duckGain >= silentBelowGain) PlayAround(GameAudio.Library.mazeFar, farDistance, farVolume * _duckGain, farLowPassHz);
        }
    }

    private static void PlayAround(AudioSlot slot, Vector2 distance, float volume, float lowPassHz)
    {
        Camera cam = Camera.main;
        if (cam == null || !slot.Has) return;
        Vector2 dir = Random.insideUnitCircle.normalized;
        Vector3 at = cam.transform.position + new Vector3(dir.x, 0f, dir.y) * Random.Range(distance.x, distance.y);
        AudioSource src = GameAudio.SpawnAt(slot, at, volume);
        if (src == null) return;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 4f;
        src.maxDistance = distance.y + 8f;
        if (!src.TryGetComponent(out AudioLowPassFilter lp)) lp = src.gameObject.AddComponent<AudioLowPassFilter>();
        lp.cutoffFrequency = Mathf.Min(lp.cutoffFrequency, lowPassHz);
    }
}
