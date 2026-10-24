using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class PlayerHud
{
    private static readonly Color SecondWindColor = new Color(0.25f, 1f, 0.7f);
    private static readonly Color BatterySlotColor = new Color(1.00f, 0.78f, 0.28f);
    private static readonly Color CompassSlotColor = new Color(0.38f, 0.88f, 1.00f);
    private static readonly Color PlanSlotColor = new Color(0.55f, 0.95f, 0.68f);
    // F74: throwable carry count, stacked above the two consumable slots.
    private static readonly Color ThrowColour = new Color(0.95f, 0.80f, 0.42f);

    private struct ItemSlotCard
    {
        public GameObject Root;
        public RectTransform Rect;
        public Image Background;
        public Image Accent;
        public Image KeyCapBg;
        public TextMeshProUGUI KeyText;
        public TextMeshProUGUI NameText;
        public Image CountBg;
        public TextMeshProUGUI CountText;
        public Color AccentColor;
    }

    private ItemSlotCard _slot1Card;
    private ItemSlotCard _slot2Card;
    private ItemSlotCard _throwCard;
    private ItemSlotCard _planCard;
    private ItemSlotCard _secondWindCard;

    private TextMeshProUGUI _slot1Text;
    private TextMeshProUGUI _slot2Text;
    private TextMeshProUGUI _secondWindText;
    private float _slot1Punch;
    private float _slot2Punch;
    // F110: the Floor Plan line (M, hold), stacked above the throw line.
    private TextMeshProUGUI _planText;
    private float _planPunch;
    private string _slotKey1, _slotKey2, _slotKeyThrow;
    private string _slotKeyPlan;
    private string _slotKeySecondWind;

    private TextMeshProUGUI _throwText;
    private ThrowController _throwController;

    private static string ShardLabel(int shards) => L10n.T("hud.shards", ("currency", PlayerWallet.CurrencyName), ("shards", shards));

    private static void PlaceBottomRight(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        UiTheme.PlaceBottomRight(rect, anchoredPosition, size);
    }

    private static ItemSlotCard BuildItemSlotCard(Transform parent, string name, Color accentColor, Vector2 anchoredPos)
    {
        ItemSlotCard card = new ItemSlotCard { AccentColor = accentColor };

        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        RectTransform rect = root.AddComponent<RectTransform>();
        PlaceBottomRight(rect, anchoredPos, new Vector2(296f, 38f));

        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.04f, 0.055f, 0.82f);
        bg.raycastTarget = false;

        // Top highlight hairline
        GameObject topEdge = new GameObject("TopEdge");
        topEdge.transform.SetParent(root.transform, false);
        RectTransform topRect = topEdge.AddComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0f, 1f);
        topRect.anchorMax = new Vector2(1f, 1f);
        topRect.pivot = new Vector2(0.5f, 1f);
        topRect.anchoredPosition = Vector2.zero;
        topRect.sizeDelta = new Vector2(0f, 1f);
        Image topImg = topEdge.AddComponent<Image>();
        topImg.color = new Color(1f, 1f, 1f, 0.08f);
        topImg.raycastTarget = false;

        // Left color-coded accent bar
        GameObject accentObj = new GameObject("Accent");
        accentObj.transform.SetParent(root.transform, false);
        RectTransform accentRect = accentObj.AddComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(4f, 0f);
        Image accentImg = accentObj.AddComponent<Image>();
        accentImg.color = accentColor;
        accentImg.raycastTarget = false;

        // Boxed [Key] cap on the left
        GameObject keyCapObj = new GameObject("KeyCap");
        keyCapObj.transform.SetParent(root.transform, false);
        RectTransform keyCapRect = keyCapObj.AddComponent<RectTransform>();
        keyCapRect.anchorMin = new Vector2(0f, 0.5f);
        keyCapRect.anchorMax = new Vector2(0f, 0.5f);
        keyCapRect.pivot = new Vector2(0f, 0.5f);
        keyCapRect.anchoredPosition = new Vector2(12f, 0f);
        keyCapRect.sizeDelta = new Vector2(44f, 26f);
        Image keyCapBg = keyCapObj.AddComponent<Image>();
        keyCapBg.color = new Color(0.13f, 0.16f, 0.22f, 0.95f);
        keyCapBg.raycastTarget = false;

        TextMeshProUGUI keyText = RuntimeUi.CreateText(keyCapObj.transform, "Key", "", 15f, new Color(0.98f, 0.94f, 0.82f));
        keyText.alignment = TextAlignmentOptions.Center;
        keyText.characterSpacing = 1f;
        RectTransform keyTextRect = keyText.rectTransform;
        keyTextRect.anchorMin = Vector2.zero;
        keyTextRect.anchorMax = Vector2.one;
        keyTextRect.offsetMin = new Vector2(3f, 1f);
        keyTextRect.offsetMax = new Vector2(-3f, -1f);
        RuntimeUi.AutoFit(keyText, 0.55f);

        // Item name in the middle
        TextMeshProUGUI nameText = RuntimeUi.CreateText(root.transform, "Label", "", 19f, Color.white);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
        nameText.characterSpacing = 2.5f;
        RectTransform nameRect = nameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.offsetMin = new Vector2(64f, 2f);
        nameRect.offsetMax = new Vector2(-60f, -2f);
        RuntimeUi.AutoFit(nameText, 0.58f);

        // Bold ×Count badge on the right
        GameObject countObj = new GameObject("CountBadge");
        countObj.transform.SetParent(root.transform, false);
        RectTransform countRect = countObj.AddComponent<RectTransform>();
        countRect.anchorMin = new Vector2(1f, 0.5f);
        countRect.anchorMax = new Vector2(1f, 0.5f);
        countRect.pivot = new Vector2(1f, 0.5f);
        countRect.anchoredPosition = new Vector2(-8f, 0f);
        countRect.sizeDelta = new Vector2(46f, 26f);
        Image countBg = countObj.AddComponent<Image>();
        countBg.color = new Color(accentColor.r * 0.22f, accentColor.g * 0.22f, accentColor.b * 0.22f, 0.85f);
        countBg.raycastTarget = false;

        TextMeshProUGUI countText = RuntimeUi.CreateText(countObj.transform, "Count", "×0", 19f, accentColor);
        countText.alignment = TextAlignmentOptions.Center;
        countText.characterSpacing = 1f;
        RectTransform countTextRect = countText.rectTransform;
        countTextRect.anchorMin = Vector2.zero;
        countTextRect.anchorMax = Vector2.one;
        countTextRect.offsetMin = new Vector2(2f, 1f);
        countTextRect.offsetMax = new Vector2(-2f, -1f);
        RuntimeUi.AutoFit(countText, 0.6f);

        card.Root = root;
        card.Rect = rect;
        card.Background = bg;
        card.Accent = accentImg;
        card.KeyCapBg = keyCapBg;
        card.KeyText = keyText;
        card.NameText = nameText;
        card.CountBg = countBg;
        card.CountText = countText;
        return card;
    }

    private void UpdateItemSlots()
    {
        if (_slot1Card.Root == null) return;

        // F86: the Intake hides the consumable slots like the shop does, but the THROW line stays - it is taught there.
        bool inShop = GameFlow.IsInSafeRoom;
        int secondWindCount = PlayerInventory.Count(ShopItem.SecondWind);
        bool secondWindHeld = secondWindCount > 0;

        if (GameFlow.IsInIntake)
        {
            _slot1Card.Root.SetActive(false);
            _slot2Card.Root.SetActive(false);
            if (_planCard.Root != null) _planCard.Root.SetActive(false);
            if (_secondWindCard.Root != null) _secondWindCard.Root.SetActive(false);
            if (_throwCard.Root != null)
            {
                int held = _throwController != null ? _throwController.Carried : 0;
                _throwCard.Root.SetActive(held > 0);
                if (held > 0)
                {
                    SetSlotCard(ref _throwCard, ref _slotKeyThrow, "hud.slot.throw", GameAction.Throw, held);
                    ApplySlotCard(ref _throwCard, true, 0f);
                    _throwCard.Rect.anchoredPosition = new Vector2(-24f, 20f);
                }
            }
            return;
        }

        if (inShop)
        {
            _slot1Card.Root.SetActive(false);
            _slot2Card.Root.SetActive(false);
            if (_throwCard.Root != null) _throwCard.Root.SetActive(false);
            if (_planCard.Root != null) _planCard.Root.SetActive(false);
            if (_secondWindCard.Root != null) _secondWindCard.Root.SetActive(false);
            return;
        }

        _slot1Punch = Mathf.MoveTowards(_slot1Punch, 0f, Time.unscaledDeltaTime / 0.25f);
        _slot2Punch = Mathf.MoveTowards(_slot2Punch, 0f, Time.unscaledDeltaTime / 0.25f);
        _planPunch = Mathf.MoveTowards(_planPunch, 0f, Time.unscaledDeltaTime / 0.25f);

        float stackY = 20f;
        const float stepY = 44f;

        // 1. Star Compass (Slot 2)
        int compassCount = PlayerInventory.Count(ShopItem.StarCompass);
        _slot2Card.Root.SetActive(true);
        _slot2Card.Rect.anchoredPosition = new Vector2(-24f, stackY);
        SetSlotCard(ref _slot2Card, ref _slotKey2, "hud.slot.compass", GameAction.Item2, compassCount);
        ApplySlotCard(ref _slot2Card, compassCount > 0, _slot2Punch);
        stackY += stepY;

        // 2. Spare Battery (Slot 1)
        int batteryCount = PlayerInventory.Count(ShopItem.SpareBattery);
        _slot1Card.Root.SetActive(true);
        _slot1Card.Rect.anchoredPosition = new Vector2(-24f, stackY);
        SetSlotCard(ref _slot1Card, ref _slotKey1, "hud.slot.battery", GameAction.Item1, batteryCount);
        ApplySlotCard(ref _slot1Card, batteryCount > 0, _slot1Punch);
        stackY += stepY;

        // 3. Throwables (Bottle / Can)
        if (_throwCard.Root != null)
        {
            int carried = _throwController != null ? _throwController.Carried : 0;
            bool showThrow = carried > 0 && !GameFlow.IsInShop && !GameFlow.IsInHub;
            _throwCard.Root.SetActive(showThrow);
            if (showThrow)
            {
                _throwCard.Rect.anchoredPosition = new Vector2(-24f, stackY);
                SetSlotCard(ref _throwCard, ref _slotKeyThrow, "hud.slot.throw", GameAction.Throw, carried);
                ApplySlotCard(ref _throwCard, true, 0f);
                stackY += stepY;
            }
        }

        // 4. Floor Plan
        if (_planCard.Root != null)
        {
            int plans = PlayerInventory.Count(ShopItem.FloorPlan);
            _planCard.Root.SetActive(plans > 0);
            if (plans > 0)
            {
                _planCard.Rect.anchoredPosition = new Vector2(-24f, stackY);
                SetSlotCard(ref _planCard, ref _slotKeyPlan, "hud.slot.floor_plan", GameAction.Map, plans);
                ApplySlotCard(ref _planCard, true, _planPunch);
                stackY += stepY;
            }
        }

        // 5. Second Wind
        if (_secondWindCard.Root != null)
        {
            _secondWindCard.Root.SetActive(secondWindHeld);
            if (secondWindHeld)
            {
                _secondWindCard.Rect.anchoredPosition = new Vector2(-24f, stackY);
                SetPassiveSlotCard(ref _secondWindCard, ref _slotKeySecondWind, "hud.second_wind", "✦", secondWindCount);
                ApplySlotCard(ref _secondWindCard, true, 0f);
            }
        }
    }

    /// <summary>S9: populates key cap, item name, and ×count badge only when the key mode, count or language changed.</summary>
    private static void SetSlotCard(ref ItemSlotCard card, ref string cache, string key, GameAction action, int count)
    {
        string chip = InputGlyphs.Key(action, L10n.T("ui.key.tap"));
        string id = key + "|" + chip + "|" + count;
        if (cache == id) return;
        cache = id;

        card.KeyText.text = chip;
        card.CountText.text = "×" + count;

        string raw = L10n.T(key, ("key", ""), ("count", count)).Trim();
        string suffix = "x" + count;
        if (raw.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
        {
            raw = raw.Substring(0, raw.Length - suffix.Length).TrimEnd();
        }
        card.NameText.text = raw;
    }

    private static void SetPassiveSlotCard(ref ItemSlotCard card, ref string cache, string key, string badgeGlyph, int count)
    {
        string id = key + "|" + badgeGlyph + "|" + count;
        if (cache == id) return;
        cache = id;

        card.KeyText.text = badgeGlyph;
        card.NameText.text = L10n.T(key);
        card.CountText.text = "×" + count;
    }

    private static void ApplySlotCard(ref ItemSlotCard card, bool has, float punch)
    {
        float scale = 1f + 0.18f * punch;
        card.Rect.localScale = new Vector3(scale, scale, 1f);

        Color accent = card.AccentColor;
        card.Background.color = Color.Lerp(
            has ? new Color(0.03f, 0.04f, 0.055f, 0.84f) : new Color(0.03f, 0.04f, 0.055f, 0.36f),
            new Color(0.22f, 0.25f, 0.32f, 0.92f),
            punch);

        card.Accent.color = Color.Lerp(
            has ? accent : new Color(accent.r * 0.45f, accent.g * 0.45f, accent.b * 0.45f, 0.35f),
            Color.white,
            punch);

        card.KeyCapBg.color = has
            ? new Color(0.13f, 0.16f, 0.22f, 0.95f)
            : new Color(0.09f, 0.10f, 0.14f, 0.42f);

        card.KeyText.color = Color.Lerp(
            has ? new Color(0.98f, 0.94f, 0.82f, 1f) : new Color(0.65f, 0.65f, 0.68f, 0.42f),
            Color.white,
            punch);

        card.NameText.color = Color.Lerp(
            has ? new Color(0.94f, 0.95f, 0.98f, 1f) : new Color(0.62f, 0.64f, 0.68f, 0.38f),
            Color.white,
            punch);

        card.CountBg.color = has
            ? new Color(accent.r * 0.24f, accent.g * 0.24f, accent.b * 0.24f, 0.88f)
            : new Color(0.08f, 0.09f, 0.12f, 0.35f);

        card.CountText.color = Color.Lerp(
            has ? accent : new Color(0.55f, 0.57f, 0.62f, 0.38f),
            Color.white,
            punch);
    }

    private void UpdateShardCounter()
    {
        if (_shardText == null) return;

        // Hide shards in safe rooms, Intake, and DESCENT mode (where shards are always 0 and unused).
        if (GameFlow.IsInSafeRoom || GameFlow.IsDescent || GameFlow.IsInIntake)
        {
            _shardText.gameObject.SetActive(false);
            return;
        }
        _shardText.gameObject.SetActive(true);

        int shards = PlayerWallet.Shards;
        if (shards != _shardShown)
        {
            _shardShown = shards;
            _shardText.text = ShardLabel(_shardShown);
            _shardPunch = 1f;
        }

        _shardPunch = Mathf.MoveTowards(_shardPunch, 0f, Time.unscaledDeltaTime / 0.25f);
        float scale = 1f + 0.25f * _shardPunch;
        _shardText.rectTransform.localScale = new Vector3(scale, scale, 1f);
    }
}
