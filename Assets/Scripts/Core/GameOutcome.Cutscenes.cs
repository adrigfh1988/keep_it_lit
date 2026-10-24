using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.UI;

public partial class GameOutcome
{
    /// <summary>`letGo` runs at full black (the hunter's warp, or the loop's throw-out); `releaseLineKey` is the subtitle when the screen comes back.</summary>
    private IEnumerator SecondWindSequence(System.Action letGo, string releaseLineKey)
    {
        IsEnding = true; // pause, dread, phantoms, and Lose() all wait
        if (_escape != null) _escape.Frozen = true; // not Stop(): Stop is permanent
        bool torchWasOn = _flashlight != null && _flashlight.IsOn;

        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;
        if (_audio != null) _audio.PlayCaptureSting();

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = canvas != null
            ? RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>()
            : null;
        if (blackout != null) blackout.transform.SetAsLastSibling();

        // A hard cut, not the slow capture fade.
        if (blackout != null) yield return Fade(blackout, 0f, 1f, 0.25f);
        else yield return new WaitForSeconds(0.25f);

        yield return new WaitForSeconds(0.7f);

        letGo?.Invoke();

        if (_stealth != null) _stealth.LastExposed = false;
        if (_audio != null && !_hubCatch) _audio.ResumeBed(); // F121: a hub catch must not start the maze's drone
        if (_flashlight != null)
        {
            _flashlight.SetOn(torchWasOn);
            _flashlight.Stutter(1.5f);
        }

        if (blackout != null)
        {
            yield return Fade(blackout, 1f, 0f, 0.6f);
            Destroy(blackout.gameObject);
        }
        else
        {
            yield return new WaitForSeconds(0.6f);
        }

        if (_escape != null) _escape.Frozen = false;
        PlayerLock.Freeze(_player, false);
        if (_flashlight != null) _flashlight.InputEnabled = true;
        IsEnding = false;
        _hubCatch = false;

        if (_hud != null) _hud.ShowSubtitle(L10n.T(releaseLineKey), 3.2f);
    }

    private IEnumerator FloorClearSequence()
    {
        IsEnding = true; // blocks pause, dread, phantoms, and a second ending
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        float secondsLeft = _escape != null ? _escape.TimeLeft : 0f;

        EndTheHunt();
        if (_flashlight != null) { _flashlight.SetOn(false); _flashlight.InputEnabled = false; }
        PlayerLock.Freeze(_player, true);

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = null;
        if (canvas != null)
        {
            blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>();
            blackout.transform.SetAsLastSibling();
        }

        if (blackout != null) yield return Fade(blackout, 0f, 1f, 0.6f);
        else yield return new WaitForSeconds(0.6f); // the player is falling onto the platform meanwhile

        // F94: when the asylum can host the shop, the player goes to the hub instead (the Commissary is there); the ledger is over black.
        bool viaHub = AsylumHub.ShopInHub;
        if (!viaHub) _shop.ArrivePlayer(_player);

        Payout payout = CommitFloorRewards(secondsLeft, true);
        // F83: shop-arrival checkpoint, with the payout banked. Resumes straight into the shop.
        // F94: not via the hub - NextFloor writes the floor-start checkpoint with the deposited wallet.
        if (!viaHub) SaveSystem.WriteCheckpoint(GameFlow.CurrentFloor, true);

        Transform ledger = blackout != null ? BuildLedger(blackout.transform, payout, viaHub) : null;
        _slipLedger = viaHub && ledger != null;
        _typing = _slipLedger; // set before the first frame of the coroutine so an early press cannot continue past the typing
        if (_slipLedger) StartCoroutine(TypeLedger());
        _showingLedger = true; // Update frees the cursor while this is true
        PlayerLock.SetCursorFree(true);

        _continuePressed = false;
        yield return new WaitUntil(() => _continuePressed);
        _showingLedger = false;
        _slipLedger = false;

        if (viaHub)
        {
            // The screen stays black; the reload lands in the hub at the cleared wing's arrival point (GameFlow.ArriveInHub).
            IsEnding = false;
            GameFlow.NextFloor();
            yield break;
        }

        GameFlow.IsInShop = true;
        IsEnding = false;
        _shop.Open();
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);

