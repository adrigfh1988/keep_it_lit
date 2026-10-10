using UnityEngine;

/// <summary>
/// F101: something in the asylum hub the player can look at closely (E, no hold). A line of text on the subtitle (or, for a readable card,
/// the reading panel the records board uses), picked by key and floor from <see cref="HubVignetteText"/>; optionally a paper rustle, or a
/// knock from one of the hub patients. Same pattern as RecordsSlot: not a LoreNote, so it raises nothing, marks nothing read and pays
/// nothing - `noteId` (negative for a readable card) only documents which note it is. The collider sits on this object (a trigger is
/// fine: PlayerInteractor's spherecast collects triggers). Never offered while a hub pursuer is after the player or a hub transition runs,
/// so nothing here can fire during a chase.
/// </summary>
public class HubExamine : MonoBehaviour, IInteractable
{
    public enum Sound { None, Paper, Knock }

    [Tooltip("Key in HubVignetteText.")]
    [SerializeField] private string key = "";
    [SerializeField] private string prompt = "EXAMINE";
    [Tooltip("Open the reading panel (a card, a ledger) instead of a subtitle.")]
    [SerializeField] private bool readable;
    [Tooltip("Negative for a readable card; documentation only (never saved, never paid).")]
    [SerializeField] private int noteId;
    [SerializeField] private Sound sound = Sound.None;
    [Tooltip("Knock sound: the patient who knocks (Tomas); 2 knocks, 3 on floors 4-5.")]
    [SerializeField] private HubPatient knocker;

    public string Key => key;
    public bool Readable => readable;
    public int NoteId => noteId;

    private static AsylumHub Hub => AsylumHub.Active;

    private static bool Busy()
    {
        AsylumHub hub = Hub;
        return hub == null || hub.IsTransitioning || hub.Leaving || hub.PursuerAfterPlayer();
    }

    string IInteractable.Prompt
    {
        get
        {
            if (readable && Hub != null && Hub.Reader != null && Hub.Reader.IsOpen) return L10n.T("hud.prompt.close");
            if (readable) return L10n.T("hud.prompt.read");
            // S9 (D10): translated by the vignette key (hub.examine.<key>.prompt); the serialized prompt is the fallback.
            string k = "hub.examine." + key + ".prompt";
            return L10n.Has(k) ? L10n.T(k) : prompt;
        }
    }

    float IInteractable.HoldSeconds => 0f;

    bool IInteractable.CanInteract
    {
        get
        {
            AsylumHub hub = Hub;
            if (hub == null || string.IsNullOrEmpty(key)) return false;
            if (readable && hub.Reader != null && hub.Reader.IsOpen) return true; // closing is always allowed
            if (Busy()) return false;
            return readable ? hub.Reader != null : hub.Hud != null;
        }
    }

    void IInteractable.Interact(PlayerInteractor who)
    {
        AsylumHub hub = Hub;
        if (hub == null) return;
        int floor = HubState.IsFinale ? 5 : HubState.Floor;
        string text = HubVignetteText.Get(key, floor, HubState.HasTruth);

        if (readable)
        {
            LoreReadingUi reader = hub.Reader;
            if (reader == null) return;
            if (reader.IsOpen) { reader.Close(); return; }
            if (Busy()) return;
            reader.Show(text, who);
            GameAudio.PlayAt(GameAudio.Library.paper, transform.position, 0.6f);
            return;
        }
        if (Busy()) return;
        if (hub.Hud != null && text.Length > 0) hub.Hud.ShowSubtitle(text, 4.5f);
        switch (sound)
        {
            case Sound.Paper: GameAudio.PlayAt(GameAudio.Library.paper, transform.position, 0.5f); break;
            case Sound.Knock: if (knocker != null) knocker.KnockNow(floor >= 4 ? 3 : 2); break;
        }
    }

    void IInteractable.OnFocus(bool focused) { }
}
