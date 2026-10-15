using UnityEngine;

/// <summary>
/// F123 floor 5 (the Director): the HOUSE RULES are printed and taped up in the maze (MazeGenerator.BuildDirectorNotices) - patients walk, patients do
/// not hide, patients do not throw things, the orderly will assist - and a ceiling PA (2D, a two-tone chime, subtitle only, no voice) enforces them.
/// The first breach of each rule is a polite warning; after that the PA reports the patient and the orderly is sent to where they are.
/// The three rules are the habits <see cref="HunterMemory"/> learns - the Director wrote the rules the orderly learned. Distinct from
/// <see cref="OrderlyPa"/>, the hunter's own chest speaker (3D audio, no text, unchanged). Ticked by <see cref="WingNightmareDirector"/>.
/// </summary>
[System.Serializable]
public class HouseRules
{
    public enum Rule { Run = 0, Hide = 1, Throw = 2 }

    [Tooltip("Run seconds before the welcome line, and before any rule is enforced")]
    [SerializeField, Min(0f)] private float welcomeAt = 10f;
    [Tooltip("Minimum seconds between two PA announcements")]
    [SerializeField, Min(0f)] private float paCooldown = 20f;
    [Tooltip("Continuous sprint seconds that break rule 1")]
    [SerializeField, Min(0.1f)] private float ruleRunSeconds = 2.5f;
    [Tooltip("Seconds hidden that break rule 2 (once per hide)")]
    [SerializeField, Min(0.1f)] private float ruleHideSeconds = 15f;
    [Tooltip("Minimum seconds between two announcements about the same rule (the second and later ones send the orderly)")]
    [SerializeField, Min(0f)] private float reportCooldown = 45f;
    [SerializeField, Min(0.5f)] private float subtitleSeconds = 4f;
    [SerializeField, Range(0f, 1f)] private float chimeVolume = 0.6f;

    private Transform _player;
    private AIFollower _hunter;
    private PlayerStealthState _stealth;
    private PlayerStamina _stamina;
    private ThrowController _throws;
    private PlayerHud _hud;
    private TerrorSettings _terror = new TerrorSettings();
    private MonoBehaviour _host;
    private AudioSource _source;
    private AudioClip _chime;

    private readonly int[] _breaches = new int[3];
    private readonly float[] _lastAbout = { -9999f, -9999f, -9999f };
    private float _lastPa = -9999f;
    private float _runFor;
    private bool _hideHandled;
    private bool _welcomed;

    public int Breaches(Rule rule) => _breaches[(int)rule];

    public void Bind(MonoBehaviour host, Transform player, AIFollower hunter, PlayerStealthState stealth, PlayerStamina stamina, ThrowController throws, PlayerHud hud, TerrorSettings terror)
    {
        Unbind();
        _host = host;
        _player = player;
        _hunter = hunter;
        _stealth = stealth;
        _stamina = stamina;
        _throws = throws;
        _hud = hud;
        _terror = terror ?? new TerrorSettings();
        if (_throws != null) _throws.Thrown += HandleThrown;

        if (_source == null)
        {
            GameObject go = new GameObject("PaChime");
            go.transform.SetParent(host.transform, false);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f; // the Director's voice is in the ceiling, not in a place
            GameSettings.RouteEffect(_source);
        }
    }

    /// <summary>Drops the ThrowController subscription (the director's OnDestroy).</summary>
    public void Unbind()
    {
        if (_throws != null) _throws.Thrown -= HandleThrown;
        _throws = null;
    }

    private float RunTime => Time.time - GameFlow.RunStartTime;

