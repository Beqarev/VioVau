using VioVau.Core.Dungeon;

namespace VioVau.Core.Threat;

/// <summary>Threat tuning. Loaded from <c>game/data/threat.json</c>.</summary>
public sealed record ThreatConfig
{
    public double Max { get; init; } = 100;

    /// <summary>Level at which each tier starts. Must start at 0 and ascend; the last tier is Collapse.</summary>
    public List<double> TierThresholds { get; init; } = [0, 15, 35, 55, 75, 100];
    public List<string> TierNames { get; init; } = ["Calm", "Stirring", "Restless", "Hunting", "Furious", "Collapse"];

    /// <summary>Unattributed drift: the dungeon's clock.</summary>
    public double PassivePerSecond { get; init; } = 0.1;

    public double SprintPerSecond { get; init; } = 0.35;
    public double OpenChest { get; init; } = 3;
    public double PickUpKey { get; init; } = 0;
    public double UnlockExit { get; init; } = 4;

    /// <summary>How fast old noise stops counting toward a player's share.</summary>
    public double AttributionHalfLifeSeconds { get; init; } = 45;

    /// <summary>How often the host sends a full threat snapshot while the run is going.</summary>
    public double BroadcastIntervalSeconds { get; init; } = 0.5;

    /// <summary>Multiplier for noise a player makes while in a room of this type (Echo ×2, Hush ×0).</summary>
    public Dictionary<RoomType, double> RoomMultipliers { get; init; } = new()
    {
        [RoomType.EchoChamber] = 2,
        [RoomType.HushRoom] = 0,
    };

    public static ThreatConfig Default { get; } = new();

    public static ThreatConfig FromJson(string json)
    {
        var config = CoreJson.Parse<ThreatConfig>(json, "Threat config");
        config.Validate();
        return config;
    }

    public int TierFor(double level)
    {
        int tier = 0;
        for (int i = 1; i < TierThresholds.Count; i++)
            if (level >= TierThresholds[i]) tier = i;
        return tier;
    }

    public string TierName(int tier) => tier >= 0 && tier < TierNames.Count ? TierNames[tier] : $"Tier {tier}";

    public double RoomMultiplier(RoomType type) => RoomMultipliers.GetValueOrDefault(type, 1.0);

    public void Validate()
    {
        if (Max <= 0) throw new InvalidDataException("threat.max must be positive.");
        if (TierThresholds.Count < 2 || TierThresholds[0] != 0)
            throw new InvalidDataException("tierThresholds must start at 0 and have at least two tiers.");
        for (int i = 1; i < TierThresholds.Count; i++)
            if (TierThresholds[i] <= TierThresholds[i - 1])
                throw new InvalidDataException("tierThresholds must be strictly ascending.");
        if (TierThresholds[^1] > Max) throw new InvalidDataException("The last tier threshold cannot exceed max.");
        if (TierNames.Count != TierThresholds.Count) throw new InvalidDataException("tierNames must match tierThresholds.");
        if (new[] { PassivePerSecond, SprintPerSecond, OpenChest, PickUpKey, UnlockExit }.Any(v => v < 0))
            throw new InvalidDataException("Threat amounts cannot be negative.");
        if (AttributionHalfLifeSeconds <= 0 || BroadcastIntervalSeconds <= 0)
            throw new InvalidDataException("attributionHalfLifeSeconds and broadcastIntervalSeconds must be positive.");
        if (RoomMultipliers.Values.Any(m => m < 0)) throw new InvalidDataException("roomMultipliers cannot be negative.");
    }
}
