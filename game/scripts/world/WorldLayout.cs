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

    /// <summary>Distance from the walls inside which loot is placed.</summary>
    private const float LootMargin = WallThickness + 16f;

    public static Vector2 RoomSize => new(RoomWidth, RoomHeight);

    public static Vector2 RoomOrigin(RoomId id) => new(id.X * RoomWidth, id.Y * RoomHeight);

    public static Vector2 RoomCenter(RoomId id) => RoomOrigin(id) + RoomSize / 2f;

    public static RoomId RoomAt(int floor, Vector2 worldPosition) =>
        new(floor, Mathf.FloorToInt(worldPosition.X / RoomWidth), Mathf.FloorToInt(worldPosition.Y / RoomHeight));

    /// <summary>World position of a loot spot (normalized room point → pixels, kept off the walls).</summary>
    public static Vector2 LootPosition(RoomId room, RoomPoint point)
    {
        var usable = RoomSize - new Vector2(LootMargin, LootMargin) * 2;
        return RoomOrigin(room) + new Vector2(LootMargin, LootMargin) + new Vector2(point.X * usable.X, point.Y * usable.Y);
    }

    /// <summary>Inverse of <see cref="LootPosition"/>: a world position as a normalized point in its room (clamped).</summary>
    public static RoomPoint ToRoomPoint(RoomId room, Vector2 world)
    {
        var usable = RoomSize - new Vector2(LootMargin, LootMargin) * 2;
        var local = world - RoomOrigin(room) - new Vector2(LootMargin, LootMargin);
        return new RoomPoint(Mathf.Clamp(local.X / usable.X, 0f, 1f), Mathf.Clamp(local.Y / usable.Y, 0f, 1f));
    }

    /// <summary>World position of the middle of a doorway, on the room's edge.</summary>
    public static Vector2 DoorCenter(RoomId room, Direction side)
    {
        var center = RoomCenter(room);
        var (dx, dy) = side.Offset();
        return center + new Vector2(dx * RoomWidth / 2f, dy * RoomHeight / 2f);
    }

    /// <summary>Where to stand inside the room to use a door.</summary>
    public static Vector2 DoorApproach(RoomId room, Direction side, float inset = 16f)
    {
        var (dx, dy) = side.Offset();
        return DoorCenter(room, side) - new Vector2(dx, dy) * inset;
    }

    /// <summary>Wall rectangles (room-local) for one side; a side with a doorway gets a centered gap.</summary>
    public static IEnumerable<Rect2> WallRects(Direction side, bool hasDoorway)
    {
        const float w = RoomWidth, h = RoomHeight, t = WallThickness, d = DoorWidth;

        bool horizontal = side is Direction.North or Direction.South;
        float length = horizontal ? w : h;
        float fixedPos = FixedPos(side);

        var spans = hasDoorway
            ? new[] { (0f, (length - d) / 2f), ((length + d) / 2f, length) }
            : new[] { (0f, length) };

        foreach (var (from, to) in spans)
        {
            yield return horizontal
                ? new Rect2(from, fixedPos, to - from, t)
                : new Rect2(fixedPos, from, t, to - from);
        }
    }

    /// <summary>The doorway gap itself (room-local) — filled by a door when locked.</summary>
    public static Rect2 DoorRect(Direction side)
    {
        const float w = RoomWidth, h = RoomHeight, t = WallThickness, d = DoorWidth;
        return side is Direction.North or Direction.South
            ? new Rect2((w - d) / 2f, FixedPos(side), d, t)
            : new Rect2(FixedPos(side), (h - d) / 2f, t, d);
    }

    private static float FixedPos(Direction side) => side switch
    {
        Direction.North or Direction.West => 0f,
        Direction.South => RoomHeight - WallThickness,
        _ => RoomWidth - WallThickness,
    };
}
