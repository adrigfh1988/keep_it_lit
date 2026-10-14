using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F101 phase D: the rooms. The five rooms F92 skipped (dining hall, kitchen store, stock, guard room, visiting room) are dressed, the two
/// seclusion cells get a RoomDef of their own, and every other room gets what the plan lists from the unused pack pieces (the Morgue Room PBR
/// pieces F92 referenced but could not place are retried by FindArea). Every piece is a marker (Dressing/&lt;Room&gt;/F101_&lt;Piece&gt;_Done) so
/// a prop moved or deleted by hand is never put back; placement is by bounds with the keep-out lanes, so NPC spots, mission items and the
/// chase end zone stay clear.
/// </summary>
public static partial class AsylumHubBuilder
{
    // The seclusion cells have no entry in DressRooms (the F92 table is never changed): their own rectangles, from the hub's SeclusionRoomA/B objects.
    private static readonly RoomDef SeclusionA = new RoomDef("SeclusionRoomA", 1.15f, 124.9f, 129.4f, -68.6f, -63.8f, true);
    private static readonly RoomDef SeclusionB = new RoomDef("SeclusionRoomB", 1.15f, 124.9f, 129.4f, -63.0f, -58.3f, true);

    private static RoomDef PropRoom(string name) => name == SeclusionA.name ? SeclusionA : name == SeclusionB.name ? SeclusionB : Def(name);

    static partial void PropsPiecesRooms(List<PropsPiece> list)
    {
        void Add(string room, System.Action<DressRoomCtx> body, string name = null)
        {
            string piece = name ?? room;
            list.Add(new PropsPiece { name = piece, phase = 'D', room = room, build = hub => RoomPass(hub, piece, room, body) });
        }
        Add("DiningRoom", RoomDining);
        Add("KitchenStock", RoomKitchenStock);
        Add("KitchenA", RoomKitchen);
        Add("Stock", RoomStock);
        Add("GuardRoom", RoomGuard);
        Add("VisitRoom", RoomVisit);
        Add("SeclusionRoomA", RoomSeclusion);
        Add("SeclusionRoomB", RoomSeclusion);
        Add("Bedroom", RoomBedroom);
        Add("Classroom", RoomClassroom);
        Add("OfficeA", RoomRecords);
        Add("Office", RoomDirector);
        Add("ExamRoom", RoomExam);
        Add("Surgery", RoomSurgery);
        Add("Morgue", RoomMorgue);
        Add("MachineRoom", RoomMachine);
        Add("DayRoom", RoomDay);
        Add("Entrance", RoomEntrance);
        Add("Gatehouse", RoomGatehouse);
    }

    private static void RoomPass(AsylumHub hub, string pieceName, string room, System.Action<DressRoomCtx> body)
    {
        PropsPiece piece = PropsPieces().First(p => p.name == pieceName);
        DevPropsResetQuiet(hub, piece);
        DressRoomCtx r = NewDressCtx(hub, PropRoom(room), piece.Prefix);
        body(r);
        PropsFinishCtx(r, piece);
    }

    // ---------------------------------------------------------------- small helpers

    /// <summary>A prop against a wall: a free w x d area at a wall, the prop backed onto it.</summary>
    private static Placed WallItem(DressRoomCtx r, string key, float w, float d, float yawRel = 0f, float scale = 1f)
    {
        Frame f = FindArea(r, w, d, true);
        return InFrame(r, f, key, 0f, d * 0.5f + 0.05f, yawRel, scale, true);
    }

    /// <summary>A prop on open floor: a free w x d area, the prop in its middle.</summary>
    private static Placed FloorItem(DressRoomCtx r, string key, float w, float d, float yawRel = 0f)
    {
        Frame f = FindArea(r, w, d, false);
        return InFrame(r, f, key, 0f, d * 0.5f, yawRel, 1f, false);
    }

    /// <summary>A hung prop / decal on a wall found by FindArea (a free 1.4 x 0.6 m patch of wall).</summary>
    private static Placed WallHang(DressRoomCtx r, string key, float height, float scale = 1f, bool decal = false, float yawRel = 0f)
    {
        Frame f = FindArea(r, 1.4f, 0.6f, true);
        return HangAt(r, f, key, 0f, height, yawRel, scale, decal);
    }

