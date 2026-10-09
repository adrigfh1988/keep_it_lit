using System;
using UnityEngine;

/// <summary>
/// F97: one sound "slot" - a pool of real recorded clips for one job (a door rattle, a far scream...) plus how to play it.
/// An empty slot means "use the generated fallback at the call site". Variations are picked with UnityEngine.Random, never a maze rng stream.
/// </summary>
[Serializable]
public class AudioSlot
{
    [Tooltip("Variations; one is picked at random each time. Empty = the call site keeps its generated sound.")]
    public AudioClip[] clips = new AudioClip[0];
    [Tooltip("Loudness trim (normalises this clip pool against the others). Multiplied with the call site's own volume, capped at 1 by the AudioSource.")]
    [Range(0f, 4f)] public float volume = 1f;
    [Tooltip("Random pitch spread, e.g. 0.08 = +-8 %.")]
    [Range(0f, 0.4f)] public float pitchJitter;
    [Tooltip("Skip this many seconds of dead air at the start of the file.")]
    [Min(0f)] public float startOffset;
    [Tooltip("Fade out and stop after this many seconds (0 = play the whole clip). For files with a long tail.")]
    [Min(0f)] public float maxSeconds;
    [Tooltip("Low-pass cutoff in Hz for 'heard through walls' sounds (0 = off).")]
    [Min(0f)] public float lowPassHz;

    public bool Has
    {
        get
        {
            if (clips == null) return false;
            for (int i = 0; i < clips.Length; i++) if (clips[i] != null) return true;
            return false;
        }
    }

    /// <summary>A random clip of the pool, or null when the slot is empty.</summary>
    public AudioClip Pick()
    {
        if (clips == null || clips.Length == 0) return null;
        int start = UnityEngine.Random.Range(0, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip c = clips[(start + i) % clips.Length];
            if (c != null) return c;
        }
        return null;
    }

    /// <summary>The first clip of the pool (for loops and music), or null.</summary>
    public AudioClip First()
    {
        if (clips == null) return null;
        for (int i = 0; i < clips.Length; i++) if (clips[i] != null) return clips[i];
        return null;
    }

    public float PickPitch() => pitchJitter > 0f ? 1f + UnityEngine.Random.Range(-pitchJitter, pitchJitter) : 1f;
}

/// <summary>
/// F97: every recorded sound the game uses in place of a generated one, in one asset
/// (Assets/SourceFiles/Audio/GameAudioLibrary.asset, filled by GameAudioLibraryBuilder, assigned to MazeGenerator.audioLibrary).
/// Read through <see cref="GameAudio.Library"/>; slot numbers refer to plannings/audio-asset-list.md.
/// </summary>
[CreateAssetMenu(menuName = "LIGHTS OUT/Game Audio Library", fileName = "GameAudioLibrary")]
public class GameAudioLibrary : ScriptableObject
{
    [Header("Asylum hub - ambience (2-11)")]
    public AudioSlot roomTone = new AudioSlot();       // 2, loop (restarted with a fade, the file ends in silence)
    public AudioSlot machinery = new AudioSlot();      // 3, loop
    public AudioSlot drip = new AudioSlot();           // 4 / 10, retriggered at random
    public AudioSlot farScream = new AudioSlot();      // 6
    public AudioSlot farSlam = new AudioSlot();        // 7
    public AudioSlot pipeClank = new AudioSlot();      // 8 (also the patient knock)
    public AudioSlot farRumble = new AudioSlot();      // new far event
    public AudioSlot buildingSettle = new AudioSlot(); // 9
    public AudioSlot floorCreak = new AudioSlot();     // 11
    public AudioSlot farDrag = new AudioSlot();        // 47 / 60

    [Header("Asylum hub - set pieces and story (12-24)")]
    public AudioSlot timmyStep = new AudioSlot();      // 12
    public AudioSlot timmySpot = new AudioSlot();      // 56
    public AudioSlot caught = new AudioSlot();         // 13
    public AudioSlot caughtLayer = new AudioSlot();    // 13 layer
    public AudioSlot liftGrind = new AudioSlot();      // 14
    public AudioSlot liftBang = new AudioSlot();       // 15
    public AudioSlot liftThud = new AudioSlot();       // 16
    public AudioSlot liftJamRumble = new AudioSlot();  // new
    public AudioSlot bulbPop = new AudioSlot();        // 18 / 44
    public AudioSlot keyJingle = new AudioSlot();      // 19
    public AudioSlot lockedRattle = new AudioSlot();   // 20
    public AudioSlot exitDoorCreak = new AudioSlot();  // 21
    public AudioSlot masterLever = new AudioSlot();    // 22
    public AudioSlot powerDown = new AudioSlot();      // 22
    public AudioSlot deskDrawer = new AudioSlot();     // 23
    public AudioSlot whisperEcho = new AudioSlot();    // 24
    public AudioSlot patientSound = new AudioSlot();   // 57
    public AudioSlot flickerCrackle = new AudioSlot(); // 50

    [Header("Mazes (25-39)")]
    public AudioSlot shutterRoll = new AudioSlot();    // 25
    public AudioSlot shutterSlam = new AudioSlot();    // 26
    public AudioSlot fuseClunk = new AudioSlot();      // 27
    public AudioSlot fuseHum = new AudioSlot();        // 27
    public AudioSlot wallButton = new AudioSlot();     // 28
    public AudioSlot hatchOpen = new AudioSlot();      // 29
    public AudioSlot shardChime = new AudioSlot();     // 31
    public AudioSlot glassStep = new AudioSlot();      // 32
    public AudioSlot bottleShatter = new AudioSlot();  // 33
    public AudioSlot canClatter = new AudioSlot();     // 33
    public AudioSlot steamValve = new AudioSlot();     // 34
    public AudioSlot terminalBeep = new AudioSlot();   // 34
    public AudioSlot candleSnuff = new AudioSlot();    // 34
    public AudioSlot stanTick = new AudioSlot();       // 35
    public AudioSlot stanSkitter = new AudioSlot();    // 35
    public AudioSlot relocationCue = new AudioSlot();  // 37
    public AudioSlot phantomWhisper = new AudioSlot(); // 38
    public AudioSlot captureSting = new AudioSlot();   // 39
    public AudioSlot heartbeat = new AudioSlot();      // one lub-dub, retriggered by HorrorAudioDirector (Unity AI generated, 9 Oct 2026)

    [Header("Player, UI, music")]
    public AudioSlot torchClick = new AudioSlot();     // 53
    public AudioSlot lockerDoor = new AudioSlot();     // 54
    public AudioSlot lockerBreath = new AudioSlot();   // 55
    public AudioSlot paper = new AudioSlot();          // 59
    public AudioSlot shopTick = new AudioSlot();       // 49
    public AudioSlot chaseMusic = new AudioSlot();     // F93 floor-4 chase, loop
    public AudioSlot chaseEnd = new AudioSlot();       // the chase's last bars (fades out by itself)
    public AudioSlot shopMusic = new AudioSlot();      // shop, loop
    public AudioSlot typewriter = new AudioSlot();     // F100: one key tap of the readmission slip typing out (optional; empty = a generated click)
}
