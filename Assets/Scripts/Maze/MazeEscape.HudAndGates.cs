using TMPro;
using UnityEngine;

public partial class MazeEscape
{
    private void UpdatePing(float dt)
    {
        if (_player == null) return;

        _pingTimer -= dt;
        if (_pingTimer > 0f) return;

        Vector3 target = WayfindingTarget();
        float distance = Vector3.Distance(_player.position, target);
        _pingTimer = pingInterval * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(distance / 20f));
        _chevronPulse = 1f;

        if (_ping != null && _pingClip != null)
        {
            // F77: the ping comes from wherever the chevron points - the hatch while unlocked, the
            // nearest missing key while locked.
            if (_pingAnchor != null) _pingAnchor.position = target;
            _ping.PlayOneShot(_pingClip, pingVolume);
        }
    }

    private void UpdateChevron(float dt)
    {
        if (_chevronText == null) return;

        _chevronPulse = Mathf.Max(0f, _chevronPulse - 3f * dt);

        Camera cam = Camera.main;
        if (cam != null && _player != null)
        {
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;

            Vector3 toHatchFlat = WayfindingTarget() - _player.position;
            toHatchFlat.y = 0f;

            if (forward.sqrMagnitude > 0.0001f && toHatchFlat.sqrMagnitude > 0.0001f)
            {
                float bearing = Vector3.SignedAngle(forward, toHatchFlat, Vector3.up);
                float rad = bearing * Mathf.Deg2Rad;

                RectTransform rect = _chevronText.rectTransform;
                rect.anchoredPosition = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad)) * chevronRadius;
                rect.localRotation = Quaternion.Euler(0f, 0f, -bearing);
            }
        }

        Color c = _locked ? LockedColor : hatchColor;
        c.a = 0.35f + 0.65f * _chevronPulse;
        // F104 B4: the last seconds are by ear (the ping and the alarm stay). Not while padlocked: then it points at a missing key.
        if (_terror.escapeHunt && !_locked && _timeLeft <= chevronHideLastSeconds) c.a = 0f;
        _chevronText.color = c;
    }

    /// <summary>F77: while locked, the ping and chevron point at the nearest missing key instead of the hatch or gate - fairness while the player cannot finish yet.</summary>
    private Vector3 WayfindingTarget()
    {
        if (_locked && _keys != null && _keys.TryNearestMissing(_player.position, out Vector3 key)) return key;
        return _gateMode ? NearestExit(_player.position) : _hatchPosition;
    }

    /// <summary>F77: red while locked (same language as a fuse box's red indicator), back to hatchColor once every key is held.</summary>
    private void ApplyLockLook()
    {
        Color c = _locked ? LockedColor : hatchColor;
        if (_exitLight != null) _exitLight.color = c;
        if (_exitGlow != null)
        {
            _exitGlow.SetColor("_EmissionColor", c * 3f);
            _exitGlow.SetColor("_BaseColor", c);
        }
        foreach (ExitGate gate in _gates) gate.SetLocked(_locked); // F111: the gates draw their own lamps and chains
        SetLidAngle(_locked ? 0f : LidAjarAngle, 0.7f);
    }

    // ---------------------------------------------------------------- ui

    private static TMP_FontAsset ResolveFont() => RuntimeUi.ResolveFont();

    private Canvas ResolveCanvas() => RuntimeUi.ResolveCanvas();

    private void BuildTimerText()
    {
        Canvas canvas = ResolveCanvas();
        if (canvas == null)
        {
            Debug.LogWarning("MazeEscape: no Canvas in the scene, the countdown will not be shown.", this);
            return;
        }

        GameObject holder = new GameObject("EscapeTimer");
        holder.transform.SetParent(canvas.transform, false);
        holder.layer = canvas.gameObject.layer;

        _timerText = holder.AddComponent<TextMeshProUGUI>();
        _timerText.font = ResolveFont();
        _timerText.fontSize = timerFontSize;
        _timerText.color = timerColor;
        _timerText.alignment = TextAlignmentOptions.Center;
        _timerText.text = FormatTime(_timeLeft); // BeginEscape sets the countdown first

        RectTransform rect = _timerText.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -40f);
        rect.sizeDelta = new Vector2(400f, 90f);

        // Parented under the timer so it hides with it in Stop().
        _chevronText = RuntimeUi.CreateText(_timerText.transform, "HatchBearing", "▲", 34f, hatchColor);
        RectTransform chevronRect = _chevronText.rectTransform;
        chevronRect.anchorMin = new Vector2(0.5f, 0.5f);
        chevronRect.anchorMax = new Vector2(0.5f, 0.5f);
        chevronRect.pivot = new Vector2(0.5f, 0.5f);
        chevronRect.sizeDelta = new Vector2(50f, 50f);
    }
}
