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

/// <summary>Host only: turns physical facts (crossing rooms, touching keys, interact/card requests, revive channels) into Core commands.</summary>
public partial class GameSession
{
    // ── Host: physical facts → commands ─────────────────────────────────────

    private void DetectRoomChange(PlayerCharacter character)
    {
        if (_net.State!.GetPlayer(character.Id)?.Room is not { } current) return;

        var at = WorldLayout.RoomAt(0, character.AuthoritativePosition);
        if (at == current || !_view.Dungeon.Contains(at))
        {
            _rejectedMoves.Remove(character.Id);
            return;
        }
        if (_rejectedMoves.TryGetValue(character.Id, out var rejected) && rejected == at) return;

        var result = _net.ApplyCommand(new MovePlayerToRoom(character.Id, at));
        if (result.Accepted)
        {
            _rejectedMoves.Remove(character.Id);
        }
        else
        {
            _rejectedMoves[character.Id] = at;
            GD.PushWarning($"[Game] Rejected move {character.Id} → {at}: {result.RejectionReason}");
        }
    }

    private void PickUpTouchedKeys(PlayerCharacter character)
    {
        var state = _net.State!;
        if (state.GetPlayer(character.Id)?.Room is not { } room) return;

        var pos = character.AuthoritativePosition;
        foreach (var key in state.Dungeon!.LootIn(room).Where(l => l.Kind == LootKind.Key && !l.Claimed).ToList())
        {
            if (pos.DistanceTo(WorldLayout.LootPosition(room, key.Position)) <= KeyRange)
                _net.ApplyCommand(new PickUpKey(character.Id, key.Id));
        }
    }

    private void OnInteractRequested(PlayerId player, InteractTarget target)
    {
        if (!_characters.TryGetValue(player, out var character)) return;

        // Re-check reach with the host's view of where that player is.
        float tolerance = character.IsLocal ? 0f : RemoteTolerance;
        if (FindTarget(character.AuthoritativePosition, tolerance, target, self: player) is null)
        {
            GD.Print($"[Game {_elapsed:0.0}s] Ignored {player} → {target}: out of reach or already used.");
            return;
        }

        if (target.Kind == InteractKind.Revive)
        {
            // Reviving is a channel: stay beside them for a moment (see TickRevives). Pressing again doesn't restart it.
            var downed = new PlayerId(target.Value);
            if (!_revives.TryGetValue(downed, out var channel) || channel.Reviver != player) _revives[downed] = (player, 0);
            return;
        }

        ICommand command = target.Kind switch
        {
            InteractKind.Chest => new OpenChest(player, new EntityId(target.Value)),
            InteractKind.Card => new TakeCard(player, new EntityId(target.Value)),
            InteractKind.Bag => new ClaimBag(player, new EntityId(target.Value)),
            InteractKind.Relic => new TakeRelic(player, new EntityId(target.Value)),
            InteractKind.Extract => new Extract(player),
            _ => new UnlockExit(player, (Direction)target.Value),
        };
        var result = _net.ApplyCommand(command);
        if (!result.Accepted) _net.NotifyPlayer(player, result.RejectionReason ?? "Can't do that.");
    }

    /// <summary>
    /// Host: play a card. The host picks the target from its own positions (a locked door in reach,
    /// the player beside you, or the nearest player), so clients can't fake one.
    /// </summary>
    private void OnPlayCardRequested(PlayerId player, int slot)
    {
        var state = _net.State;
        if (state?.GetPlayer(player)?.Deck?.PeekHand(slot) is not { } cardId || !_characters.TryGetValue(player, out var character)) return;
        if (state.Config.CardSet.Get(cardId) is not { } card) return;

        var pos = character.AuthoritativePosition;
        float tolerance = character.IsLocal ? 0f : RemoteTolerance;
        PlayerId? targetPlayer = null;
        Direction? door = null;
        EnemyId? enemy = null;

        switch (card.Target)
        {
            case CardTarget.LockedDoor:
                if (FindTarget(pos, tolerance, doorsOnly: true) is { } d) door = (Direction)d.Value;
                break;
            case CardTarget.NearbyPlayer:
                targetPlayer = NearestOtherPlayer(character, NearbyPlayerRange + tolerance);
                break;
            case CardTarget.OtherPlayer:
                targetPlayer = NearestOtherPlayer(character, float.MaxValue);
                break;
            case CardTarget.NearbyEnemy:
                enemy = _enemies.Values
                    .Where(e => e.GlobalPosition.DistanceTo(pos) <= NearbyEnemyRange + tolerance)
                    .OrderBy(e => e.Kind == EnemyKind.Warden) // prefer something Shatter can actually break
                    .ThenBy(e => e.GlobalPosition.DistanceTo(pos))
                    .Select(e => (EnemyId?)e.Id)
                    .FirstOrDefault();
                break;
            case CardTarget.DownedPlayer:
                var room = WorldLayout.RoomAt(0, pos);
                targetPlayer = _characters.Values
                    .Where(c => c.IsDowned && WorldLayout.RoomAt(0, c.AuthoritativePosition) == room)
                    .OrderBy(c => c.AuthoritativePosition.DistanceTo(pos))
                    .Select(c => (PlayerId?)c.Id)
                    .FirstOrDefault();
                break;
        }

        var result = _net.ApplyCommand(new PlayCard(player, slot, targetPlayer, door, enemy));
        if (!result.Accepted) _net.NotifyPlayer(player, result.RejectionReason ?? $"Can't play {card.Name}.");
    }

    private PlayerId? NearestOtherPlayer(PlayerCharacter from, float range) =>
        _characters.Values
            .Where(c => c != from && !c.IsDead)
            .Select(c => (c.Id, Distance: c.AuthoritativePosition.DistanceTo(from.AuthoritativePosition)))
            .Where(c => c.Distance <= range)
            .OrderBy(c => c.Distance)
            .Select(c => (PlayerId?)c.Id)
            .FirstOrDefault();

    // ── Reviving (host) ─────────────────────────────────────────────────────

    /// <summary>A revive completes after <see cref="ReviveSeconds"/> beside the downed player; moving away or getting downed cancels it.</summary>
    private void TickRevives(double delta)
    {
        foreach (var (targetId, (reviverId, time)) in _revives.ToList())
        {
            bool valid = _characters.TryGetValue(targetId, out var target) && target.IsDowned &&
                         _characters.TryGetValue(reviverId, out var reviver) && reviver.IsActive &&
                         reviver.AuthoritativePosition.DistanceTo(target.AuthoritativePosition) <= ReviveRange + RemoteTolerance;
            if (!valid)
            {
                _revives.Remove(targetId);
                _net.SendReviveProgress(targetId, 0);
                continue;
            }

            double now = time + delta;
            if (now >= ReviveSeconds)
            {
                _revives.Remove(targetId);
                _net.SendReviveProgress(targetId, 0);
                var result = _net.ApplyCommand(new RevivePlayer(reviverId, targetId));
                if (!result.Accepted) _net.NotifyPlayer(reviverId, result.RejectionReason ?? "Can't revive.");
                continue;
            }
            _revives[targetId] = (reviverId, now);
            _net.SendReviveProgress(targetId, (float)(now / ReviveSeconds));
        }
    }

    private void OnReviveProgress(PlayerId target, float progress)
    {
        if (_characters.TryGetValue(target, out var character)) character.ShowReviveProgress(progress);
    }
}
