using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

public static class EncounterFactory
{
    public const string PlayerCombatantId = "player_crest";
    public const string TestEncounterId = "enc_proving_grounds";

    /// <summary>The Proving Grounds test encounter (defined in Content/encounters.json).</summary>
    public static CombatSimulation CreateTestEncounter(
        CrestLoadout playerLoadout,
        int seed = 1337,
        GameCatalog? catalog = null)
    {
        var cat = catalog ?? GameCatalog.Default;
        return CreateEncounter(cat.Encounters[TestEncounterId], playerLoadout, seed, cat);
    }

    /// <summary>
    /// Builds a simulation from an encounter definition. Enemy combatants are named
    /// <c>enemy_{n}_{archetype}</c> in placement order.
    /// </summary>
    public static CombatSimulation CreateEncounter(
        EncounterDefinition encounter,
        CrestLoadout playerLoadout,
        int seed,
        GameCatalog catalog)
    {
        var combatants = new List<Combatant>
        {
            new(PlayerCombatantId, playerLoadout.Name, Faction.Player, playerLoadout,
                encounter.PlayerStart, encounter.PlayerStartingMotes)
        };

        for (int i = 0; i < encounter.Enemies.Count; i++)
        {
            var placement = encounter.Enemies[i];
            var def = catalog.Enemies[placement.EnemyId];
            combatants.Add(new Combatant(
                $"enemy_{i + 1}_{def.Archetype.ToString().ToLowerInvariant()}",
                def.Name,
                Faction.Enemy,
                CrestLoadout.CreateFromConfiguration(def.Assembly, catalog, $"e{i + 1}"),
                placement.Position,
                placement.StartingMotes,
                def.Archetype,
                def.PreferredRange));
        }

        return new CombatSimulation(combatants, seed, catalog, arenaWidth: encounter.ArenaWidth, arenaHeight: encounter.ArenaHeight);
    }
}
