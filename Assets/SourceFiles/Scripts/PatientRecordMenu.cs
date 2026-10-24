using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// F122 D14: the PATIENT RECORD screen - tabs RECORD (counts, endings, best Oath per ending, best grade per floor), UNLOCKS (every milestone with its progress;
/// NEW marks are cleared on view), CODEX (the six things the player has met; locked entries read ???) and, only when opened from the pause menu during a story
/// campaign, ASSISTS (the assist rows, effective at the next maze build, D15). Opened from the title (RECORD) and from the pause menu (RECORD, once the codex is unlocked).
/// Modeled on <see cref="SettingsMenu"/>: one shared instance, built lazily, Esc / pad B closes only this panel (<see cref="BlocksBack"/>), Q / E or LB / RB switch tabs,
/// every row is a Selectable and <see cref="MenuFocus"/> keeps the gamepad on one.
/// </summary>
public class PatientRecordMenu : MonoBehaviour
{
    public enum Tab { Record, Unlocks, Codex, Assists }

    private static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    private static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);
    private static readonly Color Accent = new Color(0.86f, 0.12f, 0.09f);
    private static readonly Color Dim = new Color(0.78f, 0.78f, 0.8f);
    private static readonly Color Grey = new Color(0.55f, 0.55f, 0.58f);
    private static readonly Color Amber = new Color(1f, 0.78f, 0.45f);
    private static readonly Color Green = new Color(0.25f, 1f, 0.7f);

    public static PatientRecordMenu Active { get; private set; }

    /// <summary>True while up, and for the rest of the frame it closed in: the Esc / B that closes it must not also pause, unpause or act on the title.</summary>
    public static bool BlocksBack => Active != null && (Active._open || Active._closedFrame == Time.frameCount);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = null;

    /// <summary>Opens the shared panel (creating it if needed). <paramref name="fromPause"/> adds the ASSISTS tab during a story campaign.</summary>
    public static void Show(Tab tab, bool fromPause)
    {
        if (Active == null)
        {
            GameObject holder = new GameObject("PatientRecordMenu");
            holder.AddComponent<PatientRecordMenu>();
        }
        Active.Open(tab, fromPause);
    }

    private GameObject _panel;
    private RectTransform _rows;
    private readonly List<Button> _tabButtons = new List<Button>();
    private readonly List<Tab> _tabs = new List<Tab>();
    private Tab _tab;
    private bool _open;
    private bool _fromPause;
    private bool _stale;
    private int _closedFrame = -1;
    private Selectable _first;
    private TextMeshProUGUI _codexTitle, _codexBody;
    private CodexEntry _codexSelected = CodexEntry.Hunter;

    public bool IsOpen => _open;
    public Tab CurrentTab => _tab;
    public RectTransform Rows => _rows;
    public GameObject Panel => _panel;
    public IReadOnlyList<Tab> Tabs => _tabs;

    private void Awake()
    {
        Active = this;
        L10n.Changed += OnLanguageChanged;
    }

    private void OnLanguageChanged() => _stale = true;

    private void OnDestroy()
    {
        L10n.Changed -= OnLanguageChanged;
        if (Active == this) Active = null;
    }

    // ---------------------------------------------------------------- open / close

    private void Open(Tab tab, bool fromPause)
    {
        RuntimeUi.EnsureEventSystem();
        _fromPause = fromPause;
        DestroyPanel();
        BuildPanel();
        _open = true;
        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();
        PlayerLock.SetCursorFree(true);
        ShowTab(_tabs.Contains(tab) ? tab : _tabs[0]);
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        _closedFrame = Time.frameCount;
        if (_panel != null) _panel.SetActive(false);
        MenuFocus.Release();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        PatientRecord.MarkUnlocksSeen();
    }

    private void Update()
    {
        if (!_open) return;
        PlayerLock.SetCursorFree(true); // every frame: focus changes re-lock it
        if (_stale)
        {
            _stale = false;
            Tab keep = _tab;
            DestroyPanel();
            BuildPanel();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            ShowTab(_tabs.Contains(keep) ? keep : _tabs[0]);
        }

        bool back = GameInput.Pressed(GameAction.MenuCancel);
        int step = 0;
        if (GameInput.Pressed(GameAction.MenuTabPrev)) step = -1;
        if (GameInput.Pressed(GameAction.MenuTabNext)) step = 1;
        if (back) { Close(); return; }
        MenuFocus.Keep(_panel, _first);
        if (step != 0) ShowTab(_tabs[(_tabs.IndexOf(_tab) + step + _tabs.Count) % _tabs.Count]);
    }

    // ---------------------------------------------------------------- panel

    private static string S(string suffix) => L10n.T("ui.record." + suffix);
    private static string[] OffOn() => new[] { L10n.T("ui.settings.opt.off"), L10n.T("ui.settings.opt.on") };

    private void DestroyPanel()
    {
        if (_panel != null) Destroy(_panel);
        _panel = null;
        _rows = null;
        _tabs.Clear();
        _tabButtons.Clear();
        _first = null;
    }

    private void BuildPanel()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        _panel = RuntimeUi.CreatePanel(canvas.transform, "PatientRecordScreen", new Color(0.02f, 0.02f, 0.03f, 1f));

        TextMeshProUGUI title = RuntimeUi.CreateText(_panel.transform, "Title", S("title"), 68f, Color.white);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.AutoFit(title, 0.6f);
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -85f), new Vector2(1400f, 90f));

        _tabs.Add(Tab.Record);
        _tabs.Add(Tab.Unlocks);
        if (PatientRecord.IsUnlocked(Unlock.Codex)) _tabs.Add(Tab.Codex);
        if (_fromPause && !GameFlow.IsDescent) _tabs.Add(Tab.Assists);

        float tabWidth = 330f, gap = 14f;
        float x0 = -(tabWidth + gap) * (_tabs.Count - 1) * 0.5f;
        for (int i = 0; i < _tabs.Count; i++)
        {
            Tab tab = _tabs[i];
            Button b = RuntimeUi.CreateButton(_panel.transform, S("tab." + tab.ToString().ToLowerInvariant()), Vector2.zero, new Vector2(tabWidth, 66f), 32f, ButtonIdle, ButtonHover, Color.white);
            b.name = "Tab_" + tab;
            RuntimeUi.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(x0 + i * (tabWidth + gap), -185f), new Vector2(tabWidth, 66f));
            RuntimeUi.AutoFit(b.GetComponentInChildren<TMP_Text>(), 0.6f);
            b.onClick.AddListener(() => ShowTab(tab));
            _tabButtons.Add(b);
        }

        GameObject rows = new GameObject("Rows");
        rows.transform.SetParent(_panel.transform, false);
        rows.layer = _panel.layer;
        _rows = rows.AddComponent<RectTransform>();
        RuntimeUi.Stretch(_rows);

        Button back = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.common.back"), Vector2.zero, new Vector2(440f, 76f), 36f, ButtonIdle, ButtonHover, Color.white);
        back.name = "Back";
        RuntimeUi.Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(440f, 76f));
        back.onClick.AddListener(Close);
        RuntimeUi.BindButton(back, "ui.common.back");

        _panel.SetActive(false);
    }

    public void ShowTab(Tab tab)
    {
        _tab = tab;
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            ColorBlock c = _tabButtons[i].colors;
            c.normalColor = _tabs[i] == tab ? Accent * 0.75f : ButtonIdle;
            c.selectedColor = c.normalColor;
            _tabButtons[i].colors = c;
        }

        for (int i = _rows.childCount - 1; i >= 0; i--)
        {
            Transform child = _rows.GetChild(i);
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        _first = null;
        _codexTitle = _codexBody = null;

        switch (tab)
        {
            case Tab.Record: BuildRecord(); break;
            case Tab.Unlocks: BuildUnlocks(); break;
            case Tab.Codex: BuildCodex(); break;
            default: BuildAssists(); break;
        }

        if (EventSystem.current != null && _first != null) EventSystem.current.SetSelectedGameObject(_first.gameObject);
        if (tab == Tab.Unlocks) PatientRecord.MarkUnlocksSeen();
    }

    // ---------------------------------------------------------------- helpers

    private TextMeshProUGUI Text(string name, string text, float size, Color colour, TextAlignmentOptions align, Vector2 pos, Vector2 box, bool wrap = false)
    {
        TextMeshProUGUI t = RuntimeUi.CreateText(_rows, name, text, size, colour);
        t.alignment = align;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        RuntimeUi.AutoFit(t, 0.6f);
        RuntimeUi.Place(t.rectTransform, new Vector2(0.5f, 1f), pos, box);
        return t;
    }

    private void Row(string label, string value, float y, Color valueColour)
    {
        Text(label + " Label", label, 30f, Dim, TextAlignmentOptions.Left, new Vector2(-330f, y), new Vector2(700f, 44f));
        Text(label + " Value", value, 30f, valueColour, TextAlignmentOptions.Right, new Vector2(400f, y), new Vector2(560f, 44f));
    }

    private static string DashIf(int n) => n > 0 ? n.ToString() : "-";

    // ---------------------------------------------------------------- RECORD

    private void BuildRecord()
    {
        PatientRecordData d = PatientRecord.Data;
        if (PatientRecord.IsEmpty)
        {
            Text("Empty", S("empty"), 40f, Grey, TextAlignmentOptions.Center, new Vector2(0f, -520f), new Vector2(1400f, 60f));
            return;
        }

        float y = -290f;
        const float step = 44f;
        Row(S("started"), d.campaignsStarted.ToString(), y, Color.white); y -= step;
        Row(S("finished"), d.campaignsFinished.ToString(), y, Color.white); y -= step;

        string endings = (d.endingsMask & 1) != 0 && (d.endingsMask & 2) != 0
            ? L10n.T("lore.ending.discharged.title") + " · " + L10n.T("lore.ending.lights_out.title")
            : (d.endingsMask & 1) != 0 ? L10n.T("lore.ending.discharged.title")
            : (d.endingsMask & 2) != 0 ? L10n.T("lore.ending.lights_out.title") : "-";
        Row(S("endings"), endings, y, Color.white); y -= step;
        Row(S("best_oath_a"), d.bestOathDischargedPlus1 > 0 ? (d.bestOathDischargedPlus1 - 1).ToString() : "-", y, Amber); y -= step;
        Row(S("best_oath_b"), d.bestOathLightsOutPlus1 > 0 ? (d.bestOathLightsOutPlus1 - 1).ToString() : "-", y, Amber); y -= step;
        Row(S("shards"), d.totalShards.ToString(), y, new Color(0.7f, 0.9f, 1f)); y -= step;
        Row(S("descent"), DashIf(DescentRecords.BestDepth), y, Color.white); y -= step + 14f;

        Text("FloorsHeader", S("floors_header"), 28f, Amber, TextAlignmentOptions.Left, new Vector2(-330f, y), new Vector2(700f, 40f)); y -= 42f;
        for (int floor = 1; floor <= FloorProfile.FinalFloor; floor++)
        {
            Grade g = (Grade)d.bestGrade[floor];
            if (g == Grade.None)
            {
                Row(L10n.T("ui.record.floor", ("floor", floor)), "-", y, Grey);
            }
            else
            {
                int secs = Mathf.Max(0, d.bestSecondsPlus1[floor] - 1);
                string value = FloorGrade.Letter(g) + "   " + L10n.T("ui.record.floor_oath", ("rank", d.bestGradeOath[floor])) + "   " + MazeEscape.FormatTime(secs);
                Row(L10n.T("ui.record.floor", ("floor", floor)), value, y, FloorGrade.LetterColour(g));
            }
            y -= 40f;
        }
    }

    // ---------------------------------------------------------------- UNLOCKS

    private static string UnlockSlug(Unlock u)
    {
        switch (u)
        {
            case Unlock.Codex: return "codex";
            case Unlock.KitScavenger: return "kit_scavenger";
            case Unlock.CardStethoscope: return "card_stethoscope";
            case Unlock.ContractUnseen: return "contract_unseen";
            case Unlock.Oaths: return "oaths";
            case Unlock.ColourBone: return "colour_bone";
            case Unlock.KitRunner: return "kit_runner";
            case Unlock.CardArchivist: return "card_archivist";
            case Unlock.CardDynamo: return "card_dynamo";
            case Unlock.KitCartographer: return "kit_cartographer";
            case Unlock.ContractsSkill: return "contracts_skill";
            case Unlock.ColourOathRed: return "colour_oath_red";
            default: return "colour_ward";
        }
    }

    /// <summary>(current, target) for a locked milestone, so the screen can show "2 / 4".</summary>
    public static void Progress(Unlock u, PatientRecordData d, out int current, out int target)
    {
        int bits(int v) { int n = 0; while (v != 0) { n += v & 1; v = (int)((uint)v >> 1); } return n; }
        switch (u)
        {
            case Unlock.Codex: current = d.floorsCleared; target = 1; break;
            case Unlock.KitScavenger: current = d.highestFloorCleared; target = 2; break;
            case Unlock.CardStethoscope:
            case Unlock.ContractUnseen: current = d.highestFloorCleared; target = 4; break;
            case Unlock.Oaths:
            case Unlock.ColourBone: current = d.endingsMask != 0 ? 1 : 0; target = 1; break;
            case Unlock.KitRunner: current = d.campaignsFinished; target = 2; break;
            case Unlock.CardArchivist: current = bits(d.endingsMask & 3); target = 2; break;
            case Unlock.CardDynamo: current = d.totalShards; target = 1500; break;
            case Unlock.KitCartographer: current = d.campaignsFinished; target = 3; break;
            case Unlock.ContractsSkill: current = d.sGrades; target = 1; break;
            case Unlock.ColourOathRed: current = Mathf.Max(0, Mathf.Max(d.bestOathDischargedPlus1, d.bestOathLightsOutPlus1) - 1); target = 5; break;
            default: current = bits(d.metMask & PatientRecord.AllCodexMask); target = bits(PatientRecord.AllCodexMask); break;
        }
        current = Mathf.Min(current, target);
    }

    private void BuildUnlocks()
    {
        PatientRecordData d = PatientRecord.Data;
        Unlock[] all = (Unlock[])Enum.GetValues(typeof(Unlock));
        int perColumn = 7;
        for (int i = 0; i < all.Length; i++)
        {
            Unlock u = all[i];
            int col = i / perColumn;
            int row = i % perColumn;
            float x = col == 0 ? -480f : 480f;
            float y = -290f - row * 86f;
            string slug = UnlockSlug(u);
            bool got = PatientRecord.IsUnlocked(u, d);
            string name = L10n.T("ui.record.unlock." + slug + ".name");
            if (got && PatientRecord.IsNew(u)) name += "   " + S("new");
            Text("Unlock_" + slug, name, 29f, got ? Color.white : Grey, TextAlignmentOptions.Left, new Vector2(x, y), new Vector2(880f, 38f));
            string detail;
            Color detailColour;
            if (got) { detail = S("unlocked"); detailColour = Green; }
            else
            {
                Progress(u, d, out int cur, out int target);
                detail = L10n.T("ui.record.unlock." + slug + ".cond") + "  " + cur + " / " + target;
                detailColour = Grey;
            }
            Text("Unlock_" + slug + "_Detail", detail, 23f, detailColour, TextAlignmentOptions.Left, new Vector2(x, y - 34f), new Vector2(880f, 32f));
        }
    }

    // ---------------------------------------------------------------- CODEX

    private static string CodexSlug(CodexEntry e)
    {
        switch (e)
        {
            case CodexEntry.Hunter: return "hunter";
            case CodexEntry.Stan: return "stan";
            case CodexEntry.Visitor: return "visitor";

            case CodexEntry.Tenant: return "tenant";
            case CodexEntry.Elias: return "elias";
            default: return "tenant";
        }
    }

    private void BuildCodex()
    {
        CodexEntry[] all = (CodexEntry[])Enum.GetValues(typeof(CodexEntry));
        for (int i = 0; i < all.Length; i++)
        {
            CodexEntry entry = all[i];
            bool met = PatientRecord.Met(entry);
            string label = met ? L10n.T("lore.codex." + CodexSlug(entry) + ".name") : "???";
            Button b = RuntimeUi.CreateButton(_rows, label, Vector2.zero, new Vector2(520f, 70f), 30f, ButtonIdle, ButtonHover, met ? Color.white : Grey);
            b.name = "Codex_" + CodexSlug(entry);
            RuntimeUi.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(-560f, -320f - i * 84f), new Vector2(520f, 70f));
            RuntimeUi.AutoFit(b.GetComponentInChildren<TMP_Text>(), 0.6f);
            CodexEntry captured = entry;
            b.onClick.AddListener(() => ShowCodexEntry(captured));
            SelectHint hint = b.gameObject.AddComponent<SelectHint>();
            hint.OnHint = () => ShowCodexEntry(captured);
            if (_first == null) _first = b;
        }

        _codexTitle = Text("CodexTitle", "", 40f, Amber, TextAlignmentOptions.TopLeft, new Vector2(330f, -300f), new Vector2(1000f, 56f));
        _codexBody = Text("CodexBody", "", 28f, Dim, TextAlignmentOptions.TopLeft, new Vector2(330f, -660f), new Vector2(1000f, 600f), true);
        ShowCodexEntry(_codexSelected);
    }

    private void ShowCodexEntry(CodexEntry entry)
    {
        _codexSelected = entry;
        if (_codexTitle == null) return;
        bool met = PatientRecord.Met(entry);
        string slug = CodexSlug(entry);
        _codexTitle.text = met ? L10n.T("lore.codex." + slug + ".name") : "???";
        string body = met ? L10n.T("lore.codex." + slug + ".body") : S("not_met");
        if (met && entry == CodexEntry.Hunter) body += "\n\n" + HabitsText();
        _codexBody.text = body;
    }

    /// <summary>What the hunter has learned, as the highest level ever seen per habit.</summary>
    private string HabitsText()
    {
        TMP_FontAsset font = RuntimeUi.ResolveFont();
        char on = '#', off = '-';
        if (font != null && font.HasCharacter('▮', true, true) && font.HasCharacter('▯', true, true)) { on = '▮'; off = '▯'; }
        else if (font != null && font.HasCharacter('■', true, true) && font.HasCharacter('□', true, true)) { on = '■'; off = '□'; }

        PatientRecordData d = PatientRecord.Data;
        System.Text.StringBuilder sb = new System.Text.StringBuilder("<size=24><color=#C9B58A>").Append(S("habits_header")).Append("</color></size>");
        foreach (HunterMemory.Habit h in Enum.GetValues(typeof(HunterMemory.Habit)))
        {
            int level = (int)h < d.habitMax.Length ? d.habitMax[(int)h] : 0;
            if (level < 1) continue;
            sb.Append('\n').Append(HunterMemory.NameText(h)).Append("  ");
            for (int i = 0; i < HunterMemory.MaxLevel; i++) sb.Append(i < level ? on : off);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- ASSISTS (pause menu, D15)

    private void BuildAssists()
    {
        float y = -300f;
        foreach (OathCatalogue.AssistDef def in OathCatalogue.Assists)
        {
            Text(def.Slug + " Label", L10n.T("ui.assist." + def.Slug + ".name"), 30f, Dim, TextAlignmentOptions.Left, new Vector2(-300f, y), new Vector2(760f, 50f));
            StepperControl stepper = RuntimeUi.CreateStepper(_rows, "Assist_" + def.Slug, new Vector2(0.5f, 1f), new Vector2(420f, y), new Vector2(330f, 54f), 30f, ButtonIdle, ButtonHover);
            stepper.name = "Assist_" + def.Slug;
            Assist id = def.Id;
            stepper.Configure(OffOn(), (CampaignRules.Assists & id) != 0 ? 1 : 0, true, i =>
            {
                Assist now = i == 1 ? CampaignRules.Assists | id : CampaignRules.Assists & ~id;
                CampaignRules.SetAssists(now);
            });
            if (_first == null) _first = stepper;
            y -= 62f;
        }
        Text("NextFloor", S("assists_next_floor"), 26f, Amber, TextAlignmentOptions.Center, new Vector2(0f, y - 20f), new Vector2(1500f, 40f));
        Text("Mark", S("assists_mark"), 26f, Grey, TextAlignmentOptions.Center, new Vector2(0f, y - 62f), new Vector2(1500f, 40f));
    }
}
