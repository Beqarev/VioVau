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

/// <summary>
/// The in-run scene. Builds the world from the <see cref="RunView"/>, spawns characters, drives the cameras
/// and HUD, and — on the host — turns physical facts (crossing into a room, sprinting, standing next to a chest)
/// into Core commands.
/// </summary>
public partial class GameSession : Node, IEnemyWorld
{
    private static readonly Vector2[] SpawnOffsets = [new(-24, 0), new(24, 0), new(0, -24), new(0, 24)];

    /// <summary>Reach for chests / card offers / locked doors / walking over keys / beside a player, in pixels.</summary>
    private const float ChestRange = 20f, DoorRange = 28f, KeyRange = 12f, NearbyPlayerRange = 28f;

    /// <summary>Extra reach the host allows for remote players (their position is ~1 network tick old).</summary>
    private const float RemoteTolerance = 12f;

    private const double SprintReportInterval = 0.25;
    private const double EnemySyncInterval = 1.0 / 15.0;
    private const float ReviveRange = 22f, NearbyEnemyRange = 30f, ExtractRange = 34f;
    private const double ReviveSeconds = 1.5;

    private readonly Dictionary<RoomId, RoomNode> _rooms = new();
    private readonly Dictionary<EntityId, LootNode> _loot = new();
    private readonly Dictionary<PlayerId, PlayerCharacter> _characters = new();
    private readonly Dictionary<PlayerId, RoomId> _rejectedMoves = new();
    private readonly Dictionary<PlayerId, double> _sprintSeconds = new();
    private readonly Dictionary<EnemyId, EnemyNode> _enemies = new();
    private Node2D _enemyLayer = null!;
    private Node2D _lootLayer = null!;
    private double _enemySyncTimer;

    /// <summary>Host: revives in progress, by downed player → (reviver, seconds so far).</summary>
    private readonly Dictionary<PlayerId, (PlayerId Reviver, double Time)> _revives = new();
    private double _sprintReportTimer;

    private NetSession _net = null!;
    private RunView _view = null!;
    private Node2D _world = null!;
    private CanvasModulate _ambient = null!;
    private SplitScreen _screens = null!;
    private TierBanner _banner = null!;
    private HandHud _hands = null!;
    private Scoreboard _scoreboard = null!;
    private Label _info = null!;
    private double _elapsed;
    private double _endedFor;
    private readonly Queue<double> _screenshotTimes = new(DevOptions.ScreenshotTimes.Order());

    public override void _Ready()
    {
        _net = NetSession.Instance;
        if (_net.View is null)
        {
            _net.Leave();
            return;
        }
        _view = _net.View;

        BuildWorld();
        BuildUi();

        _net.EventReceived += OnEvent;
        _net.PlayerStateReceived += OnPlayerState;
        _net.RosterChanged += OnRosterChanged;
        _net.InteractRequested += OnInteractRequested;
        _net.PlayCardRequested += OnPlayCardRequested;
        _net.PlayerNotified += OnPlayerNotified;
        _net.EnemyStatesReceived += OnEnemyStates;
        _net.ReviveProgress += OnReviveProgress;
    }

    public override void _ExitTree()
    {
        if (_net is null) return;
        _net.EventReceived -= OnEvent;
        _net.PlayerStateReceived -= OnPlayerState;
        _net.RosterChanged -= OnRosterChanged;
        _net.InteractRequested -= OnInteractRequested;
        _net.PlayCardRequested -= OnPlayCardRequested;
        _net.PlayerNotified -= OnPlayerNotified;
        _net.EnemyStatesReceived -= OnEnemyStates;
        _net.ReviveProgress -= OnReviveProgress;
    }

    // ── Setup ───────────────────────────────────────────────────────────────

