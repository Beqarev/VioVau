using VioVau.Core.Dungeon;
using VioVau.Core.Random;

namespace VioVau.Core;

public enum RunPhase
{
    Lobby,
    InRun,
    Ended,
}

public sealed class PlayerState
{
    private readonly HashSet<RoomId> _visited = new();

    public PlayerId Id { get; }
    public string DisplayName { get; }

    /// <summary>The room the player is in. Only set once the run has started.</summary>
    public RoomId? Room { get; private set; }

    public IReadOnlySet<RoomId> VisitedRooms => _visited;

    public PlayerState(PlayerId id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    /// <returns>True if this is the player's first visit to the room.</returns>
    internal bool EnterRoom(RoomId room)
    {
        Room = room;
        return _visited.Add(room);
    }
}

/// <summary>
/// The whole run. The only way to change it is <see cref="Apply"/> (player intents) and
/// <see cref="Tick"/> (time-based rules). Runs on the host; clients follow along with a <see cref="RunView"/>.
/// </summary>
public sealed class RunState
{
    public const int MaxPlayers = 4;

    private readonly Dictionary<PlayerId, PlayerState> _players = new();
    private readonly List<PlayerId> _joinOrder = new();
    private readonly HashSet<RoomId> _revealed = new();

    public RngStreams Rng { get; }
    public DungeonConfig Config { get; }
    public RunPhase Phase { get; private set; } = RunPhase.Lobby;
    public TimeSpan Elapsed { get; private set; } = TimeSpan.Zero;

    /// <summary>Generated when the run starts.</summary>
    public DungeonMap? Dungeon { get; private set; }

    /// <summary>Players in the order they joined.</summary>
    public IReadOnlyList<PlayerState> Players => _joinOrder.Select(id => _players[id]).ToList();

    public IReadOnlySet<RoomId> RevealedRooms => _revealed;

    public RunState(ulong seed, DungeonConfig? config = null)
    {
        Rng = new RngStreams(seed);
        Config = config ?? DungeonConfig.Default;
        Config.Validate();
    }

    public PlayerState? GetPlayer(PlayerId id) => _players.GetValueOrDefault(id);

    public CommandResult Apply(ICommand command) => command switch
    {
        JoinPlayer c => Join(c),
        LeavePlayer c => Leave(c),
        StartRun => Start(),
        MovePlayerToRoom c => Move(c),
        _ => CommandResult.Reject($"Unknown command {command.GetType().Name}."),
    };

    /// <summary>Advances time-based rules. Only runs while a run is in progress.</summary>
    public IReadOnlyList<IGameEvent> Tick(TimeSpan dt)
    {
        if (dt < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(dt), "Time cannot go backwards.");
        if (Phase != RunPhase.InRun) return Array.Empty<IGameEvent>();

        Elapsed += dt;
        return Array.Empty<IGameEvent>();
    }

    private CommandResult Join(JoinPlayer c)
    {
        if (Phase != RunPhase.Lobby) return CommandResult.Reject("Players can only join in the lobby.");
        if (_players.ContainsKey(c.Player)) return CommandResult.Reject($"{c.Player} has already joined.");
        if (_players.Count >= MaxPlayers) return CommandResult.Reject($"The run is full ({MaxPlayers} players).");
        if (string.IsNullOrWhiteSpace(c.DisplayName)) return CommandResult.Reject("Display name cannot be empty.");

        var name = c.DisplayName.Trim();
        _players[c.Player] = new PlayerState(c.Player, name);
        _joinOrder.Add(c.Player);
        return CommandResult.Ok(new PlayerJoined(c.Player, name));
    }

    private CommandResult Leave(LeavePlayer c)
    {
        if (Phase != RunPhase.Lobby) return CommandResult.Reject("Leaving mid-run is not supported yet.");
        if (!_players.Remove(c.Player)) return CommandResult.Reject($"{c.Player} is not in the run.");

        _joinOrder.Remove(c.Player);
        return CommandResult.Ok(new PlayerLeft(c.Player));
    }

    private CommandResult Start()
    {
        if (Phase != RunPhase.Lobby) return CommandResult.Reject("The run has already started.");
        if (_players.Count == 0) return CommandResult.Reject("At least one player is needed to start.");

        Dungeon = DungeonGenerator.Generate(Rng.Seed, Config);
        Phase = RunPhase.InRun;

        var events = new List<IGameEvent>
        {
            new RunStarted(Rng.Seed, Players.Select(p => new PlayerInfo(p.Id, p.DisplayName)).ToList(), Config),
        };
        foreach (var player in Players)
            events.Add(new PlayerEnteredRoom(player.Id, Dungeon.Entrance, player.EnterRoom(Dungeon.Entrance)));
        events.AddRange(Reveal(Dungeon.Entrance));

        return CommandResult.Ok(events);
    }

    private CommandResult Move(MovePlayerToRoom c)
    {
        if (Phase != RunPhase.InRun || Dungeon is null) return CommandResult.Reject("Players can only move during a run.");
        if (GetPlayer(c.Player) is not { Room: { } current } player) return CommandResult.Reject($"{c.Player} is not in the run.");
        if (!Dungeon.Contains(c.Target)) return CommandResult.Reject($"{c.Target} is outside the dungeon.");
        if (current == c.Target) return CommandResult.Reject($"{c.Player} is already in {c.Target}.");

        if (DirectionExtensions.Between(current, c.Target) is not { } dir)
            return CommandResult.Reject($"{c.Target} is not adjacent to {current}.");
        if (!Dungeon.GetRoom(current).IsPassable(dir))
            return CommandResult.Reject($"The {dir} exit of {current} is not passable.");

        var events = new List<IGameEvent> { new PlayerEnteredRoom(c.Player, c.Target, player.EnterRoom(c.Target)) };
        events.AddRange(Reveal(c.Target));
        return CommandResult.Ok(events);
    }

    private IEnumerable<IGameEvent> Reveal(RoomId room)
    {
        if (_revealed.Add(room)) yield return new RoomRevealed(room);
    }
}
