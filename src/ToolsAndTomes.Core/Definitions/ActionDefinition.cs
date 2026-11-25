namespace ToolsAndTomes.Core.Definitions;

/// <summary>
/// Data definition of an action. All timings are in whole beats (1 beat = 1 tabletop second);
/// all distances are in whole tabletop units.
/// </summary>
public sealed record ActionDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ActionCategory Category { get; init; }
    public required TargetingType Targeting { get; init; }
    public required TelegraphKind Telegraph { get; init; }

    /// <summary>Telegraph beats before the impact window. Reactions must have 0.</summary>
    public int WindupBeats { get; init; }

    /// <summary>Impact window beats. Damage and effects resolve at the end of the final impact beat. Minimum 1.</summary>
    public int ImpactBeats { get; init; } = 1;

    public int RecoveryBeats { get; init; }

    /// <summary>Beats the action stays unavailable after it finishes (counted after recovery).</summary>
    public int CooldownBeats { get; init; }

    public int Range { get; init; }
    public int AreaRadius { get; init; }

    public int WitReserve { get; init; }
    public int MoteCost { get; init; }
    public int AmmoCost { get; init; }
    public int HeatGenerated { get; init; }

    public int BaseDamage { get; init; }
    public int HitCount { get; init; } = 1;
    /// <summary>Additional damage per hit against targets with particular traits (Heavy, Armored, ...).</summary>
    public IReadOnlyList<TraitBonus> TraitBonuses { get; init; } = [];

    /// <summary>Positive pushes away from the source; negative pulls toward it.</summary>
    public int PushDistance { get; init; }
    public int BlinkDistance { get; init; }
    public int EvadeBeats { get; init; }

    public int AegisBlocks { get; init; }
    public int AegisDurationBeats { get; init; }

    public int FastIronProduced { get; init; }

    /// <summary>A Shaping spell: turns fast-iron into a component, consumable, or ammo per the command's <see cref="ShapingOrder"/>.</summary>
    public bool IsShaping { get; init; }

    /// <summary>Refills the ammo of one of the Crest's own weapons (the command's own-component target).</summary>
    public bool RefillsAmmo { get; init; }

    /// <summary>If set, using this action consumes one of this consumable from cargo.</summary>
    public string? ConsumableItemId { get; init; }

    public int InterruptionPower { get; init; }
    public int InterruptionResistance { get; init; }

    /// <summary>Self-propelled relocation (e.g. a jet dash) of up to this many units toward the target point at impact.</summary>
    public int DashDistance { get; init; }

    /// <summary>Uninterruptible actions ignore disruption, interruption, and cancellation.</summary>
    public bool IsUninterruptible { get; init; }


    /// <summary>
    /// Reactions are declared during a beat and resolve at the end of that same beat,
    /// before any impacts. They have no windup.
    /// </summary>
    public bool IsReactive { get; init; }

    // ---- Tome spell effects ----

    /// <summary>Extra damage against some targets (see <see cref="DamageType"/>).</summary>
    public DamageType DamageType { get; init; }

    /// <summary>After hitting its target, the action jumps to up to this many more enemies, each within <see cref="ChainRange"/> of the last.</summary>
    public int ChainJumps { get; init; }
    public int ChainRange { get; init; }

    /// <summary>Extra protection dice granted to allies in the area (the caster included) for <see cref="BuffDurationBeats"/>.</summary>
    public int ProtectionBonusDice { get; init; }
    public int BuffDurationBeats { get; init; }

    /// <summary>Slow: the target's movement speed drops by this much (to a minimum of 1) for <see cref="SlowBeats"/>.</summary>
    public int SlowAmount { get; init; }
    public int SlowBeats { get; init; }

    /// <summary>The target's next windup takes this many extra beats.</summary>
    public int WindupDelay { get; init; }

    /// <summary>Pin: the target can't move, dash, blink, or be displaced for this many beats.</summary>
    public int PinBeats { get; init; }

    /// <summary>Durability restored to the target's most damaged part (a disabled part comes back).</summary>
    public int MendPoints { get; init; }

    /// <summary>Fast-iron the caster spends when the action takes effect (Quick Mend, Conjured Blade).</summary>
    public int FastIronSpent { get; init; }

    /// <summary>A component conjured into a free hand until the fight ends.</summary>
    public string? ConjuresComponentId { get; init; }

    /// <summary>A harness swap: exchanges the stowed handheld with the held item named by the command.</summary>
    public bool IsSwap { get; init; }

    // ---- Obstacles ----

    /// <summary>Arcs or falls from above: needs no line of fire, and ignores cover.</summary>
    public bool Indirect { get; init; }

    /// <summary>An obstacle this action destroys becomes rubble (otherwise the wreck vanishes).</summary>
    public bool LeavesRubble { get; init; }

    /// <summary>Raises an obstacle of this type (a wall <see cref="WallLength"/> long, centered on the target point, square to the caster).</summary>
    public string? RaisesObstacleId { get; init; }
    public double WallLength { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public int TotalBeats => WindupBeats + ImpactBeats + RecoveryBeats;
}
