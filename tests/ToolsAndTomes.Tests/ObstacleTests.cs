using Microsoft.VisualStudio.TestTools.UnitTesting;
using ToolsAndTomes.Core.AI;
using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;
using ToolsAndTomes.Core.Simulation;
using static ToolsAndTomes.Tests.TestHelpers;

namespace ToolsAndTomes.Tests;

/// <summary>Obstacles and terrain (docs/DESIGN.md §3c).</summary>
[TestClass]
public sealed class ObstacleTests
{
    private readonly GameCatalog _catalog = GameCatalog.Default;

    private Obstacle Circle(string type, double x, double y, string id = "o") =>
        Obstacle.FromPlacement(id, _catalog.Obstacles[type], new ObstaclePlacement { Type = type, At = new Vec2(x, y) });

    private Obstacle Wall(string type, double x1, double y1, double x2, double y2, string id = "w") =>
        Obstacle.FromPlacement(id, _catalog.Obstacles[type], new ObstaclePlacement { Type = type, From = new Vec2(x1, y1), To = new Vec2(x2, y2) });

    private static Combatant Plain(Vec2 pos, string id = "player", Faction faction = Faction.Player, params string[] parts)
    {
        var cfg = new CrestConfiguration
        {
            Id = $"cfg_{id}", Name = id,
            ComponentIds = parts.Length > 0 ? parts : ["eq_talon", "mob_stride_legs", "core_aether_dynamo"]
        };
        return new Combatant(id, id, faction, CrestLoadout.CreateFromConfiguration(cfg, GameCatalog.Default, id), pos, 3);
    }

    // ---- Line of fire, indirect fire, and approaching -------------------------------------------

    [TestMethod]
    public void TallWalls_BlockDirectFire_LowCoverDoesnt_AndIndirectFireArcsOver()
    {
        var terrain = new Terrain([Wall("stone_wall", 10, 2, 10, 12), Circle("rubble", 15, 7, "r")], 24, 14, _catalog.Rules, _catalog.Rubble);
        Assert.IsFalse(terrain.HasLineOfFire(new Vec2(5, 7), new Vec2(14, 7)), "the wall blocks");
        Assert.IsTrue(terrain.HasLineOfFire(new Vec2(12, 7), new Vec2(18, 7)), "rubble is low");
        Assert.AreEqual(1, terrain.CoverDice(new Vec2(12, 7), new Vec2(16.5, 7)), "rubble right in front of the defender");
        Assert.AreEqual(0, terrain.CoverDice(new Vec2(18, 7), new Vec2(11, 7)), "rubble 3\" from the defender gives nothing");

        var shooter = Plain(new Vec2(5, 7), parts: ["eq_talon", "eq_artillery_mortar", "mob_stride_legs", "core_aether_dynamo"]);
        var target = Plain(new Vec2(12, 7), "enemy", Faction.Enemy);
        _ = new CombatSimulation([shooter, target], 1, _catalog, arenaWidth: 24, arenaHeight: 14,
            obstacles: [Wall("stone_wall", 9, 2, 9, 12)]);
        Assert.IsFalse(ActionValidationRules.IsInRange(shooter, _catalog.GetAction("weapon_talon"), target, null), "no line of fire");
        Assert.IsTrue(ActionValidationRules.IsInRange(shooter, _catalog.GetAction("weapon_artillery_mortar"), null, target.Position), "the mortar arcs over");
    }

    [TestMethod]
    public void Moves_PathAroundWalls_WithoutEverEnteringOne()
    {
        var crest = Plain(new Vec2(4, 7));
        var foe = Plain(new Vec2(22, 13), "enemy", Faction.Enemy);
        var wall = Wall("stone_wall", 10, 1, 10, 11);
        var sim = new CombatSimulation([crest, foe], 1, _catalog, arenaWidth: 24, arenaHeight: 14, obstacles: [wall]);

        sim.IssueCommand(new MoveCommand(crest.Id, new Vec2(16, 7)));
        for (int i = 0; i < 30 && crest.Position.DistanceTo(new Vec2(16, 7)) > 0.05; i++)
        {
            sim.StepBeat();
            Assert.IsGreaterThanOrEqualTo(sim.Terrain.CrestRadius - 1e-6, wall.DistanceTo(crest.Position), $"inside the wall at beat {i}");
        }
        Assert.IsLessThan(0.05, crest.Position.DistanceTo(new Vec2(16, 7)), "arrived by going around");
    }

