using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;

namespace ToolsAndTomes.Core.Rules;

public sealed record ActionValidationResult(bool IsValid, string? FailureReason = null)
{
    public static ActionValidationResult Valid { get; } = new(true);
    public static ActionValidationResult Fail(string reason) => new(false, reason);
}

public static class ActionValidationRules
{
    private const double RangeEpsilon = 1e-6;

    public static ActionValidationResult ValidateUseAction(
        Combatant actor,
        ActionDefinition action,
        Combatant? target,
        Vec2? targetPoint)
    {
        if (actor.IsDefeated)
            return ActionValidationResult.Fail("Combatant is defeated.");

        var stats = actor.Stats;

        var provider = actor.Loadout.FindOperationalProviderForAction(action.Id);
        if (provider is null)
            return ActionValidationResult.Fail($"No operational component provides {action.Name}.");

        if (action.RequiresSpellbook && !stats.HasSpellbook)
            return ActionValidationResult.Fail($"{action.Name} requires an operational Spellbook.");

        if (action.RequiresSpellAmplifier && !stats.HasSpellAmplifier)
            return ActionValidationResult.Fail($"{action.Name} requires an operational Arcane Amplifier.");

        if (actor.GetCooldown(action.Id) > 0)
            return ActionValidationResult.Fail($"{action.Name} is on cooldown.");

        if (action.AmmoCost > 0 && provider.CurrentAmmo < action.AmmoCost)
            return ActionValidationResult.Fail($"Insufficient ammo for {action.Name}.");

        if (action.MoteCost > stats.MaxMotes)
            return ActionValidationResult.Fail($"{action.Name} needs {action.MoteCost} Motes; this Crest can only store {stats.MaxMotes}.");

        if (action.MoteCost > 0 && actor.Resources.CurrentMotes < action.MoteCost)
            return ActionValidationResult.Fail($"Requires {action.MoteCost} Mote(s).");

        if (action.WitReserve > 0 && stats.AvailableWit(actor.Resources.ReservedWit) < action.WitReserve)
            return ActionValidationResult.Fail($"Insufficient available Wit (needs {action.WitReserve}).");

        if (action.Targeting == TargetingType.EnemyUnit)
        {
            if (target is null)
                return ActionValidationResult.Fail("Requires a target combatant.");
            if (target.IsDefeated)
                return ActionValidationResult.Fail("Target is already defeated.");
        }
        else if (action.Targeting == TargetingType.Point && targetPoint is null && target is null && action.BlinkDistance == 0)
        {
            return ActionValidationResult.Fail("Requires a target point.");
        }

        if (action.IsReactive)
        {
            if (actor.FindFreeProvider(action.Id) is null)
                return ActionValidationResult.Fail($"{action.Name}'s component is busy.");

            // Reactions resolve in the beat they are declared, so they cannot walk into range.
            if (action.Targeting == TargetingType.EnemyUnit && !IsInRange(actor, action, target, targetPoint))
                return ActionValidationResult.Fail($"Target is out of range for {action.Name}.");
        }
        else if (!IsInRange(actor, action, target, targetPoint) && actor.FindLocomotionProvider() is null)
        {
            return ActionValidationResult.Fail($"Target is out of range and the Crest cannot move.");
        }

        return ActionValidationResult.Valid;
    }

    /// <summary>
    /// Full validation including battlefield context: reactions are only valid while an enemy impact
    /// is landing on the actor.
    /// </summary>
    public static ActionValidationResult ValidateUseAction(
        Combatant actor,
        ActionDefinition action,
        Combatant? target,
        Vec2? targetPoint,
        IReadOnlyList<Combatant> battlefield)
    {
        var basic = ValidateUseAction(actor, action, target, targetPoint);
        if (!basic.IsValid)
            return basic;

        if (action.IsReactive && ImpactRules.GetIncomingImpacts(actor, battlefield).Count == 0)
            return ActionValidationResult.Fail($"{action.Name} can only be used during an incoming impact.");

        return ActionValidationResult.Valid;
    }

    public static bool IsInRange(Combatant actor, ActionDefinition action, Combatant? target, Vec2? targetPoint)
    {
        if (action.Targeting == TargetingType.Self || action.Range <= 0)
            return true;

        var destination = target?.Position ?? targetPoint;
        if (destination is null)
            return false;

        return actor.Position.DistanceTo(destination.Value) <= action.Range + RangeEpsilon;
    }
}
