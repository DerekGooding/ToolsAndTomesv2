using ToolsAndTomes.Core.Definitions;

namespace ToolsAndTomes.Core.Rules;

public sealed record InterruptionThresholds
{
    public static InterruptionThresholds Default { get; } = new();

    public int DisruptionDelta { get; init; } = -1;
    public int InterruptionDelta { get; init; } = 0;
    public int CancellationDelta { get; init; } = 2;

    public int DisruptionDelayBeats { get; init; } = 1;
    public int InterruptedStunBeats { get; init; } = 1;
    public int CancellationMinPenaltyBeats { get; init; } = 2;
}

public static class InterruptionRules
{
    public static InterruptionTier Evaluate(
        int incomingPower,
        int actionResistance,
        bool wasDisplaced,
        InterruptionThresholds? thresholds = null)
    {
        var t = thresholds ?? InterruptionThresholds.Default;
        int effectivePower = incomingPower + (wasDisplaced ? 1 : 0);
        if (effectivePower <= 0)
            return InterruptionTier.None;

        int delta = effectivePower - actionResistance;

        if (delta >= t.CancellationDelta)
            return InterruptionTier.Cancellation;

        if (delta >= t.InterruptionDelta)
            return InterruptionTier.Interruption;

        if (delta >= t.DisruptionDelta)
            return InterruptionTier.Disruption;

        return InterruptionTier.None;
    }
}
