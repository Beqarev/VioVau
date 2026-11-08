using Godot;
using VioVau.Core;
using VioVau.Core.Dungeon;
using VioVau.Game.Input;
using VioVau.Game.World;

namespace VioVau.Game.Dev;

/// <summary>
/// Dev-only "player" for automated play-tests. Priorities each frame: revive a downed ally in the room; deal with
/// a Sentry that's too close (Flash Powder, Shatter, Decoy, or run); grab keys / chests / bags / affordable cards;
/// open a locked door (key or Lockpick); then leave through an open exit (avoiding immediate backtracking).
/// Plays a casual card every few seconds. Always sprints.
/// </summary>
public sealed class AutopilotDevice : InputDevice
{
    private const float Arrive = 6f;
    private const ulong CardIntervalMs = 4000;
    private const int GiveUpPulses = 40;

    /// <summary>Cards it plays on its own (no target needed, or the host picks the nearest player).</summary>
    private static readonly HashSet<string> CasualCards = ["dash", "blink", "hush", "smoke-veil", "lantern", "void-sight", "gilded-greed", "scapegoat", "shared-fortune"];

    private readonly RunView _view;
    private readonly PlayerId _player;
    private readonly Func<Vector2> _position;
    private readonly Func<PlayerId, Vector2?> _positionOf;
    private readonly Func<Vector2, Vector2?> _nearestEnemy;
    private readonly System.Random _random = new(1234); // dev tool only; not game-rule randomness
    private readonly Dictionary<EntityId, int> _attempts = new();

    private RoomId _room;
    private RoomId? _previousRoom;
    private Vector2? _exitTarget;
    private bool _interact;
    private int _pressCard = -1;
    private int _pulse;
    private ulong _nextCardAt = 3000;

    /// <param name="positionOf">Where a (living) player is.</param>
    /// <param name="nearestEnemy">The nearest enemy to a point, if any.</param>
    public AutopilotDevice(RunView view, PlayerId player, Func<Vector2> position, Func<PlayerId, Vector2?> positionOf, Func<Vector2, Vector2?> nearestEnemy)
    {
        _view = view;
        _player = player;
        _position = position;
        _positionOf = positionOf;
        _nearestEnemy = nearestEnemy;
        _room = view.Dungeon.Entrance;
    }

    public override string Key => "autopilot";
    public override string Label => "Autopilot";
    public override string InteractLabel => "A";
    public override string[] CardLabels => ["1", "2", "3"];
    public override bool ReadSprint() => true;

    /// <summary>Pulses the button (pressed every other frame) while standing at a target.</summary>
    public override bool ReadInteract() => _interact;

    public override bool ReadCard(int slot) => _pressCard == slot;

    public override Vector2 ReadMove()
    {
        _interact = false;
        _pressCard = -1;
        var pos = _position();
        var room = WorldLayout.RoomAt(0, pos);
        if (!_view.Dungeon.Contains(room) || _view.Player(_player) is not { } me) return Vector2.Zero;

        if (room != _room)
        {
            _previousRoom = _room;
            _room = room;
            _exitTarget = null;
        }

        if (me.Downed || me.Dead) return Vector2.Zero;

        // 0a. A downed ally anywhere: head to them and revive by hand (Rally only if an enemy is close).
        foreach (var other in _view.Players.Select(p => _view.Player(p.Id)!).Where(p => p.Downed && p.Info.Id != _player))
        {
            if (_positionOf(other.Info.Id) is not { } at) continue;
            bool sameRoom = WorldLayout.RoomAt(0, at) == room;
            int rally = SlotOf(me, "rally");
            bool danger = _nearestEnemy(pos) is { } threat && pos.DistanceTo(threat) < 30f;
            if (sameRoom && danger && rally >= 0 && _pulse++ % 2 == 0)
            {
                _pressCard = rally;
                return Vector2.Zero;
            }
            if (pos.DistanceTo(at) > Arrive)
                return RoomNavigator.NextWaypoint(_view.Dungeon, pos, at) is { } next ? (next - pos).Normalized() : Vector2.Zero;
            PulseInteract();
            return Vector2.Zero;
        }

        // 0b. A Sentry too close: stun it, break it, distract it — or run.
        if (_nearestEnemy(pos) is { } enemy && pos.DistanceTo(enemy) < 40f)
        {
            int flash = SlotOf(me, "flash-powder"), shatter = SlotOf(me, "shatter"), decoy = SlotOf(me, "decoy");
            if (shatter >= 0 && pos.DistanceTo(enemy) < 26f) _pressCard = shatter;
            else if (flash >= 0) _pressCard = flash;
            else if (decoy >= 0) _pressCard = decoy;
            var away = (pos - enemy).Normalized();
            // Run away, but slide along walls toward the room's middle so we don't pin ourselves in a corner.
            return (away + (WorldLayout.RoomCenter(room) - pos).Normalized() * 0.6f).Normalized();
        }

        MaybePlayCasualCard(me);

        // 1. Loot: keys by touch, chests by Interact, cards if free or affordable.
        var loot = _view.Dungeon.LootIn(room)
            .Where(l => !l.Claimed && _attempts.GetValueOrDefault(l.Id) < GiveUpPulses)
            .Where(l => l.Kind != LootKind.Card || l.Price <= me.CarriedPoints)
            .Select(l => (Item: l, At: WorldLayout.LootPosition(room, l.Position)))
            .OrderBy(l => l.At.DistanceTo(pos))
            .FirstOrDefault();
        if (loot.Item is not null)
        {
            if (pos.DistanceTo(loot.At) > Arrive) return (loot.At - pos).Normalized();
            if (loot.Item.Kind != LootKind.Key)
            {
                _attempts[loot.Item.Id] = _attempts.GetValueOrDefault(loot.Item.Id) + 1;
                PulseInteract();
            }
            return Vector2.Zero;
        }

        // 2. A locked door: with a key (Interact) or a Lockpick (card).
        var data = _view.Dungeon.GetRoom(room);
        int lockpick = Array.FindIndex(me.Hand, c => c?.Value == "lockpick");
        var locked = DirectionExtensions.All.Where(d => data.GetExit(d) == ExitState.Locked).ToList();
        if (locked.Count > 0 && (me.Keys > 0 || lockpick >= 0))
        {
            var approach = WorldLayout.DoorApproach(room, locked[0]);
            if (pos.DistanceTo(approach) > Arrive) return (approach - pos).Normalized();
            if (me.Keys > 0) PulseInteract();
            else if (_pulse++ % 2 == 0) _pressCard = lockpick;
            return Vector2.Zero;
        }

        // 3. Leave through an open exit.
        _exitTarget ??= PickExit(data);
        if (_exitTarget is not { } target) return Vector2.Zero;

        var center = WorldLayout.RoomCenter(room);
        var waypoint = (pos - center).Length() > Arrive && !OnDoorAxis(pos, center, target) ? center : target;
        return (waypoint - pos).Normalized();
    }

    private void MaybePlayCasualCard(PlayerView me)
    {
        if (Time.GetTicksMsec() < _nextCardAt) return;
        int slot = Array.FindIndex(me.Hand, c => c is { } id && CasualCards.Contains(id.Value));
        if (slot < 0) return;
        _pressCard = slot;
        _nextCardAt = Time.GetTicksMsec() + CardIntervalMs;
    }

    private void PulseInteract() => _interact = _pulse++ % 2 == 0;

    private static int SlotOf(PlayerView me, string card) => Array.FindIndex(me.Hand, c => c?.Value == card);

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
