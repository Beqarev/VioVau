using Godot;
using VioVau.Core.Dungeon;
using VioVau.Game.UI;

namespace VioVau.Game.World;

/// <summary>Graybox room: floor, walls with doorways, locked doors, and their collision. Unrevealed rooms draw as void.</summary>
public partial class RoomNode : Node2D
{
    private const float TileSize = 16f;

    private bool _revealed;
    private Direction? _highlightedDoor;
    private StaticBody2D? _walls;

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

    /// <summary>A locked door a local player can use right now.</summary>
    public Direction? HighlightedDoor
    {
        get => _highlightedDoor;
        set
        {
            if (_highlightedDoor == value) return;
            _highlightedDoor = value;
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

    public override void _Ready() => BuildCollision();

    /// <summary>Call after an exit of this room changed (e.g. a door was unlocked).</summary>
    public void RefreshExits()
    {
        _walls?.QueueFree();
        _walls = null;
        Callable.From(BuildCollision).CallDeferred(); // never swap physics bodies mid-physics-step
        QueueRedraw();
    }

    private void BuildCollision()
    {
        _walls = new StaticBody2D { Name = "Walls", CollisionLayer = PhysicsLayers.Walls, CollisionMask = 0 };
        foreach (var side in DirectionExtensions.All)
        {
            var exit = Room.GetExit(side);
            var rects = WorldLayout.WallRects(side, exit != ExitState.None).ToList();
            if (!Room.IsPassable(side) && exit != ExitState.None) rects.Add(WorldLayout.DoorRect(side));

            foreach (var rect in rects)
            {
                _walls.AddChild(new CollisionShape2D
                {
                    Shape = new RectangleShape2D { Size = rect.Size },
                    Position = rect.GetCenter(),
                });
            }
        }
        AddChild(_walls);
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
            var exit = Room.GetExit(side);
            foreach (var rect in WorldLayout.WallRects(side, exit != ExitState.None))
            {
                DrawRect(rect, Palette.Wall);
                DrawRect(rect, Palette.WallEdge, filled: false, width: 1f);
            }

            if (exit == ExitState.Locked) DrawLockedDoor(side);
        }

        // Bottom-right: the top corners belong to the HUD and minimap.
        DrawString(ThemeDB.FallbackFont, new Vector2(0, bounds.Size.Y - 14), Label(Room.Type), HorizontalAlignment.Right, bounds.Size.X - 14, 8, Palette.Dim);
    }

    private void DrawLockedDoor(Direction side)
    {
        var door = WorldLayout.DoorRect(side);
        bool lit = _highlightedDoor == side;
        DrawRect(door, lit ? Palette.White : Palette.Accent);
        DrawRect(door, Palette.Void, filled: false, width: 1f);

        // Keyhole.
        var c = door.GetCenter();
        DrawRect(new Rect2(c.X - 1, c.Y - 2, 2, 2), Palette.Void);
        DrawRect(new Rect2(c.X - 0.5f, c.Y, 1, 2), Palette.Void);
    }

    private static string Label(RoomType type) => type switch
    {
        RoomType.GuardPost => "GUARD POST",
        RoomType.EchoChamber => "ECHO CHAMBER  ·  NOISE x2",
        RoomType.HushRoom => "HUSH ROOM  ·  SILENT",
        RoomType.RelicChamber => "RELIC CHAMBER",
        _ => type.ToString().ToUpperInvariant(),
    };
}

public static class PhysicsLayers
{
    public const uint Walls = 1 << 0;
    public const uint Players = 1 << 1;
}
