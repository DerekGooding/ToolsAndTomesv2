using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.Rules;

public sealed record ProtectionRollResult(bool Saved, IReadOnlyList<int> Rolls);

public sealed class DamageResolutionService
{
    public const int DefaultSaveTarget = 5;

    private readonly InterruptionThresholds _thresholds;

    public DamageResolutionService(InterruptionThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? InterruptionThresholds.Default;
    }

    public ProtectionRollResult RollProtection(int bonusProtectionDice, ISeededRandom rng, int saveTarget = DefaultSaveTarget)
    {
        int diceCount = Math.Max(1, 1 + bonusProtectionDice);
        var rolls = new int[diceCount];
        bool saved = false;

        for (int i = 0; i < diceCount; i++)
        {
            int roll = rng.RollD6();
            rolls[i] = roll;

            if (roll == 1)
                continue; // 1 always hits (fails protection roll)

            if (roll == 6 || roll >= saveTarget)
                saved = true;
        }

        return new ProtectionRollResult(saved, rolls);
    }

    public EquipmentInstance? SelectHitComponent(
        Combatant target,
        string? preferredComponentInstanceId,
        ISeededRandom rng)
    {
        if (!string.IsNullOrEmpty(preferredComponentInstanceId))
        {
            var preferred = target.Loadout.FindById(preferredComponentInstanceId);
            if (preferred is not null && preferred.IsOperational)
                return preferred;
        }

        var outerPool = new List<EquipmentInstance>();
        foreach (var item in target.Loadout.Installed)
        {
            if (!item.IsOperational || item.Definition.IsCore)
                continue;

            outerPool.Add(item);
            if (item.Definition.IsArmor)
                outerPool.Add(item); // Armor absorbs more hits to protect subsystems
        }

        if (outerPool.Count > 0)
            return outerPool[rng.NextInt(0, outerPool.Count)];

        return target.Loadout.Installed.FirstOrDefault(e => e.IsOperational && e.Definition.IsCore);
    }

    public void ResolveActorBatch(
        Combatant target,
        IReadOnlyList<IncomingHit> batch,
        int beat,
        ISeededRandom rng,
        double arenaWidth,
        double arenaHeight,
        List<CombatEvent> events)
    {
        if (target.IsDefeated || batch.Count == 0)
            return;

        if (target.IsEvading)
        {
            events.Add(new CombatEvent(
                beat,
                CombatEventType.DamageBlocked,
                target.Id,
                $"{target.Name} evaded {batch.Count} incoming attack(s) via Blink Drive!"));
            return;
        }

        int maxInterruptionPower = 0;
        bool anyDisplaced = false;

        foreach (var hit in batch)
        {
            if (target.IsDefeated)
                break;

            if (hit.DamagePoints > 0 && target.HasActiveAegis)
            {
                target.AegisBlocksRemaining--;
                events.Add(new CombatEvent(
                    beat,
                    CombatEventType.AegisBlocked,
                    target.Id,
                    $"{target.Name}'s Aegis absorbed {hit.SourceActionName} ({target.AegisBlocksRemaining} block(s) left).",
                    hit.SourceActorId));

                if (target.AegisBlocksRemaining <= 0)
                    target.ClearAegis();

                continue;
            }

            maxInterruptionPower = Math.Max(maxInterruptionPower, hit.InterruptionPower);

            for (int p = 0; p < hit.DamagePoints && !target.IsDefeated; p++)
            {
                var rollResult = RollProtection(target.Stats.BonusProtectionDice, rng);
                string diceText = string.Join(",", rollResult.Rolls);

                if (rollResult.Saved)
                {
                    events.Add(new CombatEvent(
                        beat,
                        CombatEventType.DamageBlocked,
                        target.Id,
                        $"{target.Name} saved vs {hit.SourceActionName} (d6: [{diceText}]).",
                        hit.SourceActorId));
                    continue;
                }

                var component = SelectHitComponent(target, hit.TargetComponentInstanceId, rng);
                if (component is null)
                    break;

                bool nowDisabled = component.ApplyDamage(1);
                events.Add(new CombatEvent(
                    beat,
                    CombatEventType.ComponentDamaged,
                    target.Id,
                    $"{target.Name}'s {component.Definition.Name} damaged by {hit.SourceActionName} (d6: [{diceText}], {component.CurrentDurability}/{component.Definition.MaxDurability}).",
                    hit.SourceActorId,
                    1));

                if (nowDisabled)
                {
                    events.Add(new CombatEvent(
                        beat,
                        CombatEventType.ComponentDisabled,
                        target.Id,
                        $"{target.Name}'s {component.Definition.Name} DISABLED! (Wit tax released: {component.Definition.WitTax}).",
                        hit.SourceActorId));
                }
            }

            if (hit.PushDistance != 0 && !target.IsDefeated)
            {
                int effectiveDist = EffectiveDisplacement(hit.PushDistance, target.Stats.DisplacementResistance);
                var offset = target.Position - hit.SourcePosition;

                // A pull never drags the target past (or onto) its source.
                if (effectiveDist < 0)
                    effectiveDist = -Math.Min(-effectiveDist, Math.Max(0, (int)Math.Floor(offset.Length) - 1));

                if (effectiveDist != 0)
                {
                    var dir = offset.Normalized();
                    if (dir.Length < 0.01)
                        dir = new Vec2(1, 0);

                    target.Position = (target.Position + dir * effectiveDist)
                        .Clamp(0.5, arenaWidth - 0.5, 0.5, arenaHeight - 0.5);
                    anyDisplaced = true;

                    string verb = effectiveDist > 0 ? "pushed" : "pulled";
                    events.Add(new CombatEvent(
                        beat,
                        CombatEventType.CombatantDisplaced,
                        target.Id,
                        $"{target.Name} {verb} {Math.Abs(effectiveDist)} by {hit.SourceActionName}.",
                        hit.SourceActorId,
                        Position: target.Position));
                }
            }
        }

        if (target.IsDefeated)
        {
            target.CurrentAction = null;
            target.MoveDestination = null;
            target.State = ActionState.Idle;
            target.ClearAegis();
            target.Resources.ReleaseAllWit();

            events.Add(new CombatEvent(
                beat,
                CombatEventType.CombatantDefeated,
                target.Id,
                $"{target.Name}'s Core failed — Combatant DEFEATED!"));
            return;
        }

        ApplyBatchInterruption(target, maxInterruptionPower, anyDisplaced, beat, events);
    }

