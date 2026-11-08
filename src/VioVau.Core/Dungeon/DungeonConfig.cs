using System.Text.Json;
using System.Text.Json.Serialization;

namespace VioVau.Core.Dungeon;

/// <summary>Tunable generation settings. Loaded from <c>game/data/dungeon.json</c>; sent to clients inside <see cref="RunStarted"/>.</summary>
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

    public static DungeonConfig Default { get; } = new();

    public static DungeonConfig FromJson(string json)
    {
        var config = JsonSerializer.Deserialize<DungeonConfig>(json, JsonOptions)
            ?? throw new InvalidDataException("Dungeon config is empty.");
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
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
