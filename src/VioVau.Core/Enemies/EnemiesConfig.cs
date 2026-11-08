using VioVau.Core.Dungeon;

namespace VioVau.Core.Enemies;

public enum EnemyKind
{
    /// <summary>Hollow Sentry: patrols, chases what it sees or hears, chops.</summary>
    Sentry,
    /// <summary>Unkillable hunter that always tracks the loudest player. Appears once at <see cref="EnemiesConfig.WardenTier"/>.</summary>
    Warden,
}

/// <summary>How many Sentries a room type starts with (inclusive range).</summary>
public sealed record SpawnRule
{
    public int Min { get; init; }
    public int Max { get; init; }
}

/// <summary>Player health. Rules live in Core.</summary>
public sealed record HealthConfig
{
    public int Hearts { get; init; } = 3;

    /// <summary>Invulnerability after taking a hit.</summary>
    public double InvulnerableSeconds { get; init; } = 1.2;

    /// <summary>Invulnerability granted by a Dash.</summary>
    public double DashInvulnerableSeconds { get; init; } = 0.35;

    /// <summary>How long a downed player can be revived before they die.</summary>
    public double BleedOutSeconds { get; init; } = 20;

    public int ReviveHearts { get; init; } = 1;
}

/// <summary>AI tuning. Read by the physics layer (which runs the AI); Core only carries it.</summary>
public sealed record AiTuning
{
    public float PatrolSpeed { get; init; } = 35;
    public float ChaseSpeed { get; init; } = 72;

    /// <summary>Sees a walking player within this distance (px).</summary>
    public float SightRadius { get; init; } = 85;

    /// <summary>Hears a sprinting player within this distance.</summary>
    public float HearingRadius { get; init; } = 140;

    /// <summary>A Hushed player is only noticed this close.</summary>
    public float HushedRadius { get; init; } = 30;

    public float AttackRange { get; init; } = 14;
    public float HitRange { get; init; } = 20;
    public float WindupSeconds { get; init; } = 0.45f;
    public float CooldownSeconds { get; init; } = 1.1f;

    /// <summary>Gives up the chase after this long without noticing the target.</summary>
    public float LoseAfterSeconds { get; init; } = 3f;
}

/// <summary>Enemy and health rules. Loaded from <c>game/data/enemies.json</c>.</summary>
public sealed record EnemiesConfig
{
    public int MaxEnemies { get; init; } = 14;

    /// <summary>Seconds between Strikes, per Threat tier (0 = no strikes at that tier).</summary>
    public List<double> StrikeIntervals { get; init; } = [0, 40, 28, 20, 15, 12];

    /// <summary>The Warden awakens when Threat reaches this tier (-1 = never).</summary>
    public int WardenTier { get; init; } = 4;

    public Dictionary<RoomType, SpawnRule> Spawns { get; init; } = new();
    public HealthConfig Health { get; init; } = new();
    public AiTuning Sentry { get; init; } = new();
    public AiTuning Warden { get; init; } = new() { PatrolSpeed = 58, ChaseSpeed = 58, AttackRange = 16, HitRange = 22, WindupSeconds = 0.6f, CooldownSeconds = 1.6f };

    /// <summary>No enemies — runs without danger (used by default and in non-enemy tests).</summary>
    public static EnemiesConfig Default { get; } = new() { StrikeIntervals = [0], WardenTier = -1 };

    public static EnemiesConfig FromJson(string json)
    {
        var config = CoreJson.Parse<EnemiesConfig>(json, "Enemies config");
        config.Validate();
        return config;
    }

    public double StrikeInterval(int tier) =>
        StrikeIntervals.Count == 0 ? 0 : StrikeIntervals[Math.Clamp(tier, 0, StrikeIntervals.Count - 1)];

    public AiTuning Ai(EnemyKind kind) => kind == EnemyKind.Warden ? Warden : Sentry;

    public void Validate()
    {
        if (MaxEnemies < 0) throw new InvalidDataException("maxEnemies cannot be negative.");
        if (StrikeIntervals.Any(i => i < 0)) throw new InvalidDataException("strikeIntervals cannot be negative.");
        if (Spawns.Values.Any(s => s.Min < 0 || s.Max < s.Min)) throw new InvalidDataException("spawns: invalid range.");
        if (Spawns.ContainsKey(RoomType.Lobby)) throw new InvalidDataException("The Lobby must stay safe (no spawns).");
        if (Health.Hearts < 1 || Health.ReviveHearts < 1 || Health.ReviveHearts > Health.Hearts)
            throw new InvalidDataException("health: hearts must be ≥ 1 and reviveHearts within 1..hearts.");
        if (Health.BleedOutSeconds <= 0 || Health.InvulnerableSeconds < 0 || Health.DashInvulnerableSeconds < 0)
            throw new InvalidDataException("health: invalid durations.");
    }
}