    /// <summary>Displacement resistance subtracts from the magnitude of a push or pull, never reversing it.</summary>
    public static int EffectiveDisplacement(int pushDistance, int resistance) =>
        pushDistance > 0
            ? Math.Max(0, pushDistance - resistance)
            : Math.Min(0, pushDistance + resistance);

    private void ApplyBatchInterruption(
        Combatant target,
        int maxInterruptionPower,
        bool anyDisplaced,
        int beat,
        List<CombatEvent> events)
    {
        if (target.CurrentAction is null)
            return;

        if (target.State != ActionState.Windup && target.State != ActionState.Executing)
            return;

        var tier = InterruptionRules.Evaluate(
            maxInterruptionPower,
            target.CurrentAction.Definition.InterruptionResistance,
            anyDisplaced,
            _thresholds);

        if (tier == InterruptionTier.None)
            return;

        string actionName = target.CurrentAction.Definition.Name;

        if (tier == InterruptionTier.Disruption)
        {
            target.CurrentAction.PhaseBeatsRemaining += _thresholds.DisruptionDelayBeats;
            target.CurrentAction.PhaseTotalBeats += _thresholds.DisruptionDelayBeats;
            events.Add(new CombatEvent(
                beat,
                CombatEventType.ActionInterrupted,
                target.Id,
                $"{target.Name}'s {actionName} DISRUPTED (+{_thresholds.DisruptionDelayBeats} beat delay).",
                Interruption: tier));
            return;
        }

        int penaltyBeats = tier == InterruptionTier.Cancellation
            ? Math.Max(_thresholds.CancellationMinPenaltyBeats, target.CurrentAction.Definition.RecoveryBeats)
            : _thresholds.InterruptedStunBeats;

        target.CurrentAction = null;
        target.Resources.ReleaseWit(Combatant.ActionWitReservationKey);
        target.State = ActionState.Interrupted;
        target.InterruptedBeatsRemaining = penaltyBeats;
        target.InterruptedTotalBeats = penaltyBeats;

        string label = tier == InterruptionTier.Cancellation ? "CANCELED" : "INTERRUPTED";
        events.Add(new CombatEvent(
            beat,
            CombatEventType.ActionInterrupted,
            target.Id,
            $"{target.Name}'s {actionName} {label}!",
            Interruption: tier));
    }
}
