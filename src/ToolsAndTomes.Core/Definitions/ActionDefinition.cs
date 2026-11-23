namespace ToolsAndTomes.Core.Definitions;

public sealed record ActionDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ActionCategory Category { get; init; }
    public required TargetingType Targeting { get; init; }
    public required TelegraphKind Telegraph { get; init; }

    public int WindupTicks { get; init; }
    public int ExecutionTicks { get; init; }
    public int RecoveryTicks { get; init; }
    public int CooldownTicks { get; init; }

    public double Range { get; init; }
    public double AreaRadius { get; init; }

    public int WitReserve { get; init; }
    public int MoteCost { get; init; }
    public int AmmoCost { get; init; }
    public double HeatGenerated { get; init; }

    public int BaseDamage { get; init; }
    public int HitCount { get; init; } = 1;
    public int HeavyBonusDamage { get; init; }
    public double HeavyWeightThreshold { get; init; } = 100.0;

    public double PushDistance { get; init; }
    public double BlinkDistance { get; init; }
    public int EvadeDurationTicks { get; init; }

    public int AegisBlocks { get; init; }
    public int AegisDurationTicks { get; init; }

    public int InterruptionPower { get; init; }
    public int InterruptionResistance { get; init; }

    public bool RequiresSpellAmplifier { get; init; }
    public bool RequiresSpellbook { get; init; }
    public bool IsReactive { get; init; }

    public int TotalDurationTicks => WindupTicks + ExecutionTicks + RecoveryTicks;
}
