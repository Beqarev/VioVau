using Godot;
using VioVau.Core;
using VioVau.Core.Dungeon;

namespace VioVau.Game.World;

/// <summary>
/// Maps the room grid to world space. Every room of a floor is laid out side by side, so players walk
/// continuously through door gaps and the room they're in is simply derived from their position.
/// </summary>
public static class WorldLayout
{
    /// <summary>One room fills exactly one screen at the base resolution (480×270).</summary>
    public const float RoomWidth = 480f;
    public const float RoomHeight = 270f;
    public const float WallThickness = 8f;
    public const float DoorWidth = 48f;

    public static Vector2 RoomSize => new(RoomWidth, RoomHeight);

    public static Vector2 RoomOrigin(RoomId id) => new(id.X * RoomWidth, id.Y * RoomHeight);

    public static Vector2 RoomCenter(RoomId id) => RoomOrigin(id) + RoomSize / 2f;

    public static RoomId RoomAt(int floor, Vector2 worldPosition) =>
        new(floor, Mathf.FloorToInt(worldPosition.X / RoomWidth), Mathf.FloorToInt(worldPosition.Y / RoomHeight));

    /// <summary>Wall rectangles (room-local) for one side; a passable side gets a centered door gap.</summary>
    public static IEnumerable<Rect2> WallRects(Direction side, bool hasDoor)
    {
        const float w = RoomWidth, h = RoomHeight, t = WallThickness, d = DoorWidth;

        bool horizontal = side is Direction.North or Direction.South;
        float length = horizontal ? w : h;
        float fixedPos = side switch
        {
            Direction.North or Direction.West => 0f,
            Direction.South => h - t,
            _ => w - t,
        };

        var spans = hasDoor
            ? new[] { (0f, (length - d) / 2f), ((length + d) / 2f, length) }
            : new[] { (0f, length) };

        foreach (var (from, to) in spans)
        {
            yield return horizontal
                ? new Rect2(from, fixedPos, to - from, t)
                : new Rect2(fixedPos, from, t, to - from);
        }
    }
}
