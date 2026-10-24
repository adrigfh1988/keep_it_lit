using System.Collections;
using TMPro;
using UnityEngine;

public partial class PlayerHud
{
    // F72 decision D12: PlayerInteractor's focus prompt overrides Locker's own trigger prompt while set,
    // so the two never fight over the same line - see RefreshPrompt.
    // S9 (D15, plan section 6): a prompt is kept as PARTS - the physical key label ("E", or "USE" on touch), the translated text, an
    // optional hold percentage - so nothing (the touch INTERACT button) ever has to parse the composed "E  HIDE  53%" line again.
    // A prompt set by key (SetPromptKey) is translated when it is drawn, so it follows a language change without being set again.
    private struct PromptParts
    {
        public string Key;
        public bool HasAction;          // F119: the label comes from InputGlyphs (keyboard key, gamepad button or touch word), resolved when the line is drawn
        public GameAction Action;
        public string TouchLabel;
        public string Text;
        public string TextKey;
        public int Percent;

        public bool IsEmpty => string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(TextKey);
        public string ResolvedText => !string.IsNullOrEmpty(TextKey) ? L10n.T(TextKey) : Text;
    }

    private PromptParts _triggerPrompt;
    private PromptParts _focusPrompt;
    private PromptParts _hintPrompt; // F106: a prompt that shows by itself for a few seconds, below any interaction prompt
    private Coroutine _hintRoutine;
    private string _promptBuiltKey, _promptBuiltText;
    private int _promptBuiltPercent = int.MinValue;
    private string _promptBuilt = "";

    /// <summary>Called by Locker on enter/exit/trigger. Null or empty hides the prompt (unless a focus prompt is showing).</summary>
    public void SetPrompt(string text)
    {
        _triggerPrompt = string.IsNullOrEmpty(text) ? default : new PromptParts { Text = text, Percent = -1 };
        RefreshPrompt();
    }

    /// <summary>S9: a trigger prompt drawn as "{keyLabel}  {L10n.T(textKey)}" and re-translated on a language change.</summary>
    public void SetPromptKey(string keyLabel, string textKey)
    {
        _triggerPrompt = string.IsNullOrEmpty(textKey) ? default : new PromptParts { Key = keyLabel, TextKey = textKey, Percent = -1 };
        RefreshPrompt();
    }

    /// <summary>F119: as SetPromptKey(string, string) but the key label follows the player's device (keyboard key / gamepad button / touch word).</summary>
    public void SetPromptKey(GameAction action, string textKey)
    {
        _triggerPrompt = string.IsNullOrEmpty(textKey) ? default : new PromptParts { HasAction = true, Action = action, TextKey = textKey, Percent = -1 };
        RefreshPrompt();
    }

    /// <summary>Called every frame by PlayerInteractor. Null or empty falls back to the trigger prompt.</summary>
    public void SetFocusPrompt(string text)
    {
        _focusPrompt = string.IsNullOrEmpty(text) ? default : new PromptParts { Text = text, Percent = -1 };
        RefreshPrompt();
    }

    /// <summary>S9: the focus prompt as parts - the key label, the (already translated) text and, while a hold is filling, its percentage (or -1).</summary>
    public void SetFocusPrompt(string keyLabel, string text, int percent = -1)
    {
        _focusPrompt = string.IsNullOrEmpty(text) ? default : new PromptParts { Key = keyLabel, Text = text, Percent = percent };
        RefreshPrompt();
    }

    /// <summary>F119: the focus prompt with a device-following key label.</summary>
    public void SetFocusPrompt(GameAction action, string text, int percent = -1)
    {
        _focusPrompt = string.IsNullOrEmpty(text) ? default : new PromptParts { HasAction = true, Action = action, Text = text, Percent = percent };
        RefreshPrompt();
    }

    // The prompt of something you can act on; the F106 hint only fills the line when there is none (and is never the INTERACT button's text).
    private PromptParts InteractionPrompt => !_focusPrompt.IsEmpty ? _focusPrompt : _triggerPrompt;
    private PromptParts ActivePrompt => !InteractionPrompt.IsEmpty ? InteractionPrompt : _hintPrompt;

    /// <summary>
    /// F106: "{keyLabel}  {L10n.T(textKey)}" for `seconds`, whenever nothing else holds the prompt line (an interactable's prompt wins and the hint
    /// reappears behind it). A second call replaces the first. Hidden on touch, like every prompt: the on-screen buttons say it.
    /// </summary>
    public void ShowHintPrompt(string keyLabel, string textKey, float seconds, int percent = -1)
    {
        if (_hintRoutine != null) StopCoroutine(_hintRoutine);
        _hintPrompt = new PromptParts { Key = keyLabel, TextKey = textKey, Percent = percent };
        RefreshPrompt();
        _hintRoutine = StartCoroutine(ClearHintAfter(seconds));
    }

