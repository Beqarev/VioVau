using VioVau.Core.Cards;
using VioVau.Core.Dungeon;
using VioVau.Core.Random;
using VioVau.Core.Threat;

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

    /// <summary>Points carried (at risk). Banked only by extracting.</summary>
    public int CarriedPoints { get; internal set; }

    public int Keys { get; internal set; }

    /// <summary>Created when the run starts.</summary>
    public PlayerDeck? Deck { get; internal set; }

    /// <summary>Seconds accumulated toward the next timed draw (only while the hand isn't full).</summary>
    internal double DrawTimer { get; set; }

    /// <summary>Run time until which this player makes no noise (Hush).</summary>
    public TimeSpan SilencedUntil { get; internal set; }

    /// <summary>Points multiplier for the next chest this player opens (Gilded Greed).</summary>
    public double NextChestMultiplier { get; internal set; } = 1;

    /// <summary>Card offers taken per room (Library limit).</summary>
    internal Dictionary<RoomId, int> OffersTaken { get; } = new();

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
public sealed partial class RunState
{
    public const int MaxPlayers = 4;

    /// <summary>Longest sprint a single report may claim (guards against bogus reports).</summary>
    public static readonly TimeSpan MaxSprintReport = TimeSpan.FromSeconds(1);

    private readonly Dictionary<PlayerId, PlayerState> _players = new();
    private readonly List<PlayerId> _joinOrder = new();
    private readonly HashSet<RoomId> _revealed = new();

    public RngStreams Rng { get; }
    public GameConfig Config { get; }
    public ThreatSystem Threat { get; }
    public RunPhase Phase { get; private set; } = RunPhase.Lobby;
    public TimeSpan Elapsed { get; private set; } = TimeSpan.Zero;

    /// <summary>Generated when the run starts.</summary>
    public DungeonMap? Dungeon { get; private set; }

    /// <summary>Players in the order they joined.</summary>
    public IReadOnlyList<PlayerState> Players => _joinOrder.Select(id => _players[id]).ToList();

    public IReadOnlySet<RoomId> RevealedRooms => _revealed;

    public RunState(ulong seed, GameConfig? config = null)
    {
        Rng = new RngStreams(seed);
        Config = config ?? GameConfig.Default;
        Config.Validate();
        Threat = new ThreatSystem(Config.Threat);
    }

    public PlayerState? GetPlayer(PlayerId id) => _players.GetValueOrDefault(id);

    public CommandResult Apply(ICommand command) => command switch
    {
        JoinPlayer c => Join(c),
        LeavePlayer c => Leave(c),
        StartRun => Start(),
        MovePlayerToRoom c => Move(c),
        ReportSprint c => Sprint(c),
        OpenChest c => Open(c),
        PickUpKey c => PickUp(c),
        UnlockExit c => Unlock(c),
        PlayCard c => Play(c),
        TakeCard c => Take(c),
        _ => CommandResult.Reject($"Unknown command {command.GetType().Name}."),
    };

    /// <summary>Advances time-based rules. Only runs while a run is in progress.</summary>
    public IReadOnlyList<IGameEvent> Tick(TimeSpan dt)
    {
        if (dt < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(dt), "Time cannot go backwards.");
        if (Phase != RunPhase.InRun) return Array.Empty<IGameEvent>();

        Elapsed += dt;
        var events = new List<IGameEvent>();
        Threat.Tick(dt, events);
        TickDraws(dt, events);
        return events;
    }

    // ── Lobby ───────────────────────────────────────────────────────────────

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

        Dungeon = DungeonGenerator.Generate(Rng.Seed, Config.Dungeon, Config.CardSet);
        Phase = RunPhase.InRun;

        var events = new List<IGameEvent>
        {
            new RunStarted(Rng.Seed, Players.Select(p => new PlayerInfo(p.Id, p.DisplayName)).ToList(), Config),
        };
        foreach (var player in Players)
        {
            Threat.AddPlayer(player.Id);
            events.Add(new PlayerEnteredRoom(player.Id, Dungeon.Entrance, player.EnterRoom(Dungeon.Entrance)));
        }
        events.AddRange(Reveal(Dungeon.Entrance));
        events.Add(Threat.Snapshot());
        foreach (var player in Players) DealStartingHand(player, events);