    private void BuildWorld()
    {
        _world = new Node2D { Name = "World" };
        AddChild(_world);

        // Darkness: the vault is lit only by lanterns, crystals and treasure; it gets darker as the Threat rises.
        _ambient = new CanvasModulate { Color = Lights.Ambient(0, _view.Config.Threat.TierThresholds.Count) };
        _world.AddChild(_ambient);

        foreach (var room in _view.Dungeon.Floors[0].Rooms)
        {
            var node = new RoomNode().Init(room, _view.IsRevealed(room.Id));
            _world.AddChild(node);
            _rooms[room.Id] = node;
        }

        var loot = new Node2D { Name = "Loot" };
        _lootLayer = loot;
        _world.AddChild(loot);
        foreach (var item in _view.Dungeon.Loot)
        {
            var card = item.Card is { } id ? _view.Config.CardSet.Get(id) : null;
            var node = new LootNode().Init(item, fancy: _view.Dungeon.GetRoom(item.Room).Type == RoomType.Vault, card);
            loot.AddChild(node);
            _loot[item.Id] = node;
        }

        _enemyLayer = new Node2D { Name = "Enemies" };
        _world.AddChild(_enemyLayer);

        var players = new Node2D { Name = "Players" };
        _world.AddChild(players);

        var spawnCenter = WorldLayout.RoomCenter(_view.Dungeon.Entrance);
        for (int i = 0; i < _view.Players.Count; i++)
        {
            var info = _view.Players[i];
            var entry = _net.Roster.FirstOrDefault(r => r.PlayerId == info.Id.Value);
            var device = entry is not null && entry.PeerId == _net.LocalPeerId ? LocalPlayers.Get(entry.LocalSlot) : null;

            PlayerCharacter character = null!;
            if (DevOptions.Autopilot && device is not null && (entry!.LocalSlot == 0 || DevOptions.AutopilotAll))
                device = new AutopilotDevice(_view, info.Id, () => character.GlobalPosition,
                    id => _characters.TryGetValue(id, out var c) && c.IsInside ? c.GlobalPosition : null,
                    at => _enemies.Values.Select(e => (Vector2?)e.GlobalPosition).OrderBy(p => p!.Value.DistanceTo(at)).FirstOrDefault());

            character = new PlayerCharacter().Init(info.Id, info.DisplayName, i, device, spawnCenter + SpawnOffsets[i % SpawnOffsets.Length]);
            character.InteractPressed += OnLocalInteractPressed;
            character.CardPressed += (c, slot) => _net.RequestPlayCard(c.Id, slot);
            players.AddChild(character);
            _characters[info.Id] = character;
        }

        // Enemies spawned at run start arrived before this scene existed; the view remembers them.
        foreach (var spawned in _view.Enemies.Values) SpawnEnemy(spawned);
    }

    private void BuildUi()
    {
        var screensLayer = new CanvasLayer { Name = "Screens", Layer = 1 };
        AddChild(screensLayer);
        _screens = new SplitScreen().Init(GetViewport().World2D);
        screensLayer.AddChild(_screens);

        var hud = new CanvasLayer { Name = "Hud", Layer = 2 };
        AddChild(hud);
        hud.AddChild(new Minimap().Init(_view, MinimapMarkers));
        hud.AddChild(new ThreatHud().Init(_view));
        _banner = new TierBanner();
        hud.AddChild(_banner);
        _hands = new HandHud().Init(_view, LocalHands);
        hud.AddChild(_hands);
        hud.AddChild(new CollapseHud().Init(_view));
        _scoreboard = new Scoreboard().Init(_view, _net.IsHost);
        hud.AddChild(_scoreboard);

        _info = new Label { Position = new Vector2(196, 1) }; // top centre; the bottom-left belongs to the hand
        _info.AddThemeFontSizeOverride("font_size", 8);
        _info.AddThemeColorOverride("font_color", Palette.Dim);
        hud.AddChild(_info);
    }

    // ── Per frame ───────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_view is null) return;

        // One view per room containing a local player; with no local players (spectating), watch the Lobby.
        var rooms = _characters.Values
            .Where(c => c.IsLocal && c.IsInside)
            .Select(c => WorldLayout.RoomAt(0, c.GlobalPosition))
            .Where(_view.Dungeon.Contains)
            .Distinct()
            .ToList();
        // All local players dead (or none): spectate a teammate who's still in, else the Lobby.
        if (rooms.Count == 0 && _characters.Values.FirstOrDefault(c => c.IsInside) is { } survivor)
            rooms.Add(WorldLayout.RoomAt(0, survivor.GlobalPosition));
        if (rooms.Count == 0) rooms.Add(_view.Dungeon.Entrance);
        _screens.ShowRooms(rooms);
        foreach (var (id, node) in _rooms) node.OnScreen = rooms.Contains(id);

        UpdateHighlights();

        var ambient = Lights.Ambient(_view.ThreatTier, _view.Config.Threat.TierThresholds.Count);
        if (_view.CollapseSeconds is not null && _view.Results is null)
            ambient = ambient.Lerp(new Color(0.32f, 0.2f, 0.4f), 0.5f + 0.5f * Mathf.Sin((float)_elapsed * 5f)); // the vault shudders
        _ambient.Color = _ambient.Color.Lerp(ambient, 1f - Mathf.Exp(-3f * (float)delta));

        if (_view.Results is not null && _net.IsHost && DevOptions.AutoRematch)
        {
            _endedFor += delta;
            if (_endedFor > 3 && _endedFor - delta <= 3) _net.Rematch();
        }

        _elapsed += delta;
        if (_screenshotTimes.Count > 0 && _elapsed >= _screenshotTimes.Peek()) SaveScreenshot(_screenshotTimes.Dequeue());

