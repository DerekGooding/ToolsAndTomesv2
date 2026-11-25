using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Rules;

namespace ToolsAndTomes.Core.Simulation;

internal static class ActionEffectResolver
{
    /// <summary>
    /// Applies an action's effect at the end of its impact window. Every event the effect produces is tagged
    /// with the action's id, so presentation (sound, VFX) can key off it.
    /// </summary>
    public static void ApplyExecutionEffect(
        Combatant actor,
        ActiveAction active,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        double arenaWidth,
        double arenaHeight,
        List<IncomingHit> incomingHits,
        List<CombatEvent> events,
        GameCatalog catalog)
    {
        int first = events.Count;
        ApplyEffect(actor, active, allCombatants, beat, arenaWidth, arenaHeight, incomingHits, events, catalog);
        for (int i = first; i < events.Count; i++)
            if (events[i].ActionId is null)
                events[i] = events[i] with { ActionId = active.Definition.Id };
    }

    private static void ApplyEffect(
        Combatant actor,
        ActiveAction active,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        double arenaWidth,
        double arenaHeight,
        List<IncomingHit> incomingHits,
        List<CombatEvent> events,
        GameCatalog catalog)
    {
        var def = active.Definition;

        if (ApplySpellUtility(actor, active, allCombatants, beat, events, catalog))
            return;

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
            double dist = actor.IsPinned ? 0 : Math.Min(def.DashDistance, delta.Length);
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

            if (def.BlinkDistance > 0 && !actor.IsPinned)
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
        // An Arcane Amplifier lends tome and core spells +1 interruption power.
        int interruptionPower = def.InterruptionPower +
                                (def.Category == ActionCategory.Spell && def.InterruptionPower > 0 && actor.Stats.HasSpellAmplifier ? 1 : 0);
        foreach (var target in targets)
        {
            ApplyDebuffs(def, target, actor, beat, events);
            if (def.BaseDamage <= 0 && def.PushDistance == 0 && def.InterruptionPower <= 0)
                continue; // a pure debuff: no hit to resolve

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
                    interruptionPower));
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

        // Chain: jump from the last target to the nearest other enemy within reach, up to ChainJumps times.
        for (int jump = 0; jump < def.ChainJumps && results.Count > 0; jump++)
        {
            var last = results[^1];
            var next = allCombatants
                .Where(c => !c.IsDefeated && c.Faction != actor.Faction && !results.Contains(c))
                .Where(c => c.Position.DistanceTo(last.Position) <= def.ChainRange)
                .OrderBy(c => c.Position.DistanceTo(last.Position))
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (next is null)
                break;
            results.Add(next);
        }

