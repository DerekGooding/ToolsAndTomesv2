using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.Rules;

/// <summary>
/// Field shaping: turning fast-iron (loose from Square Summon, or carried in cargo) into components,
/// consumables, or ammunition mid-fight. Costs come from content (per component, consumable, or weapon).
/// </summary>
public static class ShapingRules
{
    /// <summary>Fast-iron to shape a component: its own fastIronCost, else ceil(weight / rules divisor), minimum 1.</summary>
    public static int ComponentCost(EquipmentDefinition def, RulesConfig rules) =>
        def.FastIronCost > 0
            ? def.FastIronCost
            : Math.Max(1, (def.Weight + rules.ComponentWeightPerFastIron - 1) / Math.Max(1, rules.ComponentWeightPerFastIron));

    /// <summary>What an order will cost and produce, or why it cannot be done.</summary>
    public sealed record ShapingPlan(int FastIronCost, EquipmentDefinition? Component, ConsumableDefinition? Consumable, EquipmentInstance? AmmoTarget);

    /// <summary>
    /// Validates the command-specific parts of shaping and ammo-refill actions (which components, which
    /// consumable, cargo room, fast-iron on hand). Returns the plan when valid.
    /// </summary>
    public static (ActionValidationResult Result, ShapingPlan? Plan) Validate(
        Combatant actor, ActionDefinition action, UseActionCommand command, GameCatalog catalog)
    {
        if (action.RefillsAmmo)
        {
            var (ok, target) = ValidateAmmoTarget(actor, command.OwnComponentInstanceId);
            return ok.IsValid ? (ok, new ShapingPlan(0, null, null, target)) : (ok, null);
        }

        if (!action.IsShaping)
            return (ActionValidationResult.Valid, null);

        if (command.Shaping is not { } order)
            return (ActionValidationResult.Fail("Choose what to shape."), null);

        var rules = actor.Rules;
        var stats = actor.Stats;
        ShapingPlan plan;

        switch (order.Kind)
        {
            case ShapingKind.Component:
                if (order.ItemId is null || !catalog.Equipment.TryGetValue(order.ItemId, out var comp))
                    return (ActionValidationResult.Fail("Unknown component."), null);
                if (actor.KnownBlueprints is { } known && !known.Contains(comp.Id))
                    return (ActionValidationResult.Fail($"{comp.Name}: blueprint not researched."), null);
                if (stats.AvailableWit(0) + comp.BonusWitCapacity - comp.WitTax < 0)
                    return (ActionValidationResult.Fail($"{comp.Name}'s Wit Tax would exceed capacity."), null);
                plan = new ShapingPlan(ComponentCost(comp, rules), comp, null, null);
                break;

            case ShapingKind.Consumable:
                if (order.ItemId is null || !catalog.Consumables.TryGetValue(order.ItemId, out var item))
                    return (ActionValidationResult.Fail("Unknown consumable."), null);
                if (stats.CargoUsed + item.CargoSize > stats.CargoCapacity)
                    return (ActionValidationResult.Fail($"No cargo room for {item.Name}."), null);
                plan = new ShapingPlan(item.FastIronCost, null, item, null);
                break;

            case ShapingKind.AmmoRefill:
                var (ammoOk, weapon) = ValidateAmmoTarget(actor, command.OwnComponentInstanceId);
                if (!ammoOk.IsValid)
                    return (ammoOk, null);
                plan = new ShapingPlan(weapon!.Definition.AmmoRefillFastIronCost, null, null, weapon);
                break;

            default:
                return (ActionValidationResult.Fail("Unknown shaping order."), null);
        }

        if (actor.TotalFastIron < plan.FastIronCost)
            return (ActionValidationResult.Fail($"Needs {plan.FastIronCost} fast-iron (have {actor.TotalFastIron})."), null);

        return (ActionValidationResult.Valid, plan);
    }

    /// <summary>
    /// Produces what a validated plan describes (fast-iron already paid): a component installed at the very end of
    /// the Damage Order, a consumable stowed in cargo (fast-iron returned loose if there is no room), or a full
    /// ammo refill. Shared by the Shape Fast-iron spell in combat and shaping during rests.
    /// </summary>
    public static void Apply(Combatant actor, ShapingPlan plan, string instanceIdPrefix, int beat, List<CombatEvent> events)
    {
        if (plan.Component is { } component)
        {
            // Shaped parts persist on the assembly, so ids must stay unique across rests, fights, and missions.
            // Include the assembly's id: every player Crest shares one combatant id, and counters restart per mission.
            string baseId = $"{actor.Loadout.Id}_{instanceIdPrefix}_{component.Id}";
            string id = baseId;
            for (int n = 2; actor.Loadout.FindById(id) is not null; n++)
                id = $"{baseId}_{n}";
            var instance = new EquipmentInstance(id, component);
            actor.Loadout.InstallAtEnd(instance);
            events.Add(new CombatEvent(beat, CombatEventType.FieldShaped, actor.Id,
                $"{actor.Name} shaped {plan.FastIronCost} fast-iron into a new {component.Name} (end of Damage Order).",
                Value: plan.FastIronCost));
        }
        else if (plan.Consumable is { } item)
        {
            if (actor.Loadout.TryAddConsumable(item, actor.Rules))
            {
                events.Add(new CombatEvent(beat, CombatEventType.FieldShaped, actor.Id,
                    $"{actor.Name} shaped {plan.FastIronCost} fast-iron into {item.Name} (stowed in cargo).",
                    Value: plan.FastIronCost));
            }
            else
            {
                actor.Resources.AddFastIron(plan.FastIronCost);
                events.Add(new CombatEvent(beat, CombatEventType.CommandRejected, actor.Id,
                    $"{actor.Name} has no cargo room for {item.Name}; the fast-iron is kept."));
            }
        }
        else if (plan.AmmoTarget is { } weapon && weapon.IsOperational)
        {
            weapon.RefillAmmo();
            events.Add(new CombatEvent(beat, CombatEventType.FieldShaped, actor.Id,
                $"{actor.Name} shaped {plan.FastIronCost} fast-iron into ammunition for {weapon.Definition.Name} ({weapon.CurrentAmmo}/{weapon.Definition.MaxAmmo}).",
                Value: plan.FastIronCost));
        }
    }

    private static (ActionValidationResult, EquipmentInstance?) ValidateAmmoTarget(Combatant actor, string? componentId)
    {
        var weapon = componentId is null ? null : actor.Loadout.FindById(componentId);
        if (weapon is null)
            return (ActionValidationResult.Fail("Choose a weapon to restock."), null);
        if (weapon.Definition.MaxAmmo <= 0)
            return (ActionValidationResult.Fail($"{weapon.Definition.Name} does not use ammo."), null);
        if (!weapon.IsOperational)
            return (ActionValidationResult.Fail($"{weapon.Definition.Name} is disabled."), null);
        if (weapon.CurrentAmmo >= weapon.Definition.MaxAmmo)
            return (ActionValidationResult.Fail($"{weapon.Definition.Name} is already full."), null);

        return (ActionValidationResult.Valid, weapon);
    }
}
