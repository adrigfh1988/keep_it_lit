using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F101 phase B: the three halls (ground, upper, basement) get a rhythm of landmark clusters in the dark gaps between the lamp pools, every
/// 7-8 m and alternating walls: the WEST half of the ground hall is the kitchen / supply side (crates, boxes, drawers), the EAST half the
/// medical side (gurney trains, wheelchairs, racks); the upper hall is an overflow ward; the basement a morgue annex. Between clusters the
/// walls get low rhythm (heaters, dead lamps, sockets, vents, posters, grime, claw marks, hand prints). Everything is placed against walls
/// (Put's 0.85 m keep-out kills free-floor props in a corridor), alternating sides so the F100 star trail always finds a plain wall: tall
/// props (over 1 m) stand on one side per stretch, in runs of at most about 1.5 m with bare wall between.
/// </summary>
public static partial class AsylumHubBuilder
{
    static partial void PropsPiecesHalls(List<PropsPiece> list)
    {
        list.Add(new PropsPiece { name = "HallGround", phase = 'B', room = "HallDownstair", build = BuildHallGround });
        list.Add(new PropsPiece { name = "HallUpper", phase = 'B', room = "HallUpstair", build = BuildHallUpper });
        list.Add(new PropsPiece { name = "HallBasement", phase = 'B', room = "HallUnderground", build = BuildHallBasement });
    }

    // ---------------------------------------------------------------- wall frames

    /// <summary>A frame (+Z into the room, X along the wall, origin on the wall at floor level) for a plain `halfW` x 0.7 m patch of wall found from `centre` along `dir`, nudged along the wall up to +-2.8 m. Null when no plain wall is there.</summary>
    private static Frame FindWall(DressRoomCtx r, Vector3 centre, Vector3 dir, float halfW = 0.9f, float reach = 7f)
    {
        dir.y = 0f; dir.Normalize();
        Vector3 along = Vector3.Cross(Vector3.up, dir);
        foreach (float shift in new[] { 0f, 0.7f, -0.7f, 1.4f, -1.4f, 2.1f, -2.1f, 2.8f, -2.8f })
        {
            Vector3 o = centre + along * shift; o.y = r.def.floorY + 1.1f;
            if (!FlatWallAt(o, dir, halfW, 0.7f, out Vector3 pt, out Vector3 n, out _, reach)) continue;
            return new Frame { pos = new Vector3(pt.x, r.def.floorY, pt.z), yaw = Quaternion.LookRotation(n).eulerAngles.y };
        }
        return null;
    }

    /// <summary>A frame on the wall on the `side` (+1 / -1 along `axis`) of `centre`, falling back to the other side.</summary>
    private static Frame FindWallEither(DressRoomCtx r, Vector3 centre, Vector3 axis, int side, float halfW = 0.9f)
    {
        Frame f = FindWall(r, centre, axis * side, halfW);
        return f ?? FindWall(r, centre, -axis * side, halfW);
    }

    /// <summary>A hung prop or decal on the wall of frame `f`, `lx` along it, centred `height` m above the floor.</summary>
    private static Placed HangAt(DressRoomCtx r, Frame f, string key, float lx, float height, float yawRel = 0f, float scale = 1f, bool decal = false)
    {
        if (f == null) return null;
        Vector3 origin = f.W(lx, 1.0f);
        Vector3 dir = -(f.Rot * Vector3.forward);
        return Hang(r, key, origin, dir, height, yawRel, scale, 0.02f, decal, 2.5f);
    }

    private static Placed Gurney(DressRoomCtx r, Frame f, float lx, float lz, float yawRel, bool covered)
    {
        Placed bed = InFrame(r, f, covered ? "MorgueWheelBed" : "SurgeryWheelBad", lx, lz, covered ? yawRel : 90f + yawRel, 1f, true);
        if (bed != null && covered) Sheet(On(r, bed, Body(r), 0f, 0f, bed.size.x > bed.size.z ? 0f : 90f, 1f, 0.02f));
        else if (bed != null && r.rng.Next(3) == 0) Sheet(On(r, bed, Body(r), 0f, 0f, bed.size.x > bed.size.z ? 90f : 0f, 1f, 0.03f));
        return bed;
    }

