using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.AI;

/// <summary>
/// Drives the player's allied Crests (every Player-faction Crest except the player's own). Allies are fully
/// autonomous: each fights like <see cref="AutoPilot"/>, but picks the enemy that is close to itself and to
/// the player's Crest, so the lance converges instead of scattering.
/// </summary>
public sealed class AllyAIController
{
    /// <summary>How much an enemy's distance to the player's Crest counts against it, relative to its distance to the ally.</summary>
    public const double LeadWeight = 0.5;

    private readonly AutoPilot _pilot = new();

    public void Update(CombatSimulation sim)
    {
        if (sim.IsPaused || sim.Outcome != EncounterOutcome.InProgress)
            return;

        var lead = sim.FindCombatant(EncounterFactory.PlayerCombatantId);
        foreach (var ally in sim.Combatants.Where(IsAlly).ToList())
        {
            if (!ally.IsDefeated)
                _pilot.Update(sim, ally, ChooseTarget(sim, ally, lead));
        }
    }

    public static bool IsAlly(Combatant c) => c.Faction == Faction.Player && c.Id != EncounterFactory.PlayerCombatantId;

    public static Combatant? ChooseTarget(CombatSimulation sim, Combatant ally, Combatant? lead)
    {
        bool leadUp = lead is { IsDefeated: false };
        return sim.Combatants
            .Where(c => c.Faction == Faction.Enemy && !c.IsDefeated)
            .OrderBy(c => c.Position.DistanceTo(ally.Position) + (leadUp ? LeadWeight * c.Position.DistanceTo(lead!.Position) : 0))
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
