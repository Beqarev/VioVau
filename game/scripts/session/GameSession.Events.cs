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

/// <summary>Reacting to Core events and net callbacks: visuals, Fx, audio, HUD messages.</summary>
public partial class GameSession
{
    // ── Events ──────────────────────────────────────────────────────────────

    private void OnEvent(IGameEvent e)
    {
        switch (e)
        {
            case PlayerEnteredRoom entered:
                GD.Print($"[Game {_elapsed:0.0}s] {entered.Player} entered {entered.Room}{(entered.FirstVisit ? " (first visit)" : "")}");
                break;

            case RoomRevealed revealed when _rooms.TryGetValue(revealed.Room, out var room):
                room.Revealed = true;
                break;

            case ChestOpened opened:
                GD.Print($"[Game {_elapsed:0.0}s] {opened.Player} opened {opened.Chest} for {opened.Points}p");
                if (_loot.TryGetValue(opened.Chest, out var chest))
                {
                    chest.Refresh();
                    Fx.ChestOpened(_world, chest.Position);
                    _world.AddChild(new FloatingText().Init(chest.Position + new Vector2(0, -16), $"+{opened.Points}p", Palette.White));
                    Audio.Instance.PlaySfxAt(_world, "chest_open", chest.Position, (float)_view.Config.Threat.OpenChest);
                }
                break;

            case KeyPickedUp picked:
                GD.Print($"[Game {_elapsed:0.0}s] {picked.Player} picked up key {picked.Key}");
                if (_loot.TryGetValue(picked.Key, out var key))
                {
                    Fx.KeyTaken(_world, key.Position);
                    key.Refresh();
                    Audio.Instance.PlaySfxAt(_world, "key_pickup", key.Position, volume: 0.8f);
                }
                break;

            case ExitUnlocked unlocked:
                GD.Print($"[Game {_elapsed:0.0}s] {unlocked.Player} unlocked {unlocked.Side} of {unlocked.Room}");
                if (_rooms.TryGetValue(unlocked.Room, out var near)) near.RefreshExits();
                var doorAt = WorldLayout.DoorCenter(unlocked.Room, unlocked.Side);
                Fx.DoorUnlocked(_world, doorAt);
                Audio.Instance.PlaySfxAt(_world, "door_unlock", doorAt, (float)_view.Config.Threat.UnlockExit);
                if (_view.Dungeon.Neighbor(unlocked.Room, unlocked.Side) is { } far && _rooms.TryGetValue(far.Id, out var farNode))
                    farNode.RefreshExits();
                break;

            case NoiseMade noise when _characters.TryGetValue(noise.Player, out var maker):
                _world.AddChild(new NoiseRing().Init(maker.GlobalPosition, noise.Amount));
                Audio.Instance.PlaySfxAt(_world, "noise_ring", maker.GlobalPosition, (float)noise.Amount);
                break;

            case CardPlayed played when _characters.TryGetValue(played.Player, out var player):
                var cardDef = _view.Config.CardSet.Get(played.Card);
                var name = cardDef?.Name ?? played.Card.Value;
                GD.Print($"[Game {_elapsed:0.0}s] {played.Player} played {name}");
                _world.AddChild(new FloatingText().Init(player.GlobalPosition + new Vector2(0, -22), name.ToUpperInvariant(),
                    cardDef is { } def ? CardStyle.CategoryColor(def.Category) : Palette.White));
                if (cardDef is not null)
                    Audio.Instance.PlayCard(cardDef.Category, player.GlobalPosition, _world);
                break;

            case MovementEffect move when _characters.TryGetValue(move.Player, out var mover):
                if (move.Kind == EffectType.Blink) mover.Blink((float)move.Distance);
                else mover.Dash((float)move.Distance);
                break;

            case PlayerSilenced silenced when _characters.TryGetValue(silenced.Player, out var quiet):
                quiet.ShowHushed(silenced.Seconds);
                break;

            case EnemySpawned spawned:
                SpawnEnemy(spawned);
                break;

            case EnemyKilled killed when _enemies.Remove(killed.Enemy, out var corpse):
                GD.Print($"[Game {_elapsed:0.0}s] {killed.By} destroyed {killed.Enemy}");
                Fx.EnemyDestroyed(_world, corpse.GlobalPosition);
                corpse.Die();
                break;

            case EnemiesStunned stunned:
                foreach (var e2 in _enemies.Values.Where(n => WorldLayout.RoomAt(0, n.GlobalPosition) == stunned.Room)) e2.Stun(stunned.Seconds);
                if (_characters.TryGetValue(stunned.By, out var flasher))
                    _world.AddChild(new NoiseRing().Init(flasher.GlobalPosition, 12));
                break;

            case DecoyPlaced decoy when _characters.TryGetValue(decoy.By, out var thrower):
                var spot = thrower.AuthoritativePosition;
                _world.AddChild(new DecoyNode().Init(spot, decoy.Seconds));
                foreach (var e3 in _enemies.Values.Where(n => WorldLayout.RoomAt(0, n.GlobalPosition) == decoy.Room)) e3.Distract(spot, decoy.Seconds);
                break;

            case PlayerHit hit when _characters.TryGetValue(hit.Player, out var hurt):
                GD.Print($"[Game {_elapsed:0.0}s] {hit.Player} hit by {hit.Enemy} → {hit.Hearts} hearts");
                hurt.ShowHurt(_view.Config.EnemySet.Health.InvulnerableSeconds);
                Fx.Hit(_world, hurt.GlobalPosition);
                Audio.Instance.PlaySfxAt(_world, "player_hurt", hurt.GlobalPosition, volume: 1f);
                if (_enemies.TryGetValue(hit.Enemy, out var hitter))
                    Audio.Instance.PlaySfxAt(_world, "sentry_hit", hitter.GlobalPosition, volume: 1f);
                if (hurt.IsLocal) _screens.Shake(3f);
                break;

            case PlayerDowned downed when _characters.TryGetValue(downed.Player, out var fallen):
                GD.Print($"[Game {_elapsed:0.0}s] {downed.Player} is DOWN");
                fallen.SetDowned(true);
                _world.AddChild(new FloatingText().Init(fallen.GlobalPosition + new Vector2(0, -22), "DOWN!", Palette.White));
                Audio.Instance.PlaySfxAt(_world, "player_downed", fallen.GlobalPosition);
                break;

            case PlayerRevived revived when _characters.TryGetValue(revived.Player, out var risen):
                GD.Print($"[Game {_elapsed:0.0}s] {revived.By} revived {revived.Player}");
                risen.SetDowned(false);
                _world.AddChild(new FloatingText().Init(risen.GlobalPosition + new Vector2(0, -22), "BACK UP", Palette.Accent));
                Audio.Instance.PlaySfxAt(_world, "player_revived", risen.GlobalPosition);
                break;

            case PlayerDied died when _characters.TryGetValue(died.Player, out var gone):
                gone.CarryingRelic = false;
                GD.Print($"[Game {_elapsed:0.0}s] {died.Player} DIED");
                _world.AddChild(new FloatingText().Init(gone.GlobalPosition + new Vector2(0, -22), $"{gone.DisplayName.ToUpperInvariant()} FELL", Palette.White));
                gone.SetDead();
                Audio.Instance.PlaySfxAt(_world, "player_died", gone.GlobalPosition);
                break;

            case LootDropped dropped when _view.Dungeon.GetLoot(dropped.Bag) is { } bagItem:
                var bagNode = new LootNode().Init(bagItem, fancy: false);
                _lootLayer.AddChild(bagNode);
                _loot[bagItem.Id] = bagNode;
                break;

            case BagClaimed claimed:
                GD.Print($"[Game {_elapsed:0.0}s] {claimed.Player} claimed a bag: {claimed.Points}p {claimed.Keys}K");
                if (_loot.TryGetValue(claimed.Bag, out var claimedBag)) claimedBag.Refresh();
                break;

            case RunEnded ended:
                GD.Print($"[Game {_elapsed:0.0}s] Run ended: {ended.Reason} — " +
                    string.Join(", ", ended.Results.Select(r => $"{r.Player} {r.Outcome} {r.Banked}p{(r.Winner ? " WINNER" : "")}")));
                _scoreboard.ShowResults();
                if (!_endStingPlayed)
                {
                    _endStingPlayed = true;
                    bool anyEscaped = ended.Results.Any(r => r.Outcome == RunOutcome.Escaped);
                    if (anyEscaped) Audio.Instance.PlayVictory();
                    else Audio.Instance.PlayDeath();
                }
                break;

            case RelicTaken relic when _characters.TryGetValue(relic.Player, out var lifter):
                GD.Print($"[Game {_elapsed:0.0}s] {relic.Player} lifted a RELIC worth {relic.Points}p");
                if (_loot.TryGetValue(relic.Relic, out var relicNode))
                {
                    Fx.RelicTaken(_world, relicNode.Position);
                    relicNode.Refresh();
                    Audio.Instance.PlaySfxAt(_world, "relic_lift", relicNode.Position, (float)_view.Config.Threat.TakeRelic);
                }
                lifter.CarryingRelic = true;
                _world.AddChild(new FloatingText().Init(lifter.GlobalPosition + new Vector2(0, -28), $"RELIC +{relic.Points}", Palette.White));
                _screens.Shake(2f);
                break;

            case PlayerExtracted escaped when _characters.TryGetValue(escaped.Player, out var escapee):
                GD.Print($"[Game {_elapsed:0.0}s] {escaped.Player} ESCAPED with {escaped.Banked}p");
                _world.AddChild(new FloatingText().Init(escapee.GlobalPosition + new Vector2(0, -22), $"ESCAPED  {escaped.Banked}p", Palette.White));
                Fx.Escaped(_world, escapee.GlobalPosition);
                escapee.SetEscaped();
                Audio.Instance.PlaySfxAt(_world, "escape", escapee.GlobalPosition, volume: 1.1f);
                break;

            case CollapseStarted collapse:
                GD.Print($"[Game {_elapsed:0.0}s] COLLAPSE: {collapse.Reason}");
                _banner.Show(collapse.Reason.ToUpperInvariant(), Palette.White, 4);
                if (_rooms.TryGetValue(_view.Dungeon.Entrance, out var lobby)) lobby.Collapsing = true;
                _screens.Shake(4f);
                Audio.Instance.PlayCollapse();
                break;

            case CardTaken taken:
                GD.Print($"[Game {_elapsed:0.0}s] {taken.Player} took card {taken.Card} for {taken.Price}p");
                if (_loot.TryGetValue(taken.Offer, out var offer)) offer.Refresh();
                break;

            case ThreatTierChanged tier:
                var config = _view.Config.Threat;
                GD.Print($"[Game {_elapsed:0.0}s] Threat tier {tier.Tier}: {config.TierName(tier.Tier)}");
                _banner.Show($"THREAT RISES  —  {config.TierName(tier.Tier).ToUpperInvariant()}",
                    ThreatHud.TierColor(tier.Tier, config.TierThresholds.Count));
                Audio.Instance.PlaySfx("tier_rise", 0.9f + tier.Tier * 0.08f);
                Audio.Instance.SetThreatTier(tier.Tier);
                break;
        }
    }

