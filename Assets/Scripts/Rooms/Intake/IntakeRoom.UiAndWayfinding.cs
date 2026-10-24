using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public partial class IntakeRoom
{
    // ---------------------------------------------------------------- goals: card + arrow

    private static string[] K(params string[] keys) => keys;

    // S9: the current step's live goal / chips, so a language change can refresh the card without animating it.
    private Func<string> _stepGoal;
    private Func<string[]> _stepKeys;

    private void OnEnable() => L10n.Changed += OnLanguageChanged;
    private void OnDisable() => L10n.Changed -= OnLanguageChanged;

    private void OnLanguageChanged()
    {
        // Live() rewrites only what changed (the goal text, rebuilt chips) and leaves a tick or a fade alone; the counter is language-free.
        if (_card != null && _stepGoal != null && _stepKeys != null) _card.Live(_stepGoal(), _stepKeys());
    }

    /// <summary>The interact chip: the E key, or the on-screen USE button on touch.</summary>
    private static string UseKey() => InputGlyphs.Key(GameAction.Interact, L10n.T("ui.key.use"));

    private struct Marker
    {
        public bool Has;
        /// <summary>Where the arrow floats (before the bob).</summary>
        public Vector3 Pos;
        /// <summary>The target's centre, where a pickup's glow sits.</summary>
        public Vector3 Center;
        public bool Glow;
    }

    /// <summary>A marker over a scene object: the arrow floats `lift` above the top of its renderers; glow = a soft pulsing light on it too.</summary>
    private static Marker Mk(Transform target, float lift = 0.35f, bool glow = false)
    {
        if (target == null) return default;
        Bounds b = new Bounds(target.position, Vector3.zero);
        bool any = false;
        foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return new Marker { Has = true, Center = b.center, Pos = new Vector3(b.center.x, b.max.y + lift, b.center.z), Glow = glow };
    }

    /// <summary>
    /// One goal at a time: the objective card fades in, the live goal/keys follow the player's state, the arrow floats over the
    /// target, and on completion a tick shows for a beat before the card fades out and the next goal comes in.
    /// </summary>
    private IEnumerator Step(int index, Func<string> goal, Func<string[]> keys, Func<bool> done, Func<Marker> marker = null, Action onStart = null)
    {
        onStart?.Invoke();
        _stepGoal = goal;
        _stepKeys = keys;
        if (_card != null) _card.Set($"{index + 1} / {StepCount}", goal(), keys());
        if (_card != null) yield return _card.Fade(0f, 1f, 0.3f);

        while (true)
        {
            if (_card != null) _card.Live(goal(), keys());
            ApplyMarker(marker != null ? marker() : default);
            if (Time.timeScale > 0f && done()) break;
            yield return null;
        }

        ApplyMarker(default);
        if (_card != null) yield return _card.Tick();
        if (_card != null) yield return _card.Fade(1f, 0f, 0.25f);
        yield return new WaitForSeconds(0.1f);
    }

    private void ApplyMarker(Marker m)
    {
        if (goalArrow != null)
        {
            if (goalArrow.gameObject.activeSelf != m.Has) goalArrow.gameObject.SetActive(m.Has);
            if (m.Has)
            {
                float bob = Mathf.Sin(Time.time * 2f * Mathf.PI * 1.2f) * 0.15f;
                Vector3 basePos = m.Pos;
                if (_camera != null)
                {
                    // Nudged toward the player so a wall-mounted target (the sign, the note) never swallows one arm of the chevron.
                    Vector3 flat = _camera.transform.position - basePos;
                    flat.y = 0f;
                    // (less when the player is right on top of it, or the chevron fills the screen)
                    if (flat.sqrMagnitude > 0.01f) basePos += flat.normalized * Mathf.Clamp(flat.magnitude * 0.15f, 0.1f, 0.55f);
                }
                goalArrow.position = basePos + Vector3.up * bob;
                if (_camera != null)
                {
                    Vector3 toCamera = _camera.transform.position - goalArrow.position;
                    // Roughly constant on-screen size: smaller up close, larger across the room.
                    goalArrow.localScale = Vector3.one * Mathf.Clamp(toCamera.magnitude / 4f, 0.22f, 1.6f);
                    toCamera.y = 0f;
                    if (toCamera.sqrMagnitude > 0.001f) goalArrow.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
                }
            }
        }

        if (goalGlow != null)
        {
            bool on = m.Has && m.Glow;
            if (goalGlow.enabled != on) goalGlow.enabled = on;
            if (on)
            {
                goalGlow.transform.position = m.Center;
                goalGlow.intensity = 1.5f * (0.65f + 0.35f * Mathf.Sin(Time.time * 3.2f));
            }
        }
    }

    private IEnumerator Fade(float from, float to, float seconds)
    {
        return ScreenFader.Fade(_blackout, from, to, seconds, unscaled: true);
    }

    // ---------------------------------------------------------------- ui

    private void BuildUi()
    {
        RuntimeUi.EnsureEventSystem();
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _uiRoot = new GameObject("IntakeUi", typeof(RectTransform));
        _uiRoot.transform.SetParent(canvas.transform, false);
        _uiRoot.layer = canvas.gameObject.layer;
        RuntimeUi.Stretch((RectTransform)_uiRoot.transform);

        _card = ObjectiveCard.Create(_uiRoot.transform, "ObjectiveCardRoot", false, false);

        // Black cover, topmost.
        _blackout = RuntimeUi.CreatePanel(_uiRoot.transform, "IntakeBlackout", Color.black).GetComponent<Image>();
        _blackout.raycastTarget = false;
    }
}
