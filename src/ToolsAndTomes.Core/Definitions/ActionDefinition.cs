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
    public int HeavyBonusDamage { get; init; }
    public int HeavyWeightThreshold { get; init; } = 100;

    /// <summary>Positive pushes away from the source; negative pulls toward it.</summary>
    public int PushDistance { get; init; }
    public int BlinkDistance { get; init; }
    public int EvadeBeats { get; init; }

    public int AegisBlocks { get; init; }
    public int AegisDurationBeats { get; init; }

    public int FastIronProduced { get; init; }

    public int InterruptionPower { get; init; }
    public int InterruptionResistance { get; init; }

    public bool RequiresSpellAmplifier { get; init; }
    public bool RequiresSpellbook { get; init; }

    /// <summary>
    /// Reactions are declared during a beat and resolve at the end of that same beat,
    /// before any impacts. They have no windup.
    /// </summary>
    public bool IsReactive { get; init; }

    public int TotalBeats => WindupBeats + ImpactBeats + RecoveryBeats;
}
