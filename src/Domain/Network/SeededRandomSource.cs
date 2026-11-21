namespace Domain.Network;

/// <summary>Deterministic pseudo-random source so network impairments are reproducible for a given seed.</summary>
public sealed class SeededRandomSource(int seed) : IRandomSource
{
    private readonly Random _random = new(seed);

    public int Seed { get; } = seed;

    public double NextDouble() => _random.NextDouble();
}
