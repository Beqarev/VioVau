namespace VioVau.Core.Random;

/// <summary>
/// Deterministic xoshiro256** generator. Same seed → same sequence on every platform and .NET version
/// (unlike System.Random), which keeps runs reproducible and host/client generation in sync.
/// </summary>
public sealed class GameRng
{
    private ulong _s0, _s1, _s2, _s3;

    public GameRng(ulong seed)
    {
        // Expand the seed with SplitMix64 so that similar seeds produce unrelated states.
        _s0 = SplitMix64(ref seed);
        _s1 = SplitMix64(ref seed);
        _s2 = SplitMix64(ref seed);
        _s3 = SplitMix64(ref seed);
    }

    public ulong NextULong()
    {
        ulong result = RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);
        return result;
    }

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");

        ulong range = (ulong)((long)maxExclusive - minInclusive);
        // Rejection sampling to avoid modulo bias.
        ulong limit = ulong.MaxValue - (ulong.MaxValue % range);
        ulong value;
        do value = NextULong(); while (value >= limit);
        return (int)((long)minInclusive + (long)(value % range));
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public bool Chance(double probability) => NextDouble() < probability;

    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0) throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
        return items[NextInt(0, items.Count)];
    }

    /// <summary>In-place Fisher–Yates shuffle.</summary>
    public void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = NextInt(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

    internal static ulong SplitMix64(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}
