using System.Linq;
using TMPro;
using UnityEngine;

/// <summary>F92 phase D: the per-room stories. Frames: +Z points into the room (wall frames) and X runs along the wall.</summary>
public static partial class AsylumHubBuilder
{
    private static void DressRoomStory(DressRoomCtx r)
    {
        switch (r.def.name)
        {
            case "Morgue": Morgue(r); break;
            case "HallUnderground": HallUnderground(r); break;
            case "MachineRoom": MachineRoom(r); break;
            case "Surgery": Surgery(r); break;
            case "Office": Office(r); break;
            case "OfficeA": Records(r); break;
            case "DayRoom": DayRoom(r); break;
            case "Bedroom": Bedroom(r); break;
            case "Entrance": Entrance(r); break;
            case "HallDownstair": HallDown(r); break;
            case "HallUpstair": HallUp(r); break;
        }
        DressRoomOnExisting(r);
    }

    /// <summary>Lays a chair (or anything upright) on its side: a quarter turn about the horizontal axis across its yaw, reseated on the floor.</summary>
    private static void Topple(DressRoomCtx r, Placed p, float side = 1f)
    {
        if (p == null) return;
        Vector3 axis = Quaternion.Euler(0f, p.yaw, 0f) * Vector3.forward * side;
        p.go.transform.RotateAround(p.center, axis, 90f);
        Bounds b = RendBounds(p.go);
        p.go.transform.position += Vector3.up * (r.def.floorY + 0.01f - b.min.y);
        b = RendBounds(p.go);
        p.center = b.center; p.size = b.size;
    }

    private static void Stack(DressRoomCtx r, Frame f, string key, float lx, float lz, int count, float yawRel, float rise)
    {
        for (int i = 0; i < count; i++)
        {
            InFrame(r, f, key, lx + Jit(r, 0.04f), lz + Jit(r, 0.04f), yawRel + Jit(r, 10f), 1f, false, false, rise * i);
        }
    }

    // ---------------------------------------------------------------- basement