    private void OnPlayerNotified(PlayerId player, string message) => _hands.ShowMessage(player, message);

    private IReadOnlyList<HandHud.LocalHand> LocalHands() =>
        _characters.Values
            .Where(c => c.IsLocal && c.IsInside && _view.Results is null)
            .OrderBy(c => c.ColorIndex)
            .Select(c => new HandHud.LocalHand(c.Id, c.ColorIndex, c.Device!))
            .ToList();

    private void OnPlayerState(PlayerId player, Vector2 position, Vector2 velocity, bool sprinting)
    {
        if (_characters.TryGetValue(player, out var character))
            character.ApplyRemoteState(position, velocity, sprinting);
    }

    private void OnRosterChanged()
    {
        // Remove characters of players who disconnected.
        var present = _net.Roster.Select(r => new PlayerId(r.PlayerId)).ToHashSet();
        foreach (var id in _characters.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _characters[id].QueueFree();
            _characters.Remove(id);
        }
    }

    /// <summary>
    /// Sound falloff by rooms: full in a room with one of this machine's players, ~40% next door, ~15% two rooms
    /// away, a faint echo beyond. Spectators hear from the players still inside.
    /// </summary>
    private float HearingAt(Vector2 position)
    {
        var listeners = _characters.Values.Where(c => c.IsLocal && c.IsInside).ToList();
        if (listeners.Count == 0) listeners = _characters.Values.Where(c => c.IsInside).ToList();
        if (listeners.Count == 0) return 1f;

        var room = WorldLayout.RoomAt(0, position);
        int rooms = listeners.Min(c =>
        {
            var at = WorldLayout.RoomAt(0, c.GlobalPosition);
            return Math.Abs(at.X - room.X) + Math.Abs(at.Y - room.Y);
        });
        return rooms switch { 0 => 1f, 1 => 0.4f, 2 => 0.15f, _ => 0.05f };
    }
}
