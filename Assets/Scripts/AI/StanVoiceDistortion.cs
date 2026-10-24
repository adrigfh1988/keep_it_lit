using UnityEngine;

/// <summary>
/// Real-time DSP audio distortion for Stan's voice. Runs directly in the audio engine via OnAudioFilterRead,
/// ensuring genuine, audible harmonic saturation, overdrive clipping, and eerie lo-fi grit.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class StanVoiceDistortion : MonoBehaviour
{
    [Header("Distortion & Saturation")]
    [Tooltip("Input gain driving into analog tube-style Tanh saturation (1 = clean, 3.5 = crunchy, 6 = heavy fuzz, 10 = demonic crush).")]
    [Range(1f, 16f)] public float drive = 6.2f;

    [Tooltip("Sample rate downsampling / decimation factor (1 = pristine, 4 = crunchy 12 kHz grit, 6 = lo-fi intercom).")]
    [Range(1, 12)] public int downsample = 4;

    [Tooltip("Bitcrush / digital resolution reduction (0 = off, 0.5 = crunchy 6-bit quantization).")]
    [Range(0f, 1f)] public float bitcrush = 0.50f;

    [Tooltip("Sinister low-frequency ring-modulation undertone (Hz). 0 = off, 45-55 Hz gives an uncanny vibrating demonic undertone.")]
    [Range(0f, 120f)] public float subBuzzHz = 50f;

    [Tooltip("Wet / Dry blend (0 = clean original audio, 1 = 100 % distorted).")]
    [Range(0f, 1f)] public float wetMix = 0.95f;

    [Tooltip("Master output gain multiplier to tame perceived loudness after heavy saturation (0.50 - 0.70 recommended).")]
    [Range(0.1f, 1.5f)] public float outputGain = 0.65f;

    private float _buzzPhase;
    private int _sampleHoldCounter;
    private float[] _heldSample;
    private float _sampleRate = 48000f;

    private void Awake()
    {
        _sampleRate = AudioSettings.outputSampleRate;
        if (_sampleRate < 8000f) _sampleRate = 48000f;
        _heldSample = new float[16];
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (wetMix <= 0.001f) return;

        if (_heldSample == null || _heldSample.Length < channels)
        {
            _heldSample = new float[channels > 8 ? channels : 8];
        }

        float sampleRate = _sampleRate;
        float phaseInc = (subBuzzHz * Mathf.PI * 2f) / sampleRate;

        // Steps for bitcrush: 0 = 512 steps, 1 = 12 steps
        float steps = Mathf.Lerp(512f, 12f, bitcrush);
        int decimate = Mathf.Clamp(downsample, 1, 12);

        for (int i = 0; i < data.Length; i += channels)
        {
            float buzz = subBuzzHz > 1f ? (float)System.Math.Sin(_buzzPhase) * 0.25f : 0f;
            _buzzPhase += phaseInc;
            if (_buzzPhase > Mathf.PI * 2f) _buzzPhase -= Mathf.PI * 2f;

            bool isNewSample = (_sampleHoldCounter % decimate == 0);
            _sampleHoldCounter++;

            for (int c = 0; c < channels; c++)
            {
                float dry = data[i + c];

                if (isNewSample)
                {
                    // Ring modulation undertone
                    float x = dry * (1f + buzz);

                    // Analog tube-style Tanh saturation with asymmetric bias (even harmonics for demonic growl)
                    float biased = x + 0.08f;
                    float driven = biased * drive;
                    float sat = (float)System.Math.Tanh(driven) - 0.07f;

                    // Bitcrush resolution quantization
                    if (bitcrush > 0.01f)
                    {
                        sat = (float)System.Math.Round(sat * steps) / steps;
                    }

                    _heldSample[c] = sat;
                }

                float processed = _heldSample[c];
                data[i + c] = Mathf.Lerp(dry, processed * outputGain, wetMix);
            }
        }
    }
}
