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
    /// <summary>One locker per level is already occupied: hiding in it shoves you back out with a slam (LockerTenant.cs).</summary>
    Tenant = 4,
    /// <summary>Sister Agnes ("The Night Nurse"): anti-torch curfew patrol and anti-sprint/noise triage harasser (NightNurse.cs).</summary>
    NightNurse = 16,
    /// <summary>Cook Bruno ("The Butcher"): claims chopping block stations in rooms, rotates every 2 minutes, cleaves shutters, and charges intruders/starers (DarkBruno.cs).</summary>
    Butcher = 32,
    /// <summary>Elias Vogt ("The Frost" / Subject #5): solo Cold Storage blackout predator who tints nearby lamps icy cyan, harvests wall lamps over 4 escalation stages, and tracks freezing breath (Elias.cs).</summary>
    Elias = 64
}
