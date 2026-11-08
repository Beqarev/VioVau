using Godot;
using VioVau.Core;
using VioVau.Core.Dungeon;

namespace VioVau.Game.World;

/// <summary>
/// Path-finding for walkers (enemies, the autopilot). Rooms are empty rectangles, so inside a room a straight line
/// always works; between rooms we walk the room graph through passable doorways, lining up with each door first.
/// </summary>
public static class RoomNavigator
{
    private const float LineUpTolerance = 7f, ThroughDistance = 30f, Approach = 20f;

    /// <summary>The point to steer toward next on the way from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <returns>The next waypoint, or null if there's no open path (e.g. behind a locked door).</returns>
    public static Vector2? NextWaypoint(DungeonMap map, Vector2 from, Vector2 to)
    {
        var a = WorldLayout.RoomAt(0, from);
        var b = WorldLayout.RoomAt(0, to);
        if (!map.Contains(a) || !map.Contains(b)) return null;
        if (a == b) return to;

        if (FirstStep(map, a, b) is not { } dir) return null;

        var door = WorldLayout.DoorCenter(a, dir);
        var (dx, dy) = dir.Offset();
        var axis = new Vector2(dx, dy);
        var across = new Vector2(Mathf.Abs(dy), Mathf.Abs(dx)); // the axis along the wall
        float offset = Mathf.Abs((from - door).Dot(across));
        float distanceToDoor = (door - from).Dot(axis);

        // Lined up and close: go through. Otherwise line up in front of the door first.
        return offset < LineUpTolerance && distanceToDoor < ThroughDistance
            ? door + axis * 24f
            : door - axis * Approach;
    }

    /// <summary>The direction of the first doorway on the shortest open path between two rooms.</summary>
    public static Direction? FirstStep(DungeonMap map, RoomId from, RoomId to)
    {
        if (from == to) return null;
        var previous = new Dictionary<RoomId, RoomId> { [from] = from };
        var queue = new Queue<RoomId>([from]);
        while (queue.Count > 0)
        {
            var room = queue.Dequeue();
            if (room == to) break;
            var data = map.GetRoom(room);
            foreach (var d in DirectionExtensions.All.Where(data.IsPassable))
            {
                if (map.Neighbor(room, d) is { } next && previous.TryAdd(next.Id, room)) queue.Enqueue(next.Id);
            }
        }
        if (!previous.ContainsKey(to)) return null;

        var step = to;
        while (previous[step] != from) step = previous[step];
        return DirectionExtensions.Between(from, step);
    }
}