    // ---------------------------------------------------------------- styles

    private static void StyleSupply(DressRoomCtx r, Frame f, int variant)
    {
        if (f == null) return;
        if (variant % 2 == 0)
        {
            Stack(r, f, "Box_V1", -0.6f, 0.35f, 4, 0f, 0.23f);
            Stack(r, f, "Box_V2", -0.05f, 0.35f, 3, 20f, 0.23f);
            InFrame(r, f, "alch:Alch_CrateStack", 0.65f, 0.4f, 8f, 1f, true);
            InFrame(r, f, "alch:Alch_TrunkStack", 1.25f, 0.4f, -6f, 1f, true);
            InFrame(r, f, "CanOld", 0.2f, 1.0f, 40f, 1f, false, false);
        }
        else
        {
            Placed drawer = InFrame(r, f, "alch:Alch_Drawer1", -0.6f, 0.4f, 0f, 1f, true);
            On(r, drawer, "alch:Alch_Jugs", 0f, 0f, 20f, 1f, 0f);
            InFrame(r, f, "BigShelf_Stock", 0.7f, 0.45f, 0f, 1f, true);
            InFrame(r, f, "Pot", 1.7f, 0.6f, 15f, 1f, true);
            Stack(r, f, "Box_V1", -1.5f, 0.3f, 3, 8f, 0.23f);
        }
    }

    private static void StyleMedical(DressRoomCtx r, Frame f, int variant)
    {
        if (f == null) return;
        if (variant % 2 == 0)
        {
            Gurney(r, f, -1.1f, 0.6f, 4f, false);
            Gurney(r, f, 1.2f, 0.6f, -5f, true);
            InFrame(r, f, "WheelChair", 2.6f, 0.9f, 25f, 1f, false);
        }
        else
        {
            InFrame(r, f, "MedRack", -0.5f, 0.5f, 0f, 1f, true);
            InFrame(r, f, "WheelChair", 0.9f, 0.9f, 170f, 1f, false);
            Topple(r, InFrame(r, f, "ChairWhite", -1.7f, 0.8f, 30f));
            InFrame(r, f, "alch:Alch_BookPileA", 1.8f, 0.4f, 10f, 1f, false, false);
        }
    }

    /// <summary>Low wall rhythm: a pair of small things that make a wall stretch read as lived-in. `k` picks which.</summary>
    private static void StyleRhythm(DressRoomCtx r, Frame f, int k)
    {
        if (f == null) return;
        switch (k % 6)
        {
            case 0:
                InFrame(r, f, "Heater", -0.2f, 0.2f, 0f, 1f, true);

                break;
            case 1:
                HangAt(r, f, "LampWall", -0.3f, 2.2f);
                HangAt(r, f, "blood:Grime", 0.6f, 1.2f, 0f, 1.7f, true);
                break;
            case 2:
                HangAt(r, f, "PosterHuman", 0f, 1.75f, 0f, 0.8f);

                break;
            case 3:
                HangAt(r, f, "blood:ClawMarks", 0f, 1.35f, 0f, 1.0f, true);
                HangAt(r, f, "blood:Handprint", 0.9f, 1.1f, 0f, 0.4f, true);
                break;
            case 4:
                HangAt(r, f, "blood:Drip", 0f, 1.5f, 0f, 1.3f, true);
                Decal(r, "FloorGrime", f.W(0.2f, 0.5f), 1.8f, r.rng.Next(360));
                break;
            default:
                InFrame(r, f, "Bench", 0f, 0.3f, 0f, 1f, true);

                break;
        }
    }

    // ---------------------------------------------------------------- ground hall

