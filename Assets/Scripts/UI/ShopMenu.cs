using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The 3x3 item panel opened by talking to the salesman (E at the counter). Lazily built on first
/// Open(), like the pause menu builds its panel on first pause.
/// </summary>
public class ShopMenu : MonoBehaviour
{
    private static readonly Color ButtonIdle = UiTheme.ButtonIdle;
    private static readonly Color ButtonHover = UiTheme.ButtonHover;
    private static readonly Color CardColor = UiTheme.CardBg;
    private static readonly Color Grey = UiTheme.Grey;
    private static readonly Color Amber = new Color(1f, 0.72f, 0.42f);
    private static readonly Color WalletColor = UiTheme.ShardCyan;

    private sealed class ShopCard
    {
        public ShopItemDef Def;
        public TextMeshProUGUI Status;
        public TextMeshProUGUI NameText;
        public TextMeshProUGUI BlurbText;
        public Button BuyButton;
        public TextMeshProUGUI BuyLabel;
        public Button[] SwatchButtons;
        public TextMeshProUGUI[] SwatchLabels;
    }

    private Transform _player;
    private PlayerHud _hud;
    private Flashlight _flashlight;

    private GameObject _panel;
    private TextMeshProUGUI _walletText;
    private readonly List<ShopCard> _cards = new List<ShopCard>();
    private int _closedFrame = -1;

    private AudioSource _tickSource;
    private AudioClip _tickClip;

    public bool IsOpen { get; private set; }

    /// <summary>True on the exact frame Close() ran. PauseMenu uses this to swallow the Esc that closed the shop panel.</summary>
    public bool ClosedThisFrame => Time.frameCount == _closedFrame;

    public void Configure(Transform player, PlayerHud hud, Flashlight flashlight)
    {
        _player = player;
        _hud = hud;
        _flashlight = flashlight;
    }

    private void OnDestroy()
    {
        UiModalStack.Pop(this);
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        UiModalStack.Push(this);

        if (_panel == null) BuildPanel();
        if (_panel != null)
        {
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
        }
        Refresh();

        PlayerLock.Freeze(_player, true);
        PlayerLock.SetCursorFree(true);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _closedFrame = Time.frameCount;
        UiModalStack.Pop(this);

        if (_panel != null) _panel.SetActive(false);
        if (_contractPanel != null) _contractPanel.SetActive(false);
        MenuFocus.Release();
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
    }

    // ---------------------------------------------------------------- F87: the contract board's panel

    private GameObject _contractPanel;
    private readonly TextMeshProUGUI[] _offerName = new TextMeshProUGUI[2];
    private readonly TextMeshProUGUI[] _offerText = new TextMeshProUGUI[2];
    private readonly TextMeshProUGUI[] _offerReward = new TextMeshProUGUI[2];
    private readonly Button[] _offerButton = new Button[2];
    private readonly TextMeshProUGUI[] _offerButtonLabel = new TextMeshProUGUI[2];
    private TextMeshProUGUI _contractStatus;
    private Button _declineButton;

    /// <summary>
    /// F87: E at the contract board. The same panel machinery as the shop (IsOpen / ClosedThisFrame, so PauseMenu and
    /// TouchControls already treat it as a menu): two offers for the next floor, TAKE one, DECLINE, or BACK.
    /// </summary>
    public void OpenContracts()
    {
        if (IsOpen) return;
        IsOpen = true;
        UiModalStack.Push(this);

        if (_contractPanel == null) BuildContractPanel();
        if (_contractPanel != null)
        {
            _contractPanel.SetActive(true);
            _contractPanel.transform.SetAsLastSibling();
        }
        RefreshContracts();

        PlayerLock.Freeze(_player, true);
        PlayerLock.SetCursorFree(true);
    }

