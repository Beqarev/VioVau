namespace VioVau.Core.Dungeon;

public enum LootKind
{
    Chest,
    Key,
    /// <summary>A card on a lectern (Library, free) or a pedestal (Merchant, costs <see cref="LootItem.Price"/>).</summary>
    Card,
    /// <summary>A dead player's dropped points (<see cref="LootItem.Points"/>) and keys (<see cref="LootItem.Keys"/>).</summary>
    Bag,
    /// <summary>The run's big prize: many points, a Threat spike, one carried at a time.</summary>
    Relic,
}

/// <summary>A chest or key lying in a room. Claimed once opened / picked up.</summary>
public sealed class LootItem
{
    public EntityId Id { get; }
    public LootKind Kind { get; }
    public RoomId Room { get; }
    public RoomPoint Position { get; }

    /// <summary>Points inside a chest (0 for keys).</summary>
    public int Points { get; }

    /// <summary>The card offered (Card loot only).</summary>
    public CardId? Card { get; }

    /// <summary>Points it costs to take (Merchant cards; 0 = free).</summary>
    public int Price { get; }

    /// <summary>Keys inside (loot bags).</summary>
    public int Keys { get; init; }

    public bool Claimed { get; internal set; }

    internal LootItem(EntityId id, LootKind kind, RoomId room, RoomPoint position, int points, CardId? card = null, int price = 0)
    {
        Id = id;
        Kind = kind;
        Room = room;
        Position = position;
        Points = points;
        Card = card;
        Price = price;
    }

    public override string ToString() => $"{Kind} {Id} in {Room}";
}