    private static void BuildHallGround(AsylumHub hub)
    {
        PropsPiece piece = PropsPieces().First(p => p.name == "HallGround");
        DevPropsResetQuiet(hub, piece);
        DressRoomCtx r = NewDressCtx(hub, Def("HallDownstair"), piece.Prefix);
        float zc = -54.5f;
        Vector3 axis = Vector3.forward;
        // clusters in the dark gaps between the lamp pools (lamps at x 78, 85, 92, 100, 107, 115, 122), alternating sides
        (float x, int side, int kind, int variant)[] clusters =
        {
            (81.5f, +1, 0, 0), (88.5f, -1, 0, 1), (96f, -1, 0, 2), (111f, -1, 1, 0), (118.5f, +1, 1, 1),
        };
        foreach (var c in clusters)
        {
            Frame f = FindWallEither(r, new Vector3(c.x, 0f, zc), axis, c.side);
            if (c.kind == 0) StyleSupply(r, f, c.variant); else StyleMedical(r, f, c.variant);
            if (f == null) r.rejects.Add($"no wall for the cluster at x {c.x}");
        }
        // low rhythm between them
        float[] rhythm = { 78.5f, 84.5f, 92f, 104f, 108f, 114.5f, 121.5f, 123f, 86.5f, 99f };
        for (int i = 0; i < rhythm.Length; i++)
        {
            Frame f = FindWallEither(r, new Vector3(rhythm[i], 0f, zc), axis, i % 2 == 0 ? -1 : +1, 0.7f);
            StyleRhythm(r, f, i + (i > 4 ? 1 : 0));
        }
        // the dead end west of the shop door: a gurney angled so it narrows the passage (off every lane) and a stack of boxes
        Frame west = FindWallEither(r, new Vector3(76.3f, 0f, zc), axis, +1, 0.6f);
        Gurney(r, west, 0f, 0.9f, 28f, true);
        Placed bench = Put(r, "Bench", new Vector3(76.0f, r.def.floorY, -56.2f), 0f, 1f, null, true);
        // the dust of a hall nobody sweeps
        for (int i = 0; i < 4; i++) Decal(r, "FloorGrime", new Vector3(80f + i * 11f + Jit(r, 2f), 0f, zc + Jit(r, 1.2f)), 2.2f, r.rng.Next(360));
        PropsFinishCtx(r, piece);
    }

    // ---------------------------------------------------------------- upper hall: an overflow ward

    private static void BuildHallUpper(AsylumHub hub)
    {
        PropsPiece piece = PropsPieces().First(p => p.name == "HallUpper");
        DevPropsResetQuiet(hub, piece);
        DressRoomCtx r = NewDressCtx(hub, Def("HallUpstair"), piece.Prefix);
        float zc = -54.5f;
        Vector3 axis = Vector3.forward;
        (float x, int side)[] beds = { (82.5f, +1), (96.5f, -1), (112.5f, +1) };
        foreach (var b in beds)
        {
            Frame f = FindWallEither(r, new Vector3(b.x, 0f, zc), axis, b.side, 1.0f);
            if (f == null) { r.rejects.Add($"no wall for the bed at x {b.x}"); continue; }
            Placed bed = InFrame(r, f, b.x > 100f ? "Mattress" : "Bed", 0f, 0.7f, 90f, 1f, true);
            if (bed != null && b.x < 100f) Sheet(On(r, bed, Body(r), 0f, 0f, bed.size.x > bed.size.z ? 90f : 0f, 1f, 0.03f));
            InFrame(r, f, "CurtainsMedic", 2.0f, 0.6f, 0f, 1f, true);
            InFrame(r, f, "MedRack_V2", -1.6f, 0.5f, 0f, 1f, true);
        }
        (float x, int side)[] chairs = { (88.5f, -1), (111f, -1) };
        foreach (var c in chairs)
        {
            Frame f = FindWallEither(r, new Vector3(c.x, 0f, zc), axis, c.side, 0.8f);
            for (int i = 0; i < 3; i++) InFrame(r, f, "ChairWhite", -0.9f + i * 0.9f, 0.35f, 180f + Jit(r, 6f), 1f, true);
            Placed wc = InFrame(r, f, "WheelChair", 2.2f, 0.7f, 180f + Jit(r, 12f), 1f, true);
        }
        float[] rhythm = { 79f, 85.5f, 92f, 100f, 104.5f, 108f, 114.5f, 122f, 90f, 121f, 76.5f, 99f };
        for (int i = 0; i < rhythm.Length; i++)
        {
            Frame f = FindWallEither(r, new Vector3(rhythm[i], 0f, zc), axis, i % 2 == 0 ? +1 : -1, 0.7f);
            if (f != null && i == 3) { InFrame(r, f, "Heater", -0.2f, 0.2f, 0f, 1f, true); HangAt(r, f, "pack:Pack_FuseBox", 1.0f, 1.3f); }
            else if (f != null && i == 6) { HangAt(r, f, "alch:Alch_Painting1", 0f, 1.7f); HangAt(r, f, "blood:Handprint", 1.3f, 1.2f, 0f, 0.4f, true); }
            else if (f != null && i == 9) HangAt(r, f, "alch:Alch_Painting2", 0f, 1.7f);
            else StyleRhythm(r, f, i);
        }
        for (int i = 0; i < 3; i++) Decal(r, "FloorGrime", new Vector3(84f + i * 14f + Jit(r, 2f), 0f, zc + Jit(r, 1.2f)), 2.2f, r.rng.Next(360));
        PropsFinishCtx(r, piece);
    }

