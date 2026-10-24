using System.Collections;
using UnityEngine;

public partial class IntakeRoom
{
    // ---------------------------------------------------------------- the hide step

    private void ResetHide()
    {
        _silhouetteActive = false;
        _silhouettePassed = false;
        _routeIndex = 1;
        _pauseLeft = -1f;
    }

    private string HideGoal()
    {
        if (!InZone(roomBZone) && !_silhouetteActive) return L10n.T("intake.goal.hide_go");
        bool occupied = locker != null && locker.Occupied;
        if (_silhouettePassed) return occupied ? L10n.T("intake.goal.hide_leave") : "";
        if (occupied) return L10n.T("intake.goal.hide_still");
        return L10n.T("intake.goal.hide");
    }

    private string[] HideKeys()
    {
        bool occupied = locker != null && locker.Occupied;
        if (!InZone(roomBZone) && !_silhouetteActive) return K();
        if (_silhouettePassed) return occupied ? K(UseKey()) : K();
        if (occupied) return K();
        return K(UseKey());
    }

    /// <summary>
    /// Walks the silhouette past the side doorway while (and only while) the player is hidden; before that it comes
    /// close and waits outside, footsteps and all. True once it has passed and the player has climbed back out.
    /// </summary>
    private bool UpdateHide()
    {
        bool hidden = locker != null && locker.Occupied;

        if (!_silhouetteActive)
        {
            if (!InZone(roomBZone)) return false;
            _silhouetteActive = true;
            StartCoroutine(FlickerLampB());
            silhouette.gameObject.SetActive(true);
            silhouette.position = route[0].position;
            silhouette.rotation = Quaternion.LookRotation(Flat(route[1].position - route[0].position));
            EnsureSilhouetteAudio();
            _stepDistance = 0f;
            if (_hud != null) _hud.ShowSubtitle(L10n.T("intake.sub.footsteps"), 3.5f);
            SetMusic(0f, urgentVolume, urgentFadeSeconds); // the escape track: hide, now
        }

        if (_silhouettePassed) return !hidden;

        Animator animator = silhouette.GetComponentInChildren<Animator>();
        bool moving = false;

        // Where it may walk to right now: the whole route when the player is hidden, otherwise only up to the hold point.
        Vector3 target = route[_routeIndex].position;
        // Not hidden: come no closer to the doorway than holdDistance and wait there. Past the doorway it just walks on.
        bool holding = !hidden && _routeIndex == 1 && FlatDistance(silhouette.position, route[1].position) <= holdDistance;

        if (_pauseLeft >= 0f)
        {
            // Stopped in the doorway, looking in at the room.
            if (hidden)
            {
                _pauseLeft -= Time.deltaTime;
                Vector3 look = Flat(locker.transform.position - silhouette.position);
                if (look.sqrMagnitude > 0.01f) silhouette.rotation = Quaternion.Slerp(silhouette.rotation, Quaternion.LookRotation(look), 3f * Time.deltaTime);
                if (_pauseLeft <= 0f) { _pauseLeft = -1f; _routeIndex = 2; }
            }
        }
        else if (!holding)
        {
            Vector3 step = Vector3.MoveTowards(silhouette.position, target, silhouetteSpeed * Time.deltaTime);
            Vector3 delta = step - silhouette.position;
            if (delta.sqrMagnitude > 0.000001f)
            {
                moving = true;
                silhouette.rotation = Quaternion.Slerp(silhouette.rotation, Quaternion.LookRotation(Flat(delta)), 5f * Time.deltaTime);
                silhouette.position = step;
                _stepDistance += delta.magnitude;
            }

            if (FlatDistance(silhouette.position, route[_routeIndex].position) < 0.02f)
            {
                if (_routeIndex == 1) _pauseLeft = 2.2f;
                else
                {
                    _silhouettePassed = true;
                    silhouette.gameObject.SetActive(false);
                    SetMusic(musicVolume, 0f, reliefFadeSeconds); // it has gone: slowly back to the base music
                    if (_stepSource != null) _stepSource.Stop();
                    if (_dragSource != null) _dragSource.Stop();
                }
            }
        }

        if (animator != null) animator.speed = moving ? 1f : 0f;
        if (_stepSource != null) _stepSource.transform.position = silhouette.position;
        if (_dragSource != null)
        {
            if (moving && !_dragSource.isPlaying) _dragSource.Play();
            else if (!moving && _dragSource.isPlaying) _dragSource.Pause();
            _dragSource.transform.position = silhouette.position;
        }
        if (moving && _stepDistance >= 0.8f && _stepClips != null && _stepClips.Length > 0 && _stepSource != null)
        {
            _stepDistance = 0f;
            _stepSource.pitch = UnityEngine.Random.Range(0.55f, 0.7f);
            _stepSource.PlayOneShot(_stepClips[UnityEngine.Random.Range(0, _stepClips.Length)], 0.9f);
        }

        return false;
    }