    private static void Morgue(DressRoomCtx r)
    {
        // A: an autopsy in progress - table, a body under a sheet, the trolley with its instruments, a pool and the drain below
        Frame a = FindArea(r, 3.3f, 2.1f, false);
        Placed table = InFrame(r, a, "m:autopsy_table", 0f, 1.3f, 0f);
        if (table != null)
        {
            Placed body = On(r, table, Body(r), 0f, 0f, table.size.x > table.size.z ? 90f : 0f, 1f, 0.06f);
            Sheet(body);
            DecalIn(r, a, "BloodPool", 0.2f, 1.3f, 2.3f);
            DecalIn(r, a, "BloodSmear", -1.4f, 0.4f, 1.4f, 20f);
        }
        Placed trolley = InFrame(r, a, "m:instrument_trolley", 1.9f, 1.1f, 90f);
        On(r, trolley, "m:bonesaw", -0.2f, -0.1f, 20f, 1.4f);
        On(r, trolley, "m:scissors", 0.25f, 0.15f, -35f, 1.6f);
        On(r, trolley, "m:instrument_tray", 0f, 0.2f, 0f, 1.8f);
        On(r, trolley, "m:scalp", 0.2f, -0.25f, 70f, 2.5f);

        // B: the specimen wall - a steel worktop, jars, the organ scale
        Frame b = FindArea(r, 3.2f, 1.4f, true);
        Placed top = InFrame(r, b, "m:steel_worktop", -0.7f, 0.95f, 90f, 1f, true);
        On(r, top, "m:brain_jar", -0.25f, 0.2f, 0f, 1.2f);
        On(r, top, "m:glass_jar", 0.2f, -0.3f, 30f, 1.2f);
        On(r, top, "m:glass_jar", 0.25f, 0.4f, 100f, 1.2f);
        On(r, top, "m:brain", -0.1f, -0.5f, 140f, 1.2f);
        On(r, top, "m:glove_dispenser", 0f, 0.7f, 90f, 1.3f);
        InFrame(r, b, "m:organ_scale", 1.2f, 0.6f, 180f, 1f, true);
        InFrame(r, b, "m:trash_can", 0.4f, 0.4f, 0f, 1f, true);

        // C: a covered body on the floor, gurney tipped beside it
        Frame c = FindArea(r, 3.0f, 2.2f, true);
        Placed floorBody = InFrame(r, c, Body(r), -0.3f, 1.2f, 25f);
        Sheet(floorBody);
        DecalIn(r, c, "BloodPool", -0.3f, 1.2f, 2.0f);
        DecalIn(r, c, "BloodSmear", 0.2f, 0.4f, 1.6f, 10f);
        InFrame(r, c, "m:corpse_drawer", 1.0f, 1.1f, 0f);
        InFrame(r, c, "m:ceramic_sink", -1.3f, 0.5f, 180f, 1f, true);

        // D: a desk by the wall where someone kept notes
        Frame d = FindArea(r, 2.4f, 1.6f, true);
        Placed desk = InFrame(r, d, "m:table", 0f, 0.8f, 90f, 1f, true);
        On(r, desk, "m:notebook", 0.1f, 0.2f, 15f);
        On(r, desk, "m:pencil_holder", -0.2f, -0.4f, 0f, 1.4f);
        On(r, desk, "m:desk_lamp", 0.3f, -0.7f, 200f, 1.2f);
        On(r, desk, "m:photo_frame", -0.3f, 0.6f, 10f, 1.3f);
        Topple(r, InFrame(r, d, "m:desk_chair", 0.5f, 1.5f, 40f));
        InFrame(r, d, "m:writing_board", 1.1f, 0.2f, 0f, 1f, true);

        Scatter(r, 2, true, 1.5f, "m:trash_can", "Box_V1", "Box_V2", "CanOld_Damaged");
        Scatter(r, 2, true, 1.5f, "Crutches", "m:cabinet");
        Litter(r, 6, "Paper", "CanOld_Damaged", "Plate_Broken", "Paper");
        BloodSpots(r, 4, 1.0f, 2.0f);
        for (int i = 0; i < 3; i++)
        {
            Cell cell = RandomCell(r, false);
            if (cell != null) Put(r, "d:drain", cell.pos, 0f, 1.4f, null, false);
        }
    }

    private static void HallUnderground(DressRoomCtx r)
    {
        Frame a = FindArea(r, 2.4f, 1.3f, true);
        Placed bed = InFrame(r, a, "MorgueWheelBed", 0f, 0.8f, 90f, 1f, true);
        Sheet(On(r, bed, Body(r), 0f, 0f, bed.size.x > bed.size.z ? 90f : 0f, 1f, 0.02f));
        InFrame(r, a, "WheelChair", 1.8f, 0.7f, 200f);
        DecalIn(r, a, "BloodSmear", 0.2f, 1.7f, 1.8f, 90f);

        Frame b = FindArea(r, 3.0f, 1.2f, true);
        InFrame(r, b, "SurgeryWheelBad", -0.5f, 0.9f, 0f, 1f, true);
        Stack(r, b, "Box_V1", 0.9f, 0.4f, 3, 0f, 0.23f);
        InFrame(r, b, "Box_V2", 1.3f, 0.5f, 25f);
        InFrame(r, b, "CanOld_Damaged", 0.4f, 1.0f, 70f, 1f, false, false);

        Frame c = FindArea(r, 2.4f, 1.3f, true);
        InFrame(r, c, "pack:Pack_MorgueBed", 0f, 0.8f, 0f, 1f, true);
        InFrame(r, c, "Heater", 1.4f, 0.2f, 0f, 1f, true);

        Scatter(r, 2, true, 2f, "pack:Pack_EmptyBed", "Bench", "Crutches");
        Litter(r, 7, "Paper", "CanOld_Damaged", "Plate_Broken");
        BloodSpots(r, 5, 1.2f, 2.2f);
    }

