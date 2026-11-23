using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Rules;

/// <summary>An enemy action whose impact window is open against a defender.</summary>
public sealed record IncomingImpact(Combatant Attacker, ActiveAction Action, int ExpectedDamage);

public static class ImpactRules
{
    /// <summary>
    /// Enemy actions currently in their impact window that would hit <paramref name="defender"/>:
    /// either aimed at it directly, or an area whose footprint covers it.
    /// Reactions may only be declared while at least one exists.
    /// </summary>
    public static IReadOnlyList<IncomingImpact> GetIncomingImpacts(Combatant defender, IReadOnlyList<Combatant> all)
    {
        var results = new List<IncomingImpact>();
        foreach (var attacker in all)
        {
            if (attacker.IsDefeated || attacker.Faction == defender.Faction)
                continue;

            foreach (var act in attacker.ActionsInMotion)
            {
                if (act.Phase != ActionState.Executing || act.Definition.Targeting == TargetingType.Self)
                    continue;

                if (!WouldAffect(defender, act, all))
                    continue;

                int perHit = act.Definition.BaseDamage;
                if (act.Definition.HeavyBonusDamage > 0 && defender.Stats.TotalWeight >= act.Definition.HeavyWeightThreshold)
                    perHit += act.Definition.HeavyBonusDamage;

                results.Add(new IncomingImpact(attacker, act, perHit * Math.Max(1, act.Definition.HitCount)));
            }
        }

        return results;
    }

    private static bool WouldAffect(Combatant defender, ActiveAction act, IReadOnlyList<Combatant> all)
    {
        if (act.Definition.AreaRadius > 0)
        {
            var center = act.TargetPoint;
            if (center is null && act.TargetActorId is not null)
                center = all.FirstOrDefault(c => c.Id == act.TargetActorId)?.Position;

            return center is not null && defender.Position.DistanceTo(center.Value) <= act.Definition.AreaRadius;
        }

        return act.TargetActorId == defender.Id;
    }
}
