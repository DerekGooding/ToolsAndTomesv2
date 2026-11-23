using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

public static class EncounterFactory
{
    public static CombatSimulation CreateTestEncounter(
        CrestLoadout playerLoadout,
        int seed = 1337,
        GameCatalog? catalog = null)
    {
        var cat = catalog ?? GameCatalog.Default;

        var player = new Combatant(
            "player_crest",
            playerLoadout.Name,
            Faction.Player,
            playerLoadout,
            new Vec2(4.0, 7.0),
            startingMotes: 2);

        var skirmisherDef = cat.Enemies["enemy_skirmisher"];
        var bruiserDef = cat.Enemies["enemy_bruiser"];
        var artilleryDef = cat.Enemies["enemy_artillery"];

        var skirmisher = new Combatant(
            "enemy_1_skirmisher",
            skirmisherDef.Name,
            Faction.Enemy,
            CrestLoadout.CreateFromConfiguration(skirmisherDef.Assembly, cat, "sk"),
            new Vec2(16.0, 3.0),
            startingMotes: 1,
            skirmisherDef.Archetype,
            skirmisherDef.PreferredRange);

        var bruiser = new Combatant(
            "enemy_2_bruiser",
            bruiserDef.Name,
            Faction.Enemy,
            CrestLoadout.CreateFromConfiguration(bruiserDef.Assembly, cat, "br"),
            new Vec2(14.5, 7.0),
            startingMotes: 1,
            bruiserDef.Archetype,
            bruiserDef.PreferredRange);

        var artillery = new Combatant(
            "enemy_3_artillery",
            artilleryDef.Name,
            Faction.Enemy,
            CrestLoadout.CreateFromConfiguration(artilleryDef.Assembly, cat, "ar"),
            new Vec2(20.5, 10.5),
            startingMotes: 1,
            artilleryDef.Archetype,
            artilleryDef.PreferredRange);

        return new CombatSimulation([player, skirmisher, bruiser, artillery], seed, cat);
    }
}