    private static void MachineRoom(DressRoomCtx r)
    {
        Frame a = FindArea(r, 3.0f, 1.8f, true);
        InFrame(r, a, "Tank_V2", -0.2f, 0.7f, 0f, 1f, true);
        Stack(r, a, "Box_V1", 1.6f, 0.5f, 4, 0f, 0.23f);
        InFrame(r, a, "Box_V2", 2.0f, 0.9f, 30f);

        Frame b = FindArea(r, 2.4f, 1.4f, true);
        Placed bench = InFrame(r, b, "Bench", 0f, 0.3f, 180f, 1f, true);
        On(r, bench, "Pot", 0.3f, 0f, 0f);
        On(r, bench, "Mug", -0.3f, 0f, 60f);
        InFrame(r, b, "CaseMetallic", 1.3f, 0.35f, 0f, 1f, true);
        Topple(r, InFrame(r, b, "ChairOffice", -1.0f, 1.0f, 30f));

        Frame c = FindArea(r, 2.2f, 1.2f, true);
        InFrame(r, c, "SmallMetalicCase", -0.4f, 0.4f, 0f, 1f, true);
        InFrame(r, c, "SmallMetalicCase", 0.6f, 0.4f, 0f, 1f, true);
        InFrame(r, c, "Heater", 0f, 0.2f, 0f, 1f, true);

        Scatter(r, 3, true, 2f, "Pipe", "Pipe", "Box_V1");
        Litter(r, 8, "CanOld", "CanOld_Damaged", "Paper", "Plate_Broken");
        BloodSpots(r, 3, 1.0f, 1.6f);
    }

    // ---------------------------------------------------------------- upper floor

    private static void Surgery(DressRoomCtx r)
    {
        Frame a = FindArea(r, 2.6f, 2.0f, false);
        Placed bed = InFrame(r, a, "SurgeryWheelBad", 0f, 1.0f, 90f);
        if (bed != null) Sheet(On(r, bed, Body(r), 0f, 0f, bed.size.x > bed.size.z ? 90f : 0f, 1f, 0.03f));
        DecalIn(r, a, "BloodPool", 0f, 1.0f, 1.8f);
        Placed t1 = InFrame(r, a, "m:instrument_trolley", 1.3f, 1.4f, 100f);
        On(r, t1, "m:bonesaw", -0.1f, 0f, 30f, 1.4f);
        On(r, t1, "m:scissors", 0.2f, 0.2f, -20f, 1.6f);
        On(r, t1, "m:scalp", 0.2f, -0.2f, 80f, 2.5f);

        Frame b = FindArea(r, 3.0f, 1.2f, true);
        Placed t2 = InFrame(r, b, "m:instrument_trolley", -0.9f, 0.6f, 180f);
        On(r, t2, "m:instrument_tray", 0f, 0f, 90f, 1.8f);
        On(r, t2, "m:glass_jar", 0.2f, 0.2f, 0f, 1.2f);
        InFrame(r, b, "MedRack", 0.5f, 0.55f, 0f, 1f, true);
        InFrame(r, b, "Suspension_V2", 1.6f, 0.3f, 0f, 1f, true, true, 0.3f);

        Frame c = FindArea(r, 2.2f, 1.3f, true);
        InFrame(r, c, "WheelChair", -0.3f, 0.9f, 160f);
        Topple(r, InFrame(r, c, "ChairWhite", 0.7f, 0.9f, 90f));
        InFrame(r, c, "Crutches", 1.0f, 0.1f, 0f, 1f, true);

        Scatter(r, 2, true, 1.6f, "m:glass_jar", "m:brain_jar", "m:trash_can", "Box_V2");
        Litter(r, 7, "Paper", "CanOld_Damaged", "Plate_Broken");
        BloodSpots(r, 5, 1.0f, 2.0f);
    }

