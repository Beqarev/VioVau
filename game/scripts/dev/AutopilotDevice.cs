using Godot;
using VioVau.Core;
using VioVau.Core.Dungeon;
using VioVau.Game.Input;
using VioVau.Game.World;

namespace VioVau.Game.Dev;

/// <summary>
/// Dev-only "player" that wanders the dungeon through open exits, avoiding immediate backtracking.
/// Used to play-test movement, room transitions and networking without a human.
/// </summary>
public sealed class AutopilotDevice : InputDevice
{
    private readonly DungeonMap _map;
    private readonly Func<Vector2> _position;
    private readonly System.Random _random = new(1234); // dev tool only; not game-rule randomness

    private RoomId _room;
    private RoomId? _previousRoom;
    private Vector2? _doorTarget;

    public AutopilotDevice(DungeonMap map, Func<Vector2> position)
    {
        _map = map;
        _position = position;
        _room = map.Entrance;
    }

    public override string Key => "autopilot";
    public override string Label => "Autopilot";
    public override bool ReadSprint() => true;

    public override Vector2 ReadMove()
    {
        var pos = _position();
        var room = WorldLayout.RoomAt(0, pos);

        if (room != _room)
        {
            _previousRoom = _room;
            _room = room;
            _doorTarget = null;
        }

        _doorTarget ??= PickDoor(room);
        if (_doorTarget is not { } target) return Vector2.Zero;

        // Go to the room center first (never clip a corner), then straight through the door and beyond.
        var center = WorldLayout.RoomCenter(room);
        var waypoint = (pos - center).Length() > 6f && !OnDoorAxis(pos, center, target) ? center : target;
        return (waypoint - pos).Normalized();
    }

    private Vector2? PickDoor(RoomId id)
    {
        if (!_map.Contains(id)) return null;
        var room = _map.GetRoom(id);
        var floor = _map.Floors[id.Floor];

        var exits = DirectionExtensions.All.Where(room.IsPassable).ToList();
        var forward = exits.Where(d => floor.Neighbor(room, d)?.Id != _previousRoom).ToList();
        var choices = forward.Count > 0 ? forward : exits;
        if (choices.Count == 0) return null;

        var dir = choices[_random.Next(choices.Count)];
        var (dx, dy) = dir.Offset();
        // A point well inside the next room, straight through the door.
        return WorldLayout.RoomCenter(id) + new Vector2(dx * WorldLayout.RoomWidth * 0.8f, dy * WorldLayout.RoomHeight * 0.8f);
    }

    private static bool OnDoorAxis(Vector2 pos, Vector2 center, Vector2 target) =>
        Mathf.Abs(target.X - center.X) < 1f ? Mathf.Abs(pos.X - center.X) < 4f : Mathf.Abs(pos.Y - center.Y) < 4f;
}
