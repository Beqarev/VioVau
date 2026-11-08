namespace VioVau.Core.Dungeon;

/// <summary>A position inside a room, normalized to 0..1 on both axes (the view maps it to pixels).</summary>
public readonly record struct RoomPoint(float X, float Y);

/// <summary>How many chests a room type gets and how much each is worth (inclusive ranges).</summary>
public sealed record LootRule
{
    public int MinChests { get; init; }
    public int MaxChests { get; init; }
    public int MinPoints { get; init; } = 1;
    public int MaxPoints { get; init; } = 1;
}

/// <summary>Tunable generation settings. Loaded from <c>game/data/dungeon.json</c>.</summary>
public sealed record DungeonConfig
{
    public int Width { get; init; } = 6;
    public int Height { get; init; } = 6;

    /// <summary>Chance that each wall not on the spanning tree is opened anyway, creating loops.</summary>
    public double ExtraConnectionChance { get; init; } = 0.2;

    /// <summary>Relative weights for filler room types (Lobby and RelicChamber are placed explicitly).</summary>
    public Dictionary<RoomType, int> RoomWeights { get; init; } = new()
    {
        [RoomType.Corridor] = 1,
    };

    /// <summary>Chests per room type. Types without a rule get no chests.</summary>
    public Dictionary<RoomType, LootRule> Loot { get; init; } = new();

    /// <summary>Relics in the Relic Chamber, by point value (one per entry).</summary>
    public List<int> RelicValues { get; init; } = [50, 30];

    /// <summary>Keys placed beyond one per locked Vault.</summary>
    public int ExtraKeys { get; init; } = 1;

    /// <summary>Candidate spots for chests and keys. Kept off the center cross so door lanes stay clear.</summary>
    public List<RoomPoint> LootSpots { get; init; } =
    [
        new(0.15f, 0.2f), new(0.85f, 0.2f), new(0.15f, 0.8f), new(0.85f, 0.8f),
        new(0.3f, 0.2f), new(0.7f, 0.2f), new(0.3f, 0.8f), new(0.7f, 0.8f),
    ];

    public static DungeonConfig Default { get; } = new();

    public static DungeonConfig FromJson(string json)
    {
        var config = CoreJson.Parse<DungeonConfig>(json, "Dungeon config");
        config.Validate();
        return config;
    }

    public void Validate()
    {
        if (Width < 2 || Height < 2) throw new InvalidDataException("Dungeon must be at least 2x2.");
        if (Width * Height > 400) throw new InvalidDataException("Dungeon floor is too large (max 400 rooms).");
        if (ExtraConnectionChance is < 0 or > 1) throw new InvalidDataException("extraConnectionChance must be between 0 and 1.");
        if (RoomWeights.Count == 0 || RoomWeights.Values.Any(w => w < 0) || RoomWeights.Values.All(w => w == 0))
            throw new InvalidDataException("roomWeights needs at least one positive weight and no negative ones.");
        if (RoomWeights.ContainsKey(RoomType.Lobby) || RoomWeights.ContainsKey(RoomType.RelicChamber))
            throw new InvalidDataException("Lobby and RelicChamber are placed by the generator and cannot be weighted.");
        if (ExtraKeys < 0) throw new InvalidDataException("extraKeys cannot be negative.");
        if (RelicValues.Any(v => v <= 0)) throw new InvalidDataException("relicValues must be positive.");
        if (RelicValues.Count > 5) throw new InvalidDataException("At most 5 relics.");
        if (LootSpots.Any(p => p.X is < 0 or > 1 || p.Y is < 0 or > 1))
            throw new InvalidDataException("lootSpots must be normalized (0..1).");

        foreach (var (type, rule) in Loot)
        {
            if (rule.MinChests < 0 || rule.MaxChests < rule.MinChests)
                throw new InvalidDataException($"loot.{type}: chest range is invalid.");
            if (rule.MinPoints < 0 || rule.MaxPoints < rule.MinPoints)
                throw new InvalidDataException($"loot.{type}: points range is invalid.");
            // +1: a key may share the room.
            if (rule.MaxChests + 1 > LootSpots.Count)
                throw new InvalidDataException($"loot.{type}: more chests than lootSpots allow.");
        }
    }
}
