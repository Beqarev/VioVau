namespace VioVau.Core.Dungeon;

/// <summary>Exit sides of a room. Grid Y grows downwards (North = y - 1), matching screen space.</summary>
public enum Direction
{
    North,
    East,
    South,
    West,
}

public static class DirectionExtensions
{
    public static readonly IReadOnlyList<Direction> All = [Direction.North, Direction.East, Direction.South, Direction.West];

    public static Direction Opposite(this Direction d) => d switch
    {
        Direction.North => Direction.South,
        Direction.East => Direction.West,
        Direction.South => Direction.North,
        Direction.West => Direction.East,
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    public static (int Dx, int Dy) Offset(this Direction d) => d switch
    {
        Direction.North => (0, -1),
        Direction.East => (1, 0),
        Direction.South => (0, 1),
        Direction.West => (-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    /// <summary>The direction from <paramref name="from"/> to an orthogonally adjacent room on the same floor, if any.</summary>
    public static Direction? Between(RoomId from, RoomId to)
    {
        if (from.Floor != to.Floor) return null;
        foreach (var d in All)
        {
            var (dx, dy) = d.Offset();
            if (from.X + dx == to.X && from.Y + dy == to.Y) return d;
        }
        return null;
    }
}