    private void EnsureSilhouetteAudio()
    {
        if (_stepSource == null)
        {
            _stepSource = silhouette.gameObject.AddComponent<AudioSource>();
            _stepSource.spatialBlend = 1f;
            _stepSource.maxDistance = 25f;
            _stepSource.rolloffMode = AudioRolloffMode.Linear;
            _stepSource.playOnAwake = false;
        }
        if (_dragSource == null)
        {
            AudioClip recordedDrag = GameAudio.Library.farDrag.Pick(); // F97: a recorded drag; the file is never destroyed with the room
            _dragClip = recordedDrag != null ? null : BuildDragClip();
            _dragSource = silhouette.gameObject.AddComponent<AudioSource>();
            _dragSource.clip = recordedDrag != null ? recordedDrag : _dragClip;
            _dragSource.loop = true;
            _dragSource.spatialBlend = 1f;
            _dragSource.maxDistance = 22f;
            _dragSource.rolloffMode = AudioRolloffMode.Linear;
            _dragSource.volume = 0.5f;
            _dragSource.playOnAwake = false;
        }
    }

    // ---------------------------------------------------------------- music

    private AudioSource BuildMusicSource(AudioClip clip)
    {
        if (clip == null) return null;
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.volume = 0f;
        source.clip = clip;
        GameSettings.RouteMusic(source); // S2: Music mixer group
        return source;
    }

    /// <summary>Fades the base music and the escape track toward these volumes; a track that reaches 0 stops, one that rises from silence restarts.</summary>
    private void SetMusic(float musicTarget, float urgentTarget, float seconds)
    {
        if (_music == null && _urgent == null) return;
        if (_musicFade != null) StopCoroutine(_musicFade);
        _musicFade = StartCoroutine(MusicFade(musicTarget, urgentTarget, seconds));
    }

    private IEnumerator MusicFade(float musicTarget, float urgentTarget, float seconds)
    {
        if (_music != null && musicTarget > 0f && !_music.isPlaying) _music.Play();
        if (_urgent != null && urgentTarget > 0f && !_urgent.isPlaying) _urgent.Play();
        float musicFrom = _music != null ? _music.volume : 0f;
        float urgentFrom = _urgent != null ? _urgent.volume : 0f;
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            float k = t / seconds;
            if (_music != null) _music.volume = Mathf.Lerp(musicFrom, musicTarget, k);
            if (_urgent != null) _urgent.volume = Mathf.Lerp(urgentFrom, urgentTarget, k);
            yield return null;
        }
        if (_music != null) { _music.volume = musicTarget; if (musicTarget <= 0f) _music.Stop(); }
        if (_urgent != null) { _urgent.volume = urgentTarget; if (urgentTarget <= 0f) _urgent.Stop(); }
        _musicFade = null;
    }

    private IEnumerator FlickerLampB()
    {
        if (lampB == null) yield break;
        float baseIntensity = lampB.intensity;
        while (!_silhouettePassed && !_leaving)
        {
            if (FlashSafety.On)
            {
                // F118: a steady dim lamp with a 0.5 Hz breath (0.3..0.5 of base) instead of a 4-20 Hz stutter.
                lampB.intensity = baseIntensity * (0.4f + 0.1f * Mathf.Sin(Time.time * Mathf.PI));
                yield return null;
                continue;
            }
            lampB.intensity = UnityEngine.Random.value < 0.35f ? baseIntensity * UnityEngine.Random.Range(0.05f, 0.5f) : baseIntensity;
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.05f, 0.25f));
        }
        lampB.intensity = baseIntensity;
    }

    /// <summary>1.5 s of low filtered noise with a slow scrape in it - the pattern every synthesised clip here follows.</summary>
    private static AudioClip BuildDragClip()
    {
        const int sampleRate = 22050;
        const float duration = 2f;
        int count = Mathf.RoundToInt(sampleRate * duration);
        float[] samples = new float[count];
        System.Random rng = new System.Random(7);
        float low = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)sampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += (noise - low) * 0.06f;
            float scrape = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 1.5f * t);
            samples[i] = low * scrape * 3.5f;
        }
        float peak = 0f;
        for (int i = 0; i < count; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f) for (int i = 0; i < count; i++) samples[i] *= 0.6f / peak;
        // Loop-safe ends.
        for (int i = 0; i < 400; i++)
        {
            float k = i / 400f;
            samples[i] *= k;
            samples[count - 1 - i] *= k;
        }
        AudioClip clip = AudioClip.Create("IntakeDrag", count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
