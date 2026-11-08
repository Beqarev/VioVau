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

        DrawFloor(bounds);
        if (Room.Type == RoomType.Lobby) DrawExtractionCircle(bounds.GetCenter());

        foreach (var side in DirectionExtensions.All)
        {
            var exit = Room.GetExit(side);
            foreach (var rect in WorldLayout.WallRects(side, exit != ExitState.None))
                DrawWall(rect, side);
        }

        DrawCrystals();

        foreach (var side in DirectionExtensions.All)
            if (Room.GetExit(side) == ExitState.Locked) DrawLockedDoor(side);

        // Bottom-right: the top corners belong to the HUD and minimap.
        DrawString(ThemeDB.FallbackFont, new Vector2(0, bounds.Size.Y - 14), Label(Room.Type), HorizontalAlignment.Right, bounds.Size.X - 14, 8, Palette.Dim);
    }

    // ── Code-drawn tiles (stand-ins for a tileset; same look every time for the same room) ──

    private void DrawFloor(Rect2 bounds)
    {
        var baseColor = Palette.RoomFloor(Room.Type);
        DrawRect(bounds, baseColor);
        var light = new Color(Palette.Wall, 0.07f);
        var crack = new Color(Palette.Void, 0.7f);

        int cols = Mathf.CeilToInt(bounds.Size.X / TileSize), rows = Mathf.CeilToInt(bounds.Size.Y / TileSize);
        for (int ty = 0; ty < rows; ty++)
        for (int tx = 0; tx < cols; tx++)
        {
            uint h = Hash(Room.Id.X, Room.Id.Y, tx, ty);
            var o = new Vector2(tx * TileSize, ty * TileSize);

            if (h % 6 == 1) DrawRect(new Rect2(o, new Vector2(TileSize, TileSize)), light);

            // Seams: dark on top/left, a hint of light on the opposite edges (light from top-left).
            DrawLine(o, o + new Vector2(TileSize, 0), Palette.FloorLine);
            DrawLine(o, o + new Vector2(0, TileSize), Palette.FloorLine);

            if (h % 7 == 0)
            {
                // A small crack.
                var a = o + new Vector2(3 + h / 7 % 8, 3 + h / 56 % 6);
                var b = a + new Vector2(3, 2);
                DrawLine(a, b, crack);
                DrawLine(b, b + new Vector2(1, 3), crack);
            }
            else if (h % 53 == 3)
            {
                // A faint engraved rune: a small diamond with a dot (never a cross).
                var c = o + new Vector2(8, 8);
                var rune = new Color(Palette.Wall, 0.7f);
                DrawPolyline([c + new Vector2(0, -3), c + new Vector2(3, 0), c + new Vector2(0, 3), c + new Vector2(-3, 0), c + new Vector2(0, -3)], rune);
                DrawRect(new Rect2(c.X, c.Y, 1, 1), rune);
            }
        }
    }

    private void DrawWall(Rect2 rect, Direction side)
    {
        DrawRect(rect, Palette.Wall);
        var mortar = new Color(Palette.Void, 0.55f);
        bool horizontal = side is Direction.North or Direction.South;

        if (horizontal)
        {
            // One course of 16px bricks, offset per room so walls don't look stamped.
            float offset = Hash(Room.Id.X, Room.Id.Y, 0, (int)side) % 16;
            for (float x = rect.Position.X - offset; x < rect.End.X; x += 16)
                if (x > rect.Position.X) DrawLine(new Vector2(x, rect.Position.Y), new Vector2(x, rect.End.Y), mortar);
        }
        else
        {
            for (float y = rect.Position.Y + 8; y < rect.End.Y; y += 8)
            {
                DrawLine(new Vector2(rect.Position.X, y), new Vector2(rect.End.X, y), mortar);
                float x = rect.Position.X + ((int)(y / 8) % 2 == 0 ? 2 : 5);
                DrawLine(new Vector2(x, y), new Vector2(x, y + 8), mortar);
            }
        }

        // Lit top/left edges, dark edge where the wall meets the floor.
        DrawLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), Palette.WallEdge);
        DrawLine(rect.Position, new Vector2(rect.Position.X, rect.End.Y), Palette.WallEdge);
        var inner = side switch
        {
            Direction.North => (new Vector2(rect.Position.X, rect.End.Y), rect.End),
            Direction.South => (rect.Position, new Vector2(rect.End.X, rect.Position.Y)),
            Direction.West => (new Vector2(rect.End.X, rect.Position.Y), rect.End),
            _ => (rect.Position, new Vector2(rect.Position.X, rect.End.Y)),
        };
        DrawLine(inner.Item1, inner.Item2, Palette.Void, 1f);
    }

    /// <summary>A few amethyst clusters growing from the wall base, never in front of a doorway.</summary>
    private void DrawCrystals()
    {
        uint h = Hash(Room.Id.X, Room.Id.Y, 99, 99);
        int count = 2 + (int)(h % 3);
        for (int i = 0; i < count; i++)
        {
            uint r = Hash(Room.Id.X, Room.Id.Y, i, 7);
            var side = DirectionExtensions.All[(int)(r % 4)];
            float along = 0.1f + (r / 4 % 1000) / 1000f * 0.8f;
            if (Mathf.Abs(along - 0.5f) < 0.14f) along += 0.2f; // keep door lanes clear

            var at = side switch
            {
                Direction.North => new Vector2(along * WorldLayout.RoomWidth, WorldLayout.WallThickness + 3),
                Direction.South => new Vector2(along * WorldLayout.RoomWidth, WorldLayout.RoomHeight - WorldLayout.WallThickness - 1),
                Direction.West => new Vector2(WorldLayout.WallThickness + 4, along * WorldLayout.RoomHeight),
                _ => new Vector2(WorldLayout.RoomWidth - WorldLayout.WallThickness - 4, along * WorldLayout.RoomHeight),
            };
            DrawCrystal(at, r);
        }
    }

    private void DrawCrystal(Vector2 at, uint seed)
    {
        // 2–3 shards: dark body, lit left face, white tip on the tallest.
        int shards = 2 + (int)(seed % 2);
        for (int i = 0; i < shards; i++)
        {
            float x = at.X + (i - shards / 2f) * 3f;
            float height = 5 + (seed >> (i * 3)) % 5;
            var tip = new Vector2(x + (i - 1), at.Y - height);
            DrawColoredPolygon([new Vector2(x - 2, at.Y), tip, new Vector2(x + 2, at.Y)], Palette.Accent);
            DrawColoredPolygon([new Vector2(x - 2, at.Y), tip, new Vector2(x, at.Y)], new Color("c8a2ff"));
            if (i == shards / 2) DrawRect(new Rect2(tip.X, tip.Y, 1, 1), Palette.White);
        }
    }

    private void DrawExtractionCircle(Vector2 center)
    {
        var ring = new Color(Palette.Wall, 0.9f);
        DrawArc(center, 34, 0, Mathf.Tau, 48, ring, 1f);
        DrawArc(center, 28, 0, Mathf.Tau, 48, new Color(Palette.Wall, 0.5f), 1f);
        for (int i = 0; i < 8; i++)
        {
            var dir = Vector2.FromAngle(i * Mathf.Tau / 8);
            DrawRect(new Rect2(center + dir * 31 - Vector2.One, new Vector2(2, 2)), Palette.Accent);
        }
    }

    private static uint Hash(int a, int b, int c, int d)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (int v in new[] { a, b, c, d }) h = (h ^ (uint)v) * 16777619;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            return h ^ (h >> 15);
        }
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
    public const uint Enemies = 1 << 2;
}