    /// <summary>One director tick while the run is live.</summary>
    public void Tick(float dt)
    {
        if (_player == null || !_terror.houseRules) return;

        if (!_welcomed && RunTime >= welcomeAt)
        {
            _welcomed = true;
            Announce(L10n.T("sub.nightmare.director.welcome"));
            _terror.Log("House rules: welcome", _host);
        }

        if (RunTime < welcomeAt || (_hunter != null && (_hunter.IsChasing || _hunter.IsCaptured))) return;

        // Rule 1: walking pace.
        if (_stamina != null && _stamina.IsSprinting) _runFor += dt; else _runFor = 0f;
        if (_runFor >= ruleRunSeconds)
        {
            _runFor = 0f;
            Breach(Rule.Run, false);
        }

        // Rule 2: patients do not hide (once per hide).
        if (_stealth != null && _stealth.Hidden)
        {
            if (!_hideHandled && Time.time - _stealth.HiddenSince >= ruleHideSeconds)
            {
                _hideHandled = true;
                Breach(Rule.Hide, false);
            }
        }
        else
        {
            _hideHandled = false;
        }
    }

    private void HandleThrown()
    {
        if (_player == null || !_terror.houseRules || !GameFlow.IsRunActive || GameOutcome.IsOver) return;
        if (RunTime < welcomeAt || (_hunter != null && (_hunter.IsChasing || _hunter.IsCaptured))) return;
        Breach(Rule.Throw, false);
    }

    /// <summary>Test hook: announces a breach of `rule` now, ignoring both cooldowns and the grace period (still a warning the first time, a report after).</summary>
    public void DebugBreach(Rule rule) => Breach(rule, true);

    private void Breach(Rule rule, bool force)
    {
        int i = (int)rule;
        if (!force && (Time.time - _lastPa < paCooldown || Time.time - _lastAbout[i] < reportCooldown)) return;

        string slug = rule.ToString().ToLowerInvariant();
        bool report = _breaches[i] > 0;
        _breaches[i]++;
        _lastPa = Time.time;
        _lastAbout[i] = Time.time;

        Announce(L10n.T((report ? "sub.nightmare.director.report." : "sub.nightmare.director.warn.") + slug, ("mark", WingNightmares.PatientMark())));

        bool sent = false;
        if (report && _hunter != null && _player != null && !_hunter.IsBackingOff && !_hunter.IsCaptured)
        {
            _hunter.ForceLastKnownPosition(_player.position, false); // search the spot without the A7 stop-and-rush; never breaks a B1 backoff wave
            sent = true;
        }

        _terror.Log($"House rules: {slug} {(report ? "REPORTED" : "warned")}{(sent ? ", the orderly is sent" : "")} (breach {_breaches[i]})", _host);
    }

    private void Announce(string text)
    {
        PlayChime();
        _hud?.ShowSubtitle(text, subtitleSeconds);
    }

    private void PlayChime()
    {
        if (_source == null) return;
        AudioSlot slot = GameAudio.Library.paChime;
        AudioClip clip = slot.Pick();
        if (clip == null)
        {
            if (_chime == null) _chime = BuildChime();
            clip = _chime;
        }
        _source.clip = clip;
        _source.volume = Mathf.Clamp01(chimeVolume * (slot.Has ? slot.volume : 1f));
        _source.pitch = 1f;
        _source.Play();
    }

    /// <summary>The fallback chime: two soft sines, a falling third (about 660 Hz then 520 Hz), 0.35 s each with a soft attack and a long release.</summary>
    private static AudioClip BuildChime()
    {
        const int rate = 22050;
        float[] tones = { 660f, 520f };
        const float each = 0.35f;
        int per = Mathf.RoundToInt(rate * each);
        float[] data = new float[per * 2];
        for (int t = 0; t < tones.Length; t++)
        {
            for (int s = 0; s < per; s++)
            {
                float time = s / (float)rate;
                float attack = Mathf.Clamp01(time / 0.03f);
                float release = Mathf.Pow(1f - s / (float)per, 1.5f);
                data[t * per + s] = Mathf.Sin(2f * Mathf.PI * tones[t] * time) * attack * release * 0.45f;
            }
        }
        AudioClip clip = AudioClip.Create("PaChime", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
