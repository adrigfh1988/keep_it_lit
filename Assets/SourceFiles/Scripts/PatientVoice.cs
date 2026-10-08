using UnityEngine;

/// <summary>
/// F93 (E5): a hub patient's speech bubble. Same engine as Stan and the hunter (CreatureVoice) with two differences: the live
/// gate is "the player is in the hub" instead of "a run is active" (IsLive override), and the trigger is the player standing within
/// 3.5 m and looking at the patient (one line per approach, 8 s cooldown, shuffled within each set). Lines are verbatim from
/// plannings/asylum-story-plan.md. Sits on the same object as the patient's HubPatient.
/// </summary>
[RequireComponent(typeof(HubPatient))]
public class PatientVoice : CreatureVoice
{
    public enum Set { Default = 0, FileRead = 1, Late = 2, Finale = 3 }

    [SerializeField] private float speakRange = 3.5f;
    [SerializeField] private float lookCone = 40f;
    [SerializeField] private float cooldown = 8f;

    private HubPatient _patient;
    private float _nextAt;
    private bool _saidFile;

    private void Awake()
    {
        _patient = GetComponent<HubPatient>();
        minGapSeconds = 2f;
        InitVoice(null);
    }

    private void OnEnable()
    {
        AsylumHub.OnEnterHub += HandleEnter;
        _saidFile = false;
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        Silence();
    }

    private void HandleEnter(int floor)
    {
        _saidFile = false;
        _nextAt = Time.time + 2f;
    }

    // ---------------------------------------------------------------- CreatureVoice

    protected override GameOutcome Outcome => null;
    protected override bool IsLive() => GameFlow.IsInHub && !GameOutcome.IsOver && AsylumHub.Active != null && AsylumHub.Active.ContentActive;
    protected override int CategoryCount => 4;
    protected override float BubbleRange => 10f;
    protected override int PriorityOf(int category) => 1;
    protected override float CooldownOf(int category) => cooldown;
    protected override bool IsShout(int category) => false;
    protected override bool GlitchFor(int category) => false;
    protected override bool CanSpeak(int category) => _patient != null && _patient.IsPresent;
    protected override string[] LinesFor(int category) => Lines(_patient.patient, (Set)category);
    protected override Vector3 BubbleAnchor() => _patient != null ? _patient.HeadPosition + Vector3.up * 0.3f : transform.position;

    protected override void Tick(float deltaTime)
    {
        if (_patient == null || !_patient.IsPresent || Time.time < _nextAt) return;
        AsylumHub hub = AsylumHub.Active;
        Camera cam = hub != null ? hub.PlayerCamera : null;
        if (cam == null) return;

        Vector3 to = _patient.HeadPosition - cam.transform.position;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        if (flat.magnitude > speakRange) return;
        if (Vector3.Angle(cam.transform.forward, to) > lookCone) return;

        Set set = PickSet(out bool isFile);
        if (TrySay((int)set))
        {
            if (isFile) _saidFile = true;
            _nextAt = Time.time + cooldown;
        }
    }

    private Set PickSet(out bool isFile)
    {
        isFile = false;
        if (HubState.IsFinale) return Set.Finale;
        if (!_saidFile && LoreArchive.HasRead(FileOf(_patient.patient))) { isFile = true; return Set.FileRead; }
        if (LateApplies(_patient.patient, HubState.Floor) && Random.value < 0.5f) return Set.Late;
        return Set.Default;
    }

    // ---------------------------------------------------------------- the lines

    public static int FileOf(HubState.Patient p) => p == HubState.Patient.Hilde ? 100 : p == HubState.Patient.Tomas ? 101 : 102;

    public static bool LateApplies(HubState.Patient p, int floor)
    {
        switch (p)
        {
            case HubState.Patient.Hilde: return floor >= 3;
            case HubState.Patient.Tomas: return floor >= 4;
            default: return floor == 3;
        }
    }

    private static readonly string[] HildeDefault = { "Forty-one doors. Yesterday, forty.", "Don't open the new one.", "Count with me. One..." };
    private static readonly string[] HildeFile = { "You read my file. So you count too." };
    private static readonly string[] HildeLate = { "Forty-four. They keep coming." };
    private static readonly string[] HildeFinale = { "One more door. Yours." };

    private static readonly string[] TomasDefault = { "Two knocks. All clear.", "Listen. Hear them walking?", "The pipes are warm tonight." };
    private static readonly string[] TomasFile = { "They wrote that I answer the pipes. The pipes answer me." };
    private static readonly string[] TomasLate = { "Three. Three knocks. Hide." };
    private static readonly string[] TomasFinale = { "It stopped knocking. Why did it stop?" };

    private static readonly string[] EliasDefault = { "The drawers are cold. Mine is number five.", "I walk at night. I don't remember where." };
    private static readonly string[] EliasFile = { "You've seen drawer five. Did it have my name on?" };
    private static readonly string[] EliasLate = { "They're moving me to Surgery tomorrow." };
    private static readonly string[] EliasFinale = { "Thank you for coming down." };

    public static string[] Lines(HubState.Patient patient, Set set)
    {
        switch (patient)
        {
            case HubState.Patient.Hilde:
                return set == Set.FileRead ? HildeFile : set == Set.Late ? HildeLate : set == Set.Finale ? HildeFinale : HildeDefault;
            case HubState.Patient.Tomas:
                return set == Set.FileRead ? TomasFile : set == Set.Late ? TomasLate : set == Set.Finale ? TomasFinale : TomasDefault;
            default:
                return set == Set.FileRead ? EliasFile : set == Set.Late ? EliasLate : set == Set.Finale ? EliasFinale : EliasDefault;
        }
    }
}