    private static void Office(DressRoomCtx r)
    {
        if (r != null) return; // F115: the Director's office is furnished by AddG10 (AsylumHubBuilder.DirectorSuite*.cs); a regenerate must not put the clutter back
        Frame a = FindArea(r, 3.4f, 2.4f, true);
        InFrame(r, a, "Carpet", 0f, 1.4f, 0f, 1f, false, false);
        Placed desk = InFrame(r, a, "TableOffice", 0f, 0.65f, 0f, 1f, true);
        On(r, desk, "PC_Monitor", -0.35f, -0.05f, 180f);
        On(r, desk, "Keyboard", -0.35f, 0.4f, 180f, 1.1f);
        On(r, desk, "m:desk_lamp", 0.55f, -0.2f, 150f, 1.2f);
        On(r, desk, "m:notebook", 0.3f, 0.45f, 20f);
        On(r, desk, "m:pencil_holder", 0.6f, 0.5f, 0f, 1.4f);
        On(r, desk, "m:photo_frame", 0.2f, -0.45f, 190f, 1.3f);
        On(r, desk, "Paper", -0.1f, 0.2f, 40f);
        On(r, desk, "Mug", 0.8f, 0.4f, 30f);
        Placed chair = InFrame(r, a, "ChairLeather", -0.1f, 1.45f, 170f);
        InFrame(r, a, "Sofa_Chair", 1.9f, 1.8f, 230f);
        InFrame(r, a, "Sofa_Chair", -1.9f, 1.8f, 130f);
        InFrame(r, a, "TableSmall", 0f, 2.1f, 0f);

        Frame b = FindArea(r, 2.6f, 1.2f, true);
        InFrame(r, b, "BigShelf", 0f, 0.6f, 0f, 1f, true);
        Frame c = FindArea(r, 1.6f, 1.0f, true);
        InFrame(r, c, "CaseMetallic", 0f, 0.35f, 0f, 1f, true);
        Frame d = FindArea(r, 1.6f, 1.6f, false);
        Placed tbl = InFrame(r, d, "TableSmall", 0f, 0.8f, 0f);
        On(r, tbl, "FilmProjector", 0f, 0f, 90f);
        Frame e = FindArea(r, 1.2f, 0.6f, true);
        InFrame(r, e, "Antlers", -0.3f, 0.2f, 0f, 1f, true, true, 1.9f);
        InFrame(r, e, "MirrorShelf", 0.4f, 0.2f, 0f, 1f, true, true, 1.5f);

        Litter(r, 9, "Paper", "Paper", "Plate_Broken");
        BloodSpots(r, 1, 1.0f, 1.5f);
    }

    private static void Records(DressRoomCtx r)
    {
        Frame a = FindArea(r, 4.4f, 1.2f, true);
        InFrame(r, a, "BigShelf_Stock", -1.4f, 0.4f, 0f, 1f, true);
        InFrame(r, a, "BigShelf_Stock", 0f, 0.4f, 0f, 1f, true);
        InFrame(r, a, "BigShelf_Stock", 1.4f, 0.4f, 0f, 1f, true);
        Frame b = FindArea(r, 2.4f, 1.1f, true);
        InFrame(r, b, "SheetRack", -0.5f, 0.35f, 0f, 1f, true);
        InFrame(r, b, "CaseMetallic", 0.8f, 0.4f, 0f, 1f, true);
        Frame c = FindArea(r, 2.6f, 2.0f, false);
        Placed desk = InFrame(r, c, "TableOffice", 0f, 0.9f, 0f);
        On(r, desk, "Paper", -0.3f, 0f, 30f);
        On(r, desk, "Paper", 0.3f, 0.1f, 80f);
        On(r, desk, "Plate_Broken", 0.6f, -0.2f, 0f);
        On(r, desk, "Mug", -0.6f, 0.2f, 30f);
        Topple(r, InFrame(r, c, "ChairOffice", 0.3f, 1.8f, 20f));
        Frame d = FindArea(r, 2.0f, 1.0f, true);
        Stack(r, d, "Box_V1", -0.5f, 0.4f, 4, 0f, 0.23f);
        Stack(r, d, "Box_V2", 0.2f, 0.4f, 3, 20f, 0.23f);
        InFrame(r, d, "SmallMetalicCase", 0.9f, 0.4f, 0f, 1f, true);
        Litter(r, 14, "Paper", "Paper", "Paper", "CanOld");
        BloodSpots(r, 1, 0.8f, 1.2f);
    }