        return results;
    }

    /// <summary>Slow, windup delay, and pin on an enemy target (effects, so defenses don't save against them; evasion dodges them).</summary>
    private static void ApplyDebuffs(ActionDefinition def, Combatant target, Combatant actor, int beat, List<CombatEvent> events)
    {
        if ((def.SlowAmount <= 0 || def.SlowBeats <= 0) && def.WindupDelay <= 0 && def.PinBeats <= 0)
            return;
        if (target.IsEvading)
            return;

        var parts = new List<string>();
        if (def.SlowAmount > 0 && def.SlowBeats > 0)
        {
            target.SlowAmount = Math.Max(target.SlowAmount, def.SlowAmount);
            target.SlowBeatsRemaining = Math.Max(target.SlowBeatsRemaining, def.SlowBeats);
            parts.Add($"slowed by {def.SlowAmount} for {def.SlowBeats} beats");
        }
        if (def.WindupDelay > 0)
        {
            target.PendingWindupDelay = Math.Max(target.PendingWindupDelay, def.WindupDelay);
            parts.Add($"next windup +{def.WindupDelay} beat(s)");
        }
        if (def.PinBeats > 0)
        {
            target.PinnedBeatsRemaining = Math.Max(target.PinnedBeatsRemaining, def.PinBeats);
            target.StopMovement();
            parts.Add($"pinned for {def.PinBeats} beats");
        }

        events.Add(new CombatEvent(beat, CombatEventType.ActionExecuted, target.Id,
            $"{target.Name} is {string.Join(", ", parts)} by {actor.Name}'s {def.Name}.", actor.Id, ActionId: def.Id));
    }

    /// <summary>
    /// Tome utility spells that don't strike enemies: harness swaps, conjuring, mending, warding an ally, and
    /// Bulwark. Returns true if the action was one of them (and has been applied).
    /// </summary>
    private static bool ApplySpellUtility(
        Combatant actor,
        ActiveAction active,
        IReadOnlyList<Combatant> allCombatants,
        int beat,
        List<CombatEvent> events,
        GameCatalog catalog)
    {
        var def = active.Definition;

        if (def.IsSwap)
        {
            var held = active.OwnComponentInstanceId is null ? null : actor.Loadout.FindById(active.OwnComponentInstanceId);
            var harness = actor.Loadout.FindById(active.SourceEquipmentInstanceId);
            var stowed = harness is null ? null : actor.Loadout.StowedIn(harness);
            bool ok = held is not null && stowed is not null && actor.Loadout.Swap(harness!.InstanceId, held.InstanceId);
            events.Add(new CombatEvent(beat, ok ? CombatEventType.ActionExecuted : CombatEventType.CommandRejected, actor.Id,
                ok ? $"{actor.Name} swapped {held!.Definition.Name} for {stowed!.Definition.Name}."
                   : $"{actor.Name}'s {def.Name} found nothing to swap."));
            return true;
        }

        if (def.ConjuresComponentId is { } conjureId)
        {
            if (!catalog.Equipment.TryGetValue(conjureId, out var conjuredDef) || !actor.Loadout.HasFreeHand() ||
                !actor.TrySpendFastIron(def.FastIronSpent))
            {
                events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
                    $"{actor.Name}'s {def.Name} fizzled: it needs {def.FastIronSpent} fast-iron and a free hand."));
                return true;
            }

            var part = new EquipmentInstance($"{actor.Id}_conjured_{active.Id}", conjuredDef) { IsConjured = true };
            actor.Loadout.InstallAtEnd(part);
            actor.Loadout.HoldInFreeHand(part);
            events.Add(new CombatEvent(beat, CombatEventType.FieldShaped, actor.Id,
                $"{actor.Name} conjured {conjuredDef.Name} into a free hand (until the fight ends).",
                ComponentInstanceId: part.InstanceId));
            return true;
        }

        if (def.MendPoints > 0)
        {
            var target = active.TargetActorId is null ? actor : allCombatants.FirstOrDefault(c => c.Id == active.TargetActorId) ?? actor;
            var part = target.Loadout.DamageOrder
                .Where(e => e.CurrentDurability < e.Definition.MaxDurability)
                .OrderByDescending(e => e.IsDisabled)
                .ThenByDescending(e => e.Definition.IsCore)
                .ThenByDescending(e => e.Definition.MaxDurability - e.CurrentDurability)
                .FirstOrDefault();
            if (part is null || !actor.TrySpendFastIron(def.FastIronSpent))
            {
                events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
                    part is null ? $"{target.Name} has nothing to mend." : $"{actor.Name}'s {def.Name} needs {def.FastIronSpent} fast-iron."));
                return true;
            }

            part.RestoreDurability(def.MendPoints);
            events.Add(new CombatEvent(beat, CombatEventType.FieldShaped, target.Id,
                $"{actor.Name} mended {target.Name}'s {part.Definition.Name} ({part.CurrentDurability}/{part.Definition.MaxDurability}).",
                actor.Id, ComponentInstanceId: part.InstanceId));
            return true;
        }

        if (def.AegisBlocks > 0 && def.Targeting == TargetingType.AllyUnit)
        {
            var target = allCombatants.FirstOrDefault(c => c.Id == active.TargetActorId && !c.IsDefeated);
            if (target is null)
                return true;
            target.GrantWard(def.AegisBlocks, def.AegisDurationBeats);
            events.Add(new CombatEvent(beat, CombatEventType.AegisActivated, target.Id,
                $"{actor.Name} warded {target.Name} ({def.AegisBlocks} block(s) for {def.AegisDurationBeats} beats).", actor.Id));
            return true;
        }

        if (def.ProtectionBonusDice > 0)
        {
            var covered = allCombatants
                .Where(c => !c.IsDefeated && c.Faction == actor.Faction && c.Position.DistanceTo(actor.Position) <= Math.Max(0, def.AreaRadius))
                .ToList();
            foreach (var c in covered)
            {
                c.ProtectionBuffDice = Math.Max(c.ProtectionBuffDice, def.ProtectionBonusDice);
                c.ProtectionBuffBeats = Math.Max(c.ProtectionBuffBeats, def.BuffDurationBeats);
            }
            events.Add(new CombatEvent(beat, CombatEventType.AegisActivated, actor.Id,
                $"{actor.Name}'s {def.Name}: +{def.ProtectionBonusDice} protection die for {def.BuffDurationBeats} beats on {string.Join(", ", covered.Select(c => c.Name))}."));
            return true;
        }

        return false;
    }
}
