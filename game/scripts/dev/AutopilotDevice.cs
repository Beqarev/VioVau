using Godot;
using VioVau.Core;
using VioVau.Core.Dungeon;
using VioVau.Game.Input;
using VioVau.Game.World;

namespace VioVau.Game.Dev;

/// <summary>
/// Dev-only "player" for automated play-tests. In each room it grabs keys, opens chests, unlocks a locked
/// door if it carries a key, then leaves through an open exit (avoiding immediate backtracking). Always sprints.
/// </summary>
public sealed class AutopilotDevice : InputDevice
{
    private const float Arrive = 6f;

    private readonly RunView _view;
    private readonly PlayerId _player;
    private readonly Func<Vector2> _position;
    private readonly System.Random _random = new(1234); // dev tool only; not game-rule randomness

    private RoomId _room;
    private RoomId? _previousRoom;
    private Vector2? _exitTarget;
    private bool _interact;
    private int _interactFrames;

    public AutopilotDevice(RunView view, PlayerId player, Func<Vector2> position)
    {
        _view = view;
        _player = player;
        _position = position;
        _room = view.Dungeon.Entrance;
    }

    public override string Key => "autopilot";
    public override string Label => "Autopilot";
    public override string InteractLabel => "A";
    public override bool ReadSprint() => true;

    /// <summary>Pulses the button (pressed every other frame) while standing at a target.</summary>
    public override bool ReadInteract() => _interact;

    public override Vector2 ReadMove()
    {
        _interact = false;
        var pos = _position();
        var room = WorldLayout.RoomAt(0, pos);
        if (!_view.Dungeon.Contains(room)) return Vector2.Zero;

        if (room != _room)
        {
            _previousRoom = _room;
            _room = room;
            _exitTarget = null;
        }

        // 1. Loot in this room (keys are picked up by walking over them, chests need Interact).
        var loot = _view.Dungeon.LootIn(room)
            .Where(l => !l.Claimed)
            .Select(l => (Item: l, At: WorldLayout.LootPosition(room, l.Position)))
            .OrderBy(l => l.At.DistanceTo(pos))
            .FirstOrDefault();
        if (loot.Item is not null)
        {
            if (pos.DistanceTo(loot.At) > Arrive) return (loot.At - pos).Normalized();
            if (loot.Item.Kind == LootKind.Chest) PulseInteract();
            return Vector2.Zero;
        }

        // 2. A locked door, if we can open it.
        var data = _view.Dungeon.GetRoom(room);
        if (_view.Player(_player)?.Keys > 0 && DirectionExtensions.All.FirstOrDefault(d => data.GetExit(d) == ExitState.Locked, (Direction)(-1)) is var locked && (int)locked >= 0)
        {
            var approach = WorldLayout.DoorApproach(room, locked);
            if (pos.DistanceTo(approach) > Arrive) return (approach - pos).Normalized();
            PulseInteract();
            return Vector2.Zero;
        }

        // 3. Leave through an open exit.
        _exitTarget ??= PickExit(data);
        if (_exitTarget is not { } target) return Vector2.Zero;

        var center = WorldLayout.RoomCenter(room);
        var waypoint = (pos - center).Length() > Arrive && !OnDoorAxis(pos, center, target) ? center : target;
        return (waypoint - pos).Normalized();
    }

    private void PulseInteract() => _interact = _interactFrames++ % 2 == 0;

    private Vector2? PickExit(Room room)
    {
        var exits = DirectionExtensions.All.Where(room.IsPassable).ToList();
        var forward = exits.Where(d => _view.Dungeon.Neighbor(room.Id, d)?.Id != _previousRoom).ToList();
        var choices = forward.Count > 0 ? forward : exits;
        if (choices.Count == 0) return null;

        var dir = choices[_random.Next(choices.Count)];
        var (dx, dy) = dir.Offset();
        // A point well inside the next room, straight through the door.
        return WorldLayout.RoomCenter(room.Id) + new Vector2(dx * WorldLayout.RoomWidth * 0.8f, dy * WorldLayout.RoomHeight * 0.8f);
    }

    private static bool OnDoorAxis(Vector2 pos, Vector2 center, Vector2 target) =>
        Mathf.Abs(target.X - center.X) < 1f ? Mathf.Abs(pos.X - center.X) < 4f : Mathf.Abs(pos.Y - center.Y) < 4f;
}
