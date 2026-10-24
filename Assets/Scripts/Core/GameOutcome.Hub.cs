using UnityEngine;

/// <summary>
/// F121: a catch inside the asylum hub that is the real thing (the breach's hunter, after its one free grab - see <see cref="TryGrabInHub"/>). It runs
/// through the same single-shot guards as every other death: a held Second Wind is consumed and the breach's `onSecondWind` runs at full black (the
/// hunter is moved far away); otherwise <see cref="Lose"/> plays the capture sequence onto the hunter's head and the end screen offers a Retry that
/// re-enters the breach (not the maze) - see <c>HubBreach.Retrying</c>. Nothing is banked: the floor's maze was not played.
/// </summary>
public partial class GameOutcome
{
    /// <summary>F121: the catch in progress is a hub catch (the maze's sound bed must not start under the Second Wind).</summary>
    private bool _hubCatch;

    /// <summary>F121: the death in progress is the hub's: no consolation, the hub end screen, Retry goes back to the hub.</summary>
    private bool _hubDeath;

    /// <summary>True when the run ended with a death in the asylum hub.</summary>
    public bool HubDeath => _hubDeath;

    /// <summary>`lookAt` is the hunter's head; `onSecondWind` runs at full black when a Second Wind was held (it is consumed).</summary>
    public void CaughtInHub(Vector3 lookAt, System.Action onSecondWind)
    {
        if (IsOver || IsEnding) return;
        _hubCatch = true;
        if (PlayerInventory.TryConsume(ShopItem.SecondWind))
        {
            StartCoroutine(SecondWindSequence(onSecondWind, "sub.second_wind.release"));
            return;
        }

        _hubDeath = true; // only on the Lose path
        _captureLookAt = lookAt;
        _hasCaptureLookAt = true;
        DescentRun.Cause = DescentCause.Hunter;
        Lose(LoseReason.Caught);
    }

    /// <summary>The Retry / New Maze buttons of a hub death: back into the breach, in the hub, on the same floor (the floor-5 maze behind it was already built from `seed`).</summary>
    private void RetryAfterHubDeath(bool sameMaze)
    {
        HubBreach.Retrying = true;
        GameFlow.Restart(sameMaze, _seed, true);
    }
}
