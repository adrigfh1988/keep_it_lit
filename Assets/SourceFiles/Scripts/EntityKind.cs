/// <summary>
/// F107 D1: the things that live in a maze besides the hunter, as a flag set so a level (or the Inspector's debugEntities) can name any
/// combination. Append new kinds, never reorder: the values are the bit positions.
/// </summary>
[System.Flags]
public enum EntityKind
{
    None = 0,
    /// <summary>Stan, the Weeping Angel: moves only unlit (Stalker.cs). Not a MazeEntity yet - see the CLAUDE.md DESCENT bullet.</summary>
    Stalker = 1,
    /// <summary>The blind second killer that only moves toward sound (Listener.cs).</summary>
    Listener = 2,
    /// <summary>One locker per level is already occupied: hiding in it shoves you back out with a slam (LockerTenant.cs).</summary>
    Tenant = 4,
    /// <summary>One hanging body drops on you if you pass unlit, or one corpse crawls toward your beam (LivingBody.cs).</summary>
    LivingBody = 8,
    /// <summary>Sister Agnes ("The Night Nurse"): anti-torch curfew patrol and anti-sprint/noise triage harasser (NightNurse.cs).</summary>
    NightNurse = 16
}
