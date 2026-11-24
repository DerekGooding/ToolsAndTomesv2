using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

public static class EncounterFactory
{
    public const string PlayerCombatantId = "player_crest";
    public const string TestEncounterId = "enc_proving_grounds";

    /// <summary>Combatant id of an allied pilot's Crest.</summary>
    public static string AllyCombatantId(string pilotId) => $"ally_{pilotId}";

    /// <summary>A fresh allied Crest for <paramref name="pilot"/>, built from <paramref name="loadout"/> (or the pilot's starting assembly).</summary>
    public static Combatant CreateAlly(PilotDefinition pilot, GameCatalog catalog, CrestLoadout? loadout = null, int startingMotes = 0)
    {
        loadout ??= CrestLoadout.CreateFromConfiguration(pilot.Assembly, catalog, pilot.Id);
        return new Combatant(AllyCombatantId(pilot.Id), pilot.DisplayName, Faction.Player, loadout, new Vec2(0, 0), startingMotes)
        {
            Rules = catalog.Rules
        };
    }

    /// <summary>Allied Crests for the given pilot ids, each with the pilot's starting assembly.</summary>
    public static IReadOnlyList<Combatant> CreateAllies(IEnumerable<string> pilotIds, GameCatalog catalog, int startingMotes = 0) =>
        pilotIds.Select(id => CreateAlly(catalog.Pilots[id], catalog, startingMotes: startingMotes)).ToList();

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
        GameCatalog catalog,
        IReadOnlyList<Combatant>? allies = null)
    {
        var player = new Combatant(PlayerCombatantId, playerLoadout.Name, Faction.Player, playerLoadout,
            encounter.PlayerStart, encounter.PlayerStartingMotes);
        return CreateEncounter(encounter, player, seed, catalog, allies);
    }

    /// <summary>
    /// Builds a simulation around an existing player Crest (campaign missions: damage, Motes, and fast-iron carry
    /// over). The Crest is readied with <see cref="Combatant.PrepareForEncounter"/>.
    /// Allied Crests (see <see cref="CreateAlly"/>) take the encounter's ally starts in order; a defeated ally sits
    /// the fight out.
    /// </summary>
    public static CombatSimulation CreateEncounter(
        EncounterDefinition encounter,
        Combatant player,
        int seed,
        GameCatalog catalog,
        IReadOnlyList<Combatant>? allies = null)
    {
        if (player.Id != PlayerCombatantId)
            throw new ArgumentException($"The player Crest must use id '{PlayerCombatantId}'.", nameof(player));

        player.PrepareForEncounter(encounter.PlayerStart);
        var combatants = new List<Combatant> { player };

        int slot = 0;
        foreach (var ally in allies ?? [])
        {
            if (ally.Faction != Faction.Player || ally.Id == PlayerCombatantId)
                throw new ArgumentException($"'{ally.Id}' is not an allied Crest.", nameof(allies));
            if (ally.IsDefeated)
                continue;

            ally.PrepareForEncounter(encounter.AllyStart(slot++));
            combatants.Add(ally);
        }

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
