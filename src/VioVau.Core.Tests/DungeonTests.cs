using VioVau.Core.Dungeon;

namespace VioVau.Core.Tests;

public class DungeonTests
{
    private static readonly DungeonConfig Config = new()
    {
        Width = 6,
        Height = 6,
        ExtraConnectionChance = 0.2,
        RoomWeights = new() { [RoomType.Corridor] = 3, [RoomType.Vault] = 1, [RoomType.GuardPost] = 1 },
    };

    [Fact]
    public void SameSeed_GeneratesIdenticalDungeon()
    {
        Assert.Equal(Describe(DungeonGenerator.Generate(9, Config)), Describe(DungeonGenerator.Generate(9, Config)));
    }

    [Fact]
    public void DifferentSeeds_GenerateDifferentDungeons()
    {
        Assert.NotEqual(Describe(DungeonGenerator.Generate(1, Config)), Describe(DungeonGenerator.Generate(2, Config)));
    }

    [Fact]
    public void WeightOrder_DoesNotAffectResult()
    {
        // Configs arrive via JSON on clients; key order must not change the map.
        var reordered = Config with
        {
            RoomWeights = new() { [RoomType.GuardPost] = 1, [RoomType.Vault] = 1, [RoomType.Corridor] = 3 },
        };
        Assert.Equal(Describe(DungeonGenerator.Generate(5, Config)), Describe(DungeonGenerator.Generate(5, reordered)));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(3UL)]
    [InlineData(123456789UL)]
    public void EveryNonVaultRoom_IsReachableWithoutKeys(ulong seed)
    {
        var map = DungeonGenerator.Generate(seed, Config);
        var floor = map.Floors[0];
        var distances = DungeonGenerator.WalkingDistances(floor, map.GetRoom(map.Entrance));
        Assert.All(map.AllRooms.Where(r => r.Type != RoomType.Vault), r => Assert.True(distances.ContainsKey(r.Id), $"{r} unreachable"));
        Assert.All(map.AllRooms.Where(r => r.Type == RoomType.Vault), r => Assert.False(distances.ContainsKey(r.Id), $"{r} reachable without a key"));
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(77UL)]
    public void Exits_AreSymmetricAndNeverLeadOutside(ulong seed)
    {
        var map = DungeonGenerator.Generate(seed, Config);
        var floor = map.Floors[0];
        foreach (var room in floor.Rooms)
        {
            foreach (var d in DirectionExtensions.All)
            {
                var neighbor = floor.Neighbor(room, d);
                if (neighbor is null)
                    Assert.Equal(ExitState.None, room.GetExit(d));
                else
                    Assert.Equal(room.GetExit(d), neighbor.GetExit(d.Opposite()));
            }
        }
    }

    [Fact]
    public void Entrance_IsLobbyAtBottomCenter_AndOneRelicChamberExists()
    {
        var map = DungeonGenerator.Generate(3, Config);
        Assert.Equal(new RoomId(0, 3, 5), map.Entrance);
        Assert.Equal(RoomType.Lobby, map.GetRoom(map.Entrance).Type);
        Assert.Single(map.AllRooms, r => r.Type == RoomType.Lobby);
        Assert.Single(map.AllRooms, r => r.Type == RoomType.RelicChamber);
    }

    [Fact]
    public void RelicChamber_IsTheFurthestRoom()
    {
        // Measured on a vault-free map: the relic is placed before vault doors are locked.
        var map = DungeonGenerator.Generate(11, Config with { RoomWeights = new() { [RoomType.Corridor] = 1 } });
        var floor = map.Floors[0];
        var distances = DungeonGenerator.WalkingDistances(floor, map.GetRoom(map.Entrance));
        var relic = map.AllRooms.Single(r => r.Type == RoomType.RelicChamber);
        Assert.Equal(distances.Values.Max(), distances[relic.Id]);
    }

    [Fact]
    public void ZeroLoopChance_ProducesATree()
    {
        var map = DungeonGenerator.Generate(4, Config with { ExtraConnectionChance = 0 });
        int connections = map.AllRooms.Sum(r => DirectionExtensions.All.Count(d => r.GetExit(d) != ExitState.None)) / 2;
        Assert.Equal(Config.Width * Config.Height - 1, connections);
    }

    [Fact]
    public void FillerRooms_UseOnlyWeightedTypes()
    {
        var map = DungeonGenerator.Generate(8, Config);
        var allowed = Config.RoomWeights.Keys.Append(RoomType.Lobby).Append(RoomType.RelicChamber).ToHashSet();
        Assert.All(map.AllRooms, r => Assert.Contains(r.Type, allowed));
    }

    [Fact]
    public void Config_LoadsFromGameData()
    {
        var path = Path.Combine(RepoRoot(), "game", "data", "dungeon.json");
        var config = DungeonConfig.FromJson(File.ReadAllText(path));
        Assert.True(config.Width >= 2);
        Assert.NotEmpty(config.RoomWeights);
    }

    [Theory]
    [InlineData("""{ "width": 1, "height": 6, "roomWeights": { "Corridor": 1 } }""")]
    [InlineData("""{ "extraConnectionChance": 2, "roomWeights": { "Corridor": 1 } }""")]
    [InlineData("""{ "roomWeights": { "Corridor": 0 } }""")]
    [InlineData("""{ "roomWeights": { "Lobby": 1 } }""")]
    public void Config_RejectsInvalidData(string json)
    {
        Assert.Throws<InvalidDataException>(() => DungeonConfig.FromJson(json));
    }

    private static string Describe(DungeonMap map) =>
        string.Join("|", map.AllRooms.Select(r =>
            $"{r.Id}:{r.Type}:{string.Join(",", DirectionExtensions.All.Select(d => r.GetExit(d)))}"));

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VioVau.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find the repository root (VioVau.sln).");
    }
}
