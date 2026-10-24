using System;
using NUnit.Framework;
using UnityEngine;

namespace KeepItLit.Tests
{
    /// <summary>F122: the Patient Record store, its unlock predicates and its commit points. Runs against a throw-away PlayerPrefs key (PatientRecord.KeyOverride): the real record is never touched.</summary>
    [Category("Fast")]
    public class PatientRecordTests
    {
        private string _key;

        [SetUp]
        public void SetUp()
        {
            _key = "kil-test-record-" + Guid.NewGuid().ToString("N");
            PatientRecord.KeyOverride = _key;
            StandardBaselineTests.Reset();
            PatientRecord.ResetShardBaseline(0);
        }

        [TearDown]
        public void TearDown()
        {
            StandardBaselineTests.Reset();
            PlayerPrefs.DeleteKey(_key);
            PlayerPrefs.DeleteKey(_key + ".unreadable");
            PatientRecord.KeyOverride = null;
        }

        [Test]
        public void TheRealKeyIsNeverUsedByTests()
        {
            Assert.AreEqual(_key, PatientRecord.Key);
            Assert.AreNotEqual(PatientRecord.DefaultKey, PatientRecord.Key);
        }

        [Test]
        public void AFreshRecord_IsEmpty_AndUnlocksNothing()
        {
            Assert.IsTrue(PatientRecord.IsEmpty);
            foreach (Unlock u in Enum.GetValues(typeof(Unlock))) Assert.IsFalse(PatientRecord.IsUnlocked(u), u.ToString());
            Assert.IsFalse(PatientRecord.HasNew);
            Assert.IsFalse(PlayerPrefs.HasKey(_key), "reading never writes");
        }

        [Test]
        public void RoundTrip_SurvivesDroppingTheCache()
        {
            PatientRecord.NoteCampaignStarted(DifficultyPreset.Nightmare, Assist.SteadyTorch, Oath.ShortFuse, StartingKit.Runner, 4);
            PatientRecord.NoteFloorCleared(3, Grade.A, 211.4f, false, 3);
            PatientRecord.NoteEnding(true, false, 4);
            PatientRecord.Reload();
            PatientRecordData d = PatientRecord.Data;
            Assert.AreEqual(1, d.campaignsStarted);
            Assert.AreEqual((int)DifficultyPreset.Nightmare, d.lastPreset);
            Assert.AreEqual((int)Assist.SteadyTorch, d.lastAssists);
            Assert.AreEqual((int)Oath.ShortFuse, d.lastOaths);
            Assert.AreEqual(4, d.lastTorch);
            Assert.AreEqual((int)Grade.A, d.bestGrade[3]);
            Assert.AreEqual(3, d.bestGradeOath[3]);
            Assert.AreEqual(1, d.floorsCleared);
            Assert.AreEqual(3, d.highestFloorCleared);
            Assert.AreEqual(1, d.endingsMask);
            Assert.AreEqual(5, d.bestOathDischargedPlus1);
            Assert.AreEqual(1, d.campaignsFinished);
        }

        [Test]
        public void ACorruptRecord_LoadsEmpty_WithOneWarning_AndIsKeptAside()
        {
            PlayerPrefs.SetString(_key, "{ this is not json");
            PatientRecord.Reload();
            Assert.IsTrue(PatientRecord.IsEmpty);
            Assert.AreEqual("{ this is not json", PlayerPrefs.GetString(_key + ".unreadable", ""));
            PatientRecord.NoteCampaignStarted(DifficultyPreset.Standard, Assist.None, Oath.None, StartingKit.StandardIssue, 0);
            Assert.AreEqual(1, PatientRecord.Data.campaignsStarted, "it still works afterwards");
        }

        [Test]
        public void ARecordFromALongerOrShorterArrayVersion_IsFitted()
        {
            PlayerPrefs.SetString(_key, "{\"version\":1,\"floorsCleared\":4,\"bestGrade\":[0,3],\"habitMax\":[1,2,3,0,0,9,9]}");
            PatientRecord.Reload();
            PatientRecordData d = PatientRecord.Data;
            Assert.AreEqual(FloorProfile.FinalFloor + 1, d.bestGrade.Length);
            Assert.AreEqual(3, d.bestGrade[1]);
            Assert.AreEqual(5, d.habitMax.Length);
            Assert.AreEqual(4, d.floorsCleared);
        }

