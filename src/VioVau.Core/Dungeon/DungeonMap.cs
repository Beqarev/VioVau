namespace VioVau.Core.Dungeon;

public sealed class Floor
{
    private readonly Room[,] _rooms;

    public int Depth { get; }
    public int Width { get; }
    public int Height { get; }

    internal Floor(int depth, int width, int height, Func<int, int, Room> createRoom)
    {
        Depth = depth;
        Width = width;
        Height = height;
        _rooms = new Room[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                _rooms[x, y] = createRoom(x, y);
    }

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    public Room this[int x, int y] => _rooms[x, y];

    public IEnumerable<Room> Rooms
    {
        get
        {
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    yield return _rooms[x, y];
        }
    }

    public Room? Neighbor(Room room, Direction side)
    {
        var (dx, dy) = side.Offset();
        int x = room.Id.X + dx, y = room.Id.Y + dy;
        return Contains(x, y) ? _rooms[x, y] : null;
    }
}

/// <summary>The generated dungeon: floors of room grids. Fully determined by seed + <see cref="DungeonConfig"/>.</summary>
public sealed class DungeonMap
{
    public IReadOnlyList<Floor> Floors { get; }

    /// <summary>The Lobby: where players start and extract.</summary>
    public RoomId Entrance { get; }

    internal DungeonMap(IReadOnlyList<Floor> floors, RoomId entrance)
    {
        Floors = floors;
        Entrance = entrance;
    }

    public bool Contains(RoomId id) =>
        id.Floor >= 0 && id.Floor < Floors.Count && Floors[id.Floor].Contains(id.X, id.Y);

    public Room GetRoom(RoomId id)
    {
        if (!Contains(id)) throw new ArgumentOutOfRangeException(nameof(id), $"{id} is outside the dungeon.");
        return Floors[id.Floor][id.X, id.Y];
    }

    public IEnumerable<Room> AllRooms => Floors.SelectMany(f => f.Rooms);
}
