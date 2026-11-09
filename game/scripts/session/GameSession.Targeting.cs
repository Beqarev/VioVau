using Godot;
using VioVau.Core;
using VioVau.Core.Cards;
using VioVau.Core.Dungeon;
using VioVau.Core.Enemies;
using VioVau.Game.Dev;
using VioVau.Game.Entities;
using VioVau.Game.Input;
using VioVau.Game.Networking;
using VioVau.Game.UI;
using VioVau.Game.World;

namespace VioVau.Game.Session;

/// <summary>Interaction targeting, shared by local prompts and host-side validation.</summary>
public partial class GameSession
{
    // ── Interaction targeting (shared by prompts and host validation) ───────

    private void OnLocalInteractPressed(PlayerCharacter character)
    {
        if (FindTarget(character.GlobalPosition, 0f, self: character.Id) is { } target)
            _net.RequestInteract(character.Id, target);
    }

    /// <summary>
    /// The nearest usable thing within reach of a position: an unopened chest, or a locked door of the room
    /// it's in. With <paramref name="only"/>, checks just that target.
    /// </summary>
    private InteractTarget? FindTarget(Vector2 position, float tolerance, InteractTarget? only = null, bool doorsOnly = false, PlayerId? self = null)
    {
        var room = WorldLayout.RoomAt(0, position);
        if (!_view.Dungeon.Contains(room)) return null;

        InteractTarget? best = null;
        float bestDistance = float.MaxValue;

        void Consider(InteractTarget candidate, Vector2 at, float range)
        {
            if (only is { } o && o != candidate) return;
            float d = position.DistanceTo(at);
            if (d <= range + tolerance && d < bestDistance)
            {
                best = candidate;
                bestDistance = d;
            }
        }

        foreach (var loot in _view.Dungeon.LootIn(room).Where(l => !doorsOnly && !l.Claimed && l.Kind is not LootKind.Key))
        {
            var kind = loot.Kind switch
            {
                LootKind.Chest => InteractKind.Chest,
                LootKind.Bag => InteractKind.Bag,
                LootKind.Relic => InteractKind.Relic,
                _ => InteractKind.Card,
            };
            Consider(new InteractTarget(kind, loot.Id.Value), WorldLayout.LootPosition(room, loot.Position), ChestRange);
        }

        var roomData = _view.Dungeon.GetRoom(room);
        foreach (var side in DirectionExtensions.All.Where(d => roomData.GetExit(d) == ExitState.Locked))
            Consider(new InteractTarget(InteractKind.Door, (int)side), WorldLayout.DoorCenter(room, side), DoorRange);

        if (!doorsOnly && room == _view.Dungeon.Entrance)
            Consider(new InteractTarget(InteractKind.Extract, 0), WorldLayout.RoomCenter(room), ExtractRange);

        if (!doorsOnly)
        {
            foreach (var downed in _characters.Values.Where(c => c.IsDowned && c.Id != self && WorldLayout.RoomAt(0, c.AuthoritativePosition) == room))
                Consider(new InteractTarget(InteractKind.Revive, downed.Id.Value), downed.AuthoritativePosition, ReviveRange);
        }

        return best;
    }
}
