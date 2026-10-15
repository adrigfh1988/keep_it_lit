using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F120: the survivable grab - the first hunter catch on a floor is a shove, not a death. HandleCaught asks <see cref="TryGrab"/> first (before a
/// held Second Wind, so a Second Wind is never spent while the free grab is available). The state (used flag, limp, the dropped-torch prop) lives
/// on <see cref="HunterGrab"/>; this partial owns the sequence because it needs IsEnding, the fades and the escape timer.
/// F121: the same sequence serves the hub breach's hunter (<see cref="TryGrabInHub"/>) - same shove, flash, dropped torch and limp, but the captor is a
/// HubStalker, nothing of the maze's run (escape timer, sound bed, the floor's own free grab) is touched, and the breach keeps its own used flag.
/// </summary>
public partial class GameOutcome
{
    private HunterGrab _grab;

    /// <summary>F120: wired by MazeGenerator.SetUpAtmosphere. May stay null (no grab: every catch kills as before).</summary>
    public void BindGrab(HunterGrab grab) => _grab = grab;

    /// <summary>
    /// F120: tries to turn a hunter catch into a grab. Returns false (nothing done) when it cannot - already used this floor, a locker bust, the
    /// switches off, F122's DisabledByRules, no kit template - and the caller falls through to Second Wind / death.
    /// </summary>
    public bool TryGrab()
    {
        if (IsOver || IsEnding || _grab == null || !_grab.CanGrab()) return false;
        StartCoroutine(GrabSequence(_follower.transform, _follower.EyeHeight, false, (hunterPos, back, landing) =>
        {
            // Captured -> Wander, stunned; the half-step back stays on the mesh (Release samples within 3 m).
            if (_follower.Release(hunterPos + back * _grab.HunterStepBack, _grab.StunSeconds))
            {
                // After the stun it searches around the lit torch - the one place the player has to go back to.
                _follower.ForceLastKnownPosition(landing, false);
            }
        }, null));
        return true;
    }

    /// <summary>
    /// F121: a grab for a catch in the asylum hub (the breach's HubStalker). The caller keeps the "once per attempt" flag; this only plays the
    /// sequence. `onRelease(captorPosition, directionAwayFromPlayer, torchLanding)` lets the captor step back and stun itself; `onDone` runs when the
    /// player has control again. False (nothing done) when a grab is impossible - the caller then treats the catch as a real one.
    /// </summary>
    public bool TryGrabInHub(Transform captor, float eyeHeight, System.Action<Vector3, Vector3, Vector3> onRelease, System.Action onDone)
    {
        if (IsOver || IsEnding || captor == null || _grab == null || !_grab.CanGrabInHub()) return false;
        StartCoroutine(GrabSequence(captor, eyeHeight, true, onRelease, onDone));
        return true;
    }

    /// <summary>F120: a same-scene transition (floor cleared) with the torch on the floor or the player limping: put it right. Safe with no grab bound.</summary>
    private void EndGrabForTransition()
    {
        if (_grab != null) _grab.EndForTransition();
    }

    private IEnumerator GrabSequence(Transform hunter, float eyeHeight, bool hubCaptor, System.Action<Vector3, Vector3, Vector3> onRelease, System.Action onDone)
    {
        IsEnding = true; // pause, dread, phantoms and Lose() all wait
        if (!hubCaptor) _grab.MarkUsed();
        if (_escape != null && !hubCaptor) _escape.Frozen = true; // not Stop(): Stop is permanent
        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;
        if (_audio != null) _audio.PlayCaptureSting();

        ThirdPersonController controller = _player != null ? _player.GetComponent<ThirdPersonController>() : null;

        // The camera eases onto the hunter's eyes.
        float t = 0f;
        while (t < 0.35f)
        {
            t += Time.deltaTime;
            if (controller != null && hunter != null) controller.LookAt(hunter.position + Vector3.up * eyeHeight, 1f - Mathf.Pow(0.001f, Time.deltaTime));
            yield return null;
        }

        // The shove: the torch flies out of the player's hand, the player is pushed back, the view jolts, the screen flashes red.
        Vector3 hunterPos = hunter != null ? hunter.position : _player.position - _player.forward;
        Vector3 playerPos = _player.position;
        Vector3 landing = _grab.Shove(hunterPos, !hubCaptor);
        Image flash = null;
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas != null)
        {
            flash = RuntimeUi.CreatePanel(canvas.transform, "GrabFlash", new Color(0.8f, 0.05f, 0.03f, 0.45f)).GetComponent<Image>();
            flash.raycastTarget = false;
            flash.transform.SetAsLastSibling();
        }

        float flashT = 0f;
        float jolt = 0f;
        float stepBackAt = 0.25f; // 0.35 + 0.25 = t 0.6 of the plan
        bool released = false;
        while (flashT < 0.45f)
        {
            flashT += Time.deltaTime;
            jolt += Time.deltaTime;
            if (flash != null) flash.color = new Color(0.8f, 0.05f, 0.03f, 0.45f * Mathf.Clamp01(1f - flashT / 0.4f));
            if (controller != null && jolt < 0.4f && hunter != null)
            {
                // Pitch kick: look at a point well below the hunter's eyes, easing back as the kick fades.
                float kick = 1f - jolt / 0.4f;
                controller.LookAt(hunter.position + Vector3.up * (eyeHeight - 0.9f * kick), 1f - Mathf.Pow(0.0005f, Time.deltaTime));
            }

            if (!released && flashT >= stepBackAt)
            {
                released = true;
                Vector3 back = hunterPos - playerPos;
                back.y = 0f;
                back = back.sqrMagnitude < 0.0001f ? (hunter != null ? -hunter.forward : Vector3.back) : back.normalized;
                onRelease?.Invoke(hunterPos, back, landing);
            }
            yield return null;
        }

        if (flash != null) Destroy(flash.gameObject);
        if (_stealth != null) _stealth.LastExposed = false;
        if (_audio != null && !hubCaptor) _audio.ResumeBed(); // the maze's drone must not start in the hub

        _grab.BeginLimp();
        if (_escape != null && !hubCaptor) _escape.Frozen = false;
        PlayerLock.Freeze(_player, false);
        if (_flashlight != null) _flashlight.InputEnabled = true; // the dropped state inside Flashlight still blocks the torch
        IsEnding = false;

        if (_hud != null)
        {
            if (hubCaptor)
            {
                _hud.ShowSubtitle(L10n.T("hub.breach.grab"), 3.5f);
            }
            else
            {
                _hud.ShowSubtitle(L10n.T("sub.grab.release"), 3f);
                _hud.QueueSubtitle(L10n.T("sub.grab.warning"), 2.5f);
            }
        }
        onDone?.Invoke();
    }
}
