using Godot;
using VioVau.Core.Dungeon;
using VioVau.Game.UI;

namespace VioVau.Game.World;

/// <summary>
/// Room visuals: tiled floor with AO, 3/4 wall faces (north walls feel tall), decorative props by room type,
/// crystals, locked doors. Collision stays thin — props are mostly cosmetic.
/// </summary>
public partial class RoomNode : Node2D
{
    private const float TileSize = 16f;

    /// <summary>Visible front face hanging down from the north wall top (3/4 view).</summary>
    private const float WallFaceHeight = 16f;

    /// <summary>How far a character's sprite may overlap the bottom of the north face (feels like standing in front of it).</summary>
    private const float FaceOverlap = 4f;

    private bool _revealed;
    private Direction? _highlightedDoor;
    private StaticBody2D? _walls;

    public Room Room { get; private set; } = null!;

    private bool _collapsing;
    private double _time;
    private Node2D? _occluders;
    private PointLight2D? _circleLight;
    private CpuParticles2D? _dust;

    /// <summary>Ambient dust only runs in rooms someone is looking at.</summary>
    public bool OnScreen
    {
        set
        {
            if (_dust is null || _dust.Emitting == (value && _revealed)) return;
            _dust.Emitting = value && _revealed;
        }
    }

    /// <summary>Lobby only: once the Collapse starts, the escape circle burns white and pulses.</summary>
    public bool Collapsing
    {
        get => _collapsing;
        set
        {
            _collapsing = value;
            SetProcess(value);
            QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_circleLight is not null)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)_time * 6f);
            _circleLight.Color = Lights.Bone;
            _circleLight.Energy = 0.7f + 0.5f * pulse;
        }
        QueueRedraw();
    }

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

    public override void _Ready()
    {
        BuildCollision();
        AddCrystalLights();
        _dust = Fx.RoomDust(WorldLayout.RoomSize);
        AddChild(_dust);
        if (Room.Type == RoomType.Lobby)
        {
            _circleLight = Lights.Point(70f, Lights.Amethyst, 0.55f);
            _circleLight.Position = WorldLayout.RoomSize / 2f;
            AddChild(_circleLight);
        }
    }

    /// <summary>Each amethyst cluster gives off a little violet light.</summary>
    private void AddCrystalLights()
    {
        foreach (var at in CrystalSpots())
        {
            var light = Lights.Point(34f, Lights.Amethyst, 0.75f);
            light.Position = at + new Vector2(0, -4);
            AddChild(light);
        }
    }

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
        _occluders?.QueueFree();
        _occluders = new Node2D { Name = "Occluders" };
        AddChild(_occluders);

        _walls = new StaticBody2D { Name = "Walls", CollisionLayer = PhysicsLayers.Walls, CollisionMask = 0 };
        foreach (var side in DirectionExtensions.All)
        {
            var exit = Room.GetExit(side);
            var rects = WorldLayout.WallRects(side, exit != ExitState.None).ToList();
            if (side == Direction.North)
            {
                // The 3/4 front face is part of the wall: you stop at its foot (a few px of overlap reads as depth).
                rects = rects.Select(r => new Rect2(r.Position, new Vector2(r.Size.X, r.Size.Y + WallFaceHeight - FaceOverlap))).ToList();
            }
            if (!Room.IsPassable(side) && exit != ExitState.None) rects.Add(WorldLayout.DoorRect(side));

            foreach (var rect in rects)
            {
                _walls.AddChild(new CollisionShape2D
                {
                    Shape = new RectangleShape2D { Size = rect.Size },
                    Position = rect.GetCenter(),
                });
                // Walls (and locked doors) cast shadows.
                _occluders.AddChild(new LightOccluder2D
                {
                    Occluder = new OccluderPolygon2D
                    {
                        Polygon = [rect.Position, new Vector2(rect.End.X, rect.Position.Y), rect.End, new Vector2(rect.Position.X, rect.End.Y)],
                    },
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
        DrawFloorAo(bounds);
        if (Room.Type == RoomType.Lobby) DrawExtractionCircle(bounds.GetCenter());

        foreach (var side in DirectionExtensions.All)
        {
            var exit = Room.GetExit(side);
            foreach (var rect in WorldLayout.WallRects(side, exit != ExitState.None))
                DrawWall(rect, side);
        }

        DrawCrystals();
        DrawProps(); // after walls so racks/shelves/bells sit in front of the tall north face

        foreach (var side in DirectionExtensions.All)
            if (Room.GetExit(side) == ExitState.Locked) DrawLockedDoor(side);

        // Bottom-right: the top corners belong to the HUD and minimap.
        DrawString(UiFonts.Regular, new Vector2(0, bounds.Size.Y - 14), Label(Room.Type), HorizontalAlignment.Right, bounds.Size.X - 14, 8, Palette.Dim);
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

    /// <summary>Ambient occlusion: soft dark bands along the floor where it meets the walls.</summary>
    private void DrawFloorAo(Rect2 bounds)
    {
        const float depth = 12f;
        var ao = new Color(Palette.Void, 0.4f);
        var soft = new Color(Palette.Void, 0.18f);
        // North (under the tall face), south, west, east — keep doorway centres clearer.
        DrawRect(new Rect2(0, WorldLayout.WallThickness, bounds.Size.X, depth), ao);
        DrawRect(new Rect2(0, bounds.Size.Y - WorldLayout.WallThickness - depth, bounds.Size.X, depth), soft);
        DrawRect(new Rect2(WorldLayout.WallThickness, 0, depth, bounds.Size.Y), soft);
        DrawRect(new Rect2(bounds.Size.X - WorldLayout.WallThickness - depth, 0, depth, bounds.Size.Y), soft);
    }

    private void DrawWall(Rect2 rect, Direction side)
    {
        // North: draw the tall 3/4 front face first (into the room), then the top ledge.
        if (side == Direction.North)
        {
            var face = new Rect2(rect.Position.X, rect.End.Y, rect.Size.X, WallFaceHeight);
            // Brick face in stone violet, darkening toward the floor so it reads as a vertical surface.
            DrawRect(face, new Color("3a2163"));
            DrawRect(new Rect2(face.Position.X, face.Position.Y + face.Size.Y * 0.55f, face.Size.X, face.Size.Y * 0.45f), new Color("2a1a45"));
            DrawBrickCourses(face, horizontal: true, side);
            // Lit top edge, a dark shadow line where the face meets the floor, and a soft cast shadow on the floor.
            DrawLine(face.Position, new Vector2(face.End.X, face.Position.Y), Palette.WallEdge, 1.5f);
            DrawLine(new Vector2(face.Position.X, face.End.Y - 1), new Vector2(face.End.X, face.End.Y - 1), Palette.Void, 1.5f);
            DrawRect(new Rect2(face.Position.X, face.End.Y, face.Size.X, 5), new Color(Palette.Void, 0.35f));
        }

        DrawRect(rect, Palette.Wall);
        DrawBrickCourses(rect, horizontal: side is Direction.North or Direction.South, side);

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

    private void DrawBrickCourses(Rect2 rect, bool horizontal, Direction side)
    {
        var mortar = new Color(Palette.Void, 0.55f);
        if (horizontal)
        {
            float offset = Hash(Room.Id.X, Room.Id.Y, 0, (int)side) % 16;
            for (float x = rect.Position.X - offset; x < rect.End.X; x += 16)
                if (x > rect.Position.X) DrawLine(new Vector2(x, rect.Position.Y), new Vector2(x, rect.End.Y), mortar);
            for (float y = rect.Position.Y + 6; y < rect.End.Y; y += 6)
                DrawLine(new Vector2(rect.Position.X, y), new Vector2(rect.End.X, y), new Color(Palette.Void, 0.35f));
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
    }

    /// <summary>Decorative props by room type. No collision — stay clear of doorway lanes (~centre of each side).</summary>
    private void DrawProps()
    {
        switch (Room.Type)
        {
            case RoomType.Library: DrawLibraryProps(); break;
            case RoomType.Merchant: DrawMerchantProps(); break;
            case RoomType.GuardPost: DrawGuardProps(); break;
            case RoomType.Vault: DrawVaultProps(); break;
            case RoomType.RelicChamber: DrawRelicProps(); break;
            case RoomType.EchoChamber: DrawEchoProps(); break;
            case RoomType.HushRoom: DrawHushProps(); break;
        }
    }

    private void DrawLibraryProps()
    {
        // Shelves along the north face (left and right of the door gap).
        // A row against the north face right of the doorway, plus one by the south wall — clear of the HUD panels.
        DrawShelf(new Vector2(280, WorldLayout.WallThickness + WallFaceHeight + 14));
        DrawShelf(new Vector2(334, WorldLayout.WallThickness + WallFaceHeight + 14));
        DrawShelf(new Vector2(286, WorldLayout.RoomHeight - 14));
    }

    private void DrawShelf(Vector2 at)
    {
        DrawRect(new Rect2(at.X, at.Y, 48, 4), Palette.Wall);
        DrawRect(new Rect2(at.X, at.Y - 16, 48, 16), new Color("2a1a45"));
        DrawRect(new Rect2(at.X, at.Y - 16, 48, 16), Palette.Void, filled: false, width: 1f);
        DrawLine(new Vector2(at.X, at.Y - 16), new Vector2(at.X + 48, at.Y - 16), Palette.WallEdge);
        for (int i = 0; i < 5; i++)
        {
            float x = at.X + 4 + i * 9;
            var book = new Color[] { Palette.Accent, Palette.WallEdge, new Color("c8a2ff"), Palette.Dim, Palette.White }[i];
            DrawRect(new Rect2(x, at.Y - 14, 6, 12), book);
        }
    }

    private void DrawMerchantProps()
    {
        // Rug under the middle of the room.
        var rug = new Rect2(WorldLayout.RoomWidth / 2 - 48, WorldLayout.RoomHeight / 2 + 10, 96, 36);
        DrawRect(rug, new Color("2a1a45"));
        DrawRect(rug, Palette.Accent, filled: false, width: 1f);
        DrawRect(rug.Grow(-4), new Color(Palette.Wall, 0.35f), filled: false, width: 1f);
        // Counter
        var counter = new Rect2(WorldLayout.RoomWidth / 2 - 40, WorldLayout.RoomHeight / 2 - 8, 80, 14);
        DrawRect(counter, Palette.Wall);
        DrawRect(counter, Palette.Void, filled: false, width: 1f);
        DrawLine(counter.Position, new Vector2(counter.End.X, counter.Position.Y), Palette.WallEdge);
    }

    private void DrawGuardProps()
    {
        // Against the side walls, away from doorways, chests and the HUD.
        DrawWeaponRack(new Vector2(WorldLayout.WallThickness + 4, 166));
        DrawWeaponRack(new Vector2(WorldLayout.RoomWidth - WorldLayout.WallThickness - 20, 70));
        // Training ring so the post reads even when racks sit in shadow.
        var c = WorldLayout.RoomSize / 2f + new Vector2(0, 28);
        DrawArc(c, 22, 0, Mathf.Tau, 24, new Color(Palette.Dim, 0.55f), 1f);
    }

    private void DrawWeaponRack(Vector2 at)
    {
        DrawRect(new Rect2(at.X, at.Y, 16, 28), Palette.Wall);
        DrawRect(new Rect2(at.X, at.Y, 16, 28), Palette.Void, filled: false, width: 1f);
        DrawLine(at + new Vector2(0, 0), at + new Vector2(16, 0), Palette.WallEdge);
        DrawLine(at + new Vector2(4, 4), at + new Vector2(4, 24), Palette.Dim);
        DrawLine(at + new Vector2(12, 4), at + new Vector2(12, 24), Palette.Dim);
        DrawRect(new Rect2(at.X + 2, at.Y + 6, 3, 10), new Color("7d6a9e"));
        DrawRect(new Rect2(at.X + 11, at.Y + 8, 3, 12), new Color("c8a2ff"));
        DrawRect(new Rect2(at.X + 2, at.Y + 5, 3, 2), Palette.White);
        DrawRect(new Rect2(at.X + 11, at.Y + 7, 3, 2), Palette.White);
    }

    private void DrawVaultProps()
    {
        DrawGoldPile(new Vector2(300, WorldLayout.RoomHeight - 40));
        DrawGoldPile(new Vector2(440, 200));
        DrawGoldPile(new Vector2(120, 104));
    }

    private void DrawGoldPile(Vector2 at)
    {
        DrawColoredPolygon([at + new Vector2(-8, 4), at + new Vector2(0, -6), at + new Vector2(8, 4)], new Color("2a1a45"));
        for (int i = 0; i < 5; i++)
        {
            float x = at.X - 6 + i * 3;
            float y = at.Y - 2 + (i % 2);
            DrawRect(new Rect2(x, y, 2, 2), i == 2 ? Palette.White : new Color("e3c7ff"));
        }
    }

    private void DrawRelicProps()
    {
        DrawPillar(new Vector2(110, 96));
        DrawPillar(new Vector2(WorldLayout.RoomWidth - 110, 96));
        DrawPillar(new Vector2(110, 186));
        DrawPillar(new Vector2(WorldLayout.RoomWidth - 110, 186));
        // Rune floor ring in the centre.
        var c = WorldLayout.RoomSize / 2f;
        DrawArc(c, 42, 0, Mathf.Tau, 32, new Color(Palette.Accent, 0.45f), 1f);
        for (int i = 0; i < 8; i++)
        {
            var p = c + Vector2.FromAngle(i * Mathf.Tau / 8f) * 42;
            DrawRect(new Rect2(p.X - 1, p.Y - 1, 2, 2), Palette.White);
        }
    }

    private void DrawPillar(Vector2 at)
    {
        DrawRect(new Rect2(at.X - 6, at.Y - 20, 12, 28), new Color("2a1a45"));
        DrawRect(new Rect2(at.X - 7, at.Y - 22, 14, 4), Palette.Wall);
        DrawRect(new Rect2(at.X - 7, at.Y + 6, 14, 4), Palette.Wall);
        DrawLine(new Vector2(at.X - 6, at.Y - 20), new Vector2(at.X - 6, at.Y + 6), Palette.WallEdge);
        DrawRect(new Rect2(at.X - 1, at.Y - 10, 2, 2), Palette.Accent);
    }

    private void DrawEchoProps()
    {
        // Bells hang into the lit centre so they stay readable under the player light.
        // Away from the HUD corners, doorway lanes and chest spots.
        DrawBell(new Vector2(120, 80));
        DrawBell(new Vector2(WorldLayout.RoomWidth - 120, 80));
        DrawBell(new Vector2(196, 168));
        DrawBell(new Vector2(WorldLayout.RoomWidth - 196, 168));
    }

    private void DrawBell(Vector2 at)
    {
        DrawLine(at, at + new Vector2(0, 10), Palette.WallEdge);
        DrawColoredPolygon([at + new Vector2(-6, 10), at + new Vector2(0, 22), at + new Vector2(6, 10)], new Color("c8a2ff"));
        DrawColoredPolygon([at + new Vector2(-6, 10), at + new Vector2(0, 22), at + new Vector2(-1, 10)], Palette.WallEdge);
        DrawRect(new Rect2(at.X - 1, at.Y + 20, 2, 3), Palette.White);
    }

    private void DrawHushProps()
    {
        // Soft carpets — large muted rectangles.
        DrawRect(new Rect2(64, 70, 120, 60), new Color("1d1030"));
        DrawRect(new Rect2(64, 70, 120, 60), new Color(Palette.Dim, 0.35f), filled: false, width: 1f);
        DrawRect(new Rect2(WorldLayout.RoomWidth - 184, 120, 100, 50), new Color("1d1030"));
        DrawRect(new Rect2(WorldLayout.RoomWidth - 184, 120, 100, 50), new Color(Palette.Dim, 0.35f), filled: false, width: 1f);
    }

    /// <summary>A few amethyst clusters growing from the wall base, never in front of a doorway.</summary>
    private void DrawCrystals()
    {
        int i = 0;
        foreach (var at in CrystalSpots()) DrawCrystal(at, Hash(Room.Id.X, Room.Id.Y, i++, 7));
    }

    /// <summary>Where this room's crystals grow (the same every time for the same room).</summary>
    private IEnumerable<Vector2> CrystalSpots()
    {
        uint h = Hash(Room.Id.X, Room.Id.Y, 99, 99);
        int count = 2 + (int)(h % 3);
        for (int i = 0; i < count; i++)
        {
            uint r = Hash(Room.Id.X, Room.Id.Y, i, 7);
            var side = DirectionExtensions.All[(int)(r % 4)];
            float along = 0.1f + (r / 4 % 1000) / 1000f * 0.8f;
            if (Mathf.Abs(along - 0.5f) < 0.14f) along += 0.2f; // keep door lanes clear

            yield return side switch
            {
                Direction.North => new Vector2(along * WorldLayout.RoomWidth, WorldLayout.WallThickness + WallFaceHeight + 3),
                Direction.South => new Vector2(along * WorldLayout.RoomWidth, WorldLayout.RoomHeight - WorldLayout.WallThickness - 1),
                Direction.West => new Vector2(WorldLayout.WallThickness + 4, along * WorldLayout.RoomHeight),
                _ => new Vector2(WorldLayout.RoomWidth - WorldLayout.WallThickness - 4, along * WorldLayout.RoomHeight),
            };
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
        if (_collapsing)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin((float)_time * 6f);
            DrawCircle(center, 30, new Color(Palette.White, 0.05f + 0.08f * pulse));
            DrawArc(center, 34 + pulse * 2, 0, Mathf.Tau, 48, Palette.White, 1.5f);
            DrawString(UiFonts.Regular, new Vector2(center.X - 40, center.Y + 50), "ESCAPE HERE", HorizontalAlignment.Center, 80, 8, Palette.White);
        }
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