    [TestMethod]
    public void AnAttackOnAHiddenTarget_WalksUntilItCanSee_ThenFires()
    {
        var shooter = Plain(new Vec2(4, 7));
        var hidden = Plain(new Vec2(14, 7), "enemy", Faction.Enemy, "mob_anchor_legs", "core_aether_furnace");
        var sim = new CombatSimulation([shooter, hidden], 1, _catalog, arenaWidth: 24, arenaHeight: 14,
            obstacles: [Wall("stone_wall", 10, 4, 10, 10)]);

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(shooter.Id, "weapon_talon", hidden.Id)).IsValid, "it can approach");
        StepUntil(sim, () => sim.Events.Any(e => e.Type == CombatEventType.ActionStarted && e.ActorId == shooter.Id), maxBeats: 20);
        Assert.IsTrue(sim.Terrain.HasLineOfFire(shooter.Position, hidden.Position), "it fires only once it can see the target");
    }

    // ---- Cover and collisions ---------------------------------------------------------------------

    [TestMethod]
    public void LowCover_AddsAProtectionDie()
    {
        // Every protection roll is a 1 then a 6: one die fails; two dice save.
        Combatant Defender() => Plain(new Vec2(12, 7), "enemy", Faction.Enemy, "eq_talon", "mob_anchor_legs", "core_aether_furnace");
        int Hits(params Obstacle[] layout)
        {
            var shooter = Plain(new Vec2(6, 7));
            var defender = Defender();
            var sim = new CombatSimulation([shooter, defender], new FixedRandom(1, 6), _catalog, arenaWidth: 24, arenaHeight: 14, obstacles: layout);
            sim.IssueCommand(new UseActionCommand(shooter.Id, "weapon_talon", defender.Id));
            StepUntil(sim, () => sim.Events.Any(e => e.Type is CombatEventType.ComponentDamaged or CombatEventType.DamageBlocked), maxBeats: 8);
            return sim.Events.Count(e => e.Type == CombatEventType.ComponentDamaged);
        }

        Assert.AreEqual(1, Hits(), "in the open");
        Assert.AreEqual(0, Hits(Wall("barricade", 10.8, 5.5, 10.8, 8.5)), "a barricade in front of the defender");
    }

    [TestMethod]
    public void ShovingIntoHardWalls_HurtsBothSides_SoftCoverJustStops()
    {
        (Combatant Target, CombatSimulation Sim, Obstacle Wall) Shove(string wallType)
        {
            var caster = Plain(new Vec2(6, 7));
            var target = Plain(new Vec2(8, 7), "enemy", Faction.Enemy, "eq_talon", "mob_stride_legs", "core_aether_dynamo");
            var wall = Wall(wallType, 9.5, 4, 9.5, 10);
            var sim = new CombatSimulation([caster, target], new FixedRandom(1), _catalog, arenaWidth: 24, arenaHeight: 14, obstacles: [wall]);
            sim.IssueCommand(new UseActionCommand(caster.Id, "spell_push", target.Id));
            StepUntil(sim, () => sim.Events.Any(e => e.Type == CombatEventType.CombatantDisplaced), maxBeats: 8);
            return (target, sim, wall);
        }

        // Push 4 from 8 → 12, but the stone wall's face stops the Crest at about 8.75: about 3.25 short → 2 damage each.
        var (target, sim, stone) = Shove("stone_wall");
        Assert.AreEqual(8.75, target.Position.X, 0.05);
        var slam = sim.Events.Single(e => e.Type == CombatEventType.Slammed);
        Assert.AreEqual(2, slam.Value);
        Assert.AreEqual(2, sim.Events.Count(e => e.Type == CombatEventType.ComponentDamaged && e.ActorId == target.Id), "every save fails");
        Assert.AreEqual(stone.Definition.Durability - 2, stone.Durability);
        Assert.IsTrue(TerrainTactics.WouldSlam(Plain(new Vec2(6, 7)), _catalog.GetAction("spell_push"), Plain(new Vec2(8, 7), "x", Faction.Enemy)) == false,
            "outside a simulation there is no wall to slam into");

        var (soft, softSim, barricade) = Shove("barricade");
        Assert.IsFalse(softSim.Events.Any(e => e.Type == CombatEventType.Slammed), "soft cover doesn't hurt");
        Assert.IsTrue(IsUndamaged(soft));
        Assert.AreEqual(barricade.Definition.Durability - 2, barricade.Durability, "but it still takes the hit");
    }

    [TestMethod]
    public void AreaAttacks_DamageObstacles_AndTheKeywordDecidesRubble()
    {
        var gunner = Plain(new Vec2(4, 7), parts: ["eq_artillery_mortar", "mob_stride_legs", "core_aether_dynamo"]);
        var foe = Plain(new Vec2(22, 2), "enemy", Faction.Enemy);
        var pillar = Circle("stone_pillar", 12, 7, "pillar");
        var sim = new CombatSimulation([gunner, foe], 1, _catalog, arenaWidth: 24, arenaHeight: 14, obstacles: [pillar]);

        int hits = 0;
        while (sim.Terrain.Obstacles.Contains(pillar) && hits < 10)
        {
            StepUntil(sim, () => gunner.State == ActionState.Idle && gunner.GetCooldown("weapon_artillery_mortar") == 0, maxBeats: 12);
            gunner.Loadout.Installed.First(e => e.Definition.MaxAmmo > 0).RefillAmmo();
            Assert.IsTrue(sim.IssueCommand(new UseActionCommand(gunner.Id, "weapon_artillery_mortar", TargetPoint: new Vec2(12, 7))).IsValid);
            StepUntil(sim, () => sim.Events.Count(e => e.Type is CombatEventType.ObstacleDamaged or CombatEventType.ObstacleDestroyed) > hits, maxBeats: 10);
            hits++;
        }

        Assert.IsFalse(sim.Terrain.Obstacles.Contains(pillar), "the pillar is destroyed");
        var rubble = sim.Terrain.Obstacles.Single();
        Assert.IsTrue(rubble.Definition.IsRubble, "the Mortar leaves rubble");
        Assert.AreEqual(pillar.A, rubble.A);
        Assert.IsTrue(sim.Terrain.HasLineOfFire(new Vec2(4, 7), new Vec2(20, 7)), "rubble is low: the line of fire opens");
    }

    // ---- Iron Wall, blink, and validation ----------------------------------------------------------

    [TestMethod]
    public void IronWall_RaisesATallHardWall_ButNotOnTopOfACrest()
    {
        var caster = Plain(new Vec2(4, 7), parts: ["tome_iron_codex", "mob_stride_legs", "core_aether_dynamo"]);
        caster.Resources.AddFastIron(4);
        var foe = Plain(new Vec2(20, 7), "enemy", Faction.Enemy);
        var sim = new CombatSimulation([caster, foe], 1, _catalog, arenaWidth: 24, arenaHeight: 14);

        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(caster.Id, "spell_iron_wall", TargetPoint: new Vec2(8, 7))).IsValid);
        StepUntil(sim, () => sim.Terrain.Obstacles.Count == 1, maxBeats: 8);
        var wall = sim.Terrain.Obstacles[0];
        Assert.AreEqual("iron_wall", wall.Definition.Id);
        Assert.AreEqual(3, wall.A.DistanceTo(wall.B), 1e-6);
        Assert.IsFalse(sim.Terrain.HasLineOfFire(caster.Position, foe.Position), "it blocks the line of fire");
        Assert.AreEqual(2, caster.TotalFastIron);

        // Raising a wall on top of a Crest fizzles (and costs no fast-iron).
        StepUntil(sim, () => caster.State == ActionState.Idle && caster.GetCooldown("spell_iron_wall") == 0, maxBeats: 20);
        foe.Position = new Vec2(9, 3);
        Assert.IsTrue(sim.IssueCommand(new UseActionCommand(caster.Id, "spell_iron_wall", TargetPoint: new Vec2(9, 3))).IsValid);
        StepUntil(sim, () => sim.Events.Any(e => e.Type == CombatEventType.CommandRejected && e.Message.Contains("no room")), maxBeats: 8);
        Assert.AreEqual(1, sim.Terrain.Obstacles.Count);
        Assert.AreEqual(2, caster.TotalFastIron);
    }

    [TestMethod]
    public void TheAutopilot_WallsOffARangedShooter_WithIronWall()
    {
        var caster = Plain(new Vec2(4, 7), parts: ["tome_iron_codex", "eq_talon", "mob_stride_legs", "core_aether_dynamo"]);
        caster.Resources.AddFastIron(4);
        var shooter = Plain(new Vec2(11, 7), "enemy", Faction.Enemy);
        var sim = new CombatSimulation([caster, shooter], 1, _catalog, arenaWidth: 24, arenaHeight: 14);
        var pilot = new AutoPilot();

        for (int i = 0; i < 8 && sim.Terrain.Obstacles.Count == 0; i++)
        {
            pilot.Update(sim, caster, shooter);
            sim.StepBeat();
        }

        Assert.AreEqual(1, sim.Terrain.Obstacles.Count, "an Iron Wall went up");
        Assert.AreEqual("iron_wall", sim.Terrain.Obstacles[0].Definition.Id);
    }

    [TestMethod]
    public void ABlinkCantEndInsideAnObstacle()
    {
        var terrain = new Terrain([Circle("boulder", 10, 7)], 24, 14, _catalog.Rules, _catalog.Rubble);
        var landing = terrain.LastFreeAlong(new Vec2(5, 7), new Vec2(10, 7));
        Assert.IsTrue(terrain.IsFree(landing));
        Assert.IsLessThan(10, landing.X);
    }

    [TestMethod]
    public void Layouts_ThatOverlapStartsOrTrapEnemies_AreContentErrors()
    {
        var enc = _catalog.Encounters["enc_bm_outriders"] with
        {
            Obstacles =
            [
                new ObstaclePlacement { Type = "boulder", At = new Vec2(4, 7) },          // on the player's start
                new ObstaclePlacement { Type = "stone_wall", From = new Vec2(14, 0), To = new Vec2(14, 14) }, // seals the enemies in
                new ObstaclePlacement { Type = "iron_wall", From = new Vec2(2, 1), To = new Vec2(4, 1) }   // conjured only
            ]
        };
        var catalog = new GameCatalog(_catalog.Actions, _catalog.Equipment, _catalog.StarterConfigurations, _catalog.Enemies, _catalog.Rules,
            _catalog.Materials, new Dictionary<string, EncounterDefinition>(_catalog.Encounters) { [enc.Id] = enc },
            _catalog.Consumables, _catalog.Missions, _catalog.Pilots, _catalog.Obstacles);
        var errors = Core.Content.ContentValidator.Validate(catalog);

        Assert.IsTrue(errors.Any(e => e.Contains("player's start") && e.Contains("overlaps")), string.Join("\n", errors));
        Assert.IsTrue(errors.Any(e => e.Contains("no route")));
        Assert.IsTrue(errors.Any(e => e.Contains("only raised by spells")));
    }

    [TestMethod]
    public void FightsWithTerrain_AreDeterministic()
    {
        var runner = new BalanceRunner(_catalog, ["pilot_marrow", "pilot_vell"]);
        var a = runner.RunOne(ConfigurationCatalog.AgileSkirmish, seed: 4, maxBeats: 300, encounterId: "enc_bm_border_fort");
        var b = runner.RunOne(ConfigurationCatalog.AgileSkirmish, seed: 4, maxBeats: 300, encounterId: "enc_bm_border_fort");
        Assert.AreEqual(a, b);
    }
}
