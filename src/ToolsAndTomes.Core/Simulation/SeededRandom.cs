namespace ToolsAndTomes.Core.Simulation;

public interface ISeededRandom
{
    int RollD6();
    int NextInt(int minInclusive, int maxExclusive);
}

public sealed class SeededRandom : ISeededRandom
{
    private readonly Random _random;

    public int Seed { get; }

    public SeededRandom(int seed)
    {
        Seed = seed;
        _random = new Random(seed);
    }

    public int RollD6() => _random.Next(1, 7);

    public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
}
