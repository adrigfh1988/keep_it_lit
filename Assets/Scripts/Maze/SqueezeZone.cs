using System.Collections;
using StarterAssets;
using UnityEngine;

/// <summary>
/// F105 C3: the tight stretch of corridor. While the player stands inside its box they walk slower, cannot sprint, hear their own
/// breathing and see less far with the torch; every effect is undone on the way out (or if the zone is switched off while they are
/// in it). Built by MazeGenerator.BuildSqueeze, bound once SetUpAtmosphere has the player's components. It is a box tested against the
/// player's position every frame, not a trigger collider: nothing here touches physics or the bake, and a teleport into or out of the
/// squeeze (a locker, the hatch) is seen like any other move.
/// </summary>
public class SqueezeZone : MonoBehaviour
{
    private const float BreathGapMin = 0.8f;
    private const float BreathGapMax = 2.5f;

    private Bounds _bounds;
    private Transform _player;
    private ThirdPersonController _controller;
    private PlayerStamina _stamina;
    private Flashlight _flashlight;
    private float _speedScale = 1f;
    private float _torchScale = 1f;
    private float _breathVolume;
    private bool _inside;
    private Coroutine _breathing;
    private AudioSource _breathSource;

    /// <summary>The box (world space) inside which the squeeze applies.</summary>
    public void Define(Bounds bounds)
    {
        _bounds = bounds;
    }

    /// <summary>Hands over the player's components and the effect strengths. Any may be null; that effect is then skipped.</summary>
    public void Bind(Transform player, ThirdPersonController controller, PlayerStamina stamina, Flashlight flashlight,
        float speedScale, float torchScale, float breathVolume)
    {
        _player = player;
        _controller = controller;
        _stamina = stamina;
        _flashlight = flashlight;
        _speedScale = speedScale;
        _torchScale = torchScale;
        _breathVolume = breathVolume;
    }

    private void Update()
    {
        if (_player == null) return;

        bool inside = _bounds.Contains(_player.position);
        if (inside == _inside) return;

        if (inside) Enter();
        else Leave();
    }

    private void OnDisable()
    {
        if (_inside) Leave();
    }

    private void Enter()
    {
        _inside = true;
        // Multiplied and divided back, not set and reset: another system may own the multiplier at the same time.
        if (_controller != null) _controller.SpeedMultiplier *= _speedScale;
        if (_stamina != null) _stamina.SqueezeBlocked = true;
        if (_flashlight != null) _flashlight.SetRangeScale(_torchScale);
        if (GameAudio.Library.lockerBreath.Has) _breathing = StartCoroutine(BreathRoutine());
    }

    private void Leave()
    {
        _inside = false;
        if (_controller != null) _controller.SpeedMultiplier /= _speedScale;
        if (_stamina != null) _stamina.SqueezeBlocked = false;
        if (_flashlight != null) _flashlight.SetRangeScale(1f);
        if (_breathing != null) StopCoroutine(_breathing);
        _breathing = null;
        if (_breathSource != null) Destroy(_breathSource.gameObject);
        _breathSource = null;
    }

    /// <summary>The locker's breathing (a recorded loop-with-gaps), played 2D at low volume for as long as the player is in the squeeze.</summary>
    private IEnumerator BreathRoutine()
    {
        AudioSlot slot = GameAudio.Library.lockerBreath;
        GameObject holder = new GameObject("SqueezeBreath");
        holder.transform.SetParent(transform, false);
        _breathSource = holder.AddComponent<AudioSource>();
        _breathSource.playOnAwake = false;
        _breathSource.spatialBlend = 0f;

        while (_inside)
        {
            AudioClip clip = slot.Pick();
            if (clip == null) yield break;

            _breathSource.clip = clip;
            _breathSource.volume = Mathf.Clamp01(_breathVolume * slot.volume);
            _breathSource.pitch = slot.PickPitch();
            _breathSource.Play();
            yield return new WaitForSeconds(clip.length / _breathSource.pitch + Random.Range(BreathGapMin, BreathGapMax));
        }
    }
}
