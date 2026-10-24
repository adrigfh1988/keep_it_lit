using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// F123 floor 3 (Elias Vogt): a bank of four morgue drawers against the end wall of a dead end (InteractableKit.morgueDrawerBank, cloned by
/// MazeGenerator.BuildMorgueDrawers). Hold E and ONE of the four rolls out - loudly, so the hunter may hear it. Most are empty and cold.
/// <list type="bullet">
/// <item><b>Five</b>: the tag reads "5 · E. VOGT" and the tray holds his toe tag (the case file 102 stays on its wall; this is the morgue's side of it).</item>
/// <item><b>Yours</b>: the tag carries the player's own number (<see cref="WingNightmares.PatientMark"/>, redacted until the truth is known) and the
/// player's own robot body lies on the tray, still; a spare battery is "among your things" when the hands have room.</item>
/// </list>
/// Not a Pickup / ObjectiveToken / LoreNote: nothing here gates the hatch, and the bank is never a Shiftable (B9/B10 must not copy it).
/// </summary>
public class MorgueDrawerBank : MonoBehaviour, IInteractable
{
    public enum Role { None, Five, Yours }

    [Header("Wired by the builder")]
    [Tooltip("The four drawers: a pivot per drawer holding its tray and its front, moved together")]
    [SerializeField] private Transform[] trays = new Transform[0];
    [Tooltip("The front of each drawer, hinged at the cabinet's outer side; it swings open (doorSwingDegrees about Y) as the tray rolls out")]
    [SerializeField] private Transform[] doorHinges = new Transform[0];
    [SerializeField] private float[] doorSwingDegrees = new float[0];
    [Tooltip("One number tag per drawer front")]
    [SerializeField] private TextMeshPro[] tags = new TextMeshPro[0];
    [Tooltip("Where something lies on each tray (child of the drawer pivot, on the tray top, ~half a metre behind its front)")]
    [SerializeField] private Transform[] trayAnchors = new Transform[0];
    [Tooltip("The player's own robot, lying on its back (inactive); moved onto the opened tray")]
    [SerializeField] private Transform yourBody;
    [Tooltip("Elias Vogt's toe tag (inactive); moved onto the opened tray of drawer five")]
    [SerializeField] private Transform toeTag;
    [SerializeField] private TextMeshPro toeTagText;
    [Tooltip("How far a drawer rolls out, in this object's local space")]
    [SerializeField] private Vector3 slideLocal = new Vector3(0f, 0f, 1f);

    [Header("Tuning")]
    [SerializeField, Min(0.1f)] private float slideSeconds = 0.9f;
    [SerializeField, Min(0.05f)] private float swingSeconds = 0.45f;
    [Tooltip("The tray starts rolling out this long after the front starts to swing (s)")]
    [SerializeField, Min(0f)] private float slideDelay = 0.2f;
    [SerializeField, Min(0f)] private float holdSeconds = 0.8f;
    [Tooltip("How far the opening can be heard by the hunter (m)")]
    [SerializeField, Min(0f)] private float drawerNoiseRadius = 10f;
    [SerializeField, Min(0.5f)] private float subtitleSeconds = 3f;

    private Role _role;
    private int _openTray;
    private bool _opened;
    private PlayerHud _hud;
    private AIFollower _hunter;
    private DreadMeter _dread;
    private AudioClip _scrape;

    public Role BankRole => _role;
    public int OpenTray => _openTray;
    public bool IsOpened => _opened;

