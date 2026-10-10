using UnityEngine;

/// <summary>
/// Timmy, the robot behind the shop counter. He idles on the Starter Assets idle, waves when the player
/// arrives or walks up to the counter, and talks (the Mixamo Talking clip) while the shop or contract menu
/// is open. ShopRoom calls <see cref="Greet"/> and <see cref="SetTalking"/>; everything else is the
/// controller made by the shop builder (ShopRoomBuilder.Timmy.cs; states Idle / Talk / Wave, crossfaded by name;
/// Wave returns to Idle on its own). Without a Wave state a greeting is a short burst of talking instead.
/// </summary>
public class ShopkeeperAnimator : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [Tooltip("Seconds before he waves again (walking in and out of the counter zone should not make him wave every time).")]
    [SerializeField] private float waveCooldown = 8f;
    [Tooltip("Without a Wave clip, a greeting is this many seconds of talking.")]
    [SerializeField] private float greetTalkSeconds = 2f;
    [SerializeField] private float crossFade = 0.25f;

    private static readonly int IdleState = Animator.StringToHash("Idle");
    private static readonly int TalkState = Animator.StringToHash("Talk");
    private static readonly int WaveState = Animator.StringToHash("Wave");

    private bool _talking;
    private float _nextWave;
    private float _greetTalkUntil;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator == null) return;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // The Starter Assets clips fire OnFootstep/OnLand at a ThirdPersonController this body no longer has.
        animator.fireEvents = false;
    }

    /// <summary>Waves at the player (at most once per waveCooldown). Ignored while talking.</summary>
    public void Greet()
    {
        if (animator == null || _talking || Time.time < _nextWave) return;
        _nextWave = Time.time + waveCooldown;

        if (animator.HasState(0, WaveState)) animator.CrossFadeInFixedTime(WaveState, crossFade);
        else
        {
            _greetTalkUntil = Time.time + greetTalkSeconds;
            animator.CrossFadeInFixedTime(TalkState, crossFade);
        }
    }

    /// <summary>Talks while true (the shop menu is open), back to idle when false. Safe to call every frame.</summary>
    public void SetTalking(bool talking)
    {
        if (animator == null || talking == _talking) return;
        _talking = talking;
        _greetTalkUntil = 0f;
        animator.CrossFadeInFixedTime(talking ? TalkState : IdleState, crossFade);
    }

    private void Update()
    {
        if (animator == null || _talking || _greetTalkUntil <= 0f || Time.time < _greetTalkUntil) return;
        _greetTalkUntil = 0f;
        animator.CrossFadeInFixedTime(IdleState, crossFade);
    }
}