    private static void Bedroom(DressRoomCtx r)
    {
        for (int i = 0; i < 3; i++)
        {
            Frame f = FindArea(r, 1.2f, 2.3f, true);
            Placed m = InFrame(r, f, "Mattress", 0f, 1.1f, 0f, 1f, true);
            On(r, m, "Pillow", 0f, -0.75f, Jit(r, 10f));
            if (i == 1) On(r, m, "BedFeather", 0.1f, 0.2f, 40f);
        }
        Frame a = FindArea(r, 2.0f, 1.2f, true);
        Placed tb = InFrame(r, a, "Table_Bed", -0.5f, 0.4f, 0f, 1f, true);
        On(r, tb, "Mug", 0f, 0f, 0f);
        Topple(r, InFrame(r, a, "ChairWhite", 0.7f, 0.8f, 20f));
        Frame b = FindArea(r, 2.4f, 1.3f, true);
        InFrame(r, b, "pack:Pack_EmptyBed", 0f, 0.8f, 0f, 1f, true);
        InFrame(r, b, "Crutches", 1.1f, 0.1f, 0f, 1f, true);
        Frame c = FindArea(r, 2.0f, 1.6f, false);
        Placed bed = InFrame(r, c, "Bed", 0f, 1.0f, 90f);
        On(r, bed, "Pillow", -0.8f, 0f, 90f);
        DecalIn(r, c, "BloodSplat", 0.3f, 1.0f, 1.2f);
        Litter(r, 8, "BedFeather", "BedFeather", "Paper", "Pillow");
        BloodSpots(r, 3, 0.8f, 1.6f);
    }

    // ---------------------------------------------------------------- ground floor

    private static void DayRoom(DressRoomCtx r)
    {
        // a wheelchair turned to face the wall, a chair knocked over beside it
        Frame a = FindArea(r, 2.4f, 1.4f, true);
        InFrame(r, a, "WheelChair", -0.4f, 0.75f, 180f, 1f, true);
        Topple(r, InFrame(r, a, "ChairWhite", 0.8f, 0.9f, 60f));
        DecalIn(r, a, "BloodSplat", 0.2f, 0.6f, 1.0f);

        // the television corner
        Frame b = FindArea(r, 2.8f, 2.4f, true);
        Placed tv = InFrame(r, b, "TableWhite", 0f, 0.55f, 0f, 1f, true);
        On(r, tv, "Television", 0f, 0f, 0f);
        InFrame(r, b, "ChairWhite", -0.7f, 1.7f, 175f);
        InFrame(r, b, "ChairWhite", 0.0f, 1.9f, 180f);
        InFrame(r, b, "ChairWhite", 0.8f, 1.7f, 190f);

        // the sofa group on a carpet
        Frame c = FindArea(r, 3.6f, 2.8f, true);
        InFrame(r, c, "Carpet", 0f, 1.5f, 0f, 1f, false, false);
        InFrame(r, c, "Sofa", 0f, 0.45f, 0f, 1f, true);
        InFrame(r, c, "Sofa_Chair", -1.5f, 1.4f, 70f);
        InFrame(r, c, "Sofa_Chair", 1.5f, 1.4f, -70f);
        Placed low = InFrame(r, c, "TableSmall", 0f, 1.7f, 0f);
        On(r, low, "Mug", -0.3f, 0f, 20f);
        On(r, low, "Plate_Broken", 0.3f, 0.1f, 0f);
        On(r, low, "Kettle", 0f, -0.1f, 0f);

        // school tables: one chair thrown over
        Frame d = FindArea(r, 2.8f, 2.2f, false);
        Placed s1 = InFrame(r, d, "TableSchool", -0.7f, 1.0f, 0f);
        On(r, s1, "Paper", 0f, 0f, 15f);
        Placed s2 = InFrame(r, d, "TableSchool", 0.8f, 1.1f, 15f);
        On(r, s2, "Plate_Broken", 0f, 0f, 0f);
        InFrame(r, d, "ChairSchool", -0.8f, 1.8f, 170f);
        InFrame(r, d, "ChairSchool", 0.9f, 1.9f, 180f);
        Topple(r, InFrame(r, d, "ChairSchool", 0.1f, 0.2f, 60f), -1f);

        Frame e = FindArea(r, 3.6f, 1.2f, true);
        InFrame(r, e, "Bench", -0.9f, 0.3f, 0f, 1f, true);
        InFrame(r, e, "Bench", 0.9f, 0.3f, 0f, 1f, true);
        InFrame(r, e, "Crutches", 1.7f, 0.05f, 0f, 1f, true);
        Frame g = FindArea(r, 2.4f, 1.3f, true);
        InFrame(r, g, "WheelChair", -0.3f, 0.8f, 215f);
        InFrame(r, g, "Crutches", 1.0f, 0.1f, 0f, 1f, true);
        Frame h = FindArea(r, 2.4f, 1.4f, true);
        InFrame(r, h, "pack:Pack_EmptyBed", 0f, 0.9f, 0f, 1f, true);
        Placed body = InFrame(r, h, Body(r), 1.2f, 1.4f, 50f);
        Sheet(body);
        DecalIn(r, h, "BloodPool", 1.2f, 1.4f, 1.8f);
        Frame p1 = FindArea(r, 2.2f, 0.5f, true);
        InFrame(r, p1, "PosterHuman", 0f, 0.1f, 0f, 1f, true, true, 1.0f);

        Scatter(r, 3, true, 2.5f, "ChairWhite", "ChairSchool", "Crutches");
        Litter(r, 11, "Paper", "Paper", "Plate_Broken", "CanOld", "Mug");
        BloodSpots(r, 4, 1.0f, 2.0f);
    }

