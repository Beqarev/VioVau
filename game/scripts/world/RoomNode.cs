using Godot;
using VioVau.Core.Dungeon;
using VioVau.Game.UI;

namespace VioVau.Game.World;

/// <summary>Graybox room: floor, walls with door gaps, and wall collision. Unrevealed rooms draw as void.</summary>
public partial class RoomNode : Node2D
{
    private const float TileSize = 16f;

    private bool _revealed;

    public Room Room { get; private set; } = null!;

    public bool Revealed
    {
        get => _revealed;
        set
        {
            if (_revealed == value) return;
            _revealed = value;
            QueueRedraw();
        }
    }

    public RoomNode Init(Room room, bool revealed)
    {
        Room = room;
        _revealed = revealed;
        Name = $"Room_{room.Id.X}_{room.Id.Y}";
        Position = WorldLayout.RoomOrigin(room.Id);
        return this;
    }

    public override void _Ready()
    {
        var body = new StaticBody2D { Name = "Walls", CollisionLayer = PhysicsLayers.Walls, CollisionMask = 0 };
        foreach (var side in DirectionExtensions.All)
        {
            foreach (var rect in WorldLayout.WallRects(side, Room.IsPassable(side)))
            {
                body.AddChild(new CollisionShape2D
                {
                    Shape = new RectangleShape2D { Size = rect.Size },
                    Position = rect.GetCenter(),
                });
            }
        }
        AddChild(body);
    }

    public override void _Draw()
    {
        var bounds = new Rect2(Vector2.Zero, WorldLayout.RoomSize);
        if (!_revealed)
        {
            DrawRect(bounds, Palette.Void);
            return;
        }

        DrawRect(bounds, Palette.RoomFloor(Room.Type));
        for (float x = TileSize; x < bounds.Size.X; x += TileSize)
            DrawLine(new Vector2(x, 0), new Vector2(x, bounds.Size.Y), Palette.FloorLine);
        for (float y = TileSize; y < bounds.Size.Y; y += TileSize)
            DrawLine(new Vector2(0, y), new Vector2(bounds.Size.X, y), Palette.FloorLine);

        foreach (var side in DirectionExtensions.All)
        {
            foreach (var rect in WorldLayout.WallRects(side, Room.IsPassable(side)))
            {
                DrawRect(rect, Palette.Wall);
                DrawRect(rect, Palette.WallEdge, filled: false, width: 1f);
            }
        }

        var font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(14, 22), Label(Room.Type), HorizontalAlignment.Left, -1, 8, Palette.Dim);
    }

    private static string Label(RoomType type) => type switch
    {
        RoomType.GuardPost => "GUARD POST",
        RoomType.EchoChamber => "ECHO CHAMBER",
        RoomType.HushRoom => "HUSH ROOM",
        RoomType.RelicChamber => "RELIC CHAMBER",
        _ => type.ToString().ToUpperInvariant(),
    };
}

public static class PhysicsLayers
{
    public const uint Walls = 1 << 0;
    public const uint Players = 1 << 1;
}
