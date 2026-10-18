using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace KeepItLit.Tests
{
    /// <summary>
    /// F122 test T0: a plain STANDARD campaign (no assists, no Oaths, standard-issue kit, an empty Patient Record) is byte-for-byte the game as it was before F122.
    /// The fixture <c>Fixtures/standard-baseline.txt</c> was captured from the code BEFORE F122 (seeds 12345 / 777 / 20261015, floors 1-5, with and without shop
    /// modifiers: every public FloorProfile field, the rolled run rule and its four multipliers, the TerrorSettings defaults, the contract offers, the shelf size,
    /// the payouts). This test re-runs the same pipeline with the F122 steps inserted and compares text. If you touch a knob on purpose, regenerate the fixture
    /// from the OLD behaviour or amend the test: never silently re-baseline.
    /// </summary>
    [Category("Fast")]
    public class StandardBaselineTests
    {
        private string _key;

        [SetUp]
        public void SetUp()
        {
            _key = "kil-test-record-" + Guid.NewGuid().ToString("N");
            PatientRecord.KeyOverride = _key; // the real record is never read or written
            Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Reset();
            PlayerPrefs.DeleteKey(_key);
            PlayerPrefs.DeleteKey(_key + ".unreadable");
            PatientRecord.KeyOverride = null;
        }

        internal static void Reset()
        {
            GameFlow.IsDescent = false;
            CampaignRules.ResetCampaign();
            RunRules.ResetCampaign();
            PlayerInventory.ResetCampaign();
            ContractState.ResetCampaign();
            PlayerWallet.ResetCampaign();
            FloorGrade.ResetCampaign();
            Array.Clear(GameFlow.RolledObjectives, 0, GameFlow.RolledObjectives.Length);
        }

        private static string Fmt(object v)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            if (v == null) return "null";
            if (v is float f) return f.ToString("R", inv);
            if (v is double d) return d.ToString("R", inv);
            if (v is Color c) return c.r.ToString("R", inv) + "," + c.g.ToString("R", inv) + "," + c.b.ToString("R", inv) + "," + c.a.ToString("R", inv);
            return Convert.ToString(v, inv);
        }

        /// <summary>The same dump the fixture holds, with the F122 pipeline steps inserted.</summary>
        internal static string Dump()
        {
            StringBuilder sb = new StringBuilder();
            Reset();
            int[] seeds = { 12345, 777, 20261015 };
            foreach (string invName in new[] { "none", "all" })
            {
                foreach (int seed in seeds)
                {
                    PlayerInventory.ResetCampaign();
                    if (invName == "all")
                    {
                        PlayerInventory.ActiveModifiers.Add(ShopItem.LongFuse);
                        PlayerInventory.ActiveModifiers.Add(ShopItem.StaminaTonic);
                        PlayerInventory.ActiveModifiers.Add(ShopItem.ExtraLamps);
                        PlayerInventory.ActiveModifiers.Add(ShopItem.QuietShoes);
                    }
                    Array.Clear(GameFlow.RolledObjectives, 0, GameFlow.RolledObjectives.Length);
                    for (int floor = 1; floor <= FloorProfile.FinalFloor; floor++)
                    {
                        FloorProfile p = FloorProfile.For(floor, seed);
                        PlayerInventory.ApplyActiveModifiers(p);
                        RunRules.Resolve(seed, floor);
                        RunRules.ApplyToProfile(p);
                        CampaignRules.ApplyToProfile(p); // F122
                        Assert.AreEqual(1f, p.ChaseCommitScale, "ChaseCommitScale must stay 1 under plain STANDARD");
                        sb.AppendLine("== inv=" + invName + " seed=" + seed + " floor=" + floor);
                        foreach (FieldInfo fi in typeof(FloorProfile).GetFields(BindingFlags.Public | BindingFlags.Instance))
                        {
                            if (fi.Name == "ChaseCommitScale") continue; // added by F122 (asserted above)
                            sb.AppendLine("  " + fi.Name + "=" + Fmt(fi.GetValue(p)));
                        }
                        sb.AppendLine("  Floor=" + p.Floor + " Progress=" + Fmt(p.Progress));
                        sb.AppendLine("  rule=" + RunRules.Current + " noisy=" + Fmt(RunRules.NoisyPatchMultiplier) + " shard=" + Fmt(RunRules.ShardPayoutMultiplier)
                            + " bstart=" + Fmt(RunRules.BatteryStartFraction) + " brech=" + Fmt(RunRules.BatteryRechargeMultiplier));
                    }
                }
            }
            RunRules.ResetCampaign();
            PlayerInventory.ResetCampaign();
            Array.Clear(GameFlow.RolledObjectives, 0, GameFlow.RolledObjectives.Length);

            TerrorSettings original = new TerrorSettings();
            TerrorSettings terror = CampaignRules.ApplyTerror(original); // F122
            Assert.AreSame(original, terror, "plain STANDARD must get the very same TerrorSettings instance, not a clone");
            // F123 added six wing-nightmare switches after the fixture was captured: skipped in the dump, asserted at their defaults here.
            string[] f123 = { "extraDoors", "pipeKnocks", "lyingKnocks", "morgueDrawers", "ampoules", "houseRules" };
            foreach (string name in f123) Assert.IsTrue((bool)typeof(TerrorSettings).GetField(name).GetValue(terror), "F123 switch " + name + " defaults to true");
            foreach (FieldInfo fi in typeof(TerrorSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (Array.IndexOf(f123, fi.Name) >= 0) continue;
                sb.AppendLine("terror." + fi.Name + "=" + Fmt(fi.GetValue(terror)));
            }

            foreach (int seed in seeds)
            {
                for (int floor = 1; floor <= 4; floor++)
                {
                    ContractState.RollOffers(seed, floor);
                    sb.AppendLine("offers seed=" + seed + " floor=" + floor + " -> " + ContractState.OfferA + "," + ContractState.OfferB);
                }
            }
            ContractState.ResetCampaign();
            sb.AppendLine("items=" + ShopCatalogue.Available().Count + " torchColours=" + (ShopCatalogue.ShopTorchSwatches + 1) + " cheapest1=" + ShopCatalogue.CheapestOpenPrice(1));
            for (int floor = 1; floor <= 5; floor++)
            {
                PlayerWallet.ResetCampaign();
                Payout po = PlayerWallet.ComputeFloorClear(floor, 3, 41.5f);
                int linesBefore = po.Lines.Count;
                int totalBefore = po.Total;
                CampaignRules.AppendOathLine(po); // F122
                FloorGrade.AppendBonus(po, Grade.S, floor); // F122: an S on plain STANDARD pays nothing
                Assert.AreEqual(linesBefore, po.Lines.Count, "plain STANDARD adds no ledger line");
                Assert.AreEqual(totalBefore, po.Total, "plain STANDARD pays no Oath or grade bonus");
                sb.AppendLine("clear floor=" + floor + " total=" + po.Total + " lines=" + po.Lines.Count);
            }
            PlayerWallet.ResetCampaign();
            sb.AppendLine("cons=" + PlayerWallet.ComputeConsolation(2, 3).Total);
            return sb.ToString();
        }

        private static string Normalise(string s) => s.Replace("\r\n", "\n").TrimEnd();

        [Test]
        public void PlainStandard_MatchesThePreF122Fixture()
        {
            string fixture = File.ReadAllText(Path.Combine(Application.dataPath, "Editor/Tests/Fixtures/standard-baseline.txt"));
            string now = Dump();
            Assert.AreEqual(Normalise(fixture), Normalise(now), "STANDARD changed. If this is on purpose, say so in the plan; otherwise a knob leaked.");
        }

        [Test]
        public void PlainStandard_DerivedValuesAreNeutral()
        {
            Assert.IsTrue(CampaignRules.IsPlain);
            Assert.IsFalse(CampaignRules.IsAssisted);
            Assert.AreEqual(0, CampaignRules.OathRank);
            Assert.AreEqual(1f, CampaignRules.ShardMultiplier);
            Assert.AreEqual(1f, CampaignRules.DrainScale);
            Assert.AreEqual(1f, CampaignRules.RechargeScale);
            Assert.AreEqual(0, CampaignRules.ForcedRuleMask);
            Assert.IsTrue(CampaignRules.SecondWindAllowed);
            Assert.IsFalse(CampaignRules.LockersWelded);
            Assert.AreEqual(EntityRoster.StalkerFrom, CampaignRules.StalkerFromFloor);
            Assert.IsTrue(CampaignRules.UnreliableCues);
            Assert.AreEqual(1, PlayerInventory.CapFor(ShopItem.SecondWind));
            Assert.AreEqual(HubBreach.BreachMode.OncePerCampaign, HubBreach.Mode);
            Assert.IsFalse(HunterGrab.DisabledByRules);
            Assert.AreEqual(12, ShopCatalogue.Available().Count, "an empty record leaves the shelf at today's twelve cards");
            Assert.AreEqual(8, ContractState.OfferPool().Length);
        }

        [Test]
        public void PlainStandard_RunRulesStillRollOneRule_AndHasAgreesWithCurrent()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                for (int floor = 1; floor <= 5; floor++)
                {
                    RunRules.Resolve(seed * 7919, floor);
                    foreach (RunRules.Rule r in Enum.GetValues(typeof(RunRules.Rule)))
                    {
                        if (r == RunRules.Rule.None) continue;
                        Assert.AreEqual(r == RunRules.Current, RunRules.Has(r), $"seed {seed} floor {floor} rule {r}");
                    }
                }
            }
        }

        [Test]
        public void DescentVoidShapesAndScalingAreMonotonic()
        {
            DescentRun.Begin();
            int prevArea = 0;
            FloorProfile.FloorShape prevShape = FloorProfile.FloorShape.Rectangle;
            for (int depth = 1; depth <= 15; depth++)
            {
                FloorProfile p = FloorProfile.ForDescent(depth, 1000 + depth * 37);
                int minExpected = Mathf.Min(42 + (depth - 1) * 6, 16 * 13);
                if (depth == 1) Assert.AreEqual(FloorProfile.FloorShape.Rectangle, p.Shape, "Depth 1 must be Rectangle");
                if (depth >= 2 && prevShape != FloorProfile.FloorShape.Rectangle)
                    Assert.AreNotEqual(prevShape, p.Shape, $"Depth {depth} repeated shape {p.Shape}");
                Assert.GreaterOrEqual(p.Width * p.Height, minExpected, $"Depth {depth} bounding area too small");
                prevShape = p.Shape;
                prevArea = p.Width * p.Height;
            }
            DescentRun.ResetState();
        }
    }
}
