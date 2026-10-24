using UnityEngine;

/// <summary>
/// F107 C: a closed cache against a wall in a DESCENT level (InteractableKit.itemCache, cloned by MazeGenerator.BuildItemCaches). Hold E for a
/// second and it gives one item and says what. Opening makes a small noise the hunter can hear: that is the risk side of the reward.
/// Deliberately not a Pickup, an ObjectiveToken or a KeyPickup (CLAUDE.md's star-count gotcha): nothing here gates the hatch.
/// </summary>
public class DescentItem : MonoBehaviour, IInteractable
{
    [Header("F107 C - opening")]
    [Tooltip("How long E is held to open the cache (s)")]
    [SerializeField, Min(0f)] private float holdSeconds = 1f;
    [Tooltip("How far the opening can be heard by the hunter (m)")]
    [SerializeField, Min(0f)] private float noiseRadius = 8f;
    [Tooltip("How long the 'FOUND: ...' line stays on screen (s)")]
    [SerializeField, Min(0.5f)] private float subtitleSeconds = 3f;
    [Tooltip("Optional point light that guides the player to the cache; switched off once it is open")]
    [SerializeField] private Light glintLight;

    private ShopItem _rolled = ShopItem.SpareBattery;
    private int _depth = 1;
    private bool _opened;
    private PlayerHud _hud;
    private AIFollower _hunter;
    private ThrowController _throws;
    private Flashlight _flashlight;
    private AudioClip _clunk;

    /// <summary>True once the cache has been opened and emptied.</summary>
    public bool IsOpened => _opened;

    /// <summary>The item the +24 roll put inside (what is actually given can be a later row of the table if the player's hands are full).</summary>
    public ShopItem Rolled => _rolled;

    /// <summary>Called right after Instantiate by MazeGenerator.BuildItemCaches.</summary>
    public void Configure(ShopItem rolled, int depth)
    {
        _rolled = rolled;
        _depth = depth;
    }

    /// <summary>Late-bound by MazeGenerator.SetUpAtmosphere, once the HUD, hunter, throw controller and torch exist (the cache is built earlier in Awake).</summary>
    public void Bind(PlayerHud hud, AIFollower hunter, ThrowController throws, Flashlight flashlight)
    {
        _hud = hud;
        _hunter = hunter;
        _throws = throws;
        _flashlight = flashlight;
        _clunk = DoorAudio.BuildClunkClip(0.18f, 160f, 0.3f);
    }

    private bool HandsFull => !DescentLoot.TryResolve(_rolled, _throws, _flashlight, _depth, out _);

    string IInteractable.Prompt => L10n.T(HandsFull ? "hud.descent.hands_full" : "hud.prompt.open_cache");
    bool IInteractable.CanInteract => !_opened;

    // A cache that cannot give anything is not worth a hold: the prompt just says HANDS FULL.
    float IInteractable.HoldSeconds => HandsFull ? 0f : holdSeconds;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_opened) return;
        if (!DescentLoot.TryResolve(_rolled, _throws, _flashlight, _depth, out ShopItem item)) return; // hands full: stays closed

        _opened = true;
        DescentLoot.Grant(item, _throws, _flashlight, _depth);

        string name = ShopCatalogue.Get(item)?.Name ?? item.ToString();
        _hud?.ShowSubtitle(L10n.T(DescentLoot.IsNextLevel(item) ? "hud.descent.found_next" : "hud.descent.found", ("item", name)), subtitleSeconds);

        _hunter?.HearNoise(transform.position, noiseRadius);
        if (!GameAudio.PlayAt(GameAudio.Library.fuseClunk, transform.position, 0.7f) && _clunk != null) GameAudio.ClipAtPoint(_clunk, transform.position, 0.7f);

        if (glintLight != null) glintLight.enabled = false; // StarPulse only sets the intensity, so it does not switch it back on
    }

    void IInteractable.OnFocus(bool focused) { }
}
