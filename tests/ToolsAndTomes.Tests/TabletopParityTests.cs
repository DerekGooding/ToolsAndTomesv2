using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

[TestClass]
public sealed class TabletopParityTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    [TestMethod]
    public void Pcg32_MatchesPublishedReferenceOutput()
    {
        // Reference: pcg32-demo, pcg32_srandom_r(&rng, 42u, 54u), first six outputs.
        var pcg = new Pcg32(42, 54);
        uint[] expected = [0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e];

        foreach (uint value in expected)
            Assert.AreEqual(value, pcg.NextUInt());
    }

    [TestMethod]
    public void SeededRandom_RollsAreUniformlyInRange_AndReproducible()
    {
        var a = new SeededRandom(1337);
        var b = new SeededRandom(1337);
        var counts = new int[7];

        for (int i = 0; i < 6000; i++)
        {
            int roll = a.RollD6();
            Assert.AreEqual(roll, b.RollD6());
            counts[roll]++;
        }

        Assert.AreEqual(0, counts[0]);
        for (int face = 1; face <= 6; face++)
            Assert.IsTrue(counts[face] is > 850 and < 1150, $"Face {face}: {counts[face]}");
    }

    [TestMethod]
    public void WitCollapse_DropsNewestCommitments_WhenAWitGrantingComponentIsDestroyed()
    {
        // Heavy Anchor: Wit 12 (10 + Furnace 2), tax 7 → 5 available.
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(5, 5), startingMotes: 3);
        var enemy = CreateEnemy("enemy_bruiser", new Vec2(7, 5));
        var sim = new CombatSimulation([player, enemy], new FixedRandom(6));

        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_titan", enemy.Id));          // 2 Wit
        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_dragons_breath", enemy.Id)); // 1 Wit
        sim.IssueCommand(new UseActionCommand(player.Id, "spell_push", enemy.Id));            // 1 Wit
        sim.StepBeat();
        Assert.AreEqual(4, player.Resources.ReservedWit);

        // A backup core keeps the Crest alive; then the Furnace (+2 Wit) is destroyed: 1 Wit short.
        player.Loadout.Install(new EquipmentInstance("backup_core", _catalog.GetEquipment("core_aether_dynamo")));
        var furnace = player.Loadout.Installed.First(e => e.Definition.Id == "core_aether_furnace");
        furnace.ApplyDamage(furnace.Definition.MaxDurability);
        Assert.AreEqual(-1, player.Stats.AvailableWit(player.Resources.ReservedWit));

        var dropped = player.EnforceWitBudget();

        Assert.AreEqual(0, player.Stats.AvailableWit(player.Resources.ReservedWit));
        CollectionAssert.AreEqual(new[] { "Kinetic Push" }, dropped, "Newest commitment drops first.");
        Assert.HasCount(2, player.ActionsInMotion);
    }

    /// <summary>
    /// A short fight resolved by hand, beat by beat, using the documented resolution order.
    /// Every die is a 1 (all protection rolls fail), so the only inputs are the rules.
    /// </summary>
    [TestMethod]
    public void HandResolvedFight_MatchesTheEngineBeatByBeat()
    {
        var player = CreatePlayer(ConfigurationCatalog.HeavyAnchor, new Vec2(5, 5), startingMotes: 2);
        var bruiser = CreateEnemy("enemy_bruiser", new Vec2(7, 5));
        var sim = new CombatSimulation([player, bruiser], new FixedRandom(1));
        var plate = bruiser.Loadout.Installed.First(e => e.Definition.Id == "armor_heavy_plate");
        var righteous = bruiser.Loadout.Installed.First(e => e.Definition.Id == "eq_righteous");

        // Beat 0 (declare): Bruiser swings Righteous Strike (1/1/1); player commits Titan Cleave (2/1/2).
        sim.IssueCommand(new UseActionCommand(bruiser.Id, "weapon_righteous", player.Id));
        sim.IssueCommand(new UseActionCommand(player.Id, "weapon_titan", bruiser.Id));
        sim.StepBeat();
        Assert.AreEqual(ActionState.Windup, bruiser.State);
        Assert.AreEqual(ActionState.Windup, player.State);

        // Beat 1: Righteous windup ends → impact next beat. Titan has 1 windup beat left.
        sim.StepBeat();
        Assert.AreEqual(ActionState.Executing, bruiser.State);
        Assert.HasCount(1, sim.GetIncomingImpacts(player));

        // Beat 2 (Righteous impact): the player's Workshop rule fires Repulsor first.
        //   Repulsor hit: 1 dmg → bruiser rolls 2 dice (base + plate), both 1s → plate 3→2.
        //   Push 4 − resistance 3 (furnace, anchor legs, plate) = 1 → bruiser x 7→8.
        //   Interruption 4 + 1 (displaced) = 5 vs Righteous resistance 2 → Cancellation → lockout max(2, 1) = 2.
        //   Then Titan's windup ends (spends 1 Mote: 2→1).
        sim.StepBeat();
        Assert.AreEqual(2, plate.CurrentDurability);
        Assert.AreEqual(8.0, bruiser.Position.X, 1e-9);
        Assert.AreEqual(2, bruiser.ComponentLockouts[righteous.InstanceId].Total);
        Assert.IsTrue(IsUndamaged(player));
        Assert.AreEqual(1, player.Resources.CurrentMotes);

        // Beat 3 (Titan impact): Mote timer reaches 4 beats (Furnace) → player 1→2 Motes.
        //   Bruiser at distance 3 ≤ range 3 + tolerance 1 → hit. Damage 1 + Heavy bonus 2 (weight 149 ≥ 90) = 3.
        //   All rolls fail: plate 2→0 (disabled), third point flows to Righteous Strike 2→1.
        //   Push 2 − resistance 2 (plate gone) = 0 → no displacement. Repulsor recovery ends → cooldown 6, ticks to 5.
        sim.StepBeat();
        Assert.IsTrue(plate.IsDisabled);
        Assert.AreEqual(1, righteous.CurrentDurability);
        Assert.AreEqual(8.0, bruiser.Position.X, 1e-9);
        Assert.AreEqual(2, player.Resources.CurrentMotes);
        Assert.AreEqual(5, player.GetCooldown("defense_repulsor"));
        Assert.AreEqual(1, bruiser.ComponentLockouts[righteous.InstanceId].Remaining);

        var transcript = TabletopTranscript.Lines(sim.Events);
        string[] expectedOrder =
        [
            "Beat 0: Ironclad Bruiser winding up Righteous Strike.",
            "Beat 2: Heavy Anchor Assembly's reaction rule fires: Repulsor Pulse",
            "Beat 2: Ironclad Bruiser's Righteous Strike CANCELED!",
            "Beat 3: Heavy Anchor Assembly executed Titan Cleave.",
            "Beat 3: Ironclad Bruiser's Heavy Armor Plating DISABLED!"
        ];

        int cursor = 0;
        foreach (var expected in expectedOrder)
        {
            int found = transcript.Skip(cursor).ToList().FindIndex(l => l.StartsWith(expected, StringComparison.Ordinal));
            Assert.IsGreaterThanOrEqualTo(0, found, $"Missing (in order): {expected}\n\n{TabletopTranscript.Format(sim.Events)}");
            cursor += found + 1;
        }
    }
}