    private static void Chairs(DressRoomCtx r, Placed table, string chair, int count, float distance)
    {
        if (table == null) return;
        for (int i = 0; i < count; i++)
        {
            float a = (360f / count) * i + r.rng.Next(20);
            Vector3 pos = table.center + new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * (distance + Jit(r, 0.12f));
            Vector3 to = table.center - pos; to.y = 0f;
            Placed c = Put(r, chair, pos, Quaternion.LookRotation(to).eulerAngles.y + Jit(r, 14f));
            if (c != null && r.rng.Next(4) == 0) Topple(r, c);
        }
    }

    // ---------------------------------------------------------------- the rooms

    private static void RoomDining(DressRoomCtx r)
    {
        Placed[] tables = new Placed[5];
        for (int i = 0; i < 5; i++)
        {
            tables[i] = FloorItem(r, i % 2 == 0 ? "TableKitchen" : "TableWhiteKitchen", 1.9f, 1.9f, i * 90f);
            if (tables[i] == null) continue;
            On(r, tables[i], Pick(r, "Plate", "Plate_Broken"), -0.3f, 0.2f);
            On(r, tables[i], "Mug", 0.4f, -0.3f);
            Chairs(r, tables[i], "ChairWhite", 2, 1.15f);
        }
        WallItem(r, "KitchenShelf", 3.1f, 0.8f);
        Decal(r, "BloodPool", r.cells.Count > 0 ? r.cells[r.rng.Next(r.cells.Count)].pos : Vector3.zero, 2.0f, 30f);
        Litter(r, 4, "Plate", "Mug", "Plate_Broken");
    }

    private static void RoomKitchenStock(DressRoomCtx r)
    {
        WallItem(r, "Fridge", 0.8f, 0.7f);
        WallItem(r, "KitchenShelf", 3.1f, 0.8f);
        WallItem(r, "alch:Alch_CrateStack", 0.8f, 0.6f);
        Placed jugs = WallItem(r, "alch:Alch_Drawer1", 1.0f, 0.7f);
        On(r, jugs, "alch:Alch_Jugs", 0f, 0f, 15f);
        for (int i = 0; i < 4; i++) Litter(r, 1, "CanOld");
        Scatter(r, 2, true, 1.6f, "Box_V1", "Box_V2", "Pot");
    }

    private static void RoomKitchen(DressRoomCtx r)
    {
        WallItem(r, "Fridge", 0.8f, 0.7f);
        WallItem(r, "alch:Alch_Cupboard1", 1.2f, 0.65f);
        Placed sink = WallItem(r, "alch:Alch_Sink", 1.0f, 0.7f);
        Scatter(r, 2, true, 2f, "Pot", "alch:Alch_CrateStack");
        for (int i = 0; i < 3; i++) Litter(r, 1, "CanOld");
        WallHang(r, "blood:Grime", 1.2f, 1.5f, true);
    }

    private static void RoomStock(DressRoomCtx r)
    {
        for (int i = 0; i < 3; i++) WallItem(r, "BigShelf_Stock", 1.6f, 0.75f);
        WallItem(r, "BigShelf", 2.4f, 1.15f);
        for (int i = 0; i < 4; i++)
        {
            Frame f = FindArea(r, 1.3f, 0.8f, true);
            Stack(r, f, i % 2 == 0 ? "Box_V1" : "Box_V2", 0f, 0.45f, 3 + r.rng.Next(3), Jit(r, 12f), 0.23f);
        }
        WallItem(r, "alch:Alch_TrunkStack", 0.8f, 0.6f);
        WallItem(r, "alch:Alch_CrateStack", 0.8f, 0.6f);
        for (int i = 0; i < 3; i++) WallItem(r, "CaseMetallic", 1.15f, 0.65f);
        Litter(r, 4, "Paper", "CanOld");
    }

