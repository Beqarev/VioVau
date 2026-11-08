using VioVau.Core.Random;

namespace VioVau.Core.Dungeon;

/// <summary>
/// Builds a dungeon from a seed. Pure and deterministic: host and clients generate identical maps
/// from the same seed and config, so the layout never has to be sent over the network.
/// </summary>
public static class DungeonGenerator
{
    public static DungeonMap Generate(ulong seed, DungeonConfig config)
    {
        config.Validate();
        var rng = RngStreams.Create(seed, "dungeon");

        // One floor for now; stairs and deeper floors arrive with later milestones.
        var floor = new Floor(0, config.Width, config.Height, (x, y) => new Room(new RoomId(0, x, y), RoomType.Corridor));
        var entrance = floor[config.Width / 2, config.Height - 1];

        CarveSpanningTree(floor, entrance, rng);
        AddLoops(floor, config.ExtraConnectionChance, rng);
        AssignRoomTypes(floor, entrance, config, rng);

        return new DungeonMap([floor], entrance.Id);
    }

    /// <summary>
    /// "Growing tree" maze: mixes newest-cell picks (long winding corridors) with random picks (branches).
    /// Guarantees every room is reachable from the entrance.
    /// </summary>
    private static void CarveSpanningTree(Floor floor, Room start, GameRng rng)
    {
        var visited = new HashSet<RoomId> { start.Id };
        var active = new List<Room> { start };
        var candidates = new List<Direction>(4);

        while (active.Count > 0)
        {
            int index = rng.Chance(0.5) ? active.Count - 1 : rng.NextInt(0, active.Count);
            var room = active[index];

            candidates.Clear();
            foreach (var d in DirectionExtensions.All)
            {
                var next = floor.Neighbor(room, d);
                if (next is not null && !visited.Contains(next.Id)) candidates.Add(d);
            }

            if (candidates.Count == 0)
            {
                active.RemoveAt(index);
                continue;
            }

            var dir = rng.Pick(candidates);
            var neighbor = floor.Neighbor(room, dir)!;
            Connect(room, neighbor, dir);
            visited.Add(neighbor.Id);
            active.Add(neighbor);
        }
    }

    private static void AddLoops(Floor floor, double chance, GameRng rng)
    {
        foreach (var room in floor.Rooms)
        {
            foreach (var dir in new[] { Direction.East, Direction.South })
            {
                var neighbor = floor.Neighbor(room, dir);
                if (neighbor is null || room.GetExit(dir) != ExitState.None) continue;
                if (rng.Chance(chance)) Connect(room, neighbor, dir);
            }
        }
    }

    private static void AssignRoomTypes(Floor floor, Room entrance, DungeonConfig config, GameRng rng)
    {
        entrance.Type = RoomType.Lobby;

        // The Relic Chamber goes in the room furthest (by walking distance) from the Lobby.
        var distances = WalkingDistances(floor, entrance);
        var relic = floor.Rooms
            .Where(r => r != entrance)
            .OrderByDescending(r => distances[r.Id])
            .ThenBy(r => r.Id.Y).ThenBy(r => r.Id.X) // deterministic tie-break
            .First();
        relic.Type = RoomType.RelicChamber;

        var weighted = config.RoomWeights
            .Where(kv => kv.Value > 0)
            .OrderBy(kv => kv.Key) // dictionary order must not affect results
            .ToList();
        int total = weighted.Sum(kv => kv.Value);

        foreach (var room in floor.Rooms)
        {
            if (room == entrance || room == relic) continue;

            int roll = rng.NextInt(0, total);
            foreach (var (type, weight) in weighted)
            {
                if (roll < weight) { room.Type = type; break; }
                roll -= weight;
            }
        }
    }

    public static Dictionary<RoomId, int> WalkingDistances(Floor floor, Room from)
    {
        var distances = new Dictionary<RoomId, int> { [from.Id] = 0 };
        var queue = new Queue<Room>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var room = queue.Dequeue();
            foreach (var d in DirectionExtensions.All)
            {
                if (!room.IsPassable(d)) continue;
                var next = floor.Neighbor(room, d);
                if (next is null || distances.ContainsKey(next.Id)) continue;
                distances[next.Id] = distances[room.Id] + 1;
                queue.Enqueue(next);
            }
        }
        return distances;
    }

    private static void Connect(Room a, Room b, Direction fromAToB)
    {
        a.SetExit(fromAToB, ExitState.Open);
        b.SetExit(fromAToB.Opposite(), ExitState.Open);
    }
}
