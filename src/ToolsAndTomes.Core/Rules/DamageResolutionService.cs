using ToolsAndTomes.Core.Definitions;
using ToolsAndTomes.Core.Instances;
using ToolsAndTomes.Core.Simulation;

namespace ToolsAndTomes.Core.Rules;

public sealed record ProtectionRollResult(bool Saved, IReadOnlyList<int> Rolls);

public sealed class DamageResolutionService
{
    private readonly RulesConfig _rules;
    private readonly InterruptionThresholds _thresholds;

    public DamageResolutionService(RulesConfig? rules = null)
    {
        _rules = rules ?? RulesConfig.Default;
        _thresholds = _rules.Interruption;
    }

    public ProtectionRollResult RollProtection(int bonusProtectionDice, ISeededRandom rng, int? saveTarget = null)
    {
        int target = saveTarget ?? _rules.ProtectionSaveTarget;
        int diceCount = Math.Max(1, 1 + bonusProtectionDice);
        var rolls = new int[diceCount];
        bool saved = false;

        for (int i = 0; i < diceCount; i++)
        {
            int roll = rng.RollD6();
            rolls[i] = roll;

            if (roll == 1)
                continue; // 1 always hits (fails protection roll)

            if (roll == 6 || roll >= target)
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

            // Partial cover: a Low obstacle the attack crossed near the target adds protection dice.
            DealDamagePoints(target, damagePoints, target.BonusProtectionDice + hit.CoverDice, hit.SourceActionName, hit.SourceActorId, rng, beat, events);

            if (hit.PushDistance != 0 && !target.IsDefeated && !target.IsPinned)
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

                    // The Crest slides until it would touch an obstacle.
                    var wanted = (target.Position + dir * effectiveDist).Clamp(0.5, arenaWidth - 0.5, 0.5, arenaHeight - 0.5);
                    var slide = target.Terrain.Sweep(target.Position, wanted);
                    target.Position = slide.Stop;
                    anyDisplaced = true;

                    string verb = effectiveDist > 0 ? "pushed" : "pulled";
                    events.Add(new CombatEvent(
                        beat,
                        CombatEventType.CombatantDisplaced,
                        target.Id,
                        $"{target.Name} {verb} {slide.Travelled:0.#} by {hit.SourceActionName}" +
                        (slide.Hit is { } wall ? $", stopped by the {wall.Definition.Name}." : "."),
                        hit.SourceActorId,
                        Position: target.Position));

                    if (slide.Hit is { } obstacle && slide.Shortfall > 0.05)
                        Collide(target, obstacle, slide.Shortfall, hit, rng, beat, events);
                }
            }
        }

        if (target.IsDefeated)
        {
            target.ClearAllActivity();

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
    /// <summary>
    /// A Crest slammed into an obstacle. A <b>hard</b> obstacle deals 1 damage per
    /// <see cref="RulesConfig.CollisionShortfallPerDamage"/> inches of push cut short (rounded up), rolled and saved
    /// like any damage. Either way, the obstacle takes the same damage; the shove's rubble keyword decides what a
    /// destroyed obstacle leaves.
    /// </summary>
    private void Collide(Combatant target, Obstacle obstacle, double shortfall, IncomingHit hit, ISeededRandom rng, int beat, List<CombatEvent> events)
    {
        int damage = (int)Math.Ceiling(shortfall / Math.Max(0.1, _rules.CollisionShortfallPerDamage) - 1e-9);
        if (damage <= 0)
            return;

        if (obstacle.Definition.Hard)
        {
            events.Add(new CombatEvent(beat, CombatEventType.Slammed, target.Id,
                $"{target.Name} slammed into the {obstacle.Definition.Name} ({damage} collision damage).",
                hit.SourceActorId, damage, Position: target.Position, ObstacleId: obstacle.Id));
            DealDamagePoints(target, damage, target.BonusProtectionDice, $"a collision with the {obstacle.Definition.Name}", hit.SourceActorId, rng, beat, events);
        }

        target.Terrain.Damage(obstacle, damage, hit.LeavesRubble, $"{target.Name}'s collision", beat, hit.SourceActorId, events);
    }

    /// <summary>Rolls protection for each damage point; each failed save damages the first operational part in the Damage Order.</summary>
    private void DealDamagePoints(Combatant target, int points, int bonusProtectionDice, string sourceName, string sourceActorId,
        ISeededRandom rng, int beat, List<CombatEvent> events)
    {
        for (int p = 0; p < points && !target.IsDefeated; p++)
        {
            var rollResult = RollProtection(bonusProtectionDice, rng);
            string diceText = string.Join(",", rollResult.Rolls);

            if (rollResult.Saved)
            {
                events.Add(new CombatEvent(
                    beat,
                    CombatEventType.DamageBlocked,
                    target.Id,
                    $"{target.Name} saved vs {sourceName} (d6: [{diceText}]).",
                    sourceActorId));
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
                $"{target.Name}'s {component.Definition.Name} damaged by {sourceName} (d6: [{diceText}], {component.CurrentDurability}/{component.Definition.MaxDurability}).",
                sourceActorId,
                1,
                ComponentInstanceId: component.InstanceId));

            if (nowDisabled)
            {
                events.Add(new CombatEvent(
                    beat,
                    CombatEventType.ComponentDisabled,
                    target.Id,
                    $"{target.Name}'s {component.Definition.Name} DISABLED! (Wit tax released: {component.Definition.WitTax}).",
                    sourceActorId,
                    ComponentInstanceId: component.InstanceId));
            }
        }
    }

    public static int EffectiveDisplacement(int pushDistance, int resistance) =>
        pushDistance > 0
            ? Math.Max(0, pushDistance - resistance)
            : Math.Min(0, pushDistance + resistance);

    /// <summary>
    /// A batch's strongest interruption is applied to <b>every</b> interruptible action the target has in
    /// windup or impact, each judged against its own resistance. Uninterruptible actions are unaffected.
    /// Interrupted or canceled actions are removed and their component is locked out for the penalty.
    /// </summary>
    private void ApplyBatchInterruption(
        Combatant target,
        int maxInterruptionPower,
        bool anyDisplaced,
        int beat,
        List<CombatEvent> events)
    {
        foreach (var act in target.ActionsInMotion.Where(a => a.IsInterruptiblePhase).ToList())
        {
            var def = act.Definition;
            if (def.IsUninterruptible)
                continue;

            var tier = InterruptionRules.Evaluate(maxInterruptionPower, def.InterruptionResistance, anyDisplaced, _thresholds);
            if (tier == InterruptionTier.None)
                continue;

            if (tier == InterruptionTier.Disruption)
            {
                act.PhaseBeatsRemaining += _thresholds.DisruptionDelayBeats;
                act.PhaseTotalBeats += _thresholds.DisruptionDelayBeats;
                events.Add(new CombatEvent(
                    beat,
                    CombatEventType.ActionInterrupted,
                    target.Id,
                    $"{target.Name}'s {def.Name} DISRUPTED (+{_thresholds.DisruptionDelayBeats} beat delay).",
                    Interruption: tier,
                    ActionId: def.Id));
                continue;
            }

            int penaltyBeats = tier == InterruptionTier.Cancellation
                ? Math.Max(_thresholds.CancellationMinPenaltyBeats, def.RecoveryBeats)
                : _thresholds.InterruptedStunBeats;

            target.AbortAction(act);
            target.LockOutComponent(act.SourceEquipmentInstanceId, penaltyBeats);

            string label = tier == InterruptionTier.Cancellation ? "CANCELED" : "INTERRUPTED";
            events.Add(new CombatEvent(
                beat,
                CombatEventType.ActionInterrupted,
                target.Id,
                $"{target.Name}'s {def.Name} {label}! (component locked {penaltyBeats} beat(s))",
                Interruption: tier,
                    ActionId: def.Id));
        }
    }
}
