namespace ToolsAndTomes.Core.Simulation;

public interface ISeededRandom
{
    int RollD6();
    int NextInt(int minInclusive, int maxExclusive);
}

/// <summary>
/// PCG32 (O'Neill, pcg-random.org): a small, fully specified generator. Unlike <see cref="System.Random"/>,
/// its sequence is fixed by this code, so seeds reproduce identically across .NET versions and platforms
/// (needed for replays, tests, and matching a tabletop log).
/// </summary>
public sealed class Pcg32
{
    private const ulong Multiplier = 6364136223846793005UL;

    private ulong _state;
    private readonly ulong _increment;

    public Pcg32(ulong initState, ulong initSequence)
    {
        _state = 0;
        _increment = (initSequence << 1) | 1UL;
        NextUInt();
        _state += initState;
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong old = _state;
        _state = unchecked(old * Multiplier + _increment);
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    /// <summary>Unbiased integer in [0, bound) using rejection sampling.</summary>
    public uint NextBounded(uint bound)
    {
        if (bound == 0)
            throw new ArgumentOutOfRangeException(nameof(bound));

        uint threshold = unchecked(0u - bound) % bound;
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold)
                return r % bound;
        }
    }
}

public sealed class SeededRandom : ISeededRandom
{
    /// <summary>Fixed stream selector so a seed alone determines the sequence.</summary>
    private const ulong Stream = 0xDA3E39CB94B95BDBUL;

    private readonly Pcg32 _pcg;

    public int Seed { get; }

    public SeededRandom(int seed)
    {
        Seed = seed;
        _pcg = new Pcg32(unchecked((ulong)seed), Stream);
    }

    public int RollD6() => NextInt(1, 7);

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            return minInclusive;

        return minInclusive + (int)_pcg.NextBounded((uint)(maxExclusive - minInclusive));
    }
}