        return CommandResult.Ok(events);
    }

    // ── In-run ──────────────────────────────────────────────────────────────

    private CommandResult Move(MovePlayerToRoom c)
    {
        if (InRunPlayer(c.Player, out var player, out var current) is { } rejection) return rejection;
        if (!Dungeon!.Contains(c.Target)) return CommandResult.Reject($"{c.Target} is outside the dungeon.");
        if (current == c.Target) return CommandResult.Reject($"{c.Player} is already in {c.Target}.");

        if (DirectionExtensions.Between(current, c.Target) is not { } dir)
            return CommandResult.Reject($"{c.Target} is not adjacent to {current}.");
        if (!Dungeon.GetRoom(current).IsPassable(dir))
            return CommandResult.Reject($"The {dir} exit of {current} is not passable.");

        bool firstVisit = player.EnterRoom(c.Target);
        var events = new List<IGameEvent> { new PlayerEnteredRoom(c.Player, c.Target, firstVisit) };
        events.AddRange(Reveal(c.Target));
        if (firstVisit) TryDraw(player, events);
        return CommandResult.Ok(events);
    }

    private CommandResult Sprint(ReportSprint c)
    {
        if (InRunPlayer(c.Player, out _, out var room) is { } rejection) return rejection;
        if (c.Duration <= TimeSpan.Zero || c.Duration > MaxSprintReport)
            return CommandResult.Reject($"Sprint reports must be between 0 and {MaxSprintReport.TotalSeconds}s.");

        // Continuous noise: no NoiseMade/snapshot here; the periodic snapshot from Tick carries it.
        var events = new List<IGameEvent>();
        PlayerNoise(c.Player, Config.Threat.SprintPerSecond * c.Duration.TotalSeconds, room, events);
        return CommandResult.Ok(events);
    }

    private CommandResult Open(OpenChest c)
    {
        if (InRunPlayer(c.Player, out var player, out var room) is { } rejection) return rejection;
        if (Dungeon!.GetLoot(c.Chest) is not { Kind: LootKind.Chest } chest) return CommandResult.Reject($"{c.Chest} is not a chest.");
        if (chest.Room != room) return CommandResult.Reject($"{c.Chest} is not in {c.Player}'s room.");
        if (chest.Claimed) return CommandResult.Reject($"{c.Chest} is already open.");

        var events = new List<IGameEvent>();
        ClaimChest(player, chest, room, events);
        return CommandResult.Ok(events);
    }

    private CommandResult PickUp(PickUpKey c)
    {
        if (InRunPlayer(c.Player, out var player, out var room) is { } rejection) return rejection;
        if (Dungeon!.GetLoot(c.Key) is not { Kind: LootKind.Key } key) return CommandResult.Reject($"{c.Key} is not a key.");
        if (key.Room != room) return CommandResult.Reject($"{c.Key} is not in {c.Player}'s room.");
        if (key.Claimed) return CommandResult.Reject($"{c.Key} was already taken.");

        var events = new List<IGameEvent>();
        ClaimKey(player, key, room, events);
        return CommandResult.Ok(events);
    }

    private CommandResult Unlock(UnlockExit c)
    {
        if (InRunPlayer(c.Player, out var player, out var room) is { } rejection) return rejection;
        if (Dungeon!.GetRoom(room).GetExit(c.Side) != ExitState.Locked)
            return CommandResult.Reject($"The {c.Side} exit of {room} is not locked.");
        if (player.Keys <= 0) return CommandResult.Reject($"{c.Player} has no keys.");

        player.Keys--;
        Dungeon.SetDoorway(room, c.Side, ExitState.Open);

        var events = new List<IGameEvent> { new ExitUnlocked(c.Player, room, c.Side) };
        MakeNoise(c.Player, Config.Threat.UnlockExit, room, events);
        return CommandResult.Ok(events);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Common checks for in-run player commands. Returns a rejection, or null with the player and their room.</summary>
    private CommandResult? InRunPlayer(PlayerId id, out PlayerState player, out RoomId room)
    {
        player = null!;
        room = default;
        if (Phase != RunPhase.InRun || Dungeon is null) return CommandResult.Reject("Only possible during a run.");
        if (GetPlayer(id) is not { Room: { } current } found) return CommandResult.Reject($"{id} is not in the run.");

        player = found;
        room = current;
        return null;
    }

    private void ClaimChest(PlayerState player, LootItem chest, RoomId room, List<IGameEvent> events)
    {
        int points = (int)Math.Round(chest.Points * player.NextChestMultiplier);
        player.NextChestMultiplier = 1;
        chest.Claimed = true;
        player.CarriedPoints += points;

        events.Add(new ChestOpened(player.Id, chest.Id, points));
        MakeNoise(player.Id, Config.Threat.OpenChest, room, events);
    }

    private void ClaimKey(PlayerState player, LootItem key, RoomId room, List<IGameEvent> events)
    {
        key.Claimed = true;
        player.Keys++;

        events.Add(new KeyPickedUp(player.Id, key.Id));
        MakeNoise(player.Id, Config.Threat.PickUpKey, room, events);
    }

    /// <summary>Player-made noise: room multiplier applies, and a silenced (Hushed) player makes none.</summary>
    private double PlayerNoise(PlayerId player, double amount, RoomId room, List<IGameEvent> events)
    {
        if (GetPlayer(player) is { } p && Elapsed < p.SilencedUntil) return 0;
        return Threat.AddPlayerNoise(player, amount, RoomType(room), events);
    }

    /// <summary>Discrete noise: adds threat, then feedback + a fresh snapshot so every HUD updates at once.</summary>
    private void MakeNoise(PlayerId player, double amount, RoomId room, List<IGameEvent> events)
    {
        double added = PlayerNoise(player, amount, room, events);
        if (added <= 0) return;
        events.Add(new NoiseMade(player, added));
        events.Add(Threat.Snapshot());
    }

    private RoomType RoomType(RoomId room) => Dungeon!.GetRoom(room).Type;

    private IEnumerable<IGameEvent> Reveal(RoomId room)
    {
        if (_revealed.Add(room)) yield return new RoomRevealed(room);
    }
}
