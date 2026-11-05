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
    public PlayerId Id { get; }
    public string DisplayName { get; }

    public PlayerState(PlayerId id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }
}

/// <summary>
/// The whole run. The only way to change it is <see cref="Apply"/> (player intents) and
/// <see cref="Tick"/> (time-based rules). Runs on the host; clients mirror it from events.
/// </summary>
public sealed class RunState
{
    public const int MaxPlayers = 4;

    private readonly Dictionary<PlayerId, PlayerState> _players = new();
    private readonly List<PlayerId> _joinOrder = new();

    public RngStreams Rng { get; }
    public RunPhase Phase { get; private set; } = RunPhase.Lobby;
    public TimeSpan Elapsed { get; private set; } = TimeSpan.Zero;

    /// <summary>Players in the order they joined.</summary>
    public IReadOnlyList<PlayerState> Players => _joinOrder.Select(id => _players[id]).ToList();

    public RunState(ulong seed)
    {
        Rng = new RngStreams(seed);
    }

    public PlayerState? GetPlayer(PlayerId id) => _players.GetValueOrDefault(id);

    public CommandResult Apply(ICommand command) => command switch
    {
        JoinPlayer c => Join(c),
        LeavePlayer c => Leave(c),
        StartRun => Start(),
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

        Phase = RunPhase.InRun;
        return CommandResult.Ok(new RunStarted(Rng.Seed, _joinOrder.ToList()));
    }
}
