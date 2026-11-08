namespace VioVau.Core.Dungeon;

public enum LootKind
{
    Chest,
    Key,
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

    public bool Claimed { get; internal set; }

    internal LootItem(EntityId id, LootKind kind, RoomId room, RoomPoint position, int points)
    {
        Id = id;
        Kind = kind;
        Room = room;
        Position = position;
        Points = points;
    }

    public override string ToString() => $"{Kind} {Id} in {Room}";
}