    private static void RoomGuard(DressRoomCtx r)
    {
        Placed desk = WallItem(r, "TableOffice", 1.6f, 0.9f);
        if (desk != null)
        {
            On(r, desk, "PC_Monitor", -0.3f, -0.3f, 0f);
            On(r, desk, "Keyboard", -0.3f, 0.3f, 90f);
            On(r, desk, "Mug", 0.5f, 0.1f);
            On(r, desk, "Paper", 0.1f, 0f, 30f);
            Vector3 back = desk.center + Quaternion.Euler(0f, desk.yaw, 0f) * new Vector3(0f, 0f, 1.0f);
            Put(r, "ChairOffice", back, desk.yaw + 190f);
        }
        WallItem(r, "alch:Alch_Cupboard2", 1.2f, 0.65f);
        Litter(r, 3, "Paper", "Mug");
    }

    private static void RoomVisit(DressRoomCtx r)
    {
        Placed table = FloorItem(r, "TableWhite", 1.8f, 1.5f, 90f);
        if (table != null)
        {
            Vector3 along = Quaternion.Euler(0f, table.yaw, 0f) * Vector3.forward;
            Put(r, "ChairWhite", table.center + along * 1.0f, table.yaw + 180f);
            Put(r, "ChairWhite", table.center - along * 1.0f, table.yaw);
            On(r, table, "Mug", 0.3f, 0.2f);
        }
        WallItem(r, "Bench", 1.9f, 0.5f);
        Litter(r, 2, "Paper");
    }

    private static void RoomSeclusion(DressRoomCtx r)
    {
        Placed mat = FloorItem(r, "Mattress", 1.0f, 2.1f, 0f);
        if (mat != null) On(r, mat, "Pillow", 0f, -0.8f, 0f, 1f, 0.01f);
        for (int i = 0; i < 3; i++) WallHang(r, "blood:ClawMarks", 1.2f + 0.2f * i, 1.0f, true);
        WallHang(r, "blood:Handprint", 1.1f, 0.45f, true);
        WallHang(r, "blood:Grime", 1.3f, 1.6f, true);
    }

    private static void RoomBedroom(DressRoomCtx r)
    {
        for (int i = 0; i < 2; i++) WallItem(r, "pack:Pack_EmptyBed", 2.1f, 1.0f, 90f);
        WallItem(r, "CurtainsMedic", 2.3f, 0.7f);
        for (int i = 0; i < 2; i++) WallItem(r, "Table_Bed", 0.5f, 0.7f);
        WallItem(r, "Suspension_V2", 2.7f, 0.4f);
        Litter(r, 3, "Paper", "Plate_Broken");
    }

    private static void RoomClassroom(DressRoomCtx r)
    {
        // against the walls only: Marta's chair and the board's view stay open, and so does the way in
        for (int i = 0; i < 3; i++)
        {
            Placed table = WallItem(r, "TableSchool", 1.25f, 0.7f);
            if (table != null && i % 2 == 0) On(r, table, "Paper", 0f, 0f, 20f);
        }
        for (int i = 0; i < 4; i++)
        {
            Placed c = WallItem(r, "ChairSchool", 0.55f, 0.75f, 180f);
            if (c != null && i == 2) Topple(r, c);
        }
        WallHang(r, "blood:Handprint", 1.1f, 0.4f, true);
    }

    private static void RoomRecords(DressRoomCtx r)
    {
        for (int i = 0; i < 2; i++) WallItem(r, "BigShelf_Stock", 1.6f, 0.75f);
        Placed drawer = WallItem(r, "alch:Alch_Drawer2", 1.0f, 0.7f);
        if (drawer != null) On(r, drawer, "alch:Alch_BookPileB", 0f, 0f, 10f);
        WallItem(r, "alch:Alch_BookPileC", 0.9f, 0.4f);
        WallItem(r, "alch:Alch_BookPileD", 0.9f, 0.4f);
        Litter(r, 3, "Paper");
    }

