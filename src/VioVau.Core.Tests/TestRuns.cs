using VioVau.Core.Dungeon;
using VioVau.Core.Threat;

namespace VioVau.Core.Tests;

/// <summary>Helpers for building runs and walking players around in tests.</summary>
internal static class TestRuns
{
    public static readonly PlayerId P1 = new(1), P2 = new(2);

    public static readonly DungeonConfig LootyDungeon = new()
    {
        Width = 6,
        Height = 6,
        ExtraConnectionChance = 0.25,
        RoomWeights = new() { [RoomType.Corridor] = 3, [RoomType.Vault] = 2, [RoomType.GuardPost] = 2, [RoomType.EchoChamber] = 1, [RoomType.HushRoom] = 1 },
        Loot = new()
        {
            [RoomType.Vault] = new LootRule { MinChests = 2, MaxChests = 3, MinPoints = 20, MaxPoints = 40 },
            [RoomType.GuardPost] = new LootRule { MinChests = 1, MaxChests = 1, MinPoints = 10, MaxPoints = 20 },
            [RoomType.Corridor] = new LootRule { MinChests = 0, MaxChests = 1, MinPoints = 3, MaxPoints = 8 },
            [RoomType.EchoChamber] = new LootRule { MinChests = 1, MaxChests = 1, MinPoints = 10, MaxPoints = 10 },
            [RoomType.HushRoom] = new LootRule { MinChests = 1, MaxChests = 1, MinPoints = 5, MaxPoints = 5 },
        },
        ExtraKeys = 1,
    };

    /// <summary>No passive drift, so tests see exactly the threat they cause.</summary>
    public static readonly ThreatConfig QuietThreat = ThreatConfig.Default with { PassivePerSecond = 0 };

    public static RunState Started(ulong seed = 42, DungeonConfig? dungeon = null, ThreatConfig? threat = null, int players = 2)
    {
        var run = new RunState(seed, new GameConfig(dungeon ?? LootyDungeon, threat ?? QuietThreat));
        for (int i = 1; i <= players; i++) run.Apply(new JoinPlayer(new PlayerId(i), $"P{i}"));
        Assert.True(run.Apply(new StartRun()).Accepted);
        return run;
    }

    /// <summary>Finds a seed whose dungeon satisfies a condition (keeps tests independent of one lucky seed).</summary>
    public static RunState StartedWhere(Func<DungeonMap, bool> condition, DungeonConfig? dungeon = null)
    {
        for (ulong seed = 1; seed < 500; seed++)
        {
            var map = DungeonGenerator.Generate(seed, dungeon ?? LootyDungeon);
            if (condition(map)) return Started(seed, dungeon);
        }
        throw new InvalidOperationException("No seed satisfied the condition.");
    }

    /// <summary>Walks a player room by room (through passable exits only) to the target. Collects all events.</summary>
    public static List<IGameEvent> WalkTo(RunState run, PlayerId player, RoomId target)
    {
        var map = run.Dungeon!;
        var start = run.GetPlayer(player)!.Room!.Value;
        var previous = new Dictionary<RoomId, RoomId> { [start] = start };
        var queue = new Queue<RoomId>([start]);

        while (queue.Count > 0 && !previous.ContainsKey(target))
        {
            var current = queue.Dequeue();
            var room = map.GetRoom(current);
            foreach (var d in DirectionExtensions.All.Where(room.IsPassable))
            {
                var next = map.Neighbor(current, d)!.Id;
                if (previous.TryAdd(next, current)) queue.Enqueue(next);
            }
        }
        if (!previous.ContainsKey(target)) throw new InvalidOperationException($"{target} is not reachable from {start}.");

        var path = new List<RoomId>();
        for (var at = target; at != start; at = previous[at]) path.Add(at);
        path.Reverse();

        var events = new List<IGameEvent>();
        foreach (var step in path)
        {
            var result = run.Apply(new MovePlayerToRoom(player, step));
            Assert.True(result.Accepted, result.RejectionReason);
            events.AddRange(result.Events);
        }
        return events;
    }
}
