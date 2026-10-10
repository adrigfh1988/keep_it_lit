using UnityEngine;

/// <summary>
/// F103/F104 (plannings/maze-terror-spec.md, maze-terror-tierB-spec.md): one on/off switch per Tier A and Tier B
/// "terror" feature, so each can be A/B tested from MazeGenerator's Inspector. Tunables stay on the component that
/// owns the feature; this only says whether it runs. Every consumer starts from defaults (all on) and is handed the
/// scene's instance through ApplyTerror in MazeGenerator.SetUpAtmosphere.
///
/// Flags that take effect once, not live: torchSway (applied in Flashlight.ApplyTerror), the floor-1 eye rule
/// (AIPresence.SetEyesLitAtRest) and escapeHunt's hatch placement (read when the hatch opens). The others are read
/// where they are used, so toggling them in the Inspector during Play works.
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

    [Header("Tier B (F104)")]
    [Tooltip("B2: after 2 s of sight the hunter commits to a chase it can win: it matches a walker and nearly a sprinter in the escape")]
    public bool committedChase = true;
    [Tooltip("B6: the hunter's footsteps are low-passed behind walls and carry a metal scrape")]
    public bool muffledHunter = true;
    [Tooltip("B1: one hidden fear value drives the heartbeat, the screen, phantom pacing and the hunter's threat, with backoffs")]
    public bool dreadMeter = true;
    [Tooltip("B11: once per floor everything goes quiet for 15-25 s, and usually nothing happens")]
    public bool heldBreath = true;
    [Tooltip("B5: the floor music leaves for room tone or dead air, and returns only on an event")]
    public bool silenceWindows = true;
    [Tooltip("B4: the escape is a hunt - closer hatch, alarm, lamp wave, hidden chevron, ducked panic track")]
    public bool escapeHunt = true;
    [Tooltip("B3 (run 2): hiding spots decay - breathing, knocks, a hunter that peeks in")]
    public bool sanctuaryDecay = true;
    [Tooltip("B7 (run 2): shadows with no owner cross the lit wall")]
    public bool shadowPhantoms = true;
    [Tooltip("B8 (run 2): a dark figure at the edge of the view, gone when you turn")]
    public bool peripheralPhantoms = true;
    [Tooltip("B9 (run 2): unobserved props change variant")]
    public bool displacement = true;
    [Tooltip("B10 (run 2): a seen set piece appears a second time elsewhere")]
    public bool lyingLandmarks = true;

    [Header("Tier C (F105)")]
    [Tooltip("C6: the hunter learns the player's habits - where it was noisy within a floor, and across the campaign what it overuses (lockers, throws, the dark, sprinting)")]
    public bool learningHunter = true;
    [Tooltip("C9: the default torch beam is narrower and dimmer and the fog a bit thicker; the focused beam is unchanged")]
    public bool narrowBeam = true;
    [Tooltip("C7: Stan takes slow steps in the edge of the beam while the player turns fast")]
    public bool penumbraLies = true;
    [Tooltip("C5 (run 2): one star per floor is a lure")]
    public bool lureStars = true;
    [Tooltip("C8 (run 2): a phantom is sometimes the player's own robot")]
    public bool doubles = true;
    [Tooltip("C2 (run 2): a pillared room per floor, a long gallery from floor 3")]
    public bool roomsAndGalleries = true;
    [Tooltip("C3 (run 2): a tight passage that slows the player")]
    public bool squeezes = true;
    [Tooltip("C4 (run 3): a corridor outside the maze that loops")]
    public bool loopCorridor = true;

    /// <summary>Console line tagged [Terror], only while logEvents is on.</summary>
    public void Log(string message, Object context = null)
    {
        if (logEvents) Debug.Log("[Terror] " + message, context);
    }
}