        [Test]
        public void Unlocks_FollowTheirConditions()
        {
            PatientRecordData d = new PatientRecordData();
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.Codex, d));
            d.floorsCleared = 1;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.Codex, d));
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.KitScavenger, d));
            d.highestFloorCleared = 2;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.KitScavenger, d));
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.CardStethoscope, d));
            d.highestFloorCleared = 4;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.CardStethoscope, d));
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.ContractUnseen, d));
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.Oaths, d));
            d.endingsMask = 2;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.Oaths, d));
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.ColourBone, d));
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.CardArchivist, d), "needs both endings");
            d.endingsMask = 3;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.CardArchivist, d));
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.KitRunner, d));
            d.campaignsFinished = 2;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.KitRunner, d));
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.KitCartographer, d));
            d.campaignsFinished = 3;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.KitCartographer, d));
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.CardDynamo, d));
            d.totalShards = 1500;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.CardDynamo, d));
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.ContractsSkill, d));
            d.sGrades = 1;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.ContractsSkill, d));
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.ColourOathRed, d));
            d.bestOathLightsOutPlus1 = 6;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.ColourOathRed, d));
            Assert.IsFalse(PatientRecord.IsUnlocked(Unlock.ColourWard, d));
            d.metMask = PatientRecord.AllCodexMask;
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.ColourWard, d));
            Assert.AreEqual(13, Enum.GetValues(typeof(Unlock)).Length);
            foreach (Unlock u in Enum.GetValues(typeof(Unlock))) Assert.IsTrue(PatientRecord.IsUnlocked(u, d), u.ToString());
        }

        [Test]
        public void Bests_AreOnlyKeptForAnUnassistedCampaign_ButCountsAlwaysAre()
        {
            bool best = PatientRecord.NoteFloorCleared(2, Grade.S, 200f, true, 0);
            Assert.IsFalse(best);
            Assert.AreEqual(0, PatientRecord.Data.bestGrade[2]);
            Assert.AreEqual(1, PatientRecord.Data.floorsCleared, "an assisted clear still counts toward the unlocks");
            Assert.AreEqual(1, PatientRecord.Data.sGrades);
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.ContractsSkill));

            Assert.IsTrue(PatientRecord.NoteFloorCleared(2, Grade.B, 300f, false, 0));
            Assert.IsFalse(PatientRecord.NoteFloorCleared(2, Grade.C, 100f, false, 5), "a lower grade is not a best");
            Assert.IsTrue(PatientRecord.NoteFloorCleared(2, Grade.B, 300f, false, 2), "same grade, higher Oath rank");
            Assert.IsTrue(PatientRecord.NoteFloorCleared(2, Grade.B, 250f, false, 2), "same grade and rank, faster");
            Assert.IsFalse(PatientRecord.NoteFloorCleared(2, Grade.B, 260f, false, 2));
            Assert.IsTrue(PatientRecord.NoteFloorCleared(2, Grade.A, 900f, false, 0));
            Assert.AreEqual((int)Grade.A, PatientRecord.Data.bestGrade[2]);
        }

        [Test]
        public void Endings_RecordTheBestOathOnlyWhenUnassisted()
        {
            PatientRecord.NoteEnding(true, true, 6);
            Assert.AreEqual(1, PatientRecord.Data.endingsMask);
            Assert.AreEqual(0, PatientRecord.Data.bestOathDischargedPlus1, "assisted: no Oath record");
            PatientRecord.NoteEnding(false, false, 3);
            PatientRecord.NoteEnding(false, false, 1);
            Assert.AreEqual(4, PatientRecord.Data.bestOathLightsOutPlus1, "the best is kept");
            Assert.AreEqual(3, PatientRecord.Data.endingsMask);
            Assert.AreEqual(3, PatientRecord.Data.campaignsFinished);
        }

        [Test]
        public void Shards_AreBankedOnce_FromTheBaseline()
        {
            PlayerWallet.ResetCampaign();
            PlayerWallet.DebugDeposit(120);
            PatientRecord.ResetShardBaseline(0);
            PatientRecord.CommitShards();
            Assert.AreEqual(120, PatientRecord.Data.totalShards);
            PatientRecord.CommitShards();
            Assert.AreEqual(120, PatientRecord.Data.totalShards, "committing twice banks nothing twice");
            PlayerWallet.DebugDeposit(30);
            PatientRecord.CommitShards();
            Assert.AreEqual(150, PatientRecord.Data.totalShards);

            PlayerWallet.ResetCampaign(); // a new campaign, wallet back to 0 below the baseline
            PatientRecord.CommitShards();
            Assert.AreEqual(150, PatientRecord.Data.totalShards);
            GameFlow.IsDescent = true;
            PlayerWallet.DebugDeposit(500);
            PatientRecord.CommitShards();
            Assert.AreEqual(150, PatientRecord.Data.totalShards, "a descent banks nothing");
        }

        [Test]
        public void Met_NeedsTwentySeconds_AndFollowsTheRoster()
        {
            PatientRecord.NoteMet(EntityKind.Stalker, false, 5f);
            Assert.AreEqual(0, PatientRecord.Data.metMask, "too short a level");
            PatientRecord.NoteMet(EntityKind.Stalker | EntityKind.Tenant, false, 60f);
            Assert.IsTrue(PatientRecord.Met(CodexEntry.Hunter) && PatientRecord.Met(CodexEntry.Stan) && PatientRecord.Met(CodexEntry.Tenant));
            Assert.IsFalse(PatientRecord.Met(CodexEntry.Visitor));
            PatientRecord.NoteMet(EntityKind.Elias, true, 90f);
            Assert.AreEqual(PatientRecord.AllCodexMask, PatientRecord.Data.metMask);
            Assert.IsTrue(PatientRecord.IsUnlocked(Unlock.ColourWard));
        }

        [Test]
        public void NewMarks_ClearWhenViewed()
        {
            PatientRecord.NoteFloorCleared(1, Grade.C, 100f, false, 0);
            Assert.IsTrue(PatientRecord.HasNew);
            Assert.AreEqual(1, PatientRecord.NewCount);
            Assert.IsTrue(PatientRecord.IsNew(Unlock.Codex));
            PatientRecord.MarkUnlocksSeen();
            Assert.IsFalse(PatientRecord.HasNew);
            PatientRecord.Reload();
            Assert.IsFalse(PatientRecord.HasNew, "the seen mask is saved");
            PatientRecord.NoteFloorCleared(2, Grade.C, 100f, false, 0);
            Assert.AreEqual(1, PatientRecord.NewCount, "only the new one");
        }

        [Test]
        public void Habits_KeepTheHighestLevelEverSeen()
        {
            HunterMemory.ResetCampaign();
            HunterMemory.Learn(HunterMemory.Habit.Dark);
            HunterMemory.Learn(HunterMemory.Habit.Dark);
            PatientRecord.NoteHabits();
            HunterMemory.ResetCampaign();
            HunterMemory.Learn(HunterMemory.Habit.Dark);
            PatientRecord.NoteHabits();
            Assert.AreEqual(2, PatientRecord.Data.habitMax[(int)HunterMemory.Habit.Dark]);
            HunterMemory.ResetCampaign();
        }

        [Test]
        public void DeletingTheRecord_LeavesTheCampaignSaveAlone()
        {
            string saveKey = SaveSystem.Key;
            bool had = PlayerPrefs.HasKey(saveKey);
            PatientRecord.NoteCampaignStarted(DifficultyPreset.Standard, Assist.None, Oath.None, StartingKit.StandardIssue, 0);
            Assert.IsTrue(PlayerPrefs.HasKey(_key));
            PatientRecord.Delete();
            Assert.IsFalse(PlayerPrefs.HasKey(_key));
            Assert.IsTrue(PatientRecord.IsEmpty);
            Assert.AreEqual(had, PlayerPrefs.HasKey(saveKey));
        }
    }
}
