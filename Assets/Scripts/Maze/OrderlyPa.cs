using UnityEngine;

/// <summary>
/// The hunter ("ORDERLY 01") speaks hospital routine through the cracked speaker in its chest: every so often while it patrols
/// within earshot it says one of <see cref="GameAudioLibrary.orderlyPatrol"/>'s lines from a 3D source on its body, through a
/// PA-speaker chain (band-limited, a little distortion, a short slap echo) so a stock text-to-speech voice reads as an old
/// loudspeaker. Never while chasing or capturing; silent on an empty slot. Added by MazeGenerator.SetUpAtmosphere.
/// </summary>
public class OrderlyPa : MonoBehaviour
{
    [Tooltip("Seconds between patrol lines (random in range).")]
    [SerializeField] private Vector2 interval = new Vector2(25f, 45f);
    [Tooltip("Only speaks when the player is within this many metres (straight line), and the sound fades out at this distance.")]
    [SerializeField] private float range = 30f;
    [SerializeField, Range(0f, 1f)] private float volume = 1f;
    [Tooltip("Playback pitch (also slows the line). Below 1 takes the brightness out of a stock text-to-speech voice.")]
    [SerializeField, Range(0.5f, 1.2f)] private float pitch = 0.82f;

    [Header("PA speaker chain")]
    [SerializeField] private float highPassHz = 450f;
    [SerializeField] private float lowPassHz = 3200f;
    [SerializeField, Range(0f, 1f)] private float distortion = 0.35f;
    [SerializeField] private float echoDelayMs = 70f;
    [SerializeField, Range(0f, 1f)] private float echoWet = 0.25f;

    private AIFollower _follower;
    private AudioSource _source;
    private float _next;

    private void Start()
    {
        _follower = GetComponent<AIFollower>();
        GameObject speaker = new GameObject("OrderlyPaSpeaker");
        speaker.transform.SetParent(transform, false);
        speaker.transform.localPosition = new Vector3(0f, 1.3f, 0.1f); // the chest panel
        _source = speaker.AddComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 1f;
        _source.rolloffMode = AudioRolloffMode.Linear;
        _source.minDistance = 2f;
        _source.maxDistance = range;
        _source.dopplerLevel = 0f;

        AudioHighPassFilter hp = speaker.AddComponent<AudioHighPassFilter>();
        hp.cutoffFrequency = highPassHz;
        AudioLowPassFilter lp = speaker.AddComponent<AudioLowPassFilter>();
        lp.cutoffFrequency = lowPassHz;
        AudioDistortionFilter dist = speaker.AddComponent<AudioDistortionFilter>();
        dist.distortionLevel = distortion;
        AudioEchoFilter echo = speaker.AddComponent<AudioEchoFilter>();
        echo.delay = echoDelayMs;
        echo.decayRatio = 0.3f;
        echo.wetMix = echoWet;
        echo.dryMix = 1f;
        // a metallic, doubled machine voice
        AudioChorusFilter chorus = speaker.AddComponent<AudioChorusFilter>();
        chorus.dryMix = 0.6f;
        chorus.wetMix1 = 0.5f;
        chorus.wetMix2 = 0.5f;
        chorus.wetMix3 = 0f;
        chorus.delay = 12f;
        chorus.rate = 0.4f;
        chorus.depth = 0.15f;

        _next = Time.time + Random.Range(interval.x * 0.5f, interval.y * 0.5f);
    }

    private void Update()
    {
        if (_source == null || _source.isPlaying || Time.time < _next) return;
        _next = Time.time + Random.Range(interval.x, interval.y);
        if (!GameFlow.IsRunActive || _follower == null || _follower.IsChasing || _follower.IsCaptured) return;
        Camera cam = Camera.main;
        if (cam == null || (cam.transform.position - transform.position).sqrMagnitude > range * range) return;
        PlayNow();
    }

    /// <summary>Testing (Inspector context menu, in Play): one line from 3 m in front of the player's camera, through the same chain,
    /// so it can be heard without finding the hunter. The speaker goes back to the chest when the line ends.</summary>
    [ContextMenu("Test: say a line next to the player")]
    private void TestNearPlayer()
    {
        Camera cam = Camera.main;
        if (_source == null || cam == null) return;
        _source.transform.position = cam.transform.position + cam.transform.forward * 3f;
        if (PlayNow()) Invoke(nameof(ReturnSpeaker), 3f); else ReturnSpeaker();
    }

    private void ReturnSpeaker()
    {
        if (_source != null) _source.transform.localPosition = new Vector3(0f, 1.3f, 0.1f);
    }

    /// <summary>Says one patrol line now (also for testing from an eval). False when the slot is empty.</summary>
    public bool PlayNow()
    {
        if (_source == null) return false;
        // wordless moans while there are any; the spoken PA lines otherwise
        AudioSlot slot = GameAudio.Library.hunterMoan.Has ? GameAudio.Library.hunterMoan : GameAudio.Library.orderlyPatrol;
        AudioClip clip = slot.Pick();
        if (clip == null) return false;
        _source.pitch = pitch * slot.PickPitch();
        _source.PlayOneShot(clip, volume * slot.volume);
        return true;
    }
}
