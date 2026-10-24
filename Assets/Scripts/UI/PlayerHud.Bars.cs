using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class PlayerHud
{
    // ---- F120: the microphone meter -----------------------------------------------------------------------------------------
    private PlayerVoice _voice;
    private GameObject _voiceHolder;
    private Image _voiceFill;
    private Image _voiceGhost;
    private Image _voiceTick;
    private CanvasGroup _voiceGroup;
    private TextMeshProUGUI _voiceLabel;
    private float _voiceShown;
    private float _voiceVel;
    private float _voiceAlpha;

    private void UpdateBattery()
    {
        if (_flashlight == null || _batteryFill == null)
        {
            if (_batteryHolder != null) _batteryHolder.SetActive(false);
            if (_batteryLabel != null) _batteryLabel.gameObject.SetActive(false);
            return;
        }

        float charge = _flashlight.Charge;
        // Eased toward the real value so the bar visibly slides down instead of stepping. unscaled
        // time keeps the easing alive if the clock is frozen, and the real value does not move then.
        _batteryShown = Mathf.SmoothDamp(_batteryShown, charge, ref _batteryVel, 0.25f, float.MaxValue, Time.unscaledDeltaTime);
        SetFill(_batteryFill, _batteryShown);
        // The ghost lags behind on the way down, so a drain leaves a pale trail that shrinks after it
        SetFill(_batteryGhost, Mathf.Max(_batteryShown, Mathf.MoveTowards(GhostValue(_batteryGhost), charge, 0.05f * Time.unscaledDeltaTime)));

        // Calm above 30%, ramping to alarmed as it empties from there.
        float t = Mathf.Clamp01(charge / 0.3f);
        Color color = Color.Lerp(AlarmedColor, _tint, t);
        _batteryFill.color = color;

        if (_flashlight.IsDead)
        {
            _batteryLabel.text = L10n.T("hud.torch_dead");
            _batteryLabel.color = AlarmedColor;
        }
        else if (_flashlight.IsDropped)
        {
            // F120: the torch is on the floor - the bar keeps showing the charge, the label blinks at 2 Hz (steady with Reduce flashing)
            _batteryLabel.text = L10n.T("hud.torch_dropped");
            bool on = FlashSafety.On || ((int)(Time.unscaledTime * 4f)) % 2 == 0;
            _batteryLabel.color = on ? AlarmedColor : _tint;
        }
        else
        {
            _batteryLabel.text = L10n.T("hud.torch");
            bool blinking = charge <= 0.1f;
            if (blinking)
            {
                bool on = FlashSafety.On || ((int)(Time.time * 4f)) % 2 == 0; // F118: steady warning colour with Reduce flashing on
                _batteryLabel.color = on ? AlarmedColor : _tint;
            }
            else
            {
                _batteryLabel.color = color;
            }
        }
    }

    private void UpdateVoice()
    {
        if (_voiceFill == null || _voiceGroup == null) return;

        bool active = _voice != null && _voice.Active;
        _voiceAlpha = Mathf.MoveTowards(_voiceAlpha, active ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        _voiceGroup.alpha = _voiceAlpha;
        if (_voiceLabel != null) _voiceLabel.gameObject.SetActive(_voiceAlpha > 0.01f);
        if (!active) return;

        float level = MicInput.Level;
        _voiceShown = Mathf.SmoothDamp(_voiceShown, level, ref _voiceVel, 0.05f, float.MaxValue, Time.unscaledDeltaTime);
        SetFill(_voiceFill, _voiceShown);
        SetFill(_voiceGhost, 0f);

        float threshold = _voice.Threshold;
        if (_voiceTick != null)
        {
            RectTransform tr = _voiceTick.rectTransform;
            tr.anchorMin = new Vector2(Mathf.Clamp01(threshold), 0f);
            tr.anchorMax = new Vector2(Mathf.Clamp01(threshold), 1f);
            tr.pivot = new Vector2(0.5f, 0.5f);
            tr.sizeDelta = new Vector2(2f, 0f);
            tr.anchoredPosition = Vector2.zero;
        }

        bool loud = _voice.Loud;
        _voiceFill.color = loud ? AlarmedColor : StaminaColor;
        _voiceGroup.alpha = _voiceAlpha * (level < 0.6f * threshold && !loud ? 0.35f : 1f);
        if (_voiceLabel != null)
        {
            _voiceLabel.text = loud ? L10n.T("hud.voice_loud") : L10n.T("hud.voice");
            // 4 Hz blink; steady with Reduce flashing on (F118)
            bool on = !loud || FlashSafety.On || ((int)(Time.unscaledTime * 8f)) % 2 == 0;
            _voiceLabel.color = loud ? (on ? AlarmedColor : _tint) : _tint;
        }
    }

    private void UpdateStamina()
    {
        if (_stamina == null || _staminaFill == null)
        {
            if (_staminaHolder != null) _staminaHolder.SetActive(false);
            return;
        }

        float fraction = _stamina.Fraction;
        // Faster easing than the battery: sprinting and recovering are seconds-long, so the bar has
        // to visibly drain and refill in step with them.
        _staminaShown = Mathf.SmoothDamp(_staminaShown, fraction, ref _staminaVel, 0.12f, float.MaxValue, Time.unscaledDeltaTime);
        SetFill(_staminaFill, _staminaShown);
        SetFill(_staminaGhost, Mathf.Max(_staminaShown, Mathf.MoveTowards(GhostValue(_staminaGhost), fraction, 0.4f * Time.unscaledDeltaTime)));

        // Exhausted pulses red so the lockout is legible; a recovering bar tints toward white as it fills
        Color color = StaminaColor;
        if (_stamina.Exhausted || _stamina.Limping) // F120: a limp pulses like the lockout
        {
            float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
            color = StaminaExhaustedColor * pulse;
            color.a = 1f;
        }
        _staminaFill.color = color;

        // Stays visible until it has genuinely finished refilling, then fades out
        bool full = fraction >= 0.999f && _staminaShown >= 0.99f && !_stamina.Limping;
        float targetAlpha = full ? 0f : 1f;
        _staminaAlpha = Mathf.MoveTowards(_staminaAlpha, targetAlpha, Time.unscaledDeltaTime * (full ? 1f : 6f));
        if (_staminaGroup != null) _staminaGroup.alpha = _staminaAlpha;
    }

    /// <summary>
    /// Sets how much of the track a fill covers by moving its right anchor. Image.Type.Filled cannot
    /// be used here: with no sprite assigned Unity ignores fillAmount and always draws a full rect,
    /// which is why the bars used to look like they never moved.
    /// </summary>
    private static void SetFill(Image fill, float amount)
    {
        if (fill == null) return;
        RectTransform rect = fill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static float GhostValue(Image ghost)
    {
        return ghost != null ? ghost.rectTransform.anchorMax.x : 0f;
    }

    /// <summary>Track + a fill and a pale trailing "ghost" fill, both sized by anchors.</summary>
    private static Image CreateBar(Transform parent, string name, Vector2 anchoredPosition, Vector2 size,
        Color trackColor, Color fillColor, out GameObject holder, out Image ghost)
    {
        holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;

        Image track = holder.AddComponent<Image>();
        track.color = trackColor;
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(0f, 0f);
        trackRect.pivot = new Vector2(0f, 0f);
        trackRect.anchoredPosition = anchoredPosition;
        trackRect.sizeDelta = size;

        // Ghost first so the real fill draws on top of it
        GameObject ghostObj = new GameObject(name + "_Ghost");
        ghostObj.transform.SetParent(holder.transform, false);
        ghostObj.layer = holder.layer;
        ghost = ghostObj.AddComponent<Image>();
        ghost.color = new Color(1f, 1f, 1f, 0.28f);
        ghost.raycastTarget = false;
        SetFill(ghost, 1f);

        GameObject fillObj = new GameObject(name + "_Fill");
        fillObj.transform.SetParent(holder.transform, false);
        fillObj.layer = holder.layer;

        Image fill = fillObj.AddComponent<Image>();
        fill.color = fillColor;
        fill.raycastTarget = false;
        SetFill(fill, 1f);

        return fill;
    }
}
