using VioVau.Core.Random;

namespace VioVau.Core.Tests;

public class RngTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new GameRng(42);
        var b = new GameRng(42);
        for (int i = 0; i < 1000; i++)
            Assert.Equal(a.NextULong(), b.NextULong());
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentSequences()
    {
        var a = new GameRng(1);
        var b = new GameRng(2);
        Assert.NotEqual(
            Enumerable.Range(0, 8).Select(_ => a.NextULong()),
            Enumerable.Range(0, 8).Select(_ => b.NextULong()));
    }

    [Fact]
    public void KnownSeed_ProducesPinnedValues()
    {
        // Guards against accidental algorithm changes, which would break saved seeds and replays.
        var rng = new GameRng(12345);
        var first = Enumerable.Range(0, 3).Select(_ => rng.NextULong()).ToArray();
        var again = new GameRng(12345);
        Assert.Equal(first, Enumerable.Range(0, 3).Select(_ => again.NextULong()).ToArray());
        Assert.Equal(PinnedSeed12345, first);
    }

    // Computed by an independent reference implementation of xoshiro256** (SplitMix64 seeding). Must never change.
    private static readonly ulong[] PinnedSeed12345 = [13720838825685603483UL, 2398916695208396998UL, 17770384849984869256UL];

    [Fact]
    public void NextInt_StaysInRange()
    {
        var rng = new GameRng(7);
        for (int i = 0; i < 10_000; i++)
        {
            int v = rng.NextInt(-3, 5);
            Assert.InRange(v, -3, 4);
        }
    }

    [Fact]
    public void NextInt_CoversWholeRange()
    {
        var rng = new GameRng(7);
        var seen = new HashSet<int>();
        for (int i = 0; i < 1000; i++) seen.Add(rng.NextInt(0, 6));
        Assert.Equal(6, seen.Count);
    }

    [Fact]
    public void NextDouble_IsInUnitInterval()
    {
        var rng = new GameRng(99);
        for (int i = 0; i < 10_000; i++)
            Assert.InRange(rng.NextDouble(), 0.0, 0.9999999999999999);
    }

    [Fact]
    public void Shuffle_IsDeterministicPermutation()
    {
        var a = Enumerable.Range(0, 20).ToList();
        var b = Enumerable.Range(0, 20).ToList();
        new GameRng(5).Shuffle(a);
        new GameRng(5).Shuffle(b);

        Assert.Equal(a, b);
        Assert.Equal(Enumerable.Range(0, 20), a.Order());
    }

    [Fact]
    public void Streams_AreIndependent()
    {
        // Consuming from one stream must not affect another.
        var untouched = new RngStreams(100);
        var used = new RngStreams(100);
        for (int i = 0; i < 50; i++) used.Loot.NextULong();

        Assert.Equal(untouched.Map.NextULong(), used.Map.NextULong());
    }

    [Fact]
    public void Streams_DifferFromEachOther()
    {
        var streams = new RngStreams(100);
        var firsts = new[] { streams.Map, streams.Loot, streams.Cards, streams.Threat, streams.Ai }
            .Select(r => r.NextULong())
            .ToHashSet();
        Assert.Equal(5, firsts.Count);
    }
}
