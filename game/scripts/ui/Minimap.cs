using Godot;
using VioVau.Core;
using VioVau.Core.Dungeon;

namespace VioVau.Game.UI;

/// <summary>Top-right map of the current floor: revealed rooms, their open exits, and where every player is.</summary>
public partial class Minimap : Control
{
    public readonly record struct Marker(RoomId Room, int ColorIndex);

    private const int CellW = 10, CellH = 6, Gap = 2, Padding = 3;

    private RunView _view = null!;
    private Func<IEnumerable<Marker>> _markers = () => [];

    public Minimap Init(RunView view, Func<IEnumerable<Marker>> markers)
    {
        _view = view;
        _markers = markers;
        return this;
    }

    public override void _Ready()
    {
        var floor = _view.Dungeon.Floors[0];
        var size = new Vector2(
            floor.Width * (CellW + Gap) - Gap + Padding * 2,
            floor.Height * (CellH + Gap) - Gap + Padding * 2);

        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -size.X - 4;
        OffsetRight = -4;
        OffsetTop = 4;
        OffsetBottom = 4 + size.Y;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var floor = _view.Dungeon.Floors[0];
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(Palette.Void, 0.85f));

        foreach (var room in floor.Rooms)
        {
            if (!_view.IsRevealed(room.Id)) continue;

            var cell = CellRect(room.Id);
            var fill = room.Type switch
            {
                RoomType.Lobby => Palette.Accent,
                RoomType.RelicChamber => Palette.White,
                _ => Palette.Wall,
            };
            DrawRect(cell, fill);

            // A stub for every doorway (white = locked), so exits into unexplored rooms are visible too.
            foreach (var side in DirectionExtensions.All)
            {
                var exit = room.GetExit(side);
                if (exit == ExitState.None) continue;
                var c = cell.GetCenter();
                var link = side switch
                {
                    Direction.East => new Rect2(cell.End.X, c.Y - 1, Gap, 2),
                    Direction.West => new Rect2(cell.Position.X - Gap, c.Y - 1, Gap, 2),
                    Direction.South => new Rect2(c.X - 1, cell.End.Y, 2, Gap),
                    _ => new Rect2(c.X - 1, cell.Position.Y - Gap, 2, Gap),
                };
                DrawRect(link, exit == ExitState.Locked ? Palette.White : Palette.Dim);
            }
        }

        // Up to 4 player dots per room, laid out in a row.
        foreach (var group in _markers().GroupBy(m => m.Room))
        {
            var cell = CellRect(group.Key);
            int i = 0;
            foreach (var marker in group)
            {
                var dot = new Rect2(cell.Position.X + 1 + i * 2, cell.Position.Y + 2, 2, 2);
                DrawRect(dot, marker.ColorIndex == 0 ? Palette.Void : Palette.PlayerColor(marker.ColorIndex));
                if (marker.ColorIndex == 0) DrawRect(dot.Grow(-0.5f), Palette.White);
                i++;
            }
        }
    }

    private static Rect2 CellRect(RoomId id) =>
        new(Padding + id.X * (CellW + Gap), Padding + id.Y * (CellH + Gap), CellW, CellH);
}
