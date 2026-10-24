using UnityEngine;

/// <summary>F92 phase D: stories that grow on the pack's own furniture (the rooms are already full, so small props go on top of what stands there).</summary>
public static partial class AsylumHubBuilder
{
    private static void DressRoomOnExisting(DressRoomCtx r)
    {
        switch (r.def.name)
        {
            case "Morgue":
            {
                Placed mt = Existing(r, "MorgueTable");
                if (mt != null)
                {
                    Sheet(On(r, mt, Body(r), 0f, 0f, mt.size.x > mt.size.z ? 90f : 0f, 1f, 0.02f));
                    Decal(r, "BloodPool", new Vector3(mt.center.x + 0.1f, 0f, mt.center.z + 0.6f), 2.2f, 20f);
                    Decal(r, "BloodSplat", new Vector3(mt.center.x - 0.7f, 0f, mt.center.z - 0.8f), 1.2f, 100f);
                    Placed tr = Beside(r, mt, "m:instrument_trolley", 1.3f, 2.6f);
                    On(r, tr, "m:bonesaw", -0.2f, -0.1f, 20f, 1.4f);
                    On(r, tr, "m:scissors", 0.25f, 0.15f, -35f, 1.6f);
                    On(r, tr, "m:instrument_tray", 0f, 0.2f, 0f, 1.8f);
                    Beside(r, mt, "m:trash_can", 1.4f, 3.0f, 1f, false);
                    Put(r, "d:drain", new Vector3(mt.center.x, r.def.floorY, mt.center.z), 0f, 1.5f, null, false);
                }
                Placed tw = Existing(r, "TableWhite");
                On(r, tw, "m:brain_jar", -0.2f, 0.1f, 0f, 1.2f);
                On(r, tw, "m:glass_jar", 0.3f, -0.3f, 30f, 1.2f);
                On(r, tw, "m:glass_jar", 0.35f, 0.4f, 100f, 1.2f);
                On(r, tw, "m:brain", -0.1f, -0.5f, 140f, 1.2f);
                On(r, tw, "m:notebook", -0.45f, -0.2f, 15f, 1f);
                Placed st = Existing(r, "SurgeryTable");
                On(r, st, "m:instrument_tray", 0f, 0f, 0f, 1.8f);
                On(r, st, "m:scalp", 0.2f, 0.2f, 70f, 2.5f);
                Placed wb = Existing(r, "MorgueWheelBed");
                if (wb != null) Decal(r, "BloodSmear", new Vector3(wb.center.x, 0f, wb.center.z - 1.7f), 1.7f, 70f);
                break;
            }
            case "Surgery":
            {
                Placed tw = Existing(r, "TableWhite");
                On(r, tw, "m:brain_jar", 0f, -0.4f, 0f, 1.2f);
                On(r, tw, "m:glass_jar", 0.1f, 0.4f, 60f, 1.2f);
                On(r, tw, "m:scissors", -0.2f, 0f, 20f, 1.6f);
                On(r, tw, "m:instrument_tray", 0.2f, -0.1f, 40f, 1.8f);
                Placed st = Existing(r, "SurgeryTable");
                On(r, st, "m:bonesaw", 0f, 0f, 30f, 1.5f);
                Placed ch = Existing(r, "SurgeryChair");
                if (ch != null)
                {
                    Decal(r, "BloodPool", new Vector3(ch.center.x + 0.2f, 0f, ch.center.z - 0.4f), 2.4f, 10f);
                    Decal(r, "BloodSmear", new Vector3(ch.center.x - 1.5f, 0f, ch.center.z + 0.2f), 1.6f, 80f);
                    Placed tr = Beside(r, ch, "m:instrument_trolley", 1.5f, 2.6f);
                    On(r, tr, "m:scissors", 0.1f, 0.1f, 40f, 1.6f);
                    On(r, tr, "m:scalp", -0.2f, 0f, 90f, 2.5f);
                    Topple(r, Beside(r, ch, "ChairWhite", 1.8f, 3.2f, 1f, false));
                    Beside(r, ch, "WheelChair", 2.0f, 3.5f);
                    Beside(r, ch, "Suspension", 1.4f, 3.0f);
                    Beside(r, ch, "m:trash_can", 1.8f, 3.5f, 1f, false);
                }
                break;
            }
            case "Office":
            {
                if (r != null) break; // F115: the Director's office is furnished by AddG10; a regenerate must not put the projector tableau back
                Placed to = Existing(r, "TableOffice (1)");
                On(r, to, "m:desk_lamp", 0.55f, -0.2f, 150f, 1.2f);
                On(r, to, "m:notebook", 0.2f, 0.45f, 20f, 1f);
                On(r, to, "m:pencil_holder", -0.4f, 0.4f, 0f, 1.4f);
                On(r, to, "m:photo_frame", -0.1f, -0.45f, 190f, 1.3f);
                On(r, to, "Keyboard", 0f, 0.1f, 0f, 1.1f);
                Placed ts = Existing(r, "TableSmall");
                On(r, ts, "FilmProjector", 0f, 0f, 90f);
                Placed so = Existing(r, "Sofa (1)");
                On(r, so, "Pillow", -0.5f, 0f, 20f, 1f);
                On(r, so, "Paper", 0.3f, 0.1f, 50f);
                Topple(r, Beside(r, to, "ChairLeather", 1.0f, 2.2f, 1f, false));
                Beside(r, to, "CaseMetallic", 1.4f, 3.0f);
                Beside(r, to, "BigShelf", 2.0f, 3.5f);
                break;
            }
            case "MachineRoom":
            {
                Placed m = Existing(r, "Mattress (1)");
                On(r, m, "Pillow", 0f, -0.7f, 90f, 1f);
                On(r, m, "Mug", 0.2f, 0.2f, 10f);
                On(r, m, "CanOld", -0.2f, 0.5f, 40f);
                if (m != null)
                {
                    Decal(r, "BloodSplat", new Vector3(m.center.x + 0.8f, 0f, m.center.z + 0.9f), 1.3f, 40f);
                    Topple(r, Beside(r, m, "ChairOffice", 1.3f, 2.8f, 1f, false));
                    Beside(r, m, "Bench", 1.5f, 3.0f);
                    Beside(r, m, "Crutches", 1.2f, 2.4f, 1f, false);
                }
                Placed sh = Existing(r, "BigShelf_Stock (7)");
                On(r, sh, "Box_V1", -0.2f, 0f, 0f);
                break;
            }
            case "OfficeA":
            {
                Placed to = Existing(r, "TableOffice (4)");
                On(r, to, "Paper", -0.3f, 0f, 30f);
                On(r, to, "Paper", 0.2f, 0.2f, 80f);
                On(r, to, "m:notebook", 0.3f, -0.3f, 15f, 1f);
                On(r, to, "m:desk_lamp", -0.4f, -0.4f, 20f, 1.2f);
                On(r, to, "m:pencil_holder", 0.4f, 0.4f, 0f, 1.4f);
                Placed t2 = Existing(r, "TableSmall (3)");
                On(r, t2, "Mug", 0f, 0f, 0f);
                On(r, t2, "Paper", 0.2f, 0.1f, 40f);
                Topple(r, Beside(r, to, "ChairOffice", 1.2f, 2.6f, 1f, false));
                break;
            }
        }
    }
}
