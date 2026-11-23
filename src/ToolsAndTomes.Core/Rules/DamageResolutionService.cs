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

    /// <summary>
    /// The defender chooses where damage lands: each penetrating point goes to the first operational
    /// component in the Crest's Damage Order. Destroyed components no longer absorb damage,
    /// so remaining points flow to the next component in order.
    /// </summary>
    public static EquipmentInstance? SelectHitComponent(Combatant target) =>
        target.Loadout.DamageOrder.FirstOrDefault(e => e.IsOperational);

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

            // Defenses save against damage, never against effects: interruption and displacement always apply.
            maxInterruptionPower = Math.Max(maxInterruptionPower, hit.InterruptionPower);

            int damagePoints = hit.DamagePoints;
            if (damagePoints > 0 && target.HasActiveAegis)
            {
                damagePoints = 0;
                target.AegisBlocksRemaining--;
                events.Add(new CombatEvent(
                    beat,
                    CombatEventType.AegisBlocked,
                    target.Id,
                    $"{target.Name}'s Aegis absorbed the damage of {hit.SourceActionName} ({target.AegisBlocksRemaining} block(s) left).",
                    hit.SourceActorId));

                if (target.AegisBlocksRemaining <= 0)
                    target.ClearAegis();
            }

            for (int p = 0; p < damagePoints && !target.IsDefeated; p++)
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

                var component = SelectHitComponent(target);
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
