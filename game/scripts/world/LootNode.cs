using Godot;
using VioVau.Core.Dungeon;
using VioVau.Game.UI;

namespace VioVau.Game.World;

/// <summary>A chest or key on the floor. No collision: chests are used with Interact, keys are picked up by touch.</summary>
public partial class LootNode : Node2D
{
    private bool _highlighted;

    public LootItem Item { get; private set; } = null!;

    public bool Highlighted
    {
        get => _highlighted;
        set
        {
            if (_highlighted == value) return;
            _highlighted = value;
            QueueRedraw();
        }
    }

    public LootNode Init(LootItem item)
    {
        Item = item;
        Name = $"{item.Kind}_{item.Id.Value}";
        Position = WorldLayout.LootPosition(item.Room, item.Position);
        ZIndex = 5;
        return this;
    }

    /// <summary>Call after the item was claimed.</summary>
    public void Refresh()
    {
        if (Item.Kind == LootKind.Key && Item.Claimed) Visible = false;
        QueueRedraw();
    }

    public override void _Ready() => Refresh();

    public override void _Draw()
    {
        if (Item.Kind == LootKind.Chest) DrawChest();
        else DrawKey();
    }

    private void DrawChest()
    {
        var body = new Rect2(-7, -4, 14, 9);
        if (Item.Claimed)
        {
            // Open and empty.
            DrawRect(body, Palette.Void);
            DrawRect(body, Palette.Wall, filled: false, width: 1f);
            DrawRect(new Rect2(-7, -8, 14, 3), Palette.Wall);
            return;
        }

        var edge = _highlighted ? Palette.White : Palette.Accent;
        DrawRect(body, Palette.Wall);
        DrawRect(body, edge, filled: false, width: 1f);
        DrawLine(new Vector2(-7, -1), new Vector2(7, -1), edge);
        DrawRect(new Rect2(-1, -2, 2, 3), Palette.White);

        if (_highlighted)
            DrawString(ThemeDB.FallbackFont, new Vector2(-12, -9), $"{Item.Points}p", HorizontalAlignment.Center, 24, 8, Palette.White);
    }

    private void DrawKey()
    {
        DrawArc(new Vector2(-3, 0), 2.5f, 0, Mathf.Tau, 12, Palette.White, 1f);
        DrawLine(new Vector2(-0.5f, 0), new Vector2(5, 0), Palette.White);
        DrawLine(new Vector2(3, 0), new Vector2(3, 2), Palette.White);
        DrawLine(new Vector2(5, 0), new Vector2(5, 2), Palette.White);
    }
}
