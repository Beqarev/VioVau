using Godot;
using VioVau.Core;
using VioVau.Game.Dev;
using VioVau.Game.Entities;
using VioVau.Game.Input;
using VioVau.Game.Networking;
using VioVau.Game.UI;
using VioVau.Game.World;

namespace VioVau.Game.Session;

/// <summary>
/// The in-run scene. Builds the world from the <see cref="RunView"/>, spawns characters, drives the cameras,
/// and — on the host — turns "a player crossed into another room" into Core commands.
/// </summary>
public partial class GameSession : Node
{
    private static readonly Vector2[] SpawnOffsets = [new(-24, 0), new(24, 0), new(0, -24), new(0, 24)];

    private readonly Dictionary<RoomId, RoomNode> _rooms = new();
    private readonly Dictionary<PlayerId, PlayerCharacter> _characters = new();
    private readonly Dictionary<PlayerId, RoomId> _rejectedMoves = new();

    private NetSession _net = null!;
    private RunView _view = null!;
    private SplitScreen _screens = null!;
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
    }

    public override void _ExitTree()
    {
        if (_net is null) return;
        _net.EventReceived -= OnEvent;
        _net.PlayerStateReceived -= OnPlayerState;
        _net.RosterChanged -= OnRosterChanged;
    }

    private void BuildWorld()
    {
        var world = new Node2D { Name = "World" };
        AddChild(world);

        foreach (var room in _view.Dungeon.Floors[0].Rooms)
        {
            var node = new RoomNode().Init(room, _view.IsRevealed(room.Id));
            world.AddChild(node);
            _rooms[room.Id] = node;
        }

        var players = new Node2D { Name = "Players" };
        world.AddChild(players);

        var spawnCenter = WorldLayout.RoomCenter(_view.Dungeon.Entrance);
        for (int i = 0; i < _view.Players.Count; i++)
        {
            var info = _view.Players[i];
            var entry = _net.Roster.FirstOrDefault(r => r.PlayerId == info.Id.Value);
            var device = entry is not null && entry.PeerId == _net.LocalPeerId ? LocalPlayers.Get(entry.LocalSlot) : null;

            PlayerCharacter character = null!;
            if (DevOptions.Autopilot && device is not null && entry!.LocalSlot == 0)
                device = new AutopilotDevice(_view.Dungeon, () => character.GlobalPosition);

            character = new PlayerCharacter().Init(info.Id, info.DisplayName, i, device, spawnCenter + SpawnOffsets[i % SpawnOffsets.Length]);
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

        _info = new Label { Position = new Vector2(4, 256) };
        _info.AddThemeFontSizeOverride("font_size", 8);
        _info.AddThemeColorOverride("font_color", Palette.Dim);
        hud.AddChild(_info);
    }

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

        _elapsed += delta;
        if (_screenshotTimes.Count > 0 && _elapsed >= _screenshotTimes.Peek()) SaveScreenshot(_screenshotTimes.Dequeue());

        _info.Text = $"{(_net.IsHost ? "HOST" : "CLIENT")}  ·  {_characters.Count} players  ·  seed {_view.Seed % 100000}  ·  Esc: leave";
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_view is null || !_net.IsHost || _net.State is null) return;

        // Host: turn positions into room changes. Core validates them (no walking through walls).
        foreach (var character in _characters.Values)
        {
            if (_net.State.GetPlayer(character.Id)?.Room is not { } current) continue;

            var at = WorldLayout.RoomAt(0, character.AuthoritativePosition);
            if (at == current || !_view.Dungeon.Contains(at))
            {
                _rejectedMoves.Remove(character.Id);
                continue;
            }
            if (_rejectedMoves.TryGetValue(character.Id, out var rejected) && rejected == at) continue;

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
    }

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

    private void OnEvent(IGameEvent e)
    {
        if (e is PlayerEnteredRoom entered)
            GD.Print($"[Game] {entered.Player} entered {entered.Room}{(entered.FirstVisit ? " (first visit)" : "")}");
        if (e is RoomRevealed revealed && _rooms.TryGetValue(revealed.Room, out var room))
            room.Revealed = true;
    }

    private void OnPlayerState(PlayerId player, Vector2 position, Vector2 velocity)
    {
        if (_characters.TryGetValue(player, out var character))
            character.ApplyRemoteState(position, velocity);
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

    private IEnumerable<Minimap.Marker> MinimapMarkers() =>
        _characters.Values
            .Select(c => new Minimap.Marker(WorldLayout.RoomAt(0, c.GlobalPosition), c.ColorIndex))
            .Where(m => _view.Dungeon.Contains(m.Room));
}