    /// <summary>Called right after Instantiate by MazeGenerator.BuildMorgueDrawers. `tagTexts` has one entry per tray (the role tray's is the fixed text).</summary>
    public void Configure(Role role, int openTray, string[] tagTexts)
    {
        _role = role;
        _openTray = Mathf.Clamp(openTray, 0, Mathf.Max(0, trays.Length - 1));
        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i] == null || tagTexts == null || i >= tagTexts.Length) continue;
            tags[i].text = tagTexts[i];
            // The redaction block may be missing from the world font: take the UI font (the one PatientMark was decided against) for that tag.
            if (tagTexts[i].IndexOf('▇') >= 0 && tags[i].font != null && !tags[i].font.HasCharacter('▇', true, true))
            {
                TMP_FontAsset ui = RuntimeUi.ResolveFont();
                if (ui != null) { tags[i].font = ui; tags[i].fontSharedMaterial = ui.material; }
            }
        }

        if (toeTagText != null) toeTagText.text = L10n.T("world.nightmare.toetag");
        if (yourBody != null) yourBody.gameObject.SetActive(false);
        if (toeTag != null) toeTag.gameObject.SetActive(false);
    }

    /// <summary>Late-bound by MazeGenerator.SetUpAtmosphere, once the HUD, hunter and dread meter exist.</summary>
    public void Bind(PlayerHud hud, AIFollower hunter, DreadMeter dread)
    {
        _hud = hud;
        _hunter = hunter;
        _dread = dread;
        if (_scrape == null) _scrape = DoorAudio.BuildScrapeClip();
    }

    string IInteractable.Prompt => L10n.T("hud.prompt.drawer");
    bool IInteractable.CanInteract => !_opened && GameFlow.IsRunActive && !GameOutcome.IsOver;
    float IInteractable.HoldSeconds => holdSeconds;
    void IInteractable.OnFocus(bool focused) { }

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_opened || trays.Length == 0) return;
        _opened = true;

        Transform tray = trays[_openTray];
        Transform anchor = _openTray < trayAnchors.Length ? trayAnchors[_openTray] : tray;

        Vector3 at = tray != null ? tray.position : transform.position;
        _hunter?.HearNoise(transform.position, drawerNoiseRadius);
        if (!GameAudio.PlayAt(GameAudio.Library.deskDrawer, at, 0.9f) && _scrape != null) GameAudio.ClipAtPoint(_scrape, at, 0.9f);

        // What lies on the tray rides out with it, so it is placed before the slide starts.
        if (_role == Role.Yours && yourBody != null && anchor != null)
        {
            yourBody.SetParent(anchor, false);
            yourBody.localPosition = Vector3.zero;
            yourBody.localRotation = Quaternion.identity;
            yourBody.gameObject.SetActive(true);
        }
        else if (_role == Role.Five && toeTag != null && anchor != null)
        {
            toeTag.SetParent(anchor, false);
            toeTag.localPosition = Vector3.zero;
            toeTag.localRotation = Quaternion.identity;
            toeTag.gameObject.SetActive(true);
        }

        if (_openTray < doorHinges.Length && doorHinges[_openTray] != null) StartCoroutine(Swing(doorHinges[_openTray], _openTray < doorSwingDegrees.Length ? doorSwingDegrees[_openTray] : 0f));
        if (tray != null) StartCoroutine(Slide(tray));

        switch (_role)
        {
            case Role.Five:
                _hud?.ShowSubtitle(L10n.T("sub.nightmare.elias.five"), subtitleSeconds + 0.5f);
                break;
            case Role.Yours:
                _dread?.Spike();
                GameAudio.Play2D(GameAudio.Library.whisperEcho, 0.7f);
                _hud?.ShowSubtitle(L10n.T("sub.nightmare.elias.yours"), subtitleSeconds + 1f);
                if (PlayerInventory.CanHoldMore(ShopItem.SpareBattery))
                {
                    PlayerInventory.Grant(ShopItem.SpareBattery); // a Retry can re-grant a used one; the cap stops stacking (same rule as the loop's reward)
                    _hud?.QueueSubtitle(L10n.T("sub.nightmare.elias.your_things"), 4f);
                }
                break;
            default:
                string[] pool = L10n.Pool("sub.nightmare.elias.empty");
                if (pool.Length > 0) _hud?.ShowSubtitle(pool[Random.Range(0, pool.Length)], subtitleSeconds);
                break;
        }
    }

    private IEnumerator Swing(Transform hinge, float degrees)
    {
        Quaternion from = hinge.localRotation;
        Quaternion to = from * Quaternion.Euler(0f, degrees, 0f);
        for (float t = 0f; t < swingSeconds; t += Time.deltaTime)
        {
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / swingSeconds), 2f);
            hinge.localRotation = Quaternion.SlerpUnclamped(from, to, k);
            yield return null;
        }
        hinge.localRotation = to;
    }

    private IEnumerator Slide(Transform tray)
    {
        if (slideDelay > 0f) yield return new WaitForSeconds(slideDelay);
        Vector3 from = tray.localPosition;
        Vector3 to = from + slideLocal;
        for (float t = 0f; t < slideSeconds; t += Time.deltaTime)
        {
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / slideSeconds), 3f); // ease-out: it rolls out fast, then settles
            tray.localPosition = Vector3.LerpUnclamped(from, to, k);
            yield return null;
        }
        tray.localPosition = to;
    }
}
