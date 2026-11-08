using VioVau.Core.Dungeon;
using static VioVau.Core.Tests.TestRuns;

namespace VioVau.Core.Tests;

public class LootTests
{
    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(99UL)]
    public void VaultDoorways_AreAllLocked_AndSymmetric(ulong seed)
    {
        var map = DungeonGenerator.Generate(seed, LootyDungeon);
        foreach (var vault in map.AllRooms.Where(r => r.Type == RoomType.Vault))
        {
            foreach (var d in DirectionExtensions.All)
            {
                var exit = vault.GetExit(d);
                Assert.True(exit is ExitState.None or ExitState.Locked, $"{vault} has a {exit} exit");
                if (exit == ExitState.Locked) Assert.Equal(ExitState.Locked, map.Neighbor(vault.Id, d)!.GetExit(d.Opposite()));
            }
            Assert.Contains(DirectionExtensions.All, d => vault.GetExit(d) == ExitState.Locked);
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(99UL)]
    public void EnoughKeys_LieOutsideVaults(ulong seed)
    {
        var map = DungeonGenerator.Generate(seed, LootyDungeon);
        var reachable = DungeonGenerator.WalkingDistances(map.Floors[0], map.GetRoom(map.Entrance));
        var keys = map.Loot.Where(l => l.Kind == LootKind.Key).ToList();
        int vaults = map.AllRooms.Count(r => r.Type == RoomType.Vault);

        Assert.True(keys.Count >= vaults + LootyDungeon.ExtraKeys || keys.Count >= reachable.Count - 1);
        Assert.All(keys, k => Assert.True(reachable.ContainsKey(k.Room), $"{k} needs a key to reach"));
        Assert.DoesNotContain(keys, k => k.Room == map.Entrance);
    }

    [Theory]
    [InlineData(3UL)]
    [InlineData(31UL)]
    public void Chests_FollowTheRules(ulong seed)
    {
        var map = DungeonGenerator.Generate(seed, LootyDungeon);
        foreach (var room in map.AllRooms)
        {
            var chests = map.LootIn(room.Id).Where(l => l.Kind == LootKind.Chest).ToList();
            if (!LootyDungeon.Loot.TryGetValue(room.Type, out var rule))
            {
                Assert.Empty(chests);
                continue;
            }
            Assert.InRange(chests.Count, rule.MinChests, rule.MaxChests);
            Assert.All(chests, c => Assert.InRange(c.Points, rule.MinPoints, rule.MaxPoints));
        }
    }

    [Fact]
    public void LootInARoom_NeverSharesASpot()
    {
        var map = DungeonGenerator.Generate(5, LootyDungeon);
        foreach (var group in map.Loot.GroupBy(l => l.Room))
            Assert.Equal(group.Count(), group.Select(l => l.Position).Distinct().Count());
    }

    [Fact]
    public void LootIds_AreUnique_AndGenerationIsDeterministic()
    {
        var a = DungeonGenerator.Generate(17, LootyDungeon);
        var b = DungeonGenerator.Generate(17, LootyDungeon);

        Assert.Equal(a.Loot.Count, a.Loot.Select(l => l.Id).Distinct().Count());
        Assert.Equal(
            a.Loot.OrderBy(l => l.Id.Value).Select(l => (l.Id, l.Kind, l.Room, l.Position, l.Points)),
            b.Loot.OrderBy(l => l.Id.Value).Select(l => (l.Id, l.Kind, l.Room, l.Position, l.Points)));
    }

    [Fact]
    public void ChangingLootRules_DoesNotChangeTheLayout()
    {
        var richer = LootyDungeon with { Loot = new() { [RoomType.Corridor] = new LootRule { MinChests = 3, MaxChests = 3, MinPoints = 1, MaxPoints = 99 } } };
        var a = DungeonGenerator.Generate(23, LootyDungeon);
        var b = DungeonGenerator.Generate(23, richer);
        Assert.Equal(
            a.AllRooms.Select(r => (r.Id, r.Type, DirectionExtensions.All.Select(r.GetExit).ToArray())).Select(x => $"{x.Id}{x.Type}{string.Join(",", x.Item3)}"),
            b.AllRooms.Select(r => (r.Id, r.Type, DirectionExtensions.All.Select(r.GetExit).ToArray())).Select(x => $"{x.Id}{x.Type}{string.Join(",", x.Item3)}"));
    }

    [Fact]
    public void LootConfig_RejectsTooManyChestsForSpots()
    {
        var config = LootyDungeon with { Loot = new() { [RoomType.Corridor] = new LootRule { MinChests = 1, MaxChests = 8 } } };
        Assert.Throws<InvalidDataException>(config.Validate);
    }
}
