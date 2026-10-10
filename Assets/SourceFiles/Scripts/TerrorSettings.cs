using UnityEngine;

/// <summary>
/// F103 (plannings/maze-terror-spec.md): one on/off switch per Tier A "terror" feature, so each can be
/// A/B tested from MazeGenerator's Inspector. Tunables stay on the component that owns the feature;
/// this only says whether it runs. Every consumer starts from defaults (all on) and is handed the
/// scene's instance through ApplyTerror in MazeGenerator.SetUpAtmosphere.
///
/// Two flags take effect once, not live: torchSway (applied in Flashlight.ApplyTerror) and the floor-1
/// eye rule (AIPresence.SetEyesLitAtRest). The others are read where they are used, so toggling them in
/// the Inspector during Play works.
/// </summary>
[System.Serializable]
public class TerrorSettings
{
    [Tooltip("A2: some phantoms are real, audio phantoms may fire closer to the hunter, silhouettes leave a dead lamp")]
    public bool contaminatedPhantoms = true;
    [Tooltip("A3: the hunter's eyes are dark unless it is chasing, caught in the beam far off, or revealed")]
    public bool rationedEyes = true;
    [Tooltip("A4: the torch is offset from the eye and lags the view, so shadows move")]
    public bool torchSway = true;
    [Tooltip("A5: relocation telegraph timings vary, some telegraphs warp nothing, a rare late warp is silent")]
    public bool unpredictableTelegraphs = true;
    [Tooltip("A6: the torch cuts out at the worst moments (capped per floor)")]
    public bool torchFailures = true;
    [Tooltip("A7: a heard noise stops the hunter dead (servo whine, world text IT HEARD YOU.), then it rushes the spot for 3 s")]
    public bool hunterListens = true;
    [Tooltip("A8: a new phantom - the game's own sounds (locker, can, shutter, torch click) from somewhere unseen")]
    public bool wrongSounds = true;
    [Tooltip("A9: the chase sting varies, and from floor 3 some chases start in silence")]
    public bool varyingSpottedSting = true;
    [Tooltip("A10: some dead ends survive braiding, and a star or two waits at the end of one")]
    public bool baitDeadEnds = true;
    [Tooltip("Log one [Terror] line to the Console each time a feature fires - for play-testing")]
    public bool logEvents = true;

    /// <summary>Console line tagged [Terror], only while logEvents is on.</summary>
    public void Log(string message, Object context = null)
    {
        if (logEvents) Debug.Log("[Terror] " + message, context);
    }
}