    private void BuildContractPanel()
    {
        RuntimeUi.EnsureEventSystem();
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _contractPanel = RuntimeUi.CreatePanel(canvas.transform, "ContractScreen", new Color(0.02f, 0.02f, 0.03f, 0.94f));

        TextMeshProUGUI title = RuntimeUi.CreateText(_contractPanel.transform, "Title", L10n.T("ui.contracts.title"), 64f, Amber);
        RuntimeUi.Bind(title, "ui.contracts.title", 0.6f);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1400f, 90f));

        TextMeshProUGUI tip = RuntimeUi.CreateText(_contractPanel.transform, "Tip", L10n.T("ui.contracts.tip"), 26f, Grey);
        RuntimeUi.Bind(tip, "ui.contracts.tip", 0.65f);
        RuntimeUi.Place(tip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -160f), new Vector2(1500f, 44f));

        float[] xs = { -380f, 380f };
        for (int i = 0; i < 2; i++)
        {
            GameObject card = RuntimeUi.CreatePanel(_contractPanel.transform, "Offer" + i, CardColor);
            RuntimeUi.Place(card.GetComponent<Image>().rectTransform, new Vector2(0.5f, 0.5f), new Vector2(xs[i], 60f), new Vector2(680f, 380f));

            _offerName[i] = RuntimeUi.CreateText(card.transform, "Name", "", 40f, Color.white);
            _offerName[i].fontStyle = FontStyles.Bold;
            RuntimeUi.Place(_offerName[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(640f, 56f));

            _offerText[i] = RuntimeUi.CreateText(card.transform, "Text", "", 28f, Grey);
            RuntimeUi.Place(_offerText[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(600f, 110f));

            _offerReward[i] = RuntimeUi.CreateText(card.transform, "Reward", "", 32f, WalletColor);
            RuntimeUi.Place(_offerReward[i].rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(600f, 44f));

            int slot = i;
            Button take = RuntimeUi.CreateButton(card.transform, L10n.T("ui.contracts.take"), new Vector2(0f, 50f), new Vector2(260f, 62f), 30f, ButtonIdle, ButtonHover, Color.white);
            take.onClick.AddListener(() => TakeContract(slot));
            _offerButton[i] = take;
            _offerButtonLabel[i] = take.GetComponentInChildren<TextMeshProUGUI>();
            RuntimeUi.AutoFit(_offerButtonLabel[i], 0.65f);
            RuntimeUi.AutoFit(_offerName[i], 0.65f);
            RuntimeUi.AutoFit(_offerText[i], 0.7f);
            RuntimeUi.AutoFit(_offerReward[i], 0.7f);
        }

        _contractStatus = RuntimeUi.CreateText(_contractPanel.transform, "Status", "", 32f, Amber);
        RuntimeUi.Place(_contractStatus.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -190f), new Vector2(1500f, 50f));

        _declineButton = RuntimeUi.CreateButton(_contractPanel.transform, L10n.T("ui.contracts.decline"), new Vector2(-230f, 40f), new Vector2(380f, 70f), 34f, ButtonIdle, ButtonHover, Color.white);
        _declineButton.onClick.AddListener(DeclineContract);
        RuntimeUi.AutoFit(_declineButton.GetComponentInChildren<TextMeshProUGUI>(), 0.65f);
        RuntimeUi.AutoFit(_contractStatus, 0.7f);

        Button back = RuntimeUi.CreateButton(_contractPanel.transform, L10n.T("ui.common.back"), new Vector2(230f, 40f), new Vector2(380f, 70f), 34f, ButtonIdle, ButtonHover, Color.white);
        back.onClick.AddListener(Close);
        RuntimeUi.BindButton(back, "ui.common.back");
    }

    private void RefreshContracts()
    {
        if (_contractPanel == null) return;

        int[] offers = { ContractState.OfferA, ContractState.OfferB };
        int rewardFloor = GameFlow.ShopNextFloor;
        for (int i = 0; i < 2; i++)
        {
            bool has = offers[i] >= 0;
            _offerName[i].transform.parent.gameObject.SetActive(has);
            if (!has) continue;

            ContractDef def = ContractState.Def(offers[i]);
            _offerName[i].text = L10n.T("shop.contract.title", ("name", def.Name));
            _offerText[i].text = def.Text;
            _offerReward[i].text = L10n.T("ui.contracts.reward", ("amount", ContractState.Reward(offers[i], rewardFloor)), ("currency", PlayerWallet.CurrencyName));

            bool taken = ContractState.ActiveId == offers[i];
            _offerButtonLabel[i].text = L10n.T(taken ? "ui.contracts.taken" : "ui.contracts.take");
            _offerButton[i].interactable = !ContractState.HasActive;
        }

        if (!ContractState.HasOffers) _contractStatus.text = L10n.T("ui.contracts.empty");
        else if (ContractState.HasActive) _contractStatus.text = L10n.T("ui.contracts.took", ("name", ContractState.Def(ContractState.ActiveId).Name), ("floor", ContractState.ActiveFloor));
        else _contractStatus.text = "";

        TextMeshProUGUI declineLabel = _declineButton.GetComponentInChildren<TextMeshProUGUI>();
        if (declineLabel != null) declineLabel.text = L10n.T(ContractState.HasActive ? "ui.contracts.drop" : "ui.contracts.decline");
    }

    private void TakeContract(int slot)
    {
        int id = slot == 0 ? ContractState.OfferA : ContractState.OfferB;
        if (id < 0 || ContractState.HasActive) return;

        ContractState.Take(id, GameFlow.ShopNextFloor);
        WriteShopCheckpoint(); // like a purchase: the taken contract survives a resume
        if (_hud != null) _hud.ShowSubtitle(L10n.T("sub.shop.contract_taken"), 3f);
        Close();
    }

    private void DeclineContract()
    {
        if (ContractState.HasActive)
        {
            ContractState.ClearActive();
            WriteShopCheckpoint();
        }
        if (_hud != null) _hud.ShowSubtitle(L10n.T("sub.shop.contract_declined"), 2.5f);
        Close();
    }

    private void Update()
    {
        if (!IsOpen) return;

        // Held every frame: StarterAssetsInputs re-locks the cursor on every focus change.
        PlayerLock.SetCursorFree(true);

        // F119: a gamepad always has a selected button (the first BUY, else BACK; TAKE on the contract board).
        bool contracts = _contractPanel != null && _contractPanel.activeSelf;
        MenuFocus.Keep(contracts ? _contractPanel : _panel, contracts ? FirstUsable(_offerButton) : FirstUsableBuy());

        // F119: MenuCancel = Esc / pad B / touch Pause (the panel also has an on-screen BACK button).
        if (GameInput.Pressed(GameAction.MenuCancel)) Close();
    }

    // ---------------------------------------------------------------- panel

    private void BuildPanel()
    {
        RuntimeUi.EnsureEventSystem();
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _panel = RuntimeUi.CreatePanel(canvas.transform, "ShopScreen", new Color(0.02f, 0.02f, 0.03f, 0.94f));

        TextMeshProUGUI title = RuntimeUi.CreateText(_panel.transform, "Title", L10n.T("ui.shop.title"), 64f, Amber);
        RuntimeUi.Bind(title, "ui.shop.title", 0.6f);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1400f, 90f));

        _walletText = RuntimeUi.CreateText(_panel.transform, "Wallet", L10n.T("ui.shop.wallet", ("currency", PlayerWallet.CurrencyName), ("shards", 0)), 38f, WalletColor);
        RuntimeUi.Place(_walletText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(900f, 50f));

        TextMeshProUGUI tip = RuntimeUi.CreateText(_panel.transform, "Tip", L10n.T("ui.shop.tip"), 24f, Grey);
        RuntimeUi.Bind(tip, "ui.shop.tip", 0.65f);
        RuntimeUi.Place(tip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(900f, 40f));

        // F75: grew from 3x3 to 4x3 (12 items). Card width dropped from 540 to 420 so four columns still
        // fit inside the 1920-wide reference canvas with margin either side; every child offset inside
        // BuildCard/PlaceTopLeft was rescaled to match. Rows (ys) are untouched.
        // F122: up to 16 cards. With 12 or fewer on the shelf (always, until the Patient Record unlocks one) this is today's exact layout; 13-16 use a compact 4x4 grid.
        System.Collections.Generic.List<ShopItemDef> shelf = ShopCatalogue.Available();
        bool compact = shelf.Count > 12;
        float[] xs = { -660f, -220f, 220f, 660f };
        float[] ys = compact ? new[] { 175f, 7f, -161f, -329f } : new[] { 150f, -80f, -310f };
        for (int i = 0; i < shelf.Count && i < 16; i++)
        {
            BuildCard(shelf[i], new Vector2(xs[i % 4], ys[i / 4]), compact);
        }

        Button back = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.common.back"), new Vector2(0f, 40f), new Vector2(420f, 70f), 34f, ButtonIdle, ButtonHover, Color.white);
        back.onClick.AddListener(Close);
        RuntimeUi.BindButton(back, "ui.common.back");
    }

    private void BuildCard(ShopItemDef def, Vector2 position, bool compact = false)
    {
        float cardH = compact ? 160f : 210f; // F122: the compact 4x4 grid
        bool cosmetic = def.Kind == ItemKind.Cosmetic;
        float nameY = compact ? -8f : -16f;
        float blurbY = compact ? -42f : -52f, blurbH = compact ? (cosmetic ? 24f : 46f) : 60f, statusY = compact ? (cosmetic ? -70f : -94f) : -116f;
        float lowY = compact ? 12f : 18f;
        GameObject cardObj = RuntimeUi.CreatePanel(_panel.transform, def.Name + " Card", CardColor);
        RuntimeUi.Place(cardObj.GetComponent<Image>().rectTransform, new Vector2(0.5f, 0.5f), position, new Vector2(420f, cardH));

        TextMeshProUGUI nameText = RuntimeUi.CreateText(cardObj.transform, "Name", def.Name, 26f, Color.white);
        nameText.fontStyle = FontStyles.Bold;
        nameText.alignment = TextAlignmentOptions.TopLeft;
        PlaceTopLeft(nameText.rectTransform, new Vector2(16f, nameY), new Vector2(340f, 34f));
        RuntimeUi.AutoFit(nameText, 0.65f); // D14

        TextMeshProUGUI blurbText = RuntimeUi.CreateText(cardObj.transform, "Blurb", def.Blurb, 19f, Grey);
        blurbText.alignment = TextAlignmentOptions.TopLeft;
        PlaceTopLeft(blurbText.rectTransform, new Vector2(16f, blurbY), new Vector2(388f, blurbH));
        RuntimeUi.AutoFit(blurbText, 0.7f);

        TextMeshProUGUI statusText = RuntimeUi.CreateText(cardObj.transform, "Status", "", 19f, Grey);
        statusText.alignment = TextAlignmentOptions.TopLeft;
        PlaceTopLeft(statusText.rectTransform, new Vector2(16f, statusY), new Vector2(compact && def.Kind != ItemKind.Cosmetic ? 230f : 340f, 30f));
        RuntimeUi.AutoFit(statusText, 0.7f);

        ShopCard card = new ShopCard { Def = def, Status = statusText, NameText = nameText, BlurbText = blurbText };

        if (def.Kind == ItemKind.Cosmetic)
        {
            (string Name, Color Colour)[] palette = def.Id == ShopItem.TorchColour ? ShopCatalogue.TorchColours : ShopCatalogue.HudTints;
            card.SwatchButtons = new Button[3];
            card.SwatchLabels = new TextMeshProUGUI[3];
            float[] xs = { -132f, 0f, 132f };

            for (int i = 0; i < 3; i++)
            {
                // Palette index 0 is the default look and is never for sale - swatches map to indices 1..3.
                int paletteIndex = i + 1;
                Color swatch = palette[paletteIndex].Colour;
                Button button = RuntimeUi.CreateButton(cardObj.transform, ShopCatalogue.PaletteName(def.Id == ShopItem.TorchColour, paletteIndex),
                    new Vector2(xs[i], lowY), new Vector2(118f, 42f), 17f,
                    new Color(swatch.r, swatch.g, swatch.b, 0.35f), new Color(swatch.r, swatch.g, swatch.b, 0.6f), Color.white);

                int capturedIndex = paletteIndex;
                ShopItemDef capturedDef = def;
                button.onClick.AddListener(() => TrySelectCosmetic(capturedDef, capturedIndex));

                card.SwatchButtons[i] = button;
                card.SwatchLabels[i] = button.GetComponentInChildren<TextMeshProUGUI>();
                RuntimeUi.AutoFit(card.SwatchLabels[i], 0.6f);
            }
        }
        else
        {
            Button buy = RuntimeUi.CreateButton(cardObj.transform, L10n.T("ui.shop.buy"), new Vector2(130f, lowY), new Vector2(160f, 46f), 22f, ButtonIdle, ButtonHover, Color.white);
            ShopItemDef capturedDef = def;
            buy.onClick.AddListener(() => TryBuy(capturedDef));
            card.BuyButton = buy;
            card.BuyLabel = buy.GetComponentInChildren<TextMeshProUGUI>();
            RuntimeUi.AutoFit(card.BuyLabel, 0.65f);
        }

        _cards.Add(card);
    }

    // F119: the pad lands on the first BUY you can afford (the first TAKE on the contract board); with none, MenuFocus falls back to the first usable button.
    private Button FirstUsableBuy()
    {
        foreach (ShopCard c in _cards) if (c.BuyButton != null && c.BuyButton.gameObject.activeInHierarchy && c.BuyButton.interactable) return c.BuyButton;
        return null;
    }

    private static Button FirstUsable(Button[] buttons)
    {
        foreach (Button b in buttons) if (b != null && b.gameObject.activeInHierarchy && b.interactable) return b;
        return null;
    }

    private static void PlaceTopLeft(RectTransform rect, Vector2 offset, Vector2 size) =>
        UiTheme.PlaceTopLeft(rect, offset, size);

    /// <summary>Updates every card's text and interactability in place - never rebuilds them.</summary>
    private void Refresh()
    {
        if (_walletText != null) _walletText.text = L10n.T("ui.shop.wallet", ("currency", PlayerWallet.CurrencyName), ("shards", PlayerWallet.Shards));

        int shopFloor = GameFlow.ShopClearedFloor;
        foreach (ShopCard card in _cards)
        {
            ShopItemDef def = card.Def;
            int price = ShopCatalogue.PriceFor(def, shopFloor);
            // S9: the name and blurb follow the language (the panel is built once, this runs on every refresh).
            if (card.NameText != null) card.NameText.text = def.Name;
            if (card.BlurbText != null) card.BlurbText.text = def.Blurb;

            switch (def.Kind)
            {
                case ItemKind.Consumable:
                {
                    int count = PlayerInventory.Count(def.Id);
                    int cap = PlayerInventory.CapFor(def.Id);
                    card.Status.text = L10n.T("ui.shop.held_count", ("count", count), ("cap", cap));
                    bool canBuy = count < cap && PlayerWallet.Shards >= price;
                    SetBuyButton(card, canBuy, count >= cap ? L10n.T("ui.shop.held_max") : L10n.T("ui.shop.buy_price", ("price", price)));
                    break;
                }
                case ItemKind.SecondWind:
                {
                    bool held = PlayerInventory.Count(def.Id) > 0;
                    if (!CampaignRules.SecondWindAllowed)
                    {
                        // F122 NO SECOND CHANCES: the card is sworn off for the whole campaign.
                        card.Status.text = L10n.T("ui.shop.forsworn");
                        SetBuyButton(card, false, L10n.T("ui.shop.forsworn"));
                        break;
                    }
                    card.Status.text = L10n.T(held ? "ui.shop.held" : "ui.shop.not_held");
                    bool canBuy = !held && PlayerWallet.Shards >= price;
                    SetBuyButton(card, canBuy, held ? L10n.T("ui.shop.held_max") : L10n.T("ui.shop.buy_price", ("price", price)));
                    break;
                }
                case ItemKind.FloorModifier:
                {
                    bool pending = PlayerInventory.HasPending(def.Id);
                    card.Status.text = pending ? L10n.T("ui.shop.ready_for_floor", ("floor", GameFlow.ShopNextFloor)) : L10n.T("ui.shop.next_floor_only");
                    bool canBuy = !pending && PlayerWallet.Shards >= price;
                    SetBuyButton(card, canBuy, pending ? L10n.T("ui.shop.bought") : L10n.T("ui.shop.buy_price", ("price", price)));
                    break;
                }
                case ItemKind.Cosmetic:
                {
                    bool isTorch = def.Id == ShopItem.TorchColour;
                    int selected = isTorch ? PlayerInventory.TorchColourIndex : PlayerInventory.HudTintIndex;
                    (string Name, Color Colour)[] palette = isTorch ? ShopCatalogue.TorchColours : ShopCatalogue.HudTints;
                    card.Status.text = L10n.T("ui.shop.in_use_named", ("name", ShopCatalogue.PaletteName(isTorch, selected)));

                    for (int i = 0; i < card.SwatchButtons.Length; i++)
                    {
                        int paletteIndex = i + 1;
                        bool owned = isTorch ? PlayerInventory.OwnsTorchColour(paletteIndex) : PlayerInventory.OwnsHudTint(paletteIndex);
                        bool isSelected = selected == paletteIndex;
                        string label = isSelected ? L10n.T("ui.shop.in_use") : owned ? L10n.T("ui.shop.use") : L10n.T("ui.shop.swatch_price", ("name", ShopCatalogue.PaletteName(isTorch, paletteIndex)), ("price", price));
                        card.SwatchLabels[i].text = label;
                        card.SwatchButtons[i].interactable = !isSelected && (owned || PlayerWallet.Shards >= price);
                    }
                    break;
                }
            }
        }
    }

    private static void SetBuyButton(ShopCard card, bool canBuy, string label)
    {
        if (card.BuyButton == null) return;
        card.BuyButton.interactable = canBuy;
        if (card.BuyLabel != null) card.BuyLabel.text = label;
    }

    // ---------------------------------------------------------------- purchases

    private void TryBuy(ShopItemDef def)
    {
        int price = ShopCatalogue.PriceFor(def, GameFlow.ShopClearedFloor);

        switch (def.Kind)
        {
            case ItemKind.Consumable:
                if (!PlayerInventory.CanHoldMore(def.Id) || !PlayerWallet.TrySpend(price)) return;
                PlayerInventory.Grant(def.Id);
                break;
            case ItemKind.SecondWind:
                if (!CampaignRules.SecondWindAllowed) return; // F122
                if (PlayerInventory.Count(def.Id) > 0 || !PlayerWallet.TrySpend(price)) return;
                PlayerInventory.Grant(def.Id);
                break;
            case ItemKind.FloorModifier:
                if (PlayerInventory.HasPending(def.Id) || !PlayerWallet.TrySpend(price)) return;
                PlayerInventory.PendingModifiers.Add(def.Id);
                break;
            default:
                return; // cosmetics go through TrySelectCosmetic
        }

        WriteShopCheckpoint(); // F83: purchases are kept
        PlayTick();
        Refresh();
    }

    /// <summary>Buying an unowned swatch spends shards; selecting an already-owned one is free.</summary>
    private void TrySelectCosmetic(ShopItemDef def, int paletteIndex)
    {
        bool isTorch = def.Id == ShopItem.TorchColour;
        bool owned = isTorch ? PlayerInventory.OwnsTorchColour(paletteIndex) : PlayerInventory.OwnsHudTint(paletteIndex);

        if (!owned)
        {
            int price = ShopCatalogue.PriceFor(def, GameFlow.ShopClearedFloor);
            if (!PlayerWallet.TrySpend(price)) return;

            if (isTorch) PlayerInventory.GrantTorchColour(paletteIndex);
            else PlayerInventory.GrantHudTint(paletteIndex);
        }
        else
        {
            if (isTorch) PlayerInventory.SelectTorchColour(paletteIndex);
            else PlayerInventory.SelectHudTint(paletteIndex);
        }

        if (isTorch)
        {
            if (_flashlight != null) _flashlight.SetBeamColor(ShopCatalogue.TorchColours[PlayerInventory.TorchColourIndex].Colour);
        }
        else
        {
            Color tint = ShopCatalogue.HudTints[PlayerInventory.HudTintIndex].Colour;
            if (_hud != null) _hud.SetTint(tint);
            TensionDirector tension = FindAnyObjectByType<TensionDirector>();
            if (tension != null) tension.SetCalmColor(tint);
        }

        WriteShopCheckpoint(); // F83
        PlayTick();
        Refresh();
    }

    /// <summary>
    /// F83/F116: the save every purchase and contract change writes. In the old shop room (the fallback, GameFlow.IsInShop; AdvanceFloor has not run yet) it is a shop save of
    /// the cleared floor; in the hub's Commissary CurrentFloor is already the floor about to be played, so it is that floor's start save
    /// (CONTINUE then lands in the hub).
    /// </summary>
    private static void WriteShopCheckpoint()
    {
        SaveSystem.WriteCheckpoint(GameFlow.CurrentFloor, GameFlow.IsInShop);
    }

    private void PlayTick()
    {
        if (_tickSource == null)
        {
            _tickSource = gameObject.AddComponent<AudioSource>();
            _tickSource.playOnAwake = false;
            _tickSource.spatialBlend = 0f;
        }
        if (GameAudio.Play2D(GameAudio.Library.shopTick, 0.5f)) return; // F97
        if (_tickClip == null) _tickClip = BuildTickClip();
        _tickSource.PlayOneShot(_tickClip, 0.5f);
    }

    /// <summary>A short 1 kHz tick - the purchase confirmation.</summary>
    private static AudioClip BuildTickClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.06f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t / 0.02f);
            envelope *= Mathf.Clamp01(t / 0.002f);
            samples[i] = Mathf.Sin(2f * Mathf.PI * 1000f * t) * envelope;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.8f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("ShopTick", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
