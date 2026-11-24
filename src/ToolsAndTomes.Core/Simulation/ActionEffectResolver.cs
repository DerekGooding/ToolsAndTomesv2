using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Simulation;

internal static class ActionEffectResolver
{
    public static void ApplyExecutionEffect(
        Combatant actor,
        ActiveAction active,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
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
                    beat,
                    CombatEventType.Overheated,
                    actor.Id,
                    $"{actor.Name} OVERHEATED! Recovery extended due to thermal buildup."));
            }
        }

        // Square Summon: condense Motes into fast-iron
        if (def.FastIronProduced > 0)
        {
            actor.Resources.AddFastIron(def.FastIronProduced);
            events.Add(new CombatEvent(
                beat,
                CombatEventType.FastIronProduced,
                actor.Id,
                $"{actor.Name} summoned {def.FastIronProduced} fast-iron (total {actor.Resources.FastIron}).",
                Value: def.FastIronProduced));
            return;
        }

        // Field shaping: fast-iron becomes a component, a consumable, or ammunition
        if (def.IsShaping && active.ShapingPlan is { } plan)
        {
            ShapingRules.Apply(actor, plan, $"{actor.Id}_shaped_{active.Id}", beat, events);
            return;
        }

        // Prepared ammo pack: restock one of the Crest's weapons
        if (def.RefillsAmmo)
        {
            var weapon = active.OwnComponentInstanceId is null ? null : actor.Loadout.FindById(active.OwnComponentInstanceId);
            if (weapon is not null && weapon.IsOperational)
            {
                weapon.RefillAmmo();
                events.Add(new CombatEvent(beat, CombatEventType.AmmoRefilled, actor.Id,
                    $"{actor.Name} restocked {weapon.Definition.Name} ({weapon.CurrentAmmo}/{weapon.Definition.MaxAmmo})."));
            }
            return;
        }

        // Jet Dash: self-propelled relocation toward the target point
        if (def.DashDistance > 0)
        {
            var dest = active.TargetPoint ?? actor.Position;
            var delta = dest - actor.Position;
            double dist = Math.Min(def.DashDistance, delta.Length);
            if (dist > 0.01)
                actor.Position = (actor.Position + delta.Normalized() * dist).Clamp(0.5, arenaWidth - 0.5, 0.5, arenaHeight - 0.5);

            events.Add(new CombatEvent(
                beat,
                CombatEventType.CombatantDisplaced,
                actor.Id,
                $"{actor.Name} dashed to ({actor.Position.X:F1}, {actor.Position.Y:F1}).",
                Position: actor.Position));
            return;
        }

        // Blink Drive: reactive teleport + evade window
        if (def.BlinkDistance > 0 || def.EvadeBeats > 0)
        {
            if (def.EvadeBeats > 0)
                actor.EvadeBeatsRemaining = Math.Max(actor.EvadeBeatsRemaining, def.EvadeBeats);

            if (def.BlinkDistance > 0)
            {
                var targetPos = active.TargetPoint ?? DefaultBlinkDestination(actor, allCombatants, def.BlinkDistance);
                var delta = targetPos - actor.Position;
                var clampedDist = Math.Min(def.BlinkDistance, delta.Length);
                var newPos = delta.Length > 0.01
                    ? actor.Position + delta.Normalized() * clampedDist
                    : actor.Position;

                actor.Position = newPos.Clamp(0.5, arenaWidth - 0.5, 0.5, arenaHeight - 0.5);
                events.Add(new CombatEvent(
                    beat,
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
            actor.ActivateAegis(def.AegisBlocks, def.AegisDurationBeats, def.WitReserve);
            events.Add(new CombatEvent(
                beat,
                CombatEventType.AegisActivated,
                actor.Id,
                $"{actor.Name} raised Aegis Barrier ({def.AegisBlocks} blocks for {def.AegisDurationBeats} beats)."));
            return;
        }

        if (def.Targeting == TargetingType.Self)
        {
            events.Add(new CombatEvent(beat, CombatEventType.ActionExecuted, actor.Id, $"{actor.Name} executed {def.Name}."));
            return;
        }

        // Offensive / displacement actions
        var targets = ResolveTargets(actor, active, allCombatants);
        foreach (var target in targets)
        {
            int damagePerHit = TraitRules.DamagePerHit(def, target);

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
                    def.InterruptionPower));
            }
        }

        events.Add(new CombatEvent(
            beat,
            CombatEventType.ActionExecuted,
            actor.Id,
            $"{actor.Name} executed {def.Name}" + (targets.Count == 0 ? " (no target in reach)." : "."),
            active.TargetActorId));
    }

    /// <summary>Without an explicit destination, Blink moves directly away from the nearest hostile combatant.</summary>
    private static Vec2 DefaultBlinkDestination(Combatant actor, IReadOnlyList<Combatant> all, int distance)
    {
        var nearest = all
            .Where(c => c.Faction != actor.Faction && !c.IsDefeated)
            .OrderBy(c => c.Position.DistanceTo(actor.Position))
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        var away = nearest is null ? new Vec2(0, 0) : (actor.Position - nearest.Position).Normalized();
        if (away.Length < 0.01)
            away = actor.Faction == Faction.Player ? new Vec2(-1, 0) : new Vec2(1, 0);

        return actor.Position + away * distance;
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
                center = allCombatants.FirstOrDefault(c => c.Id == active.TargetActorId)?.Position;

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
            if (target is not null && actor.Position.DistanceTo(target.Position) <= def.Range + actor.Rules.ResolutionRangeTolerance)
                results.Add(target);
        }

        return results;
    }
}
