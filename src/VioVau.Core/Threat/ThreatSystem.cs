using VioVau.Core.Dungeon;

namespace VioVau.Core.Threat;

/// <summary>
/// The shared Threat meter. Everything raises it; nothing lowers it yet (tiers are ratchets).
/// Player-made noise is also attributed to that player and decays over time, giving each player a
/// "noise share" — later used to pick who the dungeon strikes.
/// </summary>
public sealed class ThreatSystem
{
    private readonly ThreatConfig _config;
    private readonly SortedDictionary<int, double> _noise = new(); // by PlayerId.Value, for stable ordering
    private double _sinceBroadcast;

    public double Level { get; private set; }
    public int Tier { get; private set; }

    public ThreatSystem(ThreatConfig config) => _config = config;

    public void AddPlayer(PlayerId player) => _noise.TryAdd(player.Value, 0);

    /// <summary>Noise made by a player in a room. Applies the room multiplier.</summary>
    /// <returns>The threat actually added.</returns>
    public double AddPlayerNoise(PlayerId player, double baseAmount, RoomType room, List<IGameEvent> events)
    {
        double amount = baseAmount * _config.RoomMultiplier(room);
        if (amount <= 0) return 0;

        _noise[player.Value] = _noise.GetValueOrDefault(player.Value) + amount;
        Raise(amount, events);
        return amount;
    }

    /// <summary>Passive drift, attribution decay, and periodic snapshots.</summary>
    public void Tick(TimeSpan dt, List<IGameEvent> events)
    {
        double seconds = dt.TotalSeconds;
        Raise(_config.PassivePerSecond * seconds, events);

        double decay = Math.Pow(0.5, seconds / _config.AttributionHalfLifeSeconds);
        foreach (var key in _noise.Keys.ToList()) _noise[key] *= decay;

        _sinceBroadcast += seconds;
        if (_sinceBroadcast >= _config.BroadcastIntervalSeconds) events.Add(Snapshot());
    }

    public ThreatChanged Snapshot()
    {
        _sinceBroadcast = 0;
        return new ThreatChanged(Level, Tier, Shares());
    }

    /// <summary>Each player's fraction of recent attributed noise (all zero if nobody has made any).</summary>
    public IReadOnlyList<NoiseShare> Shares()
    {
        double total = _noise.Values.Sum();
        return _noise
            .Select(kv => new NoiseShare(new PlayerId(kv.Key), total > 1e-9 ? kv.Value / total : 0))
            .ToList();
    }

    private void Raise(double amount, List<IGameEvent> events)
    {
        if (amount <= 0) return;
        Level = Math.Min(_config.Max, Level + amount);

        int tier = _config.TierFor(Level);
        if (tier != Tier)
        {
            Tier = tier;
            events.Add(new ThreatTierChanged(tier));
        }
    }
}