    private static void Entrance(DressRoomCtx r)
    {
        Frame a = FindArea(r, 2.6f, 1.4f, true);
        if (a != null)
        {
            Placed desk = InFrame(r, a, "TableWhite", 0f, 0.65f, 0f, 1f, true);
            On(r, desk, "Lamp", -0.35f, 0f, 20f);
            On(r, desk, "Paper", 0.2f, 0.1f, 10f);
            On(r, desk, "Paper", 0.4f, -0.15f, 70f);
            On(r, desk, "Mug", -0.1f, 0.3f, 0f);
            InFrame(r, a, "ChairOffice", 0f, 1.5f, 160f);
            InFrame(r, a, "ChairWhite", 1.5f, 0.6f, 120f);
            Sign(r, a, "KARLHEINZ ASYLUM\nADMISSIONS", 0f, 0.06f, 2.35f);
            InFrame(r, a, "MapGroundFloor", -1.1f, 0.1f, 0f, 1f, true, true, 1.0f);
        }
        Frame b = FindArea(r, 2.4f, 1.2f, true);
        InFrame(r, b, "Bench", 0f, 0.3f, 0f, 1f, true);
        InFrame(r, b, "Crutches", 1.2f, 0.05f, 0f, 1f, true);
        Litter(r, 5, "Paper", "Paper", "Mug");
    }

