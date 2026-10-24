using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameOutcome
{
    /// <summary>Fades a full-screen Image between two alpha values. If alsoFade is given, its CanvasGroup fades out alongside it.</summary>
    private static IEnumerator Fade(Image image, float from, float to, float seconds, Transform alsoFade = null)
    {
        return ScreenFader.Fade(image, from, to, seconds, alsoFade, unscaled: false);
    }

    private void ShowEndScreen(bool won, LoseReason reason, bool torchWasOn, string titleOverride = null, string bodyOverride = null)
    {
        RuntimeUi.EnsureEventSystem();

        // F107 B2: a descent has its own end screen (the readmission slip); no shards are paid, no maze is retried.
        if (GameFlow.IsDescent && !won)
        {
            ShowDescentEndScreen();
            return;
        }

        if (!won)
        {
            // F122: a lost story run still tells the record what it met and banks its shards (consolation was banked just before).
            NoteMetThisLevel();
            PatientRecord.CommitShards();
        }

        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        Transform root = RuntimeUi.CreatePanel(canvas.transform, "EndScreen", new Color(0f, 0f, 0f, 0.85f)).transform;
        _endRoot = root.gameObject;

        string title;
        string subtitle;
        Color titleColor = _endingTitleColorOverride ?? (won ? Green : Red);
        _endingTitleColorOverride = null;

        if (won)
        {
            bool finalFloor = GameFlow.CurrentFloor >= FloorProfile.FinalFloor;
            title = finalFloor ? L10n.T("ui.end.won_final") : L10n.T("ui.end.floor_cleared", ("floor", GameFlow.CurrentFloor));
            subtitle = finalFloor ? L10n.T("ui.end.won_final_body") : L10n.T("ui.end.floor_cleared_body");
            if (titleOverride != null) title = titleOverride;
            if (bodyOverride != null) subtitle = bodyOverride;
        }
        else if (reason == LoseReason.Caught)
        {
            title = L10n.T("ui.end.caught");

            PlayerStealthState stealth = _player != null ? _player.GetComponent<PlayerStealthState>() : null;
            if (stealth != null && stealth.LastExposed)
            {
                subtitle = L10n.T("ui.end.caught_hidden");
            }
            else if (_hubDeath)
            {
                subtitle = L10n.T("ui.end.caught_hub"); // F121: it followed you up
            }
            else if (DescentRun.Cause == DescentCause.Entity && DescentRun.CauseEntity == EntityKind.Elias)
            {
                subtitle = L10n.T("ui.end.caught_elias");
            }
            else if (_flashlight != null && _flashlight.IsDropped)
            {
                subtitle = L10n.T("ui.end.caught_dropped"); // F120: killed while the torch lay on the floor
            }
            else
            {
                subtitle = torchWasOn ? L10n.T("ui.end.caught_torch") : L10n.T("ui.end.caught_silent");
            }
        }
        else
        {
            title = L10n.T("ui.end.out_of_time");
            int metres = 0;
            if (_escape != null && _player != null)
            {
                Vector3 toHatch = _escape.HatchPosition - _player.position;
                toHatch.y = 0f;
                metres = Mathf.RoundToInt(toHatch.magnitude);
            }

            subtitle = L10n.T("ui.end.out_of_time_body", ("stars", _collected), ("metres", metres));
        }

        // F123: a loss on a story floor adds that wing's own line under the (diagnostic) cause line. Not for a hub death or the finale's bodyOverride.
        string wingLine = !won && !_hubDeath && bodyOverride == null ? WingNightmares.LossLine(GameFlow.CurrentFloor) : null;
        if (wingLine != null) subtitle += "\n" + wingLine;

        TextMeshProUGUI titleText = RuntimeUi.CreateText(root, "Title", title, 96f, titleColor);
        titleText.fontStyle = FontStyles.Bold;
        RuntimeUi.AutoFit(titleText, 0.6f);
        RuntimeUi.Place(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1600f, 140f));

        bool longBody = bodyOverride != null || wingLine != null;
        TextMeshProUGUI subText = RuntimeUi.CreateText(root, "Subtitle", subtitle, longBody ? 32f : 36f, Grey);
        if (longBody) subText.textWrappingMode = TextWrappingModes.Normal;
        RuntimeUi.AutoFit(subText, 0.7f);
        RuntimeUi.Place(subText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, longBody ? -385f : -300f), new Vector2(longBody ? 1300f : 1600f, longBody ? 200f : 60f));

        string stats = _hubDeath
            ? L10n.T("ui.end.stats_hub", ("floor", GameFlow.CurrentFloor)) // F121: no stars or time to report from the hub
            : L10n.T("ui.end.stats", ("floor", GameFlow.CurrentFloor), ("collected", _collected), ("total", _total), ("time", MazeEscape.FormatTime(_runTime)), ("seed", _seed));
        TextMeshProUGUI statsText = RuntimeUi.CreateText(root, "Stats", stats, 32f, Grey);
        statsText.characterSpacing = 4f;
        RuntimeUi.AutoFit(statsText, 0.65f);
        RuntimeUi.Place(statsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, longBody ? -540f : -380f), new Vector2(1600f, 50f));

        // Second line: what this attempt paid into the campaign wallet.
        bool isFinalWin = won && GameFlow.CurrentFloor >= FloorProfile.FinalFloor;
        string stats2 = null;
        Color shardColor = new Color(0.7f, 0.9f, 1f);
        if (!won)
        {
            int amount = _consolationPayout != null ? _consolationPayout.Total : 0;
            stats2 = amount > 0
                ? L10n.T("ui.end.consolation", ("amount", amount), ("currency", PlayerWallet.CurrencyName), ("shards", PlayerWallet.Shards))
                : L10n.T("ui.end.no_consolation", ("currency", PlayerWallet.CurrencyName), ("shards", PlayerWallet.Shards));
        }
        else if (isFinalWin)
        {
            int amount = _floorPayout != null ? _floorPayout.Total : 0;
            stats2 = L10n.T("ui.end.final_payout", ("amount", amount), ("total", PlayerWallet.TotalEarned));
        }

        if (stats2 != null)
        {
            TextMeshProUGUI stats2Text = RuntimeUi.CreateText(root, "Stats2", stats2, 32f, shardColor);
            stats2Text.characterSpacing = 4f;
            RuntimeUi.AutoFit(stats2Text, 0.65f);
            RuntimeUi.Place(stats2Text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, longBody ? -590f : -430f), new Vector2(1600f, 50f));
        }
        AddEndScreenExtras(root, won, isFinalWin, longBody); // F122: campaign tag, record note, and on a won final floor the grade stamp + GRADES line

        // Wider than the title screen's buttons: "TRY THIS MAZE AGAIN" does not fit in 420 at 40 pt
        Vector2 size = new Vector2(560f, 86f);

        // Winning below the final floor leads somewhere: the next, harder floor
        if (won && GameFlow.CurrentFloor < FloorProfile.FinalFloor)
        {
            Button next = RuntimeUi.CreateButton(root, L10n.T("ui.end.next_floor"),
                new Vector2(0f, 440f), size, 42f, new Color(0.08f, 0.32f, 0.24f, 0.95f), new Color(0.15f, 0.55f, 0.42f, 1f), Color.white);
            next.onClick.AddListener(GameFlow.NextFloor);
            RuntimeUi.BindButton(next, "ui.end.next_floor");
        }

        Button again = RuntimeUi.CreateButton(root, L10n.T(won ? "ui.end.replay" : "ui.end.retry"),
            new Vector2(0f, 330f), size, 38f, ButtonIdle, ButtonHover, Color.white);
        again.onClick.AddListener(() => { if (_hubDeath) RetryAfterHubDeath(true); else GameFlow.Restart(true, _seed); });
        RuntimeUi.BindButton(again, won ? "ui.end.replay" : "ui.end.retry");

        Button fresh = RuntimeUi.CreateButton(root, L10n.T("ui.end.new_maze"),
            new Vector2(0f, 220f), size, 38f, ButtonIdle, ButtonHover, Color.white);
        fresh.onClick.AddListener(() => { if (_hubDeath) RetryAfterHubDeath(false); else GameFlow.Restart(false, _seed); });
        RuntimeUi.BindButton(fresh, "ui.end.new_maze");

        Button menu = RuntimeUi.CreateButton(root, L10n.T("ui.end.main_menu"),
            new Vector2(0f, 110f), size, 38f, ButtonIdle, ButtonHover, Color.white);
        menu.onClick.AddListener(GameFlow.ReturnToMenu);
        RuntimeUi.BindButton(menu, "ui.end.main_menu");

        if (!won && reason == LoseReason.Caught) TryShowReplay(root, false); // F120
        PlayerLock.SetCursorFree(true);
    }
}
