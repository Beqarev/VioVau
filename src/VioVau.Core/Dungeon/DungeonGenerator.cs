using VioVau.Core.Cards;
using VioVau.Core.Random;

namespace VioVau.Core.Dungeon;

/// <summary>
/// Builds a dungeon from a seed. Pure and deterministic: host and clients generate identical maps
/// from the same seed and config, so the layout never has to be sent over the network.
/// </summary>
public static class DungeonGenerator
{
    public static DungeonMap Generate(ulong seed, DungeonConfig config, CardsConfig? cards = null)
    {
        config.Validate();
        cards ??= CardsConfig.Default;
        var layoutRng = RngStreams.Create(seed, "dungeon");
        var lootRng = RngStreams.Create(seed, "dungeon-loot"); // separate, so loot tuning never reshapes the map

        // One floor for now; stairs and deeper floors arrive with later milestones.
        var floor = new Floor(0, config.Width, config.Height, (x, y) => new Room(new RoomId(0, x, y), RoomType.Corridor));
        var entrance = floor[config.Width / 2, config.Height - 1];

        CarveSpanningTree(floor, entrance, layoutRng);
        AddLoops(floor, config.ExtraConnectionChance, layoutRng);
        AssignRoomTypes(floor, entrance, config, layoutRng);
        int lockedVaults = LockVaults(floor, entrance);
        var loot = PlaceLoot(floor, entrance, config, lockedVaults, lootRng);
        loot.AddRange(PlaceCardOffers(floor, cards, loot, RngStreams.Create(seed, "dungeon-cards")));
        loot.AddRange(PlaceRelics(floor, config, loot));

        return new DungeonMap([floor], entrance.Id, loot);
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
            Connect(room, neighbor, dir, ExitState.Open);
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
                if (rng.Chance(chance)) Connect(room, neighbor, dir, ExitState.Open);
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

    /// <summary>
    /// Every doorway into a Vault becomes Locked. A Vault that would cut other rooms off from the Lobby
    /// (a chokepoint) is downgraded to a Guard Post instead, so the dungeon never needs a key just to explore.
    /// </summary>
    /// <returns>The number of Vaults that ended up locked.</returns>
    private static int LockVaults(Floor floor, Room entrance)
    {
        int locked = 0;
        foreach (var vault in floor.Rooms.Where(r => r.Type == RoomType.Vault).ToList())
        {
            var doors = DirectionExtensions.All.Where(d => vault.GetExit(d) == ExitState.Open).ToList();
            foreach (var d in doors) Connect(vault, floor.Neighbor(vault, d)!, d, ExitState.Locked);

            var reachable = WalkingDistances(floor, entrance);
            bool cutsOffRooms = floor.Rooms.Any(r => r.Type != RoomType.Vault && !reachable.ContainsKey(r.Id));

            if (cutsOffRooms)
            {
                foreach (var d in doors) Connect(vault, floor.Neighbor(vault, d)!, d, ExitState.Open);
                vault.Type = RoomType.GuardPost;
            }
            else
            {
                locked++;
            }
        }
        return locked;
    }

    private static List<LootItem> PlaceLoot(Floor floor, Room entrance, DungeonConfig config, int lockedVaults, GameRng rng)
    {
        var loot = new List<LootItem>();
        var freeSpots = floor.Rooms.ToDictionary(r => r.Id, _ => config.LootSpots.ToList());
        int nextId = 1;

        RoomPoint TakeSpot(RoomId room)
        {
            var spots = freeSpots[room];
            int i = rng.NextInt(0, spots.Count);
            var spot = spots[i];
            spots.RemoveAt(i);
            return spot;
        }

        foreach (var room in floor.Rooms)
        {
            if (!config.Loot.TryGetValue(room.Type, out var rule)) continue;
            int chests = rng.NextInt(rule.MinChests, rule.MaxChests + 1);
            for (int i = 0; i < chests; i++)
            {
                int points = rng.NextInt(rule.MinPoints, rule.MaxPoints + 1);
                loot.Add(new LootItem(new EntityId(nextId++), LootKind.Chest, room.Id, TakeSpot(room.Id), points));
            }
        }

        // Keys only go where you can walk without a key, one room each where possible.
        var keyRooms = WalkingDistances(floor, entrance).Keys
            .Where(id => id != entrance.Id && freeSpots[id].Count > 0)
            .OrderBy(id => id.Y).ThenBy(id => id.X)
            .ToList();
        rng.Shuffle(keyRooms);

        int keys = lockedVaults + config.ExtraKeys;
        for (int i = 0; i < keys && keyRooms.Count > 0; i++)
        {
            var room = keyRooms[i % keyRooms.Count];
            if (freeSpots[room].Count == 0) continue;
            loot.Add(new LootItem(new EntityId(nextId++), LootKind.Key, room, TakeSpot(room), 0));
        }

        return loot;
    }

    /// <summary>
    /// Libraries and Merchants get cards on lecterns/pedestals in a row across the room's upper half,
    /// rolled by rarity weight. Merchant cards cost their price; Library cards are free.
    /// </summary>
    private static IEnumerable<LootItem> PlaceCardOffers(Floor floor, CardsConfig cards, List<LootItem> placed, GameRng rng)
    {
        if (cards.Cards.Count == 0) yield break;
        int nextId = placed.Count == 0 ? 1 : placed.Max(l => l.Id.Value) + 1;

        foreach (var room in floor.Rooms)
        {
            if (!cards.Offers.TryGetValue(room.Type, out var rule) || rule.Count == 0) continue;
            for (int i = 0; i < rule.Count; i++)
            {
                var card = RollCard(cards, rng);
                float x = (i + 1f) / (rule.Count + 1f);
                yield return new LootItem(new EntityId(nextId++), LootKind.Card, room.Id, new RoomPoint(x, 0.35f), 0,
                    card.CardId, rule.Free ? 0 : card.Price);
            }
        }
    }

    /// <summary>Relics stand in a row across the middle of the Relic Chamber, most valuable first.</summary>
    private static IEnumerable<LootItem> PlaceRelics(Floor floor, DungeonConfig config, List<LootItem> placed)
    {
        var chamber = floor.Rooms.FirstOrDefault(r => r.Type == RoomType.RelicChamber);
        if (chamber is null) yield break;
        int nextId = placed.Count == 0 ? 1 : placed.Max(l => l.Id.Value) + 1;
        var values = config.RelicValues.OrderByDescending(v => v).ToList();
        for (int i = 0; i < values.Count; i++)
        {
            float x = (i + 1f) / (values.Count + 1f);
            yield return new LootItem(new EntityId(nextId++), LootKind.Relic, chamber.Id, new RoomPoint(x, 0.5f), values[i]);
        }
    }

    private static CardDefinition RollCard(CardsConfig cards, GameRng rng)
    {
        var pool = cards.Cards.Where(c => cards.RarityWeights.GetValueOrDefault(c.Rarity) > 0).ToList();
        if (pool.Count == 0) pool = cards.Cards;
        int total = pool.Sum(c => Math.Max(1, cards.RarityWeights.GetValueOrDefault(c.Rarity)));
        int roll = rng.NextInt(0, total);
        foreach (var card in pool)
        {
            roll -= Math.Max(1, cards.RarityWeights.GetValueOrDefault(card.Rarity));
            if (roll < 0) return card;
        }
        return pool[^1];
    }

    /// <summary>Walking distance from <paramref name="from"/> to every room reachable through passable exits.</summary>
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

    private static void Connect(Room a, Room b, Direction fromAToB, ExitState state)
    {
        a.SetExit(fromAToB, state);
        b.SetExit(fromAToB.Opposite(), state);
    }
}