    /// <summary>F119: ShowHintPrompt with a device-following key label; <paramref name="touchLabel"/> is what a touch screen shows.</summary>
    public void ShowHintPrompt(GameAction action, string touchLabel, string textKey, float seconds, int percent = -1)
    {
        if (_hintRoutine != null) StopCoroutine(_hintRoutine);
        _hintPrompt = new PromptParts { HasAction = true, Action = action, TouchLabel = touchLabel, TextKey = textKey, Percent = percent };
        RefreshPrompt();
        _hintRoutine = StartCoroutine(ClearHintAfter(seconds));
    }

    private IEnumerator ClearHintAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        _hintPrompt = default;
        _hintRoutine = null;
        RefreshPrompt();
    }

    /// <summary>The resolved prompt as one line (focus overrides trigger, same rule RefreshPrompt uses). Null/empty when nothing is shown.</summary>
    public string CurrentPrompt => FormatPrompt(ActivePrompt);

    /// <summary>F76/S9: just the words of the resolved prompt (no key label, no percentage) for TouchControls' INTERACT button. Null/empty when nothing is shown.</summary>
    public string CurrentPromptText => InteractionPrompt.IsEmpty ? null : InteractionPrompt.ResolvedText;

    private string FormatPrompt(PromptParts p)
    {
        if (p.IsEmpty) return null;
        string text = p.ResolvedText;
        string key = p.HasAction ? InputGlyphs.Key(p.Action, p.TouchLabel) : p.Key;
        if (text == _promptBuiltText && key == _promptBuiltKey && p.Percent == _promptBuiltPercent) return _promptBuilt;
        string line = string.IsNullOrEmpty(key) ? text : $"{key}  {text}";
        if (p.Percent >= 0) line += $"  {p.Percent}%";
        _promptBuiltText = text;
        _promptBuiltKey = key;
        _promptBuiltPercent = p.Percent;
        _promptBuilt = line;
        return line;
    }

    private void RefreshPrompt()
    {
        if (_promptText == null) return;
        // F76 T6: the INTERACT button shows this same text itself while touch is active, so the
        // keyboard-shaped prompt line ("E  HIDE") underneath the crosshair would just be a duplicate.
        if (TouchInput.Active)
        {
            _promptText.gameObject.SetActive(false);
            return;
        }
        string text = CurrentPrompt;
        bool show = !string.IsNullOrEmpty(text);
        _promptText.gameObject.SetActive(show);
        if (show) _promptText.text = text;
    }

    /// <summary>
    /// A dread cue, not narration: one line at a time, low on screen, fading in then out over
    /// unscaled time so it cannot freeze on screen through a pause. A new call replaces the current one.
    /// </summary>
    public void ShowSubtitle(string text, float seconds)
    {
        if (_subtitleText == null || string.IsNullOrEmpty(text)) return;
        if (_subtitleRoutine != null) StopCoroutine(_subtitleRoutine);
        _subtitleRoutine = StartCoroutine(SubtitleRoutine(text, Mathf.Max(0.2f, seconds, ReadingSeconds(text))));
    }

    /// <summary>F100: plays after the current line has finished (at once when none is showing). ShowSubtitle still replaces the line on screen; queued lines wait their turn behind it.</summary>
    public void QueueSubtitle(string text, float seconds)
    {
        if (_subtitleText == null || string.IsNullOrEmpty(text)) return;
        if (_subtitleRoutine == null) ShowSubtitle(text, seconds);
        else _subtitleQueue.Enqueue((text, Mathf.Max(seconds, ReadingSeconds(text))));
    }

    /// <summary>S9 (D14): a longer translation needs longer on screen - never less than 1.2 s plus 15 characters per second.</summary>
    public static float ReadingSeconds(string text) => string.IsNullOrEmpty(text) ? 0f : 1.2f + text.Length / 15f;

    private IEnumerator SubtitleRoutine(string text, float seconds)
    {
        const float fadeIn = 0.15f;
        const float fadeOut = 0.6f;
        float hold = Mathf.Max(0f, seconds - fadeIn - fadeOut);

        // F86: the maze's dread cues are italic; the Intake's explanations are plain teaching text.
        _subtitleText.text = text;
        _subtitleText.fontStyle = GameFlow.IsInIntake ? FontStyles.Normal : FontStyles.Italic;
        _subtitleText.gameObject.SetActive(true);

        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            SetSubtitleAlpha(Mathf.Clamp01(t / fadeIn));
            yield return null;
        }
        SetSubtitleAlpha(1f);

        t = 0f;
        while (t < hold)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        t = 0f;
        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            SetSubtitleAlpha(1f - Mathf.Clamp01(t / fadeOut));
            yield return null;
        }

        SetSubtitleAlpha(0f);
        _subtitleText.gameObject.SetActive(false);
        _subtitleRoutine = null;
        if (_subtitleQueue.Count > 0)
        {
            (string next, float nextSeconds) = _subtitleQueue.Dequeue();
            ShowSubtitle(next, nextSeconds);
        }
    }

    private void SetSubtitleAlpha(float alpha)
    {
        Color c = SubtitleColor;
        c.a = alpha;
        _subtitleText.color = c;
    }
}
