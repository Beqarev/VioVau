namespace VioVau.Core.Random;

/// <summary>
/// One independent RNG per system, all derived from the run seed. Drawing extra numbers in one
/// system (e.g. a new loot roll) never changes the results of another (e.g. map layout).
/// </summary>
public sealed class RngStreams
{
    public ulong Seed { get; }

    public GameRng Map { get; }
    public GameRng Loot { get; }
    public GameRng Cards { get; }
    public GameRng Threat { get; }
    public GameRng Ai { get; }

    public RngStreams(ulong seed)
    {
        Seed = seed;
        Map = Create(seed, "map");
        Loot = Create(seed, "loot");
        Cards = Create(seed, "cards");
        Threat = Create(seed, "threat");
        Ai = Create(seed, "ai");
    }

    /// <summary>Derives a named stream. Uses a stable hash (string.GetHashCode is randomized per process).</summary>
    public static GameRng Create(ulong seed, string streamName)
    {
        ulong state = seed ^ Fnv1a64(streamName);
        return new GameRng(GameRng.SplitMix64(ref state));
    }

    private static ulong Fnv1a64(string text)
    {
        ulong hash = 0xCBF29CE484222325UL;
        foreach (char c in text)
        {
            hash ^= c;
            hash *= 0x100000001B3UL;
        }
        return hash;
    }
}
