using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Simulation;

internal static class ActionEffectResolver
{
    public static void ApplyExecutionEffect(
        Combatant actor,
        ActiveAction active,
        IReadOnlyList<Combatant> allCombatants,
        int tick,
        double arenaWidth,
        double arenaHeight,
        List<IncomingHit> incomingHits,
        List<CombatEvent> events)
    {
        var def = active.Definition;

        // Heat generation (e.g., Dragon's Breath)
        if (def.HeatGenerated > 0)
        {
            bool overheated = actor.Resources.AddHeat(def.HeatGenerated, actor.Stats.HeatCapacity);
            if (overheated)
            {
                events.Add(new CombatEvent(
                    tick,
                    CombatEventType.Overheated,
                    actor.Id,
                    $"{actor.Name} OVERHEATED! Recovery extended due to thermal buildup."));
            }
        }

        // Blink Drive: reactive teleport + evade window
        if (def.BlinkDistance > 0 || def.EvadeDurationTicks > 0)
        {
            if (def.EvadeDurationTicks > 0)
            {
                actor.EvadeTicksRemaining = Math.Max(actor.EvadeTicksRemaining, def.EvadeDurationTicks);
            }

            if (def.BlinkDistance > 0)
            {
                var targetPos = active.TargetPoint ?? (actor.Position + new Vec2(-def.BlinkDistance, 0));
                var delta = targetPos - actor.Position;
                var clampedDist = Math.Min(def.BlinkDistance, delta.Length);
                var newPos = delta.Length > 0.01
                    ? (actor.Position + delta.Normalized() * clampedDist)
                    : actor.Position;

                actor.Position = newPos.Clamp(0.5, arenaWidth - 0.5, 0.5, arenaHeight - 0.5);
                events.Add(new CombatEvent(
                    tick,
                    CombatEventType.CombatantBlinked,
                    actor.Id,
                    $"{actor.Name} blinked to ({actor.Position.X:F1}, {actor.Position.Y:F1}) [Evading].",
                    Position: actor.Position));
            }

            return;
        }

        // Aegis Barrier: timed active defensive ward
        if (def.AegisBlocks > 0)
        {
            actor.ActivateAegis(def.AegisBlocks, def.AegisDurationTicks, def.WitReserve);
            events.Add(new CombatEvent(
                tick,
                CombatEventType.AegisActivated,
                actor.Id,
                $"{actor.Name} raised Aegis Barrier ({def.AegisBlocks} blocks for {def.AegisDurationTicks / (double)ActionCatalog.TicksPerSecond:F1}s)."));
            return;
        }

        // Offensive / displacement actions
        var targets = ResolveTargets(actor, active, allCombatants);
        foreach (var target in targets)
        {
            int damagePerHit = def.BaseDamage;
            if (def.HeavyBonusDamage > 0 && target.Stats.TotalWeight >= def.HeavyWeightThreshold)
            {
                damagePerHit += def.HeavyBonusDamage;
            }

            int hitCount = Math.Max(1, def.HitCount);
            for (int i = 0; i < hitCount; i++)
            {
                incomingHits.Add(new IncomingHit(
                    actor.Id,
                    def.Name,
                    target.Id,
                    actor.Position,
                    damagePerHit,
                    i == 0 ? def.PushDistance : 0,
                    def.InterruptionPower,
                    active.TargetComponentInstanceId));
            }
        }

        events.Add(new CombatEvent(
            tick,
            CombatEventType.ActionExecuted,
            actor.Id,
            $"{actor.Name} executed {def.Name}" + (targets.Count == 0 ? " (no target in area)." : "."),
            active.TargetActorId));
    }

    private static List<Combatant> ResolveTargets(
        Combatant actor,
        ActiveAction active,
        IReadOnlyList<Combatant> allCombatants)
    {
        var def = active.Definition;
        var results = new List<Combatant>();

        if (def.AreaRadius > 0)
        {
            var center = active.TargetPoint;
            if (center is null && active.TargetActorId is not null)
            {
                center = allCombatants.FirstOrDefault(c => c.Id == active.TargetActorId)?.Position;
            }

            if (center is not null)
            {
                foreach (var c in allCombatants)
                {
                    if (c.IsDefeated || c.Faction == actor.Faction)
                        continue;

                    if (c.Position.DistanceTo(center.Value) <= def.AreaRadius)
                        results.Add(c);
                }
            }

            return results;
        }

        if (active.TargetActorId is not null)
        {
            var target = allCombatants.FirstOrDefault(c => c.Id == active.TargetActorId && !c.IsDefeated);
            if (target is not null && actor.Position.DistanceTo(target.Position) <= def.Range + 0.75)
            {
                results.Add(target);
            }
        }

        return results;
    }
}
