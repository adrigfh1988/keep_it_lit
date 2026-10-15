using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace KeepItLit.Tests
{
    /// <summary>F122: presets, assists, Oaths, RunRules stacking, the terror clone, grades, the payout lines, saves. Pure, no Play mode.</summary>
    [Category("Fast")]
    public class CampaignRulesTests
    {
        private string _key;

        [SetUp]
        public void SetUp()
        {
            _key = "kil-test-record-" + Guid.NewGuid().ToString("N");
            PatientRecord.KeyOverride = _key;
            StandardBaselineTests.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            StandardBaselineTests.Reset();
            PlayerPrefs.DeleteKey(_key);
            PlayerPrefs.DeleteKey(_key + ".unreadable");
            PatientRecord.KeyOverride = null;
        }

        private static FloorProfile Profile(int floor, int seed = 12345)
        {
            System.Array.Clear(GameFlow.RolledObjectives, 0, GameFlow.RolledObjectives.Length);
            for (int f = 1; f < floor; f++) FloorProfile.For(f, seed); // RollObjectives reads the previous floor's pair
            FloorProfile p = FloorProfile.For(floor, seed);
            PlayerInventory.ApplyActiveModifiers(p);
            RunRules.Resolve(seed, floor);
            RunRules.ApplyToProfile(p);
            return p;
        }

        private static FloorProfile Plain(int floor)
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.None);
            return Profile(floor);
        }

        // ------------------------------------------------------------ presets and assists reach the knobs

        [Test]
        public void Nightmare_ShortensTheCountdown_AndSharpensTheHunter()
        {
            FloorProfile baseline = Plain(3);
            CampaignRules.SetForTest(DifficultyPreset.Nightmare, Assist.None, Oath.None);
            FloorProfile p = Profile(3);
            CampaignRules.ApplyToProfile(p);
            Assert.AreEqual(baseline.EscapeSeconds * 0.85f, p.EscapeSeconds, 0.001f);
            Assert.AreEqual(baseline.GateEscapeSeconds * 0.85f, p.GateEscapeSeconds, 0.001f);
            Assert.AreEqual(baseline.HearingScale * 1.15f, p.HearingScale, 0.0001f);
            Assert.AreEqual(baseline.ViewDistance * 1.1f, p.ViewDistance, 0.0001f);
            Assert.AreEqual(0.75f, p.ChaseCommitScale, 0.0001f);
            Assert.IsFalse(p.HunterEyesLitAtRest);
            Assert.AreEqual(1.2f, CampaignRules.DrainScale, 0.0001f);
            Assert.AreEqual(0.8f, CampaignRules.RechargeScale, 0.0001f);
            Assert.AreEqual(2, CampaignRules.OathRank, "NIGHTMARE alone is rank 2");
            Assert.AreEqual(1.25f, CampaignRules.ShardMultiplier, 0.0001f);
        }

        [Test]
        public void StoryAssists_LengthenTheCountdown_SlowTheHunter_AndBrightenTheHalls()
        {
            FloorProfile baseline = Plain(3);
            CampaignRules.SetForTest(DifficultyPreset.Story, OathCatalogue.PresetAssists(DifficultyPreset.Story), Oath.None);
            FloorProfile p = Profile(3);
            CampaignRules.ApplyToProfile(p);
            Assert.AreEqual(baseline.EscapeSeconds * 1.5f, p.EscapeSeconds, 0.001f);
            Assert.AreEqual(baseline.GateEscapeSeconds * 1.5f, p.GateEscapeSeconds, 0.001f);
            Assert.AreEqual(1.75f, p.ChaseCommitScale, 0.0001f);
            Assert.AreEqual(baseline.ChaseWalkFraction * 0.85f, p.ChaseWalkFraction, 0.0001f);
            Assert.AreEqual(baseline.RunSpeed * 0.9f, p.RunSpeed, 0.0001f);
            Assert.AreEqual(baseline.HearingScale * 0.85f, p.HearingScale, 0.0001f);
            Assert.IsTrue(p.HunterEyesLitAtRest);
            Assert.AreEqual(baseline.FogDensity * 0.8f, p.FogDensity, 0.0001f);
            Assert.AreEqual(baseline.LampIntensity * 1.25f, p.LampIntensity, 0.0001f);
            Assert.AreEqual(baseline.Ambient.r * 1.8f, p.Ambient.r, 0.0001f);
            Assert.AreEqual(0.7f, CampaignRules.DrainScale, 0.0001f);
            Assert.AreEqual(1.5f, CampaignRules.RechargeScale, 0.0001f);
            Assert.IsTrue(CampaignRules.IsAssisted);
            Assert.AreEqual(1f, CampaignRules.ShardMultiplier, "an assisted campaign gets no multiplier");
        }

        [Test]
        public void Story_TurnsOffTheLyingCues_AndFlipsTheF123Flag_WithoutTouchingTheSerializedInstance()
        {
            CampaignRules.SetForTest(DifficultyPreset.Story, OathCatalogue.PresetAssists(DifficultyPreset.Story), Oath.None);
            TerrorSettings original = new TerrorSettings();
            TerrorSettings t = CampaignRules.ApplyTerror(original);
            Assert.AreNotSame(original, t, "the rules edit a clone");
            Assert.IsFalse(t.contaminatedPhantoms); Assert.IsFalse(t.rationedEyes); Assert.IsFalse(t.unpredictableTelegraphs);
            Assert.IsFalse(t.torchFailures); Assert.IsFalse(t.wrongSounds); Assert.IsFalse(t.varyingSpottedSting);
            Assert.IsFalse(t.deathReplay, "STORY includes NO DEATH REPLAY");
            Assert.IsTrue(t.survivableGrab, "STORY keeps the survivable grab");
            Assert.IsTrue(original.contaminatedPhantoms && original.rationedEyes && original.torchSway && original.deathReplay, "the original is untouched");
            Assert.IsFalse(CampaignRules.UnreliableCues, "F123 reads this");
            GameFlow.IsDescent = true;
            Assert.IsTrue(CampaignRules.UnreliableCues, "a descent is outside the presets");
            Assert.AreSame(original, CampaignRules.ApplyTerror(original));
        }

        [Test]
        public void SteadyTorchOnly_SwitchesOffTheSway_AndIsNotAssisted()
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.SteadyTorch, Oath.None);
            TerrorSettings t = CampaignRules.ApplyTerror(new TerrorSettings());
            Assert.IsFalse(t.torchSway);
            Assert.IsTrue(t.contaminatedPhantoms);
            Assert.IsFalse(CampaignRules.IsAssisted, "a comfort option sets no records either way");
            Assert.IsTrue(CampaignRules.UnreliableCues);
        }

        // ------------------------------------------------------------ Oaths

        [Test]
        public void Oaths_StackOnTheProfile_InAFixedOrder()
        {
            FloorProfile baseline = Plain(4);
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.ShortFuse | Oath.FailingGrid | Oath.NoWaiting);
            FloorProfile p = Profile(4);
            CampaignRules.ApplyToProfile(p);
            Assert.AreEqual(baseline.EscapeSeconds * 0.75f, p.EscapeSeconds, 0.001f);
            Assert.AreEqual(baseline.Ambient.r * 0.6f, p.Ambient.r, 0.0001f);
            Assert.AreEqual(baseline.LampIntensity * 0.75f, p.LampIntensity, 0.0001f);
            Assert.AreEqual(Mathf.CeilToInt(baseline.CellsPerLamp * 1.25f), p.CellsPerLamp);
            Assert.AreEqual(0.75f, p.ChaseCommitScale, 0.0001f);
            Assert.GreaterOrEqual(p.ChaseWalkFraction, 1.1f);
            Assert.AreEqual(3, CampaignRules.OathRank);
            Assert.AreEqual(1f + 0.15f + 0.10f + 0.15f, CampaignRules.ShardMultiplier, 0.0001f);
            Assert.IsFalse(CampaignRules.IsAssisted);
        }

        [Test]
        public void ShortFuse_NeverDropsTheCountdownBelowThirtySeconds()
        {
            CampaignRules.SetForTest(DifficultyPreset.Nightmare, Assist.None, Oath.ShortFuse);
            FloorProfile p = Profile(5);
            CampaignRules.ApplyToProfile(p);
            Assert.GreaterOrEqual(p.EscapeSeconds, 30f);
            Assert.GreaterOrEqual(p.GateEscapeSeconds, 30f);
        }

        [Test]
        public void OldBattery_ForcesWeakBattery_OnFloorOne_AndStacksOnTheRolledRule()
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.OldBattery);
            RunRules.Resolve(12345, 1);
            Assert.AreEqual(RunRules.Rule.None, RunRules.Current, "the rolled rule is unchanged");
            Assert.IsTrue(RunRules.Has(RunRules.Rule.WeakBattery));
            Assert.AreEqual(0.55f, RunRules.BatteryStartFraction, 0.0001f);

            // a seed whose floor-2 roll is DyingGrid keeps it AND gains the forced rule
            int found = -1;
            for (int seed = 1; seed < 400 && found < 0; seed++)
            {
                CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.None);
                RunRules.Resolve(seed, 2);
                if (RunRules.Current == RunRules.Rule.DyingGrid) found = seed;
            }
            Assert.GreaterOrEqual(found, 0, "a DyingGrid seed exists");
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.OldBattery);
            RunRules.Resolve(found, 2);
            Assert.AreEqual(RunRules.Rule.DyingGrid, RunRules.Current);
            Assert.IsTrue(RunRules.Has(RunRules.Rule.DyingGrid) && RunRules.Has(RunRules.Rule.WeakBattery));
        }

        [Test]
        public void ForcedRule_EqualToTheRolledOne_IsNotAppliedTwice()
        {
            int found = -1;
            for (int seed = 1; seed < 800 && found < 0; seed++)
            {
                RunRules.Resolve(seed, 3);
                if (RunRules.Current == RunRules.Rule.WeakBattery) found = seed;
            }
            Assert.GreaterOrEqual(found, 0);
            RunRules.Resolve(found, 3);
            float start = RunRules.BatteryStartFraction, recharge = RunRules.BatteryRechargeMultiplier;
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.OldBattery);
            RunRules.Resolve(found, 3);
            Assert.AreEqual(start, RunRules.BatteryStartFraction);
            Assert.AreEqual(recharge, RunRules.BatteryRechargeMultiplier);
        }

        [Test]
        public void HearsEverything_ForcesKeenEars_OnEveryFloor()
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.HearsEverything);
            FloorProfile baseline = Plain(1);
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.HearsEverything);
            FloorProfile p = Profile(1);
            Assert.IsTrue(RunRules.Has(RunRules.Rule.KeenEars));
            Assert.AreEqual(baseline.HearingScale * 1.35f, p.HearingScale, 0.0001f);
            Assert.AreEqual(1.5f, RunRules.ShardPayoutMultiplier);
        }

        [Test]
        public void NoSecondChances_RemovesSecondWindEverywhere_AndTheFreeGrab()
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.NoSecondChances);
            Assert.AreEqual(0, PlayerInventory.CapFor(ShopItem.SecondWind));
            Assert.IsFalse(PlayerInventory.CanHoldMore(ShopItem.SecondWind));
            PlayerInventory.Grant(ShopItem.SecondWind);
            Assert.AreEqual(0, PlayerInventory.Count(ShopItem.SecondWind));
            Assert.IsTrue(HunterGrab.DisabledByRules, "F120's switch");
            Assert.IsFalse(CampaignRules.ApplyTerror(new TerrorSettings()).survivableGrab, "F120's other switch");
            // the other cards are untouched
            PlayerInventory.Grant(ShopItem.SpareBattery);
            Assert.AreEqual(1, PlayerInventory.Count(ShopItem.SpareBattery));
        }

        [Test]
        public void DoorsDontHold_SetsHubBreachEveryVisit_AndTheDerivationClearsAgain()
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.DoorsDontHold);
            Assert.AreEqual(HubBreach.BreachMode.EveryVisit, HubBreach.Mode);
            CampaignRules.ResetCampaign();
            Assert.AreEqual(HubBreach.BreachMode.OncePerCampaign, HubBreach.Mode);
            Assert.IsFalse(HunterGrab.DisabledByRules);
        }

        [Test]
        public void StanWakesEarly_And_WeldedLockers_AreReadableFlags()
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.StanWakesEarly | Oath.WeldedLockers);
            Assert.AreEqual(2, CampaignRules.StalkerFromFloor);
            Assert.IsTrue(CampaignRules.LockersWelded);
            GameFlow.IsDescent = true;
            Assert.IsFalse(CampaignRules.LockersWelded, "a descent is outside the Oaths");
            Assert.AreEqual(EntityRoster.StalkerFrom, CampaignRules.StalkerFromFloor);
        }

        [Test]
        public void Descent_BehavesAsPlainStandard()
        {
            CampaignRules.SetForTest(DifficultyPreset.Nightmare, Assist.LongerTimer | Assist.HonestCues, Oath.ShortFuse | Oath.OldBattery);
            GameFlow.IsDescent = true;
            Assert.IsTrue(CampaignRules.IsPlain);
            Assert.AreEqual(0, CampaignRules.ForcedRuleMask);
            Assert.AreEqual(1f, CampaignRules.ShardMultiplier);
            Assert.AreEqual(1f, CampaignRules.DrainScale);
            FloorProfile baseline = FloorProfile.ForDescent(3, 99, null);
            FloorProfile p = FloorProfile.ForDescent(3, 99, null);
            CampaignRules.ApplyToProfile(p);
            Assert.AreEqual(baseline.EscapeSeconds, p.EscapeSeconds);
            Assert.AreEqual(1f, p.ChaseCommitScale);
        }

        [Test]
        public void OathChoice_IsIllegalWhileAssisted_AndLegalOnNightmare()
        {
            Assert.IsFalse(CampaignRules.IsOathChoiceLegal(DifficultyPreset.Story, Assist.None));
            Assert.IsFalse(CampaignRules.IsOathChoiceLegal(DifficultyPreset.Standard, Assist.GentleTorch));
            Assert.IsTrue(CampaignRules.IsOathChoiceLegal(DifficultyPreset.Standard, Assist.SteadyTorch | Assist.NoReplay));
            Assert.IsTrue(CampaignRules.IsOathChoiceLegal(DifficultyPreset.Nightmare, Assist.None));
        }

        [Test]
        public void MidCampaignAssist_MarksTheCampaignAssistedForGood()
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.ShortFuse);
            Assert.IsFalse(CampaignRules.IsAssisted);
            CampaignRules.SetAssists(Assist.SteadyTorch);
            Assert.IsFalse(CampaignRules.IsAssisted, "comfort only");
            CampaignRules.SetAssists(Assist.LongerTimer);
            Assert.IsTrue(CampaignRules.IsAssisted);
            CampaignRules.SetAssists(Assist.None);
            Assert.IsTrue(CampaignRules.IsAssisted, "switching it back off does not un-mark it");
            Assert.AreEqual(1f, CampaignRules.ShardMultiplier);
        }

        [Test]
        public void Signature_IgnoresKitsThatDoNotChangeTheBuild()
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.None, StartingKit.Scavenger);
            string scavenger = CampaignRules.Signature();
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.None, StartingKit.StandardIssue);
            Assert.AreEqual(CampaignRules.Signature(), scavenger);
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.None, StartingKit.Runner);
            Assert.AreNotEqual(CampaignRules.Signature(), scavenger, "RUNNER changes floor 1's perks");
            CampaignRules.SetForTest(DifficultyPreset.Nightmare, Assist.None, Oath.None);
            Assert.AreNotEqual(CampaignRules.Signature(), scavenger);
        }

        // ------------------------------------------------------------ payout

        [Test]
        public void OathLine_AndGradeBonus_OnlyForAnUnassistedOathCampaign()
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.ShortFuse | Oath.NoSecondChances);
            Payout po = PlayerWallet.ComputeFloorClear(2, 3, 30f);
            int baseTotal = po.Total, lines = po.Lines.Count;
            CampaignRules.AppendOathLine(po);
            Assert.AreEqual(lines + 1, po.Lines.Count);
            Assert.AreEqual(PayoutKind.Oath, po.Lines[po.Lines.Count - 1].Kind);
            Assert.AreEqual(Mathf.RoundToInt(baseTotal * (1.15f + 0.20f - 1f)), po.Total - baseTotal);
            int afterOath = po.Total;
            FloorGrade.AppendBonus(po, Grade.A, 2);
            Assert.AreEqual(afterOath + FloorGrade.BonusFor(Grade.A, 2), po.Total);
            Assert.AreEqual(PayoutKind.Grade, po.Lines[po.Lines.Count - 1].Kind);

            FloorGrade.AppendBonus(po, Grade.C, 2);
            Assert.AreEqual(afterOath + FloorGrade.BonusFor(Grade.A, 2), po.Total, "C and D pay nothing");

            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.GentleTorch, Oath.ShortFuse);
            Payout assisted = PlayerWallet.ComputeFloorClear(2, 3, 30f);
            int t = assisted.Total;
            CampaignRules.AppendOathLine(assisted);
            FloorGrade.AppendBonus(assisted, Grade.S, 2);
            Assert.AreEqual(t, assisted.Total, "assisted: no multiplier, no grade bonus");
        }

        [Test]
        public void GradeBonus_ScalesWithTheFloor_AndRoundsToFive()
        {
            Assert.AreEqual(30, FloorGrade.BonusFor(Grade.S, 1));
            Assert.AreEqual(15, FloorGrade.BonusFor(Grade.A, 1));
            Assert.AreEqual(5, FloorGrade.BonusFor(Grade.B, 1));
            Assert.AreEqual(0, FloorGrade.BonusFor(Grade.C, 3));
            Assert.AreEqual(0, FloorGrade.BonusFor(Grade.None, 3));
            Assert.AreEqual(0, FloorGrade.BonusFor(Grade.S, 3) % 5);
            Assert.Greater(FloorGrade.BonusFor(Grade.S, 5), FloorGrade.BonusFor(Grade.S, 1));
        }

        [Test]
        public void ShardPickup_UsesTheOathMultiplier()
        {
            CampaignRules.SetForTest(DifficultyPreset.Standard, Assist.None, Oath.WeldedLockers | Oath.StanWakesEarly);
            PlayerWallet.ResetCampaign();
            PlayerWallet.BeginAttempt();
            PlayerWallet.CollectMazeShard();
            Assert.AreEqual(Mathf.RoundToInt(PlayerWallet.ShardValue * 1.4f), PlayerWallet.Shards);
        }

        // ------------------------------------------------------------ grades

        private static GradeInput Perfect()
        {
            return new GradeInput { floor = 2, runSeconds = 120f, parSeconds = 240f, secondsLeft = 100f, countdownSeconds = 100f, shardsPlaced = 4, shardsFound = 4 };
        }

        [Test]
        public void Grade_PerfectRunIsS_AndTheScoreAddsUp()
        {
            Assert.AreEqual(100f, FloorGrade.Score(Perfect()), 0.001f);
            Assert.AreEqual(Grade.S, FloorGrade.For(Perfect()));
        }

        [Test]
        public void Grade_ASecondWind_OrADeath_CapsAtA()
        {
            GradeInput g = Perfect();
            g.secondWinds = 1;
            Assert.AreEqual(80f, FloorGrade.Score(g), 0.001f);
            Assert.AreEqual(Grade.A, FloorGrade.For(g));
            g = Perfect();
            g.deathsOnFloor = 1;
            Assert.AreEqual(Grade.A, FloorGrade.For(g));
            g.deathsOnFloor = 3;
            Assert.AreEqual(Grade.B, FloorGrade.For(g));
        }

        [Test]
        public void Grade_SlowAndSeen_IsCOrD()
        {
            GradeInput g = Perfect();
            g.runSeconds = 720f; // 3 x par: no pace points
            g.chases = 3;
            g.secondsLeft = 0f;
            g.batteriesUsed = 2;
            g.shardsFound = 0;
            Assert.AreEqual(20f, FloorGrade.Score(g), 0.001f); // Untouched only
            Assert.AreEqual(Grade.D, FloorGrade.For(g));
            g.secondWinds = 0; g.shardsFound = 4; g.chases = 1;
            Assert.AreEqual(Grade.C, FloorGrade.For(g)); // 20 + 14 + 5 = 39
        }

        [Test]
        public void Grade_ParTimeGrowsWithTheFloor()
        {
            float p1 = FloorGrade.ParSeconds(FloorProfile.For(1, 5));
            float p5 = FloorGrade.ParSeconds(FloorProfile.For(5, 5));
            Assert.Greater(p5, p1);
            Assert.AreEqual(60f + 2.5f * 49f + 30f * 2f, p1, 0.5f);
        }

        [Test]
        public void Grade_StatesRoundTripThroughASave()
        {
            FloorGrade.Store(1, Grade.B);
            FloorGrade.Store(1, Grade.C); // keeps the better one
            FloorGrade.Store(3, Grade.S);
            FloorGrade.AddDeath(2);
            FloorGrade.AddDeath(2);
            SaveData d = new SaveData();
            FloorGrade.CaptureTo(d);
            FloorGrade.ResetCampaign();
            Assert.AreEqual(Grade.None, FloorGrade.Stored(1));
            FloorGrade.RestoreFrom(d);
            Assert.AreEqual(Grade.B, FloorGrade.Stored(1));
            Assert.AreEqual(Grade.S, FloorGrade.Stored(3));
            Assert.AreEqual(2, FloorGrade.DeathsOn(2));
            Assert.AreEqual("B · - · S · - · -", FloorGrade.GradesLine());
        }

        // ------------------------------------------------------------ saves

        [Test]
        public void Campaign_RoundTripsThroughACheckpoint()
        {
            CampaignRules.SetForTest(DifficultyPreset.Nightmare, Assist.SteadyTorch, Oath.ShortFuse | Oath.WeldedLockers | Oath.DoorsDontHold, StartingKit.Runner, true);
            FloorGrade.Store(2, Grade.A);
            SaveData d = new SaveData();
            CampaignRules.CaptureTo(d);
            string json = JsonUtility.ToJson(d);

            StandardBaselineTests.Reset();
            Assert.IsTrue(CampaignRules.IsPlain);
            CampaignRules.RestoreFrom(JsonUtility.FromJson<SaveData>(json));
            Assert.AreEqual(DifficultyPreset.Nightmare, CampaignRules.Preset);
            Assert.AreEqual(Assist.SteadyTorch, CampaignRules.Assists);
            Assert.AreEqual(Oath.ShortFuse | Oath.WeldedLockers | Oath.DoorsDontHold, CampaignRules.Oaths);
            Assert.AreEqual(StartingKit.Runner, CampaignRules.Kit);
            Assert.IsTrue(CampaignRules.AssistedSticky);
            Assert.AreEqual(Grade.A, FloorGrade.Stored(2));
            Assert.AreEqual(HubBreach.BreachMode.EveryVisit, HubBreach.Mode, "CONTINUE re-derives F121's switch");
        }

        [Test]
        public void AnOlderSave_RestoresAsPlainStandard_WithNoGrades()
        {
            string json = File.ReadAllText(Path.Combine(Application.dataPath, "Editor/Tests/Fixtures/save-v1.json"));
            Assert.IsTrue(SaveSystem.TryParse(json, out SaveData d, out _, out _));
            CampaignRules.SetForTest(DifficultyPreset.Nightmare, Assist.LongerTimer, Oath.ShortFuse);
            FloorGrade.Store(1, Grade.S);
            CampaignRules.RestoreFrom(d);
            Assert.IsTrue(CampaignRules.IsPlain);
            Assert.AreEqual(DifficultyPreset.Standard, CampaignRules.Preset);
            Assert.AreEqual(Grade.None, FloorGrade.Stored(1));
        }

        [Test]
        public void ASaveWithUnknownBits_IsClamped()
        {
            SaveData d = new SaveData { campaignPreset = 99, campaignAssists = unchecked((int)0xFFFFFF00), campaignOaths = unchecked((int)0xFFFFF000), campaignKit = -4, campaignGrades = new[] { 0, 9, -3, 4, 5, 5, 5, 5 } };
            CampaignRules.RestoreFrom(d);
            Assert.AreEqual(DifficultyPreset.Standard, CampaignRules.Preset);
            Assert.AreEqual(Assist.None, CampaignRules.Assists);
            Assert.AreEqual(Oath.None, CampaignRules.Oaths);
            Assert.AreEqual(StartingKit.StandardIssue, CampaignRules.Kit);
            Assert.AreEqual(Grade.S, FloorGrade.Stored(1), "9 is clamped to S");
            Assert.AreEqual(Grade.None, FloorGrade.Stored(2), "negative is clamped to none");
        }

        // ------------------------------------------------------------ unlock content

        [Test]
        public void Contracts_Roll_FromTheBasePoolUntilUnlocked()
        {
            ContractState.RollOffers(555, 2);
            int a = ContractState.OfferA, b = ContractState.OfferB;
            Assert.Less(a, 8);
            Assert.Less(b, 8);
            Assert.AreEqual(8, ContractState.OfferPool().Length);

            PatientRecord.DebugUnlockEverything();
            Assert.AreEqual(11, ContractState.OfferPool().Length);
            Assert.AreEqual(15, ShopCatalogue.Available().Count);
            Assert.IsTrue(ShopCatalogue.Get(ShopItem.Dynamo) != null && ShopCatalogue.Get(ShopItem.Stethoscope) != null);
            // torch colours past the swatches never make CheapestOpenPrice think a card is still open
            PlayerInventory.GrantTorchColour(1); PlayerInventory.GrantTorchColour(2); PlayerInventory.GrantTorchColour(3);
            ShopItemDef torch = ShopCatalogue.Get(ShopItem.TorchColour);
            int cheapest = ShopCatalogue.CheapestOpenPrice(1);
            Assert.AreNotEqual(ShopCatalogue.PriceFor(torch, 1), cheapest, "the torch card is closed once swatches 1-3 are owned (the earned colours are not for sale)");

            PatientRecord.Delete();
            Assert.AreEqual(8, ContractState.OfferPool().Length);
            ContractState.RollOffers(555, 2);
            Assert.AreEqual(a, ContractState.OfferA);
            Assert.AreEqual(b, ContractState.OfferB);
        }

        [Test]
        public void Begin_GrantsTheKitAndTheUnlockedColours()
        {
            PatientRecord.DebugUnlockEverything();
            int startedBefore = PatientRecord.Data.campaignsStarted;
            CampaignRules.Begin(DifficultyPreset.Standard, Assist.None, Oath.None, StartingKit.Scavenger, 5);
            Assert.AreEqual(1, PlayerInventory.Count(ShopItem.SpareBattery));
            Assert.AreEqual(1, PlayerInventory.Count(ShopItem.StarCompass));
            Assert.IsTrue(PlayerInventory.OwnsTorchColour(4) && PlayerInventory.OwnsTorchColour(5) && PlayerInventory.OwnsTorchColour(6));
            Assert.AreEqual(5, PlayerInventory.TorchColourIndex);
            Assert.AreEqual(startedBefore + 1, PatientRecord.Data.campaignsStarted);
            Assert.AreEqual((int)StartingKit.Scavenger, PatientRecord.Data.lastKit);

            StandardBaselineTests.Reset();
            CampaignRules.Begin(DifficultyPreset.Standard, Assist.None, Oath.None, StartingKit.Runner, 0);
            Assert.IsTrue(PlayerInventory.HasActive(ShopItem.StaminaTonic) && PlayerInventory.HasActive(ShopItem.QuietShoes));
            Assert.AreEqual(1f, CampaignRules.ShardMultiplier);
            // RUNNER's perks flow into the profile
            FloorProfile p = FloorProfile.For(1, 5);
            PlayerInventory.ApplyActiveModifiers(p);
            Assert.AreEqual(FloorProfile.For(1, 5).SprintSeconds + 4f, p.SprintSeconds);

            StandardBaselineTests.Reset();
            CampaignRules.Begin(DifficultyPreset.Story, Assist.None, Oath.ShortFuse, StartingKit.StandardIssue, 0);
            Assert.AreEqual(Oath.None, CampaignRules.Oaths, "Oaths are dropped on STORY");
        }

        [Test]
        public void Terror_StethoscopeCard_UnmuffledTheHunter()
        {
            PlayerInventory.ActiveModifiers.Add(ShopItem.Stethoscope);
            TerrorSettings original = new TerrorSettings();
            TerrorSettings t = CampaignRules.ApplyTerror(original);
            Assert.AreNotSame(original, t);
            Assert.IsFalse(t.muffledHunter);
            Assert.IsTrue(original.muffledHunter);
        }
    }
}
