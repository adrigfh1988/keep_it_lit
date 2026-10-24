using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace KeepItLit.Tests
{
    /// <summary>F118: SaveData round trip, version migration, problem classification and the file store. Pure, no Play mode.</summary>
    [Category("Fast")]
    public class SaveDataTests
    {
        private string _dir;
        private string _legacyKey;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "kil-tests-" + Guid.NewGuid().ToString("N"));
            _legacyKey = null;
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.LegacyKeyOverride = null;
            SaveSystem.ConsumeProblem();
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.DirectoryOverride = null;
            SaveSystem.LegacyKeyOverride = null;
            SaveSystem.ConsumeProblem();
            if (_legacyKey != null)
            {
                PlayerPrefs.DeleteKey(_legacyKey);
                PlayerPrefs.DeleteKey(_legacyKey + ".moved");
            }
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch (IOException) { }
        }

        // ------------------------------------------------------------ helpers

        private static string Fixture(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "Editor/Tests/Fixtures", name));

        /// <summary>Every public instance field set to a non-default value, by reflection, so fields added later are covered automatically.</summary>
        private static SaveData FullSave()
        {
            SaveData d = new SaveData();
            int n = 3;
            foreach (FieldInfo f in typeof(SaveData).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                n++;
                if (f.FieldType == typeof(int)) f.SetValue(d, n);
                else if (f.FieldType == typeof(bool)) f.SetValue(d, true);
                else if (f.FieldType == typeof(string)) f.SetValue(d, "v" + n);
                else if (f.FieldType == typeof(float)) f.SetValue(d, n + 0.5f);
                else if (f.FieldType == typeof(int[])) f.SetValue(d, new[] { n, n + 1, n + 2 });
                else Assert.Fail("SaveData." + f.Name + " has a type the round-trip test does not know: " + f.FieldType);
            }
            d.version = SaveSystem.Version;
            d.floor = 3;
            d.inShop = true;
            return d;
        }

        private static void AssertSame(SaveData a, SaveData b, params string[] ignore)
        {
            foreach (FieldInfo f in typeof(SaveData).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (Array.IndexOf(ignore, f.Name) >= 0) continue;
                object x = f.GetValue(a), y = f.GetValue(b);
                if (x is Array xa)
                {
                    Array ya = y as Array;
                    Assert.IsNotNull(ya, f.Name);
                    Assert.AreEqual(xa.Length, ya.Length, f.Name + ".Length");
                    for (int i = 0; i < xa.Length; i++) Assert.AreEqual(xa.GetValue(i), ya.GetValue(i), f.Name + "[" + i + "]");
                }
                else Assert.AreEqual(x, y, f.Name);
            }
        }

        private string MainPath => Path.Combine(_dir, "save.json");

        // ------------------------------------------------------------ pure parse

        [Test]
        public void RoundTrip_EveryFieldSurvives()
        {
            SaveData d = FullSave();
            Assert.IsTrue(SaveSystem.TryParse(JsonUtility.ToJson(d), out SaveData back, out var problem, out bool migrated), problem.ToString());
            Assert.IsFalse(migrated);
            AssertSame(d, back);
        }

        [Test]
        public void MigrateV1Fixture_BringsItToCurrentVersion()
        {
            string json = Fixture("save-v1.json");
            Assert.IsTrue(json.Contains("\"version\":1"));
            SaveData old = JsonUtility.FromJson<SaveData>(json);
            Assert.AreNotEqual(0, old.hudTintIndex, "fixture must carry a non-zero hudTintIndex so the step is exercised");

            Assert.IsTrue(SaveSystem.TryParse(json, out SaveData d, out var problem, out bool migrated), problem.ToString());
            Assert.IsTrue(migrated);
            Assert.AreEqual(SaveSystem.Version, d.version);
            Assert.AreEqual(0, d.hudTintIndex);
            Assert.AreEqual(1, d.ownedHudTints);
            AssertSame(old, d, "version", "hudTintIndex", "ownedHudTints");
        }

        [Test]
        public void Problems_AreClassified()
        {
            SaveSystem.LoadProblem p;
            Assert.IsFalse(SaveSystem.TryParse("{", out _, out p, out _));
            Assert.AreEqual(SaveSystem.LoadProblem.Corrupt, p);

            SaveData d = FullSave();
            d.version = SaveSystem.Version + 1;
            Assert.IsFalse(SaveSystem.TryParse(JsonUtility.ToJson(d), out _, out p, out _));
            Assert.AreEqual(SaveSystem.LoadProblem.TooNew, p);

            d.version = 0;
            Assert.IsFalse(SaveSystem.TryParse(JsonUtility.ToJson(d), out _, out p, out _));
            Assert.AreEqual(SaveSystem.LoadProblem.Corrupt, p);

            d.version = SaveSystem.Version;
            d.floor = 99;
            Assert.IsFalse(SaveSystem.TryParse(JsonUtility.ToJson(d), out _, out p, out _));
            Assert.AreEqual(SaveSystem.LoadProblem.OutOfRange, p);

            d.floor = FloorProfile.FinalFloor;
            d.inShop = true;
            Assert.IsFalse(SaveSystem.TryParse(JsonUtility.ToJson(d), out _, out p, out _));
            Assert.AreEqual(SaveSystem.LoadProblem.OutOfRange, p);
        }

        [Test]
        public void MigrationChain_IsComplete()
        {
            // Fails when someone bumps SaveSystem.Version without adding the matching SaveMigrations case.
            for (int v = 1; v < SaveSystem.Version; v++)
            {
                SaveData d = new SaveData { version = v, floor = 1 };
                Assert.IsTrue(SaveMigrations.Run(d), "no migration step from v" + v);
                Assert.AreEqual(SaveSystem.Version, d.version);
            }
        }

        // ------------------------------------------------------------ file store through SaveSystem

        [Test]
        public void WriteTwice_KeepsBackup_NoTemp()
        {
            SaveData a = FullSave(); a.shards = 111;
            SaveData b = FullSave(); b.shards = 222;
            var store = new SaveFileStore(_dir, "save.json");
            store.Write(JsonUtility.ToJson(a));
            Assert.IsFalse(File.Exists(store.BackupPath));
            store.Write(JsonUtility.ToJson(b));
            Assert.AreEqual(JsonUtility.ToJson(b), File.ReadAllText(MainPath));
            Assert.AreEqual(JsonUtility.ToJson(a), File.ReadAllText(store.BackupPath));
            Assert.IsFalse(File.Exists(store.TempPath));
        }

        [Test]
        public void CorruptMain_RestoresBackup_AndRewritesMain()
        {
            SaveData a = FullSave(); a.shards = 111;
            SaveData b = FullSave(); b.shards = 222;
            var store = new SaveFileStore(_dir, "save.json");
            store.Write(JsonUtility.ToJson(a));
            store.Write(JsonUtility.ToJson(b));
            File.WriteAllText(MainPath, "garbage");

            Assert.IsTrue(SaveSystem.TryLoad(out SaveData loaded));
            Assert.AreEqual(111, loaded.shards, "the .bak (A) is restored");
            Assert.AreEqual(JsonUtility.ToJson(a), File.ReadAllText(MainPath), "main rewritten with A");
            Assert.AreEqual(SaveSystem.LoadProblem.None, SaveSystem.ConsumeProblem());
        }

        [Test]
        public void Delete_RemovesMainAndBackup()
        {
            var store = new SaveFileStore(_dir, "save.json");
            store.Write(JsonUtility.ToJson(FullSave()));
            store.Write(JsonUtility.ToJson(FullSave()));
            SaveSystem.Delete();
            Assert.IsFalse(File.Exists(MainPath));
            Assert.IsFalse(File.Exists(store.BackupPath));
            Assert.IsFalse(SaveSystem.TryLoad(out _));
        }

        [Test]
        public void BothUnreadable_SetsAside_ReportsOnce()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(MainPath, "garbage");
            File.WriteAllText(MainPath + ".bak", "more garbage");

            Assert.IsFalse(SaveSystem.TryLoad(out _));
            Assert.IsTrue(File.Exists(Path.Combine(_dir, "save.unreadable.json")), "kept, not deleted");
            Assert.IsFalse(File.Exists(MainPath));
            Assert.AreEqual(SaveSystem.LoadProblem.Corrupt, SaveSystem.ConsumeProblem());
            Assert.AreEqual(SaveSystem.LoadProblem.None, SaveSystem.ConsumeProblem());

            Assert.IsFalse(SaveSystem.TryLoad(out _), "second load: no save");
            Assert.AreEqual(SaveSystem.LoadProblem.None, SaveSystem.ConsumeProblem(), "not re-reported");
        }

        [Test]
        public void TooNewSave_IsSetAside_NotDestroyed()
        {
            Directory.CreateDirectory(_dir);
            SaveData d = FullSave(); d.version = SaveSystem.Version + 7;
            string json = JsonUtility.ToJson(d);
            File.WriteAllText(MainPath, json);

            Assert.IsFalse(SaveSystem.TryLoad(out _));
            Assert.AreEqual(SaveSystem.LoadProblem.TooNew, SaveSystem.ConsumeProblem());
            Assert.AreEqual(json, File.ReadAllText(Path.Combine(_dir, "save.unreadable.json")));
        }

        [Test]
        public void V1FileSave_IsMigrated_AndOldTextBecomesBackup()
        {
            Directory.CreateDirectory(_dir);
            string v1 = Fixture("save-v1.json");
            File.WriteAllText(MainPath, v1);

            Assert.IsTrue(SaveSystem.TryLoad(out SaveData d));
            Assert.AreEqual(SaveSystem.Version, d.version);
            Assert.AreEqual(0, d.hudTintIndex);
            Assert.AreEqual(SaveSystem.Version, JsonUtility.FromJson<SaveData>(File.ReadAllText(MainPath)).version, "written back");
            Assert.AreEqual(v1, File.ReadAllText(MainPath + ".bak"), "the pre-migration text is the backup");
        }

        [Test]
        public void TmpOnly_IsNotTrusted()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(MainPath + ".tmp", JsonUtility.ToJson(FullSave()));
            Assert.IsFalse(SaveSystem.TryLoad(out _));
            Assert.AreEqual(SaveSystem.LoadProblem.None, SaveSystem.ConsumeProblem());
        }

        // ------------------------------------------------------------ legacy PlayerPrefs move (D3)

        [Test]
        public void LegacyPlayerPrefsSave_MovesToFile_Once()
        {
            _legacyKey = "kil-test-legacy-" + Guid.NewGuid().ToString("N");
            string v1 = Fixture("save-v1.json");
            PlayerPrefs.SetString(_legacyKey, v1);
            SaveSystem.LegacyKeyOverride = _legacyKey;

            Assert.IsTrue(SaveSystem.TryLoad(out SaveData d));
            Assert.AreEqual(SaveSystem.Version, d.version, "migrated on load");
            Assert.IsTrue(File.Exists(MainPath));
            Assert.IsFalse(PlayerPrefs.HasKey(_legacyKey), "legacy key removed after a verified copy");
            Assert.AreEqual(1, PlayerPrefs.GetInt(_legacyKey + ".moved", 0));
        }

        [Test]
        public void DirectoryOverride_NeverTouchesTheRealPlayerPrefsSave()
        {
            string before = PlayerPrefs.GetString(SaveSystem.Key, "<none>"); // read only
            bool hadKey = PlayerPrefs.HasKey(SaveSystem.Key);

            Assert.IsFalse(SaveSystem.TryLoad(out _));
            Assert.IsFalse(File.Exists(MainPath), "the file store stays empty");
            Assert.AreEqual(hadKey, PlayerPrefs.HasKey(SaveSystem.Key));
            Assert.AreEqual(before, PlayerPrefs.GetString(SaveSystem.Key, "<none>"), "real PlayerPrefs save untouched");
        }

        [Test]
        public void HunterVoiceHabit_RoundTripsThroughSaveData()
        {
            // F120: SaveData.hunterVoice is additive (P1.1): missing = 0 = it has not learned your voice.
            HunterMemory.ResetCampaign();
            try
            {
                Assert.IsTrue(HunterMemory.Learn(HunterMemory.Habit.Voice));
                Assert.IsTrue(HunterMemory.Learn(HunterMemory.Habit.Voice));
                SaveData data = new SaveData();
                HunterMemory.CaptureTo(data);
                Assert.AreEqual(2, data.hunterVoice);

                HunterMemory.ResetCampaign();
                HunterMemory.RestoreFrom(data);
                Assert.AreEqual(2, HunterMemory.Level(HunterMemory.Habit.Voice));

                HunterMemory.RestoreFrom(new SaveData()); // an older save: the field is missing = 0
                Assert.AreEqual(0, HunterMemory.Level(HunterMemory.Habit.Voice));
            }
            finally { HunterMemory.ResetCampaign(); }
        }

        [Test]
        public void HubBreachDone_RoundTripsThroughSaveData()
        {
            // F121: SaveData.hubBreachDone is additive (P1.1): missing = false = the hub breach has not happened yet.
            HubState.ResetCampaign();
            try
            {
                SaveData data = new SaveData { version = SaveSystem.Version, floor = 5 };
                HubState.CaptureTo(data);
                Assert.IsFalse(data.hubBreachDone);

                HubState.BreachDone = true;
                HubState.MealFloor = 3;
                HubState.CaptureTo(data);
                Assert.IsTrue(data.hubBreachDone);
                Assert.IsTrue(SaveSystem.TryParse(JsonUtility.ToJson(data), out SaveData back, out var problem, out _), problem.ToString());
                Assert.IsTrue(back.hubBreachDone, "survives the JSON round trip");

                HubState.ResetCampaign();
                Assert.IsFalse(HubState.BreachDone, "ResetCampaign clears it");
                HubState.RestoreFrom(back);
                Assert.IsTrue(HubState.BreachDone);
                Assert.AreEqual(3, HubState.MealFloor, "the meal floor beside it is untouched");

                HubState.RestoreFrom(new SaveData()); // an older save: the field is missing = false
                Assert.IsFalse(HubState.BreachDone);

                // the v1 fixture predates the field: reads false, and migration keeps it false
                Assert.IsTrue(SaveSystem.TryParse(Fixture("save-v1.json"), out SaveData old, out var oldProblem, out _), oldProblem.ToString());
                Assert.IsFalse(old.hubBreachDone);
            }
            finally { HubState.ResetCampaign(); }
        }

        [Test]
        public void NightNurse_RosterRules_AndSaveDataRoundTrip()
        {
            NightNurse.ResetCampaign();
            DarkBruno.ResetCampaign();
            try
            {
                Assert.IsFalse(EntityRoster.IsKiller(EntityKind.NightNurse), "NightNurse is a non-lethal harasser/rule entity");
                Assert.IsTrue(EntityRoster.IsKiller(EntityKind.Butcher), "DarkBruno (Butcher) is a lethal room-rotating killer entity");
                Assert.IsTrue(EntityRoster.IsKiller(EntityKind.Elias), "Elias (The Frost) is a lethal solo blackout predator");

                for (int i = 1; i <= 50; i++)
                {
                    int seed = i * 104729;

                    // 1. Never on Floor 1 or Depth 1
                    Assert.AreEqual(EntityKind.None, EntityRoster.For(false, 1, seed) & EntityKind.NightNurse, $"Story Floor 1 must never have NightNurse (seed {seed})");
                    Assert.AreEqual(EntityKind.None, EntityRoster.For(true, 1, seed) & EntityKind.NightNurse, $"DESCENT Depth 1 must never have NightNurse (seed {seed})");
                    Assert.AreEqual(EntityKind.None, EntityRoster.For(false, 1, seed) & EntityKind.Butcher, $"Story Floor 1 must never have Butcher (seed {seed})");
                    Assert.AreEqual(EntityKind.None, EntityRoster.For(true, 1, seed) & EntityKind.Butcher, $"DESCENT Depth 1 must never have Butcher (seed {seed})");

                    // 2. At most 1 extra entity (so Hunter + at most 1 extra = max 2 entities total) on every Story floor and DESCENT depth
                    NightNurse.SeenThisCampaign = true;
                    DarkBruno.SeenThisCampaign = true;
                    for (int f = 1; f <= 5; f++)
                    {
                        EntityKind s = EntityRoster.For(false, f, seed);
                        int bits = ((int)s & 1) + (((int)s >> 1) & 1) + (((int)s >> 2) & 1) + (((int)s >> 3) & 1) + (((int)s >> 4) & 1) + (((int)s >> 5) & 1) + (((int)s >> 6) & 1);
                        Assert.LessOrEqual(bits, 1, $"Story Floor {f} must have at most 1 extra entity (seed {seed}, got {s})");
                    }
                    for (int d = 1; d <= 15; d++)
                    {
                        EntityKind s = EntityRoster.For(true, d, seed);
                        int bits = ((int)s & 1) + (((int)s >> 1) & 1) + (((int)s >> 2) & 1) + (((int)s >> 3) & 1) + (((int)s >> 4) & 1) + (((int)s >> 5) & 1) + (((int)s >> 6) & 1);
                        Assert.LessOrEqual(bits, 1, $"DESCENT Depth {d} must have at most 1 extra entity (seed {seed}, got {s})");
                    }

                    // 3. Guaranteed at least once across Floors 2-3 (and Depths 2-3) for any seed even when SeenThisCampaign is already true
                    bool seenInStoryEarly = false;
                    bool seenInDescentEarly = false;
                    bool butcherInStoryEarly = false;
                    for (int f = 2; f <= 3; f++)
                    {
                        if ((EntityRoster.For(false, f, seed) & EntityKind.NightNurse) != 0) seenInStoryEarly = true;
                        if ((EntityRoster.For(true, f, seed) & EntityKind.NightNurse) != 0) seenInDescentEarly = true;
                        if ((EntityRoster.For(false, f, seed) & EntityKind.Butcher) != 0) butcherInStoryEarly = true;
                    }
                    Assert.IsTrue(seenInStoryEarly, $"Story Floors 2-3 must include NightNurse at least once (seed {seed})");
                    Assert.IsTrue(seenInDescentEarly, $"DESCENT Depths 2-3 must include NightNurse at least once (seed {seed})");
                    Assert.IsTrue(butcherInStoryEarly, $"Story Floors 2-3 must include Butcher at least once (seed {seed})");

                    // 4. Campaign-state guarantee: if !SeenThisCampaign by Floor 4 / Depth 4, Floor 4 / Depth 4 forces NightNurse as the single extra entity
                    NightNurse.SeenThisCampaign = false;
                    Assert.AreEqual(EntityKind.NightNurse, EntityRoster.For(false, 4, seed), "Floor 4 must guarantee NightNurse when !SeenThisCampaign");
                    Assert.AreEqual(EntityKind.NightNurse, EntityRoster.For(true, 4, seed), "Depth 4 must guarantee NightNurse when !SeenThisCampaign");

                    // And if !DarkBruno.SeenThisCampaign by Floor 5 / Depth 5, Floor 5 / Depth 5 forces Butcher as the single extra entity
                    NightNurse.SeenThisCampaign = true;
                    DarkBruno.SeenThisCampaign = false;
                    Assert.AreEqual(EntityKind.Butcher, EntityRoster.For(false, 5, seed), "Floor 5 must guarantee Butcher when !DarkBruno.SeenThisCampaign");
                    Assert.AreEqual(EntityKind.Butcher, EntityRoster.For(true, 5, seed), "Depth 5 must guarantee Butcher when !DarkBruno.SeenThisCampaign");
                }

                // 5. SaveData.nightNurseSeen and SaveData.darkBrunoSeen round-trip
                NightNurse.ResetCampaign();
                DarkBruno.ResetCampaign();
                SaveData data = new SaveData { version = SaveSystem.Version, floor = 3 };
                NightNurse.CaptureTo(data);
                DarkBruno.CaptureTo(data);
                Assert.IsFalse(data.nightNurseSeen);
                Assert.IsFalse(data.darkBrunoSeen);

                NightNurse.SeenThisCampaign = true;
                DarkBruno.SeenThisCampaign = true;
                NightNurse.CaptureTo(data);
                DarkBruno.CaptureTo(data);
                Assert.IsTrue(data.nightNurseSeen);
                Assert.IsTrue(data.darkBrunoSeen);
                Assert.IsTrue(SaveSystem.TryParse(JsonUtility.ToJson(data), out SaveData back, out var problem, out _), problem.ToString());
                Assert.IsTrue(back.nightNurseSeen);
                Assert.IsTrue(back.darkBrunoSeen);

                NightNurse.ResetCampaign();
                DarkBruno.ResetCampaign();
                Assert.IsFalse(NightNurse.SeenThisCampaign);
                Assert.IsFalse(DarkBruno.SeenThisCampaign);
                NightNurse.RestoreFrom(back);
                DarkBruno.RestoreFrom(back);
                Assert.IsTrue(NightNurse.SeenThisCampaign);
                Assert.IsTrue(DarkBruno.SeenThisCampaign);

                NightNurse.RestoreFrom(new SaveData());
                DarkBruno.RestoreFrom(new SaveData());
                Assert.IsFalse(NightNurse.SeenThisCampaign);
                Assert.IsFalse(DarkBruno.SeenThisCampaign);
            }
            finally
            {
                NightNurse.ResetCampaign();
                DarkBruno.ResetCampaign();
            }
        }

        [Test]
        public void CampaignLifecycle_ResetAllCampaignState_ClearsHubAndEntityState()
        {
            try
            {
                // Dirty all campaign-scoped and per-floor static state across subsystems (Bugs B1 & B2).
                HubState.BreachDone = true;
                HubState.MealFloor = 4;
                HubState.IsFinale = true;
                HubBreach.Retrying = true;
                HubBreach.InProgress = true;
                PlayerWallet.AwardBonus(50);
                HunterMemory.Learn(HunterMemory.Habit.Lockers);
                NightNurse.SeenThisCampaign = true;
                DarkBruno.SeenThisCampaign = true;
                Elias.RestoreFrom(new SaveData { eliasSeen = true });
                GameFlow.CurrentFloor = 4;

                CampaignLifecycle.ResetAllCampaignState();

                Assert.IsFalse(HubState.BreachDone, "HubState.BreachDone must be cleared");
                Assert.AreEqual(0, HubState.MealFloor, "HubState.MealFloor must be cleared");
                Assert.IsFalse(HubState.IsFinale, "HubState.IsFinale must be cleared");
                Assert.IsFalse(HubBreach.Retrying, "HubBreach.Retrying must be cleared");
                Assert.IsFalse(HubBreach.InProgress, "HubBreach.InProgress must be cleared");
                Assert.AreEqual(0, PlayerWallet.Shards, "PlayerWallet.Shards must be cleared");
                Assert.AreEqual(0, HunterMemory.Level(HunterMemory.Habit.Lockers), "HunterMemory must be cleared");
                Assert.IsFalse(NightNurse.SeenThisCampaign, "NightNurse.SeenThisCampaign must be cleared");
                Assert.IsFalse(DarkBruno.SeenThisCampaign, "DarkBruno.SeenThisCampaign must be cleared");
                Assert.IsFalse(Elias.SeenThisCampaign, "Elias.SeenThisCampaign must be cleared");
                Assert.AreEqual(1, GameFlow.CurrentFloor, "GameFlow.CurrentFloor must reset to 1");
                Assert.AreEqual(RunPhase.TitleMenu, GameFlow.Phase, "GameFlow.Phase must reset to TitleMenu");
                Assert.AreEqual(RunLoadIntent.ShowTitleMenu, GameFlow.LoadIntent, "GameFlow.LoadIntent must reset to ShowTitleMenu");
            }
            finally
            {
                GameFlow.IsDescent = false;
                CampaignLifecycle.ResetAllCampaignState();
            }
        }

        [Test]
        public void GameFlow_PhaseAndLoadIntent_StaySynchronizedWithLegacyFlags()
        {
            try
            {
                CampaignLifecycle.ResetAllCampaignState();
                Assert.AreEqual(RunPhase.TitleMenu, GameFlow.Phase);
                Assert.AreEqual(RunLoadIntent.ShowTitleMenu, GameFlow.LoadIntent);

                // Intake transition
                GameFlow.IsInIntake = true;
                Assert.AreEqual(RunPhase.Intake, GameFlow.Phase);
                Assert.IsTrue(GameFlow.IsInIntake);
                Assert.IsFalse(GameFlow.IsRunActive);

                // MazeActive transition via BeginRun()
                GameFlow.BeginRun();
                Assert.AreEqual(RunPhase.MazeActive, GameFlow.Phase);
                Assert.IsTrue(GameFlow.IsRunActive);
                Assert.IsFalse(GameFlow.IsInIntake);
                Assert.IsFalse(GameFlow.IsInHub);
                Assert.IsFalse(GameFlow.IsInShop);

                // Hub and Finale transitions
                GameFlow.IsRunActive = false;
                GameFlow.IsInHub = true;
                Assert.AreEqual(RunPhase.Hub, GameFlow.Phase);
                Assert.IsTrue(GameFlow.IsInHub);
                Assert.IsFalse(GameFlow.IsRunActive);

                HubState.IsFinale = true;
                Assert.AreEqual(RunPhase.HubFinale, GameFlow.Phase);
                Assert.IsTrue(GameFlow.IsInHub, "IsInHub remains true during Finale");

                HubState.IsFinale = false;
                Assert.AreEqual(RunPhase.Hub, GameFlow.Phase);

                // Shop transition
                GameFlow.IsInHub = false;
                GameFlow.IsInShop = true;
                Assert.AreEqual(RunPhase.LegacyShop, GameFlow.Phase);
                Assert.IsTrue(GameFlow.IsInShop);
                Assert.IsFalse(GameFlow.IsInHub);

                // Back to Title
                GameFlow.IsInShop = false;
                Assert.AreEqual(RunPhase.TitleMenu, GameFlow.Phase);

                // RunLoadIntent synchronization with legacy flags
                GameFlow.SkipMenuOnLoad = true;
                GameFlow.ArriveInHub = true;
                Assert.AreEqual(RunLoadIntent.ArriveInHub, GameFlow.LoadIntent);
                Assert.IsTrue(GameFlow.ArriveInHub);
                Assert.IsTrue(GameFlow.SkipMenuOnLoad);

                GameFlow.ArriveInHub = false;
                GameFlow.ResumeInShop = true;
                Assert.AreEqual(RunLoadIntent.ResumeInShop, GameFlow.LoadIntent);
                Assert.IsTrue(GameFlow.ResumeInShop);
                Assert.IsFalse(GameFlow.ArriveInHub);

                GameFlow.ResumeInShop = false;
                Assert.AreEqual(RunLoadIntent.StartFloor, GameFlow.LoadIntent);

                GameFlow.SkipMenuOnLoad = false;
                GameFlow.BeginCampaignOnLoad = true;
                Assert.AreEqual(RunLoadIntent.BeginNewCampaign, GameFlow.LoadIntent);
                Assert.IsTrue(GameFlow.BeginCampaignOnLoad);

                GameFlow.BeginCampaignOnLoad = false;
                Assert.AreEqual(RunLoadIntent.ShowTitleMenu, GameFlow.LoadIntent);
                Assert.IsFalse(GameFlow.SkipMenuOnLoad);
                Assert.IsFalse(GameFlow.ArriveInHub);
                Assert.IsFalse(GameFlow.ResumeInShop);
                Assert.IsFalse(GameFlow.BeginCampaignOnLoad);
            }
            finally
            {
                GameFlow.SkipMenuOnLoad = false;
                GameFlow.ArriveInHub = false;
                GameFlow.ResumeInShop = false;
                GameFlow.BeginCampaignOnLoad = false;
                GameFlow.IsRunActive = false;
                GameFlow.IsInIntake = false;
                GameFlow.IsInHub = false;
                GameFlow.IsInShop = false;
                CampaignLifecycle.ResetAllCampaignState();
            }
        }
    }
}
