using System.Text.Json;
using Godot;
using VioVau.Core;
using VioVau.Core.Cards;
using VioVau.Core.Dungeon;
using VioVau.Core.Net;
using VioVau.Core.Threat;
using VioVau.Game.Input;

namespace VioVau.Game.Networking;

/// <summary>Who controls which player: the peer (machine) and the local slot (input device) on that machine.</summary>
public sealed record RosterEntry(int PlayerId, int PeerId, int LocalSlot, string Name);

public enum InteractKind
{
    Chest,
    /// <summary>A card offer (Library / Merchant); Value = EntityId.</summary>
    Card,
    Door,
}

/// <summary>What a player wants to interact with: a chest (Value = EntityId) or a locked door (Value = Direction).</summary>
public readonly record struct InteractTarget(InteractKind Kind, int Value);

/// <summary>
/// Autoload. Owns the connection, the roster, and — on the host — the authoritative <see cref="RunState"/>.
/// Host-authoritative: clients send intents/positions, the host applies rules and broadcasts events.
/// Solo play is just hosting with nobody else joining.
/// </summary>
public partial class NetSession : Node
{
    public const int DefaultPort = 7777;
    public const string MenuScene = "res://scenes/MainMenu.tscn";
    public const string GameScene = "res://scenes/Game.tscn";
    private const string DungeonConfigPath = "res://data/dungeon.json";
    private const string ThreatConfigPath = "res://data/threat.json";
    private const string CardsConfigPath = "res://data/cards.json";

    public static NetSession Instance { get; private set; } = null!;

    private readonly List<RosterEntry> _roster = new();
    private int _nextPlayerId = 1;

    public IReadOnlyList<RosterEntry> Roster => _roster;
    public bool IsOnline { get; private set; }
    public bool IsHost { get; private set; }
    public int LocalPeerId => Multiplayer.GetUniqueId();
    public string Status { get; private set; } = "";

    /// <summary>Authoritative rules. Host only.</summary>
    public RunState? State { get; private set; }

    /// <summary>Event-built picture of the run. Every peer, including the host.</summary>
    public RunView? View { get; private set; }

    public event Action? RosterChanged;
    public event Action? StatusChanged;
    public event Action<IGameEvent>? EventReceived;
    public event Action<PlayerId, Vector2, Vector2, bool>? PlayerStateReceived;

    /// <summary>Host only: a player (local or remote) asked to interact. The game scene validates range and applies it.</summary>
    public event Action<PlayerId, InteractTarget>? InteractRequested;

    /// <summary>Host only: a player asked to play the card in a hand slot. The game scene finds the target and applies it.</summary>
    public event Action<PlayerId, int>? PlayCardRequested;

    /// <summary>A short message for one of this machine's players (e.g. why a card couldn't be played).</summary>
    public event Action<PlayerId, string>? PlayerNotified;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public override void _Process(double delta)
    {
        if (!IsHost || State is not { Phase: RunPhase.InRun }) return;
        var events = State.Tick(TimeSpan.FromSeconds(delta));
        if (events.Count > 0) Broadcast(events);
    }

    // ── Session lifecycle ───────────────────────────────────────────────────

    public bool Host(int port = DefaultPort)
    {
        Leave(returnToMenu: false);
        LocalPlayers.EnsureOne();

        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateServer(port, RunState.MaxPlayers - 1);
        if (error != Error.Ok)
        {
            SetStatus($"Could not host on port {port}: {error}");
            return false;
        }

        Multiplayer.MultiplayerPeer = peer;
        IsOnline = true;
        IsHost = true;
        LocalPlayers.Locked = true;

        var seed = Dev.DevOptions.Seed ?? (ulong)System.Random.Shared.NextInt64();
        State = new RunState(seed, LoadGameConfig());

        var rejection = AddPlayers(LocalPeerId, LocalPlayers.Names());
        if (rejection is not null) GD.PushError($"[Net] Could not add local players: {rejection}");

        SetStatus($"Hosting on port {port}. Waiting for players…");
        BroadcastRoster();
        return true;
    }

    public bool Join(string address, int port = DefaultPort)
    {
        Leave(returnToMenu: false);
        LocalPlayers.EnsureOne();

        var peer = new ENetMultiplayerPeer();
        var error = peer.CreateClient(address, port);
        if (error != Error.Ok)
        {
            SetStatus($"Could not connect to {address}:{port}: {error}");
            return false;
        }

        Multiplayer.MultiplayerPeer = peer;
        IsOnline = true;
        IsHost = false;
        LocalPlayers.Locked = true;
        SetStatus($"Connecting to {address}:{port}…");
        return true;
    }

