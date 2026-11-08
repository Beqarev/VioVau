using VioVau.Core.Dungeon;

namespace VioVau.Core;

/// <summary>
/// Read-only picture of a run, rebuilt purely from events. Every peer (host included) keeps one,
/// and all visuals read from it — so host and clients render from the same code path.
/// </summary>
public sealed class RunView
{
    private readonly Dictionary<PlayerId, RoomId> _playerRooms = new();
    private readonly HashSet<RoomId> _revealed = new();

    public ulong Seed { get; }
    public DungeonMap Dungeon { get; }
    public IReadOnlyList<PlayerInfo> Players { get; }
    public IReadOnlySet<RoomId> RevealedRooms => _revealed;

    private RunView(RunStarted started)
    {
        Seed = started.Seed;
        Players = started.Players;
        Dungeon = DungeonGenerator.Generate(started.Seed, started.Config);
    }

    public static RunView FromStart(RunStarted started) => new(started);

    public RoomId? RoomOf(PlayerId player) => _playerRooms.TryGetValue(player, out var room) ? room : null;

    public bool IsRevealed(RoomId room) => _revealed.Contains(room);

    public void Apply(IGameEvent e)
    {
        switch (e)
        {
            case PlayerEnteredRoom entered:
                _playerRooms[entered.Player] = entered.Room;
                break;
            case RoomRevealed revealed:
                _revealed.Add(revealed.Room);
                break;
        }
    }
}
