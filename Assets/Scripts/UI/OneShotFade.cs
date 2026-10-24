using UnityEngine;

/// <summary>
/// F97: lives on the throw-away GameObject <see cref="GameAudio"/> makes for a one-shot. Fades the voice out over the last
/// stretch when the slot cuts a long file short (<see cref="AudioSlot.maxSeconds"/>) and destroys the object when it is done.
/// </summary>
public class OneShotFade : MonoBehaviour
{
    private AudioSource _source;
    private float _baseVolume, _age, _duration, _fade;

    /// <summary>`duration` = real seconds the sound should last; `fade` = seconds of fade-out at its end (0 = none).</summary>
    public void Begin(AudioSource source, float duration, float fade)
    {
        _source = source;
        _baseVolume = source.volume;
        _duration = Mathf.Max(0.05f, duration);
        _fade = Mathf.Clamp(fade, 0f, _duration);
    }

    private void Update()
    {
        _age += Time.unscaledDeltaTime;
        if (_age >= _duration + 0.05f) { Destroy(gameObject); return; }
        if (_fade > 0f && _source != null && _age > _duration - _fade)
            _source.volume = _baseVolume * Mathf.Clamp01((_duration - _age) / _fade);
    }
}