    /// <summary>Disconnect and reset. Safe to call at any time.</summary>
    public void Leave(bool returnToMenu = true)
    {
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enet) enet.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();

        bool hadRoster = _roster.Count > 0;
        IsOnline = false;
        IsHost = false;
        State = null;
        View = null;
        _roster.Clear();
        _nextPlayerId = 1;
        LocalPlayers.Locked = false;

        if (hadRoster) RosterChanged?.Invoke();
        if (returnToMenu && GetTree().CurrentScene?.SceneFilePath != MenuScene)
            GetTree().ChangeSceneToFile(MenuScene);
    }

    /// <summary>Host only: start the run for everyone in the lobby.</summary>
    public void StartGame()
    {
        if (!IsHost) return;
        var result = ApplyCommand(new StartRun());
        if (!result.Accepted) SetStatus(result.RejectionReason ?? "Could not start.");
    }

    /// <summary>Host only: apply a command to the authoritative state and broadcast its events.</summary>
    public CommandResult ApplyCommand(ICommand command)
    {
        if (!IsHost || State is null) return CommandResult.Reject("Only the host can apply commands.");
        var result = State.Apply(command);
        if (result.Accepted && result.Events.Count > 0) Broadcast(result.Events);
        return result;
    }

    // ── Player state (positions) ────────────────────────────────────────────

    /// <summary>Called by locally controlled characters ~30×/s. Unreliable: a lost packet is replaced by the next one.</summary>
    public void SendPlayerState(PlayerId player, Vector2 position, Vector2 velocity, bool sprinting)
    {
        if (!IsOnline || Multiplayer.GetPeers().Length == 0) return;
        Rpc(MethodName.ReceivePlayerState, player.Value, position, velocity, sprinting);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
    private void ReceivePlayerState(int playerId, Vector2 position, Vector2 velocity, bool sprinting)
    {
        if (!SenderOwns(playerId)) return;
        PlayerStateReceived?.Invoke(new PlayerId(playerId), position, velocity, sprinting);
    }

    // ── Interactions ────────────────────────────────────────────────────────

    /// <summary>Ask the host to interact. On the host this goes straight to the game scene.</summary>
    public void RequestInteract(PlayerId player, InteractTarget target)
    {
        if (IsHost) InteractRequested?.Invoke(player, target);
        else if (IsOnline) RpcId(1, MethodName.ReceiveInteract, player.Value, (int)target.Kind, target.Value);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveInteract(int playerId, int kind, int value)
    {
        if (!IsHost || !SenderOwns(playerId) || !Enum.IsDefined(typeof(InteractKind), kind)) return;
        InteractRequested?.Invoke(new PlayerId(playerId), new InteractTarget((InteractKind)kind, value));
    }

    // ── Cards ───────────────────────────────────────────────────────────────

    /// <summary>Ask the host to play a card. Only the slot is sent; the host decides the target from its own positions.</summary>
    public void RequestPlayCard(PlayerId player, int slot)
    {
        if (IsHost) PlayCardRequested?.Invoke(player, slot);
        else if (IsOnline) RpcId(1, MethodName.ReceivePlayCard, player.Value, slot);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceivePlayCard(int playerId, int slot)
    {
        if (!IsHost || !SenderOwns(playerId)) return;
        PlayCardRequested?.Invoke(new PlayerId(playerId), slot);
    }

    /// <summary>Host only: tell one player something (only their machine shows it).</summary>
    public void NotifyPlayer(PlayerId player, string message)
    {
        int peer = OwnerPeer(player);
        if (peer == LocalPeerId) PlayerNotified?.Invoke(player, message);
        else if (peer > 0) RpcId(peer, MethodName.ReceiveNotification, player.Value, message);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveNotification(int playerId, string message) =>
        PlayerNotified?.Invoke(new PlayerId(playerId), message);

    private int OwnerPeer(PlayerId player) => _roster.FirstOrDefault(r => r.PlayerId == player.Value)?.PeerId ?? 0;

    /// <summary>Whether the peer that sent the current RPC controls this player.</summary>
    private bool SenderOwns(int playerId)
    {
        int sender = Multiplayer.GetRemoteSenderId();
        return _roster.Any(r => r.PlayerId == playerId && r.PeerId == sender);
    }

    // ── Lobby / roster ──────────────────────────────────────────────────────

    private void OnConnectedToServer()
    {
        SetStatus("Connected. Waiting for the host to start…");
        RpcId(1, MethodName.RequestJoin, LocalPlayers.Names());
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestJoin(string[] names)
    {
        if (!IsHost) return;
        int sender = Multiplayer.GetRemoteSenderId();
        var rejection = AddPlayers(sender, names);
        if (rejection is not null)
        {
            RpcId(sender, MethodName.JoinRejected, rejection);
            return;
        }
        SetStatus($"{string.Join(", ", names)} joined.");
        BroadcastRoster();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void JoinRejected(string reason)
    {
        Leave();
        SetStatus($"The host refused: {reason}");
    }

    /// <returns>Null on success, otherwise why the players can't join.</returns>
    private string? AddPlayers(int peerId, string[] names)
    {
        if (State is null) return "No session.";
        if (State.Phase != RunPhase.Lobby) return "The run has already started.";
        if (names.Length == 0) return "No players to add.";
        if (State.Players.Count + names.Length > RunState.MaxPlayers)
            return $"Not enough room ({State.Players.Count}/{RunState.MaxPlayers} players).";

        for (int slot = 0; slot < names.Length; slot++)
        {
            var id = new PlayerId(_nextPlayerId++);
            var name = string.IsNullOrWhiteSpace(names[slot]) ? $"Player {id.Value}" : names[slot].Trim();
            if (name.Length > 16) name = name[..16];

            var result = State.Apply(new JoinPlayer(id, name));
            if (!result.Accepted) return result.RejectionReason;
            _roster.Add(new RosterEntry(id.Value, peerId, slot, name));
        }
        return null;
    }

    private void OnPeerDisconnected(long peerId)
    {
        if (!IsHost) return;

        var gone = _roster.Where(r => r.PeerId == peerId).ToList();
        if (gone.Count == 0) return;

        foreach (var entry in gone)
        {
            _roster.Remove(entry);
            // Mid-run leaving isn't a rule yet; the character just disappears from the session.
            State?.Apply(new LeavePlayer(new PlayerId(entry.PlayerId)));
        }
        SetStatus($"{string.Join(", ", gone.Select(g => g.Name))} disconnected.");
        BroadcastRoster();
    }

    private void BroadcastRoster() =>
        Rpc(MethodName.SyncRoster, JsonSerializer.Serialize(_roster));

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncRoster(string json)
    {
        if (!IsHost)
        {
            _roster.Clear();
            _roster.AddRange(JsonSerializer.Deserialize<RosterEntry[]>(json) ?? []);
        }
        RosterChanged?.Invoke();
    }

    // ── Events ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends each machine the events it may see: public events to everyone, private ones (drawn cards…)
    /// only to the machine that owns that player. Order is preserved per machine.
    /// </summary>
    private void Broadcast(IReadOnlyList<IGameEvent> events)
    {
        foreach (int peer in Multiplayer.GetPeers())
        {
            var visible = EventVisibility.For(events, p => OwnerPeer(p) == peer);
            if (visible.Count > 0) RpcId(peer, MethodName.ReceiveEvents, EventCodec.Encode(visible));
        }

        var mine = EventVisibility.For(events, p => OwnerPeer(p) == LocalPeerId);
        if (mine.Count > 0) HandleEvents(mine);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveEvents(string json) => HandleEvents(EventCodec.Decode(json));

    private void HandleEvents(IReadOnlyList<IGameEvent> events)
    {
        foreach (var e in events)
        {
            if (e is RunStarted started)
            {
                View = RunView.FromStart(started);
                GetTree().ChangeSceneToFile(GameScene);
            }

            View?.Apply(e);
            EventReceived?.Invoke(e);
        }
    }

    // ── Connection callbacks ────────────────────────────────────────────────

    private void OnConnectionFailed()
    {
        Leave(returnToMenu: false);
        SetStatus("Could not connect to the host.");
    }

    private void OnServerDisconnected()
    {
        Leave();
        SetStatus("Disconnected from the host.");
    }

    private void SetStatus(string status)
    {
        Status = status;
        GD.Print($"[Net] {status}");
        StatusChanged?.Invoke();
    }

    private static GameConfig LoadGameConfig() =>
        new(Load(DungeonConfigPath, DungeonConfig.FromJson, DungeonConfig.Default),
            Load(ThreatConfigPath, ThreatConfig.FromJson, ThreatConfig.Default),
            Load(CardsConfigPath, CardsConfig.FromJson, CardsConfig.Default));

    private static T Load<T>(string path, Func<string, T> parse, T fallback)
    {
        try
        {
            return parse(Godot.FileAccess.GetFileAsString(path));
        }
        catch (Exception ex)
        {
            GD.PushError($"[Net] Failed to load {path}, using defaults: {ex.Message}");
            return fallback;
        }
    }
}