        _info.Text = $"{(_net.IsHost ? "HOST" : "CLIENT")} · seed {_view.Seed} · Esc to leave";
        // Only at the start: afterwards it would cover whoever stands in the north doorway.
        _info.Modulate = new Color(1, 1, 1, (float)Math.Clamp(6 - _elapsed, 0, 1));
    }

    /// <summary>Light up whatever each local player would interact with, and show their button above them.</summary>
    private void UpdateHighlights()
    {
        foreach (var loot in _loot.Values) loot.Highlighted = false;
        foreach (var room in _rooms.Values) room.HighlightedDoor = null;

        foreach (var character in _characters.Values.Where(c => c.IsLocal && c.IsActive))
        {
            var target = FindTarget(character.GlobalPosition, 0f, self: character.Id);
            character.Prompt = target is null ? null : character.Device!.InteractLabel;

            switch (target)
            {
                case { Kind: InteractKind.Chest or InteractKind.Card or InteractKind.Bag or InteractKind.Relic } loot when _loot.TryGetValue(new EntityId(loot.Value), out var node):
                    node.Highlighted = true;
                    break;
                case { Kind: InteractKind.Door } door when _rooms.TryGetValue(WorldLayout.RoomAt(0, character.GlobalPosition), out var room):
                    room.HighlightedDoor = (Direction)door.Value;
                    break;
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_view is null || !_net.IsHost || _net.State is null) return;

        foreach (var character in _characters.Values.Where(c => c.IsInside))
        {
            DetectRoomChange(character);
            if (character.IsActive) PickUpTouchedKeys(character);
            if (character.IsSprinting && character.IsActive) _sprintSeconds[character.Id] = _sprintSeconds.GetValueOrDefault(character.Id) + delta;
        }

        TickRevives(delta);

        _enemySyncTimer += delta;
        if (_enemySyncTimer >= EnemySyncInterval && _enemies.Count > 0)
        {
            _enemySyncTimer = 0;
            var list = _enemies.Values.ToList();
            _net.SendEnemyStates(list.Select(e => e.Id.Value).ToArray(), list.Select(e => e.GlobalPosition).ToArray(), list.Select(e => e.PackedState).ToArray());
        }

        _sprintReportTimer += delta;
        if (_sprintReportTimer >= SprintReportInterval)
        {
            _sprintReportTimer = 0;
            foreach (var (player, seconds) in _sprintSeconds.Where(kv => kv.Value > 0).ToList())
            {
                _net.ApplyCommand(new ReportSprint(player, TimeSpan.FromSeconds(Math.Min(seconds, RunState.MaxSprintReport.TotalSeconds))));
                _sprintSeconds[player] = 0;
            }
        }
    }

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
                }
                break;

            case KeyPickedUp picked:
                GD.Print($"[Game {_elapsed:0.0}s] {picked.Player} picked up key {picked.Key}");
                if (_loot.TryGetValue(picked.Key, out var key))
                {
                    Fx.KeyTaken(_world, key.Position);
                    key.Refresh();
                }
                break;

            case ExitUnlocked unlocked:
                GD.Print($"[Game {_elapsed:0.0}s] {unlocked.Player} unlocked {unlocked.Side} of {unlocked.Room}");
                if (_rooms.TryGetValue(unlocked.Room, out var near)) near.RefreshExits();
                Fx.DoorUnlocked(_world, WorldLayout.DoorCenter(unlocked.Room, unlocked.Side));
                if (_view.Dungeon.Neighbor(unlocked.Room, unlocked.Side) is { } far && _rooms.TryGetValue(far.Id, out var farNode))
                    farNode.RefreshExits();
                break;

            case NoiseMade noise when _characters.TryGetValue(noise.Player, out var maker):
                _world.AddChild(new NoiseRing().Init(maker.GlobalPosition, noise.Amount));
                break;

            case CardPlayed played when _characters.TryGetValue(played.Player, out var player):
                var name = _view.Config.CardSet.Get(played.Card)?.Name ?? played.Card.Value;
                GD.Print($"[Game {_elapsed:0.0}s] {played.Player} played {name}");
                _world.AddChild(new FloatingText().Init(player.GlobalPosition + new Vector2(0, -22), name.ToUpperInvariant(),
                    _view.Config.CardSet.Get(played.Card) is { } def ? CardStyle.CategoryColor(def.Category) : Palette.White));
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
                if (hurt.IsLocal) _screens.Shake(3f);
                break;

            case PlayerDowned downed when _characters.TryGetValue(downed.Player, out var fallen):
                GD.Print($"[Game {_elapsed:0.0}s] {downed.Player} is DOWN");
                fallen.SetDowned(true);
                _world.AddChild(new FloatingText().Init(fallen.GlobalPosition + new Vector2(0, -22), "DOWN!", Palette.White));
                break;

            case PlayerRevived revived when _characters.TryGetValue(revived.Player, out var risen):
                GD.Print($"[Game {_elapsed:0.0}s] {revived.By} revived {revived.Player}");
                risen.SetDowned(false);
                _world.AddChild(new FloatingText().Init(risen.GlobalPosition + new Vector2(0, -22), "BACK UP", Palette.Accent));
                break;

            case PlayerDied died when _characters.TryGetValue(died.Player, out var gone):
                gone.CarryingRelic = false;
                GD.Print($"[Game {_elapsed:0.0}s] {died.Player} DIED");
                _world.AddChild(new FloatingText().Init(gone.GlobalPosition + new Vector2(0, -22), $"{gone.DisplayName.ToUpperInvariant()} FELL", Palette.White));
                gone.SetDead();
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
                break;

            case RelicTaken relic when _characters.TryGetValue(relic.Player, out var lifter):
                GD.Print($"[Game {_elapsed:0.0}s] {relic.Player} lifted a RELIC worth {relic.Points}p");
                if (_loot.TryGetValue(relic.Relic, out var relicNode))
                {
                    Fx.RelicTaken(_world, relicNode.Position);
                    relicNode.Refresh();
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
                break;

            case CollapseStarted collapse:
                GD.Print($"[Game {_elapsed:0.0}s] COLLAPSE: {collapse.Reason}");
                _banner.Show(collapse.Reason.ToUpperInvariant(), Palette.White, 4);
                if (_rooms.TryGetValue(_view.Dungeon.Entrance, out var lobby)) lobby.Collapsing = true;
                _screens.Shake(4f);
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

    // ── Misc ────────────────────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent e)
    {
        // After the run: the host starts the next one.
        if (_view?.Results is not null && _net.IsHost &&
            e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Enter or Key.KpEnter } or InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.Start })
        {
            GetViewport().SetInputAsHandled();
            _net.Rematch();
            return;
        }

        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            _net.Leave();
        }
    }

    private void SaveScreenshot(double time)
    {
        var dir = DevOptions.ScreenshotDir;
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var path = $"{dir}/{(_net.IsHost ? "host" : "client")}_{time:0.#}s.png";
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[Dev] Screenshot {path}: {error}");
    }

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

    // ── Enemies ─────────────────────────────────────────────────────────────

    private void SpawnEnemy(EnemySpawned spawned)
    {
        if (_enemies.ContainsKey(spawned.Enemy)) return;
        var position = WorldLayout.LootPosition(spawned.Room, spawned.At);
        var target = spawned.Target is { } t ? _characters.GetValueOrDefault(t) : null;
        var node = new EnemyNode().Init(spawned.Enemy, spawned.Kind, position, _view.Config.EnemySet.Ai(spawned.Kind),
            _net.IsHost ? this : null, target);
        _enemyLayer.AddChild(node);
        _enemies[spawned.Enemy] = node;

        if (spawned.Target is { } strikeTarget)
        {
            GD.Print($"[Game {_elapsed:0.0}s] {spawned.Kind} {spawned.Enemy} sent after {strikeTarget} in {spawned.Room}");
            if (spawned.Kind == EnemyKind.Warden)
                _banner.Show("THE WARDEN AWAKENS", Palette.White, 3.5);
        }
    }

    private void OnEnemyStates(int[] ids, Vector2[] positions, int[] states)
    {
        for (int i = 0; i < ids.Length && i < positions.Length && i < states.Length; i++)
            if (_enemies.TryGetValue(new EnemyId(ids[i]), out var enemy)) enemy.ApplyRemoteState(positions[i], states[i]);
    }

    // IEnemyWorld (host only)
    public DungeonMap Map => _view.Dungeon;
    public IEnumerable<PlayerCharacter> ActivePlayers => _characters.Values.Where(c => c.IsActive);
    public PlayerCharacter? WardenPrey => _net.State?.Loudest() is { } prey ? _characters.GetValueOrDefault(prey.Id) : null;

    public void ReportHit(EnemyNode enemy, PlayerCharacter target)
    {
        var at = target.AuthoritativePosition;
        _net.ApplyCommand(new HitPlayer(target.Id, enemy.Id, WorldLayout.ToRoomPoint(WorldLayout.RoomAt(0, at), at)));
    }

    private IEnumerable<Minimap.Marker> MinimapMarkers() =>
        _characters.Values
            .Where(c => c.IsInside)
            .Select(c => new Minimap.Marker(WorldLayout.RoomAt(0, c.GlobalPosition), c.ColorIndex))
            .Where(m => _view.Dungeon.Contains(m.Room));
}
