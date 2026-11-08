namespace VioVau.Core.Dungeon;

public enum RoomType
{
    Lobby,
    Corridor,
    Vault,
    GuardPost,
    EchoChamber,
    HushRoom,
    Library,
    Merchant,
    Shrine,
    TrapHall,
    ShiftingRoom,
    CollapsedPassage,
    Prison,
    VoidDoor,
    Stairs,
    RelicChamber,
}

public enum ExitState
{
    /// <summary>No exit on this side: solid wall.</summary>
    None,
    Open,
    /// <summary>Needs a standard key (or Lockpick).</summary>
    Locked,
    /// <summary>Needs a Void Key.</summary>
    Sealed,
    Collapsed,
    /// <summary>Can only be passed from this side.</summary>
    OneWay,
    /// <summary>Exists but must be revealed first.</summary>
    Hidden,
}

public sealed class Room
{
    private readonly ExitState[] _exits = new ExitState[4];

    public RoomId Id { get; }
    public RoomType Type { get; internal set; }

    public Room(RoomId id, RoomType type)
    {
        Id = id;
        Type = type;
    }

    public ExitState GetExit(Direction side) => _exits[(int)side];

    internal void SetExit(Direction side, ExitState state) => _exits[(int)side] = state;

    /// <summary>Whether a player can currently walk through this side.</summary>
    public bool IsPassable(Direction side) => GetExit(side) is ExitState.Open or ExitState.OneWay;

    public override string ToString() => $"{Type} {Id}";
}
