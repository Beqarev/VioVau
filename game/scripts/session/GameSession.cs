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
    private CollapseHud _collapseHud = null!;
    private PauseMenu _pause = null!;
    private Label _info = null!;
    private double _elapsed;
    private double _endedFor;
    private bool _endStingPlayed;
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
        Audio.Instance.Hearing = HearingAt;
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
        if (Audio.Instance.Hearing == HearingAt) Audio.Instance.Hearing = null;
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

        if (_view.CollapseSeconds is not null && _view.Results is null && !double.IsNaN(_collapseHud.Remaining))
            Audio.Instance.TickCollapse(_collapseHud.Remaining);
        Audio.Instance.SetWardenPresent(_enemies.Values.Any(e => e.Kind == EnemyKind.Warden));

        _info.Text = $"{(_net.IsHost ? "HOST" : "CLIENT")} · seed {_view.Seed} · Esc: pause";
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

        // Esc is handled by PauseMenu (Resume / Settings / Leave run).
    }

    private void SaveScreenshot(double time)
    {
        var dir = DevOptions.ScreenshotDir;
        DirAccess.MakeDirRecursiveAbsolute(dir);
        var path = $"{dir}/{(_net.IsHost ? "host" : "client")}_{time:0.#}s.png";
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"[Dev] Screenshot {path}: {error}");
    }
}
