using UnityEngine;

/// <summary>
/// F103 A4: lets the torch's beam lag behind the view, so the light swings when the player turns and the
/// shadows it casts swing with it. Lives on Flashlight's "TorchBeam" child (a child of the camera).
///
/// Execution order 100 runs after ThirdPersonController.LateUpdate (order 0), which rotates the camera
/// root, so the target rotation read here is already this frame's. Scaled Time.deltaTime on purpose:
/// pause and the title screen freeze the sway for free. The lag scales with GameSettings.CameraShake, the
/// existing comfort setting, so camera shake 0 means no lag at all.
/// </summary>
[DefaultExecutionOrder(100)]
public class TorchSway : MonoBehaviour
{
    /// <summary>A bigger gap than this is a teleport (locker, cutscene, respawn), not a turn: snap instead of swinging across the screen.</summary>
    private const float SnapDegrees = 60f;
    /// <summary>Below this lag (seconds) the sway is off and the beam is rigidly mounted.</summary>
    private const float MinLagSeconds = 0.005f;

    private Quaternion _rest = Quaternion.identity;
    private Quaternion _smoothed = Quaternion.identity;
    private float _lagSeconds;
    private float _maxDegrees;

    /// <summary>Sets the rest rotation (the beam aimed at the crosshair, in parent space), the lag and the largest angle the beam may trail the view by.</summary>
    public void Configure(Quaternion restLocalRotation, float lagSeconds, float maxDegrees)
    {
        _rest = restLocalRotation;
        _lagSeconds = lagSeconds;
        _maxDegrees = maxDegrees;
        _smoothed = transform.rotation;
    }

    private void OnEnable()
    {
        _smoothed = transform.rotation;
    }

    private void OnDisable()
    {
        // Disabled by Flashlight.ApplyTerror (torchSway off): the beam goes back to its rigid mount.
        transform.localRotation = _rest;
    }

    private void LateUpdate()
    {
        Quaternion target = transform.parent.rotation * _rest;
        float lag = _lagSeconds * GameSettings.CameraShake;

        if (lag < MinLagSeconds || Time.deltaTime <= 0f || Quaternion.Angle(_smoothed, target) > SnapDegrees)
        {
            _smoothed = target;
        }
        else
        {
            _smoothed = Quaternion.Slerp(_smoothed, target, 1f - Mathf.Exp(-Time.deltaTime / lag));
            if (Quaternion.Angle(_smoothed, target) > _maxDegrees)
            {
                _smoothed = Quaternion.RotateTowards(target, _smoothed, _maxDegrees);
            }
        }

        transform.rotation = _smoothed;
    }
}
