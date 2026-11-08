using Godot;
using VioVau.Core;
using VioVau.Core.Dungeon;
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
public partial class GameSession : Node
{
    private static readonly Vector2[] SpawnOffsets = [new(-24, 0), new(24, 0), new(0, -24), new(0, 24)];

    /// <summary>Reach for chests / locked doors / walking over keys, in pixels.</summary>
    private const float ChestRange = 20f, DoorRange = 28f, KeyRange = 12f;

    /// <summary>Extra reach the host allows for remote players (their position is ~1 network tick old).</summary>
    private const float RemoteTolerance = 12f;

    private const double SprintReportInterval = 0.25;

    private readonly Dictionary<RoomId, RoomNode> _rooms = new();
    private readonly Dictionary<EntityId, LootNode> _loot = new();
    private readonly Dictionary<PlayerId, PlayerCharacter> _characters = new();
    private readonly Dictionary<PlayerId, RoomId> _rejectedMoves = new();
    private readonly Dictionary<PlayerId, double> _sprintSeconds = new();
    private double _sprintReportTimer;

    private NetSession _net = null!;
    private RunView _view = null!;
    private Node2D _world = null!;
    private SplitScreen _screens = null!;
    private TierBanner _banner = null!;
    private Label _info = null!;
    private double _elapsed;
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
    }

    public override void _ExitTree()
    {
        if (_net is null) return;
        _net.EventReceived -= OnEvent;
        _net.PlayerStateReceived -= OnPlayerState;
        _net.RosterChanged -= OnRosterChanged;
        _net.InteractRequested -= OnInteractRequested;
    }

    // ── Setup ───────────────────────────────────────────────────────────────

    private void BuildWorld()
    {
        _world = new Node2D { Name = "World" };
        AddChild(_world);

        foreach (var room in _view.Dungeon.Floors[0].Rooms)
        {
            var node = new RoomNode().Init(room, _view.IsRevealed(room.Id));
            _world.AddChild(node);
            _rooms[room.Id] = node;
        }

        var loot = new Node2D { Name = "Loot" };
        _world.AddChild(loot);
        foreach (var item in _view.Dungeon.Loot)
        {
            var node = new LootNode().Init(item, fancy: _view.Dungeon.GetRoom(item.Room).Type == RoomType.Vault);
            loot.AddChild(node);
            _loot[item.Id] = node;
        }

        var players = new Node2D { Name = "Players" };
        _world.AddChild(players);

        var spawnCenter = WorldLayout.RoomCenter(_view.Dungeon.Entrance);
        for (int i = 0; i < _view.Players.Count; i++)
        {
            var info = _view.Players[i];
            var entry = _net.Roster.FirstOrDefault(r => r.PlayerId == info.Id.Value);
            var device = entry is not null && entry.PeerId == _net.LocalPeerId ? LocalPlayers.Get(entry.LocalSlot) : null;

            PlayerCharacter character = null!;
            if (DevOptions.Autopilot && device is not null && entry!.LocalSlot == 0)
                device = new AutopilotDevice(_view, info.Id, () => character.GlobalPosition);

            character = new PlayerCharacter().Init(info.Id, info.DisplayName, i, device, spawnCenter + SpawnOffsets[i % SpawnOffsets.Length]);
            character.InteractPressed += OnLocalInteractPressed;
            players.AddChild(character);
            _characters[info.Id] = character;
        }
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

        _info = new Label { Position = new Vector2(4, 256) };
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
            .Where(c => c.IsLocal)
            .Select(c => WorldLayout.RoomAt(0, c.GlobalPosition))
            .Where(_view.Dungeon.Contains)
            .Distinct()
            .ToList();
        if (rooms.Count == 0) rooms.Add(_view.Dungeon.Entrance);
        _screens.ShowRooms(rooms);

        UpdateHighlights();

        _elapsed += delta;
        if (_screenshotTimes.Count > 0 && _elapsed >= _screenshotTimes.Peek()) SaveScreenshot(_screenshotTimes.Dequeue());

        _info.Text = $"{(_net.IsHost ? "HOST" : "CLIENT")}  ·  {_characters.Count} players  ·  seed {_view.Seed % 100000}  ·  Esc: leave";
    }

    /// <summary>Light up whatever each local player would interact with, and show their button above them.</summary>
    private void UpdateHighlights()
    {
        foreach (var loot in _loot.Values) loot.Highlighted = false;
        foreach (var room in _rooms.Values) room.HighlightedDoor = null;

        foreach (var character in _characters.Values.Where(c => c.IsLocal))
        {
            var target = FindTarget(character.GlobalPosition, 0f);
            character.Prompt = target is null ? null : character.Device!.InteractLabel;

            switch (target)
            {
                case { Kind: InteractKind.Chest } chest when _loot.TryGetValue(new EntityId(chest.Value), out var node):
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

        foreach (var character in _characters.Values)
        {
            DetectRoomChange(character);
            PickUpTouchedKeys(character);
            if (character.IsSprinting) _sprintSeconds[character.Id] = _sprintSeconds.GetValueOrDefault(character.Id) + delta;
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
        if (FindTarget(character.AuthoritativePosition, tolerance, target) is null)
        {
            GD.Print($"[Game] Ignored {player} → {target}: out of reach or already used.");
            return;
        }

        ICommand command = target.Kind switch
        {
            InteractKind.Chest => new OpenChest(player, new EntityId(target.Value)),
            _ => new UnlockExit(player, (Direction)target.Value),
        };
        var result = _net.ApplyCommand(command);
        if (!result.Accepted) GD.Print($"[Game] {player} could not {command.GetType().Name}: {result.RejectionReason}");
    }

    // ── Interaction targeting (shared by prompts and host validation) ───────

    private void OnLocalInteractPressed(PlayerCharacter character)
    {
        if (FindTarget(character.GlobalPosition, 0f) is { } target)
            _net.RequestInteract(character.Id, target);
    }

    /// <summary>
    /// The nearest usable thing within reach of a position: an unopened chest, or a locked door of the room
    /// it's in. With <paramref name="only"/>, checks just that target.
    /// </summary>
    private InteractTarget? FindTarget(Vector2 position, float tolerance, InteractTarget? only = null)
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

        foreach (var chest in _view.Dungeon.LootIn(room).Where(l => l.Kind == LootKind.Chest && !l.Claimed))
            Consider(new InteractTarget(InteractKind.Chest, chest.Id.Value), WorldLayout.LootPosition(room, chest.Position), ChestRange);

        var roomData = _view.Dungeon.GetRoom(room);
        foreach (var side in DirectionExtensions.All.Where(d => roomData.GetExit(d) == ExitState.Locked))
            Consider(new InteractTarget(InteractKind.Door, (int)side), WorldLayout.DoorCenter(room, side), DoorRange);

        return best;
    }

    // ── Events ──────────────────────────────────────────────────────────────

    private void OnEvent(IGameEvent e)
    {
        switch (e)
        {
            case PlayerEnteredRoom entered:
                GD.Print($"[Game] {entered.Player} entered {entered.Room}{(entered.FirstVisit ? " (first visit)" : "")}");
                break;

            case RoomRevealed revealed when _rooms.TryGetValue(revealed.Room, out var room):
                room.Revealed = true;
                break;

            case ChestOpened opened:
                GD.Print($"[Game] {opened.Player} opened {opened.Chest} for {opened.Points}p");
                if (_loot.TryGetValue(opened.Chest, out var chest)) chest.Refresh();
                break;

            case KeyPickedUp picked:
                GD.Print($"[Game] {picked.Player} picked up key {picked.Key}");
                if (_loot.TryGetValue(picked.Key, out var key)) key.Refresh();
                break;

            case ExitUnlocked unlocked:
                GD.Print($"[Game] {unlocked.Player} unlocked {unlocked.Side} of {unlocked.Room}");
                if (_rooms.TryGetValue(unlocked.Room, out var near)) near.RefreshExits();
                if (_view.Dungeon.Neighbor(unlocked.Room, unlocked.Side) is { } far && _rooms.TryGetValue(far.Id, out var farNode))
                    farNode.RefreshExits();
                break;

            case NoiseMade noise when _characters.TryGetValue(noise.Player, out var maker):
                _world.AddChild(new NoiseRing().Init(maker.GlobalPosition, noise.Amount));
                break;

            case ThreatTierChanged tier:
                var config = _view.Config.Threat;
                GD.Print($"[Game] Threat tier {tier.Tier}: {config.TierName(tier.Tier)}");
                _banner.Show($"THREAT RISES  —  {config.TierName(tier.Tier).ToUpperInvariant()}",
                    ThreatHud.TierColor(tier.Tier, config.TierThresholds.Count));
                break;
        }
    }

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

    private IEnumerable<Minimap.Marker> MinimapMarkers() =>
        _characters.Values
            .Select(c => new Minimap.Marker(WorldLayout.RoomAt(0, c.GlobalPosition), c.ColorIndex))
            .Where(m => _view.Dungeon.Contains(m.Room));
}