        if (blackout != null)
        {
            yield return Fade(blackout, 1f, 0f, 0.5f, ledger);
            Destroy(blackout.gameObject);
        }
    }

    private IEnumerator EnterShopRoutine()
    {
        IsEnding = true;
        // The maze behind the shop was rebuilt from a fresh seed and just re-rolled this floor's pair; put the played one back.
        SaveSystem.ReapplyRolledObjectives();

        EndTheHunt();
        if (_flashlight != null) { _flashlight.SetOn(false); _flashlight.InputEnabled = false; }
        PlayerLock.Freeze(_player, true);

        // Created before the first yield, so the half-built maze is never seen.
        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = null;
        if (canvas != null)
        {
            blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", Color.black).GetComponent<Image>();
            blackout.transform.SetAsLastSibling();
        }

        _shop.ArrivePlayer(_player);
        GameFlow.IsInShop = true;
        IsEnding = false;
        _shop.Open();
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);

        if (blackout != null)
        {
            yield return Fade(blackout, 1f, 0f, 0.5f);
            Destroy(blackout.gameObject);
        }
    }

    /// <summary>
    /// F93 (E10): floor 5 was cleared and the asylum can host the finale. Everything Win() does up to the end screen - the payout is read and
    /// banked now (time left included), the hunt silenced - but the run stays "ending" (pause, dread and a second ending are held off) and
    /// the player goes back to the Director's desk instead. The save is NOT deleted here; <see cref="CompleteFinale"/> does it when an
    /// ending actually completes.
    /// </summary>
    private IEnumerator FinaleSequence()
    {
        IsEnding = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        float secondsLeft = _escape != null ? _escape.TimeLeft : 0f;
        _floorPayout = CommitFloorRewards(secondsLeft);

        EndTheHunt();
        if (_escape != null) _escape.Stop();
        if (_flashlight != null) { _flashlight.SetOn(false); _flashlight.InputEnabled = false; }
        PlayerLock.Freeze(_player, true);

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = null;
        if (canvas != null)
        {
            blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>();
            blackout.transform.SetAsLastSibling();
            yield return Fade(blackout, 0f, 1f, 0.6f);
        }
        else yield return new WaitForSeconds(0.6f);

        AsylumHub hub = AsylumHub.Active;
        if (hub != null && hub.CanFinale) hub.EnterFinale(); // makes its own black panel before its first yield
        else
        {
            // The hub vanished between the check and now: fall back to the plain win, never strand the player.
            if (blackout != null) Destroy(blackout.gameObject);
            IsEnding = false;
            Win();
            yield break;
        }
        if (blackout != null) Destroy(blackout.gameObject);
    }

    private IEnumerator CaptureSequence()
    {
        IsEnding = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        bool torchWasOn = _flashlight != null && _flashlight.IsOn;

        // The countdown freezes where it is, so it cannot produce a second ending mid-sequence
        if (_escape != null) _escape.Stop();
        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;
        if (_audio != null) _audio.PlayCaptureSting();

        ThirdPersonController controller = _player != null ? _player.GetComponent<ThirdPersonController>() : null;
        Image blackout = null;
        int flicker = 0;
        float elapsed = 0f;

        while (elapsed < 1.4f)
        {
            elapsed += Time.deltaTime;

            // Ease onto the hunter's eyes (or, for the loop's Visitor, its head); lands in about half a second
            if (controller != null && (_hasCaptureLookAt || _follower != null))
            {
                Vector3 target = _hasCaptureLookAt ? _captureLookAt : _follower.transform.position + Vector3.up * _follower.EyeHeight;
                controller.LookAt(target, 1f - Mathf.Pow(0.001f, Time.deltaTime));
            }

            while (flicker < FlickerTimes.Length && elapsed >= FlickerTimes[flicker])
            {
                // Even entries switch it off, odd ones back on; the last (index 6) leaves it off.
                // F118: with Reduce flashing on only the first beat happens - the torch goes off once and stays off.
                if (_flashlight != null && (!FlashSafety.On || flicker == 0)) _flashlight.SetOn(flicker % 2 == 1);
                flicker++;
            }

            if (elapsed >= 0.9f)
            {
                if (blackout == null)
                {
                    // Created now rather than earlier so it is the topmost sibling
                    Canvas canvas = RuntimeUi.ResolveCanvas();
                    if (canvas != null)
                    {
                        blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>();
                    }
                }

                if (blackout != null)
                {
                    blackout.color = new Color(0f, 0f, 0f, Mathf.Clamp01((elapsed - 0.9f) / 0.5f));
                }
            }

            yield return null;
        }

        if (blackout != null) blackout.color = Color.black;

        EndTheHunt();
        IsOver = true;
        IsEnding = false;

        if (!_hubDeath) BankConsolation(); // F121: nothing was earned in the hub

        yield return new WaitForSeconds(0.2f);

        ShowEndScreen(false, LoseReason.Caught, torchWasOn);
    }
}