    private static void RoomDirector(DressRoomCtx r)
    {
        if (r != null) return; // F115: the Director's office is furnished by AddG10 (AsylumHubBuilder.DirectorSuite*.cs)
        WallHang(r, "alch:Alch_Painting4", 1.8f);
        WallItem(r, "Cupboard", 2.1f, 0.65f);
        Frame f = FindArea(r, 3.0f, 2.2f, false);
        InFrame(r, f, "Carpet", 0f, 1.1f, 0f, 0.8f, false, false);
        Litter(r, 2, "Paper", "Mug");
    }

    private static void RoomExam(DressRoomCtx r)
    {
        WallItem(r, "MedRack_V2", 1.1f, 0.6f);
        WallItem(r, "CurtainsMedic", 2.3f, 0.7f);
        WallItem(r, "Sink_V1", 1.2f, 0.9f);
        WallHang(r, "m:glove_dispenser", 1.4f);
        WallItem(r, "m:ceramic_sink", 0.9f, 0.7f);
        FloorItem(r, "ChairWhite", 0.7f, 0.7f, r.rng.Next(360));
    }

    private static void RoomSurgery(DressRoomCtx r)
    {
        FloorItem(r, "SurgeryTable", 1.0f, 1.0f, 0f);
        for (int i = 0; i < 2; i++) WallItem(r, "MedRack", 1.4f, 0.9f);
        WallItem(r, "CurtainsMedic", 2.3f, 0.7f);
        WallItem(r, "Suspension_V2", 2.7f, 0.4f);
        WallItem(r, "m:steel_worktop", 2.0f, 0.8f);
        WallItem(r, "m:metal_sink", 1.2f, 0.8f);
    }

    private static void RoomMorgue(DressRoomCtx r)
    {
        for (int i = 0; i < 2; i++) FloorItem(r, "m:autopsy_table", 2.4f, 1.2f, i * 90f);
        WallItem(r, "m:corpse_drawer", 2.0f, 0.9f);
        WallItem(r, "m:organ_scale", 0.7f, 0.5f);
        WallItem(r, "m:shelf", 1.6f, 0.6f);
        WallHang(r, "m:light_switch", 1.3f);
        WallItem(r, "m:table", 1.4f, 0.8f);
        WallItem(r, "m:steel_worktop", 2.0f, 0.8f);
    }

    private static void RoomMachine(DressRoomCtx r)
    {
        WallItem(r, "BigBoiler", 1.8f, 2.3f);
        WallItem(r, "Tank_V2", 2.3f, 1.2f);
        for (int i = 0; i < 3; i++) WallItem(r, "Pipe", 0.3f, 0.3f);
        for (int i = 0; i < 2; i++) WallHang(r, "pack:Pack_FuseBox", 1.4f);
    }

    private static void RoomDay(DressRoomCtx r)
    {
        WallItem(r, "Board", 4.2f, 0.8f);
        WallItem(r, "Sofa", 1.8f, 0.8f);
        Placed tv = WallItem(r, "TableSmall", 1.25f, 0.7f);
        if (tv != null) On(r, tv, "Television", 0f, 0f, 0f);
        WallItem(r, "MirrorShelf", 0.5f, 0.2f);
        WallHang(r, "alch:Alch_Painting3", 1.7f);
        Frame f = FindArea(r, 1.0f, 2.0f, false);
        InFrame(r, f, "alch:Alch_Rug1", 0f, 1.0f, 0f, 1.5f, false, false);
        // hub-only scare: it reads as the hunter - that is the point here, in the darkest corner
        Placed shroud = WallItem(r, "pack:Pack_StandingShroud", 0.6f, 0.45f);
    }

    private static void RoomEntrance(DressRoomCtx r)
    {
        Frame f = FindArea(r, 4.4f, 0.6f, true);
        for (int i = 0; i < 6; i++) InFrame(r, f, "ChairWhite", -2.0f + i * 0.8f, 0.3f, 180f + Jit(r, 6f), 1f, true);
        WallItem(r, "Pot", 0.6f, 0.5f);
        WallItem(r, "Box_V1", 0.5f, 0.4f);
        for (int i = 0; i < 2; i++) WallItem(r, "CaseMetallic", 1.15f, 0.65f);
    }

    private static void RoomGatehouse(DressRoomCtx r)
    {
        Litter(r, 2, "Paper", "Mug");
    }
}