    // ---------------------------------------------------------------- basement hall: a morgue annex

    private static void BuildHallBasement(AsylumHub hub)
    {
        PropsPiece piece = PropsPieces().First(p => p.name == "HallBasement");
        DevPropsResetQuiet(hub, piece);
        DressRoomCtx r = NewDressCtx(hub, Def("HallUnderground"), piece.Prefix);
        float xc = 119f;
        Vector3 axis = Vector3.right; // walls east and west of the N-S hall
        (float z, int side, int kind)[] spots =
        {
            (-52.5f, -1, 0), (-57.5f, +1, 1), (-61.8f, -1, 2), (-66.2f, +1, 3), (-69.2f, -1, 4),
        };
        foreach (var s in spots)
        {
            Frame f = FindWallEither(r, new Vector3(xc, 0f, s.z), axis, s.side, 0.8f);
            if (f == null) { r.rejects.Add($"no wall at z {s.z}"); continue; }
            switch (s.kind)
            {
                case 0: Gurney(r, f, 0f, 0.6f, 6f, true); InFrame(r, f, "Pipe", 1.9f, 0.3f, 0f, 1f, true); break;
                case 1: InFrame(r, f, "MorgueBox", 0f, 1.5f, 0f, 1f, true); break;
                case 2: InFrame(r, f, "Tank_V2", 0f, 0.7f, 0f, 1f, true); HangAt(r, f, "pack:Pack_AirVent", 2.0f, 2.4f); break;
                case 3: Gurney(r, f, 0f, 0.6f, -7f, true); InFrame(r, f, "Pipe", -1.9f, 0.3f, 0f, 1f, true); Decal(r, "BloodSmear", f.W(0.4f, 1.6f), 1.5f, r.rng.Next(360)); break;
                default: InFrame(r, f, "Pipe", -0.4f, 0.3f, 0f, 1f, true); InFrame(r, f, "Pipe", 0.4f, 0.3f, 0f, 1f, true); break;
            }
        }
        float[] zs = { -50.9f - 0.6f, -54.5f, -59.5f, -63.8f, -68f };
        for (int i = 0; i < zs.Length; i++)
        {
            Frame f = FindWallEither(r, new Vector3(xc, 0f, zs[i]), axis, i % 2 == 0 ? +1 : -1, 0.6f);
            if (f == null) continue;
            if (i == 1) HangAt(r, f, "pack:Pack_AirVent", 0f, 2.4f);
            else if (i == 3) HangAt(r, f, "blood:Drip", 0f, 1.4f, 0f, 1.3f, true);
            else if (i == 4) HangAt(r, f, "blood:Drip", 0.3f, 1.5f, 0f, 1.1f, true);
            else HangAt(r, f, "blood:Grime", 0f, 1.2f, 0f, 1.6f, true);
        }
        // a body that hangs where the ceiling is high enough (the pack's rope fits per variant: HangingBodyFit, so a placement just has to find room)
        Placed hanging = Put(r, "pack:Pack_HangingBody", new Vector3(xc + 2.2f, r.def.floorY, -58.8f), 0f, 0.9f, null, true, false, 0.35f);
        for (int i = 0; i < 3; i++) Decal(r, "FloorGrime", new Vector3(xc + Jit(r, 1.4f), 0f, -53f - i * 6f), 2.0f, r.rng.Next(360));
        Decal(r, "BloodSmear", new Vector3(xc + 0.4f, 0f, -61f), 1.8f, 85f);
        PropsFinishCtx(r, piece);
    }
}
