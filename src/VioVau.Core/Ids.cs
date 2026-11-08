namespace VioVau.Core;

/// <summary>A human player with a character. One network peer can own several (couch co-op).</summary>
public readonly record struct PlayerId(int Value)
{
    public override string ToString() => $"P{Value}";
}

/// <summary>A room's position: floor (depth) and grid coordinates.</summary>
public readonly record struct RoomId(int Floor, int X, int Y)
{
    public override string ToString() => $"F{Floor}({X},{Y})";
}

/// <summary>Identifies a card definition in data (e.g. "dash").</summary>
public readonly record struct CardId(string Value)
{
    public override string ToString() => Value;
}

/// <summary>A thing placed in the dungeon (chest, key, …). Unique per run.</summary>
public readonly record struct EntityId(int Value)
{
    public override string ToString() => $"E{Value}";
}
