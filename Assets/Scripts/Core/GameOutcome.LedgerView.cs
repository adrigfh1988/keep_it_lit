using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameOutcome
{
    private static readonly Color Amber = new Color(1f, 0.78f, 0.45f);
    private readonly List<TextMeshProUGUI> _ledgerTexts = new List<TextMeshProUGUI>();
    private bool _slipLedger;
    private bool _typing;
    private bool _skipTyping;
    private AudioSource _clickSource;
    private AudioClip _clickClip;

    /// <summary>The floor-clear ledger, built onto the blackout panel. CONTINUE sets _continuePressed.</summary>
    /// <param name="slip">F100: the readmission slip (hub flow, floors 1-4): a heading, a slip-voice subtitle, the lines typing out and two typed orders.</param>
    private Transform BuildLedger(Transform parent, Payout payout, bool slip)
    {
        // No prior pause/menu may have built one yet (mainMenu = false skips the title screen's call).
        RuntimeUi.EnsureEventSystem();
        _ledgerTexts.Clear();

        GameObject ledger = RuntimeUi.CreatePanel(parent, "Ledger", Color.clear);
        Transform root = ledger.transform;
        int cleared = GameFlow.CurrentFloor;

        TextMeshProUGUI title = RuntimeUi.CreateText(root, "Title", payout.Title, 84f, Green);
        RuntimeUi.AutoFit(title, 0.6f);
        title.fontStyle = FontStyles.Bold;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, slip ? -150f : -200f), new Vector2(1600f, 120f));

        if (slip)
        {
            // The number stays redacted until the Director's file (105) has been read. U+2587 may be missing from the font: '#' then (F123: one answer for every surface).
            string patient = WingNightmares.PatientMark();
            TextMeshProUGUI heading = RuntimeUi.CreateText(root, "Heading", L10n.T("ui.ledger.slip_heading", ("admission", 14 + cleared), ("patient", patient)), 30f, Amber);
            RuntimeUi.AutoFit(heading, 0.7f);
            heading.characterSpacing = 4f;
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(1400f, 44f));
            _ledgerTexts.Add(heading);
        }

        TextMeshProUGUI subtitle = RuntimeUi.CreateText(root, "Subtitle",
            slip ? WingNightmares.SlipSubtitle(cleared) : L10n.T("ui.ledger.subtitle"), 34f, Grey); // F123: the Clerk types the wing's own line
        RuntimeUi.AutoFit(subtitle, 0.7f);
        RuntimeUi.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, slip ? -285f : -280f), new Vector2(1400f, 50f));
        if (slip) _ledgerTexts.Add(subtitle);

        float y = slip ? -355f : -370f;
        float step = slip && payout.Lines.Count > 4 ? 46f : slip ? 52f : 58f;
        foreach (PayoutLine line in payout.Lines)
        {
            bool informational = line.Amount == 0 && line.Kind == PayoutKind.ShardsFound;
            Color lineColor = informational ? new Color(Grey.r, Grey.g, Grey.b, 0.6f) : Grey;

            TextMeshProUGUI label = RuntimeUi.CreateText(root, "Label", line.Label, 36f, lineColor);
            RuntimeUi.AutoFit(label, 0.7f);
            label.alignment = TextAlignmentOptions.Left;
            RuntimeUi.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(-100f, y), new Vector2(900f, 50f));

            string amountText = informational ? L10n.T("ui.ledger.already_yours") : line.Amount == 0 && line.Kind == PayoutKind.Contract ? L10n.T("ui.ledger.no_pay") : $"+{line.Amount}";
            TextMeshProUGUI amount = RuntimeUi.CreateText(root, "Amount", amountText, 36f, lineColor);
            amount.alignment = TextAlignmentOptions.Right;
            RuntimeUi.Place(amount.rectTransform, new Vector2(0.5f, 1f), new Vector2(420f, y), new Vector2(300f, 50f));
            if (slip) { _ledgerTexts.Add(label); _ledgerTexts.Add(amount); }

            y -= step;
        }

        GameObject rule = RuntimeUi.CreatePanel(root, "Rule", new Color(Grey.r, Grey.g, Grey.b, 0.4f));
        RuntimeUi.Place(rule.GetComponent<Image>().rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y - 6f), new Vector2(900f, 2f));
        y -= 50f;

        TextMeshProUGUI payoutText = RuntimeUi.CreateText(root, "Payout", L10n.T("ui.ledger.payout", ("amount", payout.Total)), 44f, Green);
        RuntimeUi.Place(payoutText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(900f, 60f));
        y -= 60f;

        Color shardColor = new Color(0.7f, 0.9f, 1f);
        TextMeshProUGUI wallet = RuntimeUi.CreateText(root, "Wallet", L10n.T("ui.ledger.wallet", ("currency", PlayerWallet.CurrencyName), ("shards", PlayerWallet.Shards)), 44f, shardColor);
        RuntimeUi.Place(wallet.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(900f, 60f));

        if (slip)
        {
            _ledgerTexts.Add(payoutText);
            _ledgerTexts.Add(wallet);
            // Typed orders for the floor about to be played: who to report to (or the missing key), and the shop when it is worth the walk.
            y -= 66f;
            List<string> orders = new List<string>();
            string order = HubMission.OrderFor(cleared + 1);
            if (!string.IsNullOrEmpty(order)) orders.Add(order);
            if (AsylumHub.ShopWorthAVisit(cleared)) orders.Add(L10n.T("ui.ledger.shop_held", ("shards", PlayerWallet.Shards), ("currency", PlayerWallet.CurrencyName)));
            foreach (string text in orders)
            {
                TextMeshProUGUI o = RuntimeUi.CreateText(root, "Order", text, 34f, Amber);
                RuntimeUi.AutoFit(o, 0.7f);
                o.characterSpacing = 3f;
                RuntimeUi.Place(o.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1500f, 46f));
                _ledgerTexts.Add(o);
                y -= 48f;
            }
        }

        Button continueButton = RuntimeUi.CreateButton(root, L10n.T("ui.common.continue"), new Vector2(0f, slip ? 100f : 160f), new Vector2(560f, 86f), 42f,
            new Color(0.08f, 0.32f, 0.24f, 0.95f), new Color(0.15f, 0.55f, 0.42f, 1f), Color.white);
        continueButton.onClick.AddListener(OnLedgerContinue);
        RuntimeUi.BindButton(continueButton, "ui.common.continue");

        AddLedgerExtras(root, slip); // F122: campaign tag, record note, grade stamp
        return root;
    }

    /// <summary>CONTINUE / E / tap: while the slip is still typing the first press completes it; the next one continues.</summary>
    private void OnLedgerContinue()
    {
        if (_typing) _skipTyping = true;
        else _continuePressed = true;
    }

    private void PollLedgerInput()
    {
        if (!_slipLedger || !_showingLedger) return;
        // F119: continue = Interact (E / pad X / touch) or MenuSubmit (Enter, Space / pad A); MenuTap = left mouse or a touchscreen press (read same-frame, as before).
        bool press = GameInput.Pressed(GameAction.Interact) || GameInput.Pressed(GameAction.MenuSubmit);
        if (press) { OnLedgerContinue(); return; }
        // A click or a tap anywhere finishes the typing (the CONTINUE button itself acts on release, so it never double-fires with this).
        bool tap = GameInput.Pressed(GameAction.MenuTap);
        if (tap && _typing) _skipTyping = true;
    }

    private IEnumerator TypeLedger()
    {
        _typing = true;
        _skipTyping = false;
        foreach (TextMeshProUGUI t in _ledgerTexts) { if (t != null) t.maxVisibleCharacters = 0; }
        const float charsPerSecond = 110f;
        foreach (TextMeshProUGUI t in _ledgerTexts)
        {
            if (t == null) continue;
            t.ForceMeshUpdate();
            int count = t.textInfo.characterCount;
            float shown = 0f;
            int clicked = 0;
            while (!_skipTyping && shown < count)
            {
                shown += charsPerSecond * Time.unscaledDeltaTime;
                int visible = Mathf.Min(count, (int)shown);
                t.maxVisibleCharacters = visible;
                if (visible / 3 > clicked) { clicked = visible / 3; Click(); }
                yield return null;
            }
            t.maxVisibleCharacters = 99999;
        }
        foreach (TextMeshProUGUI t in _ledgerTexts) { if (t != null) t.maxVisibleCharacters = 99999; }
        _typing = false;
    }

    /// <summary>One key tap of the slip: the library's typewriter slot, else a short generated click.</summary>
    private void Click()
    {
        if (_clickSource == null)
        {
            _clickSource = gameObject.AddComponent<AudioSource>();
            _clickSource.playOnAwake = false;
            _clickSource.spatialBlend = 0f;
            GameSettings.RouteEffect(_clickSource);
        }
        _clickSource.pitch = 1f + Random.Range(-0.12f, 0.12f);
        AudioSlot slot = GameAudio.Library.typewriter;
        if (slot.Has)
        {
            _clickSource.PlayOneShot(slot.Pick(), 0.35f * slot.volume);
            return;
        }
        if (_clickClip == null) _clickClip = DoorAudio.BuildClunkClip(0.03f, 1500f, 0.12f);
        _clickSource.PlayOneShot(_clickClip, 0.25f);
    }
}