    private static void HallDown(DressRoomCtx r)
    {
        for (int i = 0; i < 2; i++)
        {
            Frame f = FindArea(r, 2.4f, 1.3f, true);
            Placed bed = InFrame(r, f, i == 0 ? "MorgueWheelBed" : "SurgeryWheelBad", 0f, 0.8f, 90f, 1f, true);
            if (bed != null && i == 0) Sheet(On(r, bed, Body(r), 0f, 0f, bed.size.x > bed.size.z ? 90f : 0f, 1f, 0.02f));
            DecalIn(r, f, "BloodSmear", 1.3f, 0.9f, 1.6f, 60f);
        }
        Frame a = FindArea(r, 2.2f, 1.2f, true);
        InFrame(r, a, "WheelChair", -0.4f, 0.8f, 0f, 1f, true);
        InFrame(r, a, "WheelChair", 0.6f, 0.9f, 200f);
        Frame b = FindArea(r, 2.2f, 1.2f, true);
        Stack(r, b, "Box_V1", -0.4f, 0.4f, 4, 0f, 0.23f);
        Stack(r, b, "Box_V2", 0.3f, 0.4f, 2, 25f, 0.23f);
        InFrame(r, b, "CanOld_Damaged", 1.0f, 0.5f, 40f, 1f, false, false);
        Frame c = FindArea(r, 2.2f, 1.2f, true);
        InFrame(r, c, "Bench", -0.6f, 0.3f, 0f, 1f, true);
        InFrame(r, c, "Heater", 0.8f, 0.2f, 0f, 1f, true);
        Frame d = FindArea(r, 2.6f, 1.3f, true);
        InFrame(r, d, "pack:Pack_MorgueBed", 0f, 0.8f, 0f, 1f, true);
        Topple(r, InFrame(r, d, "ChairOffice", 1.4f, 1.0f, 20f));
        Scatter(r, 3, true, 4f, "Bench", "Heater", "CupboardLight_Broken", "Crutches");
        Litter(r, 12, "Paper", "Paper", "Paper", "CanOld_Damaged", "Plate_Broken", "Mug");
        BloodSpots(r, 6, 1.2f, 2.4f);
    }

    private static void HallUp(DressRoomCtx r)
    {
        Frame a = FindArea(r, 2.4f, 1.3f, true);
        Placed bed = InFrame(r, a, "SurgeryWheelBad", 0f, 0.8f, 90f, 1f, true);
        if (bed != null) Sheet(On(r, bed, Body(r), 0f, 0f, bed.size.x > bed.size.z ? 90f : 0f, 1f, 0.03f));
        InFrame(r, a, "WheelChair", 1.6f, 0.8f, 170f);
        Frame b = FindArea(r, 2.2f, 1.2f, true);
        Stack(r, b, "Box_V1", 0f, 0.4f, 3, 0f, 0.23f);
        InFrame(r, b, "Box_V2", 0.7f, 0.5f, 15f);
        Frame c = FindArea(r, 2.2f, 1.2f, true);
        InFrame(r, c, "Bench", 0f, 0.3f, 0f, 1f, true);
        InFrame(r, c, "Crutches", 1.0f, 0.05f, 0f, 1f, true);
        Scatter(r, 3, true, 4f, "Heater", "Bench", "CupboardLight", "Crutches");
        Litter(r, 9, "Paper", "Paper", "CanOld_Damaged", "Plate_Broken");
        BloodSpots(r, 4, 1.0f, 2.0f);
    }

    // ---------------------------------------------------------------- signs

    /// <summary>A dark board with lettering, hung on a wall (same look as the wing plaques).</summary>
    private static void Sign(DressRoomCtx r, Frame f, string text, float lx, float lz, float height)
    {
        GameObject back = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(back.GetComponent<Collider>());
        back.name = "Sign";
        back.transform.SetParent(r.group, false);
        back.transform.rotation = f.Rot;
        back.transform.position = f.W(lx, lz + 0.03f) + Vector3.up * (height - 0f);
        back.transform.position = new Vector3(back.transform.position.x, r.def.floorY + height, back.transform.position.z);
        back.transform.localScale = new Vector3(2.2f, 0.7f, 0.05f);
        Material mat = AssetDatabase_LoadPlateMaterial();
        if (mat != null) back.GetComponent<Renderer>().sharedMaterial = mat;

        GameObject textGo = new GameObject("SignText");
        textGo.transform.SetParent(r.group, false);
        textGo.transform.position = back.transform.position + f.Rot * Vector3.forward * 0.032f;
        textGo.transform.rotation = f.Rot * Quaternion.Euler(0f, 180f, 0f); // TextMeshPro reads from -Z
        TextMeshPro tmp = textGo.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 1.5f;
        tmp.characterSpacing = 3f;
        tmp.color = new Color(0.85f, 0.78f, 0.6f);
        tmp.rectTransform.sizeDelta = new Vector2(2.1f, 0.65f);
        r.placed++;
    }

    private static Material AssetDatabase_LoadPlateMaterial()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("Asylum_Plate t:Material");
        return guids.Length == 0 ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
    }
}
