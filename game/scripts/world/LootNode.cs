using Godot;
using VioVau.Core.Dungeon;
using VioVau.Game.UI;

namespace VioVau.Game.World;

/// <summary>
/// A chest or key on the floor, drawn with the imported item art (falls back to shapes if missing).
/// No collision: chests are used with Interact, keys are picked up by touch.
/// </summary>
public partial class LootNode : Node2D
{
    private const string Folder = "res://assets/sprites/items";

    private bool _highlighted;
    private bool _fancy;
    private double _time;
    private Texture2D? _closed, _open, _key;

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

    /// <param name="fancy">Use the ornate vault chest art (chests inside Vaults).</param>
    public LootNode Init(LootItem item, bool fancy)
    {
        Item = item;
        _fancy = fancy;
        Name = $"{item.Kind}_{item.Id.Value}";
        Position = WorldLayout.LootPosition(item.Room, item.Position);
        ZIndex = 5;
        return this;
    }

    public override void _Ready()
    {
        _closed = TryLoad(_fancy ? "chest_vault_closed" : "chest_closed");
        _open = TryLoad(_fancy ? "chest_vault_open" : "chest_open");
        _key = TryLoad("key");
        Refresh();
    }

    /// <summary>Call after the item was claimed.</summary>
    public void Refresh()
    {
        if (Item.Kind == LootKind.Key && Item.Claimed) Visible = false;
        SetProcess(Item.Kind == LootKind.Key && !Item.Claimed);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Keys bob gently so they catch the eye.
        _time += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (Item.Kind == LootKind.Chest) DrawChest();
        else DrawKey();
    }

    private void DrawChest()
    {
        var texture = Item.Claimed ? _open : _closed;
        if (texture is null)
        {
            DrawFallbackChest();
            return;
        }

        // Item art is bottom-anchored on its canvas; stand it on this position.
        var size = texture.GetSize();
        DrawTexture(texture, new Vector2(-size.X / 2f, -size.Y + 4), Item.Claimed ? new Color(1, 1, 1, 0.75f) : Colors.White);

        if (_highlighted && !Item.Claimed)
        {
            DrawBrackets(new Rect2(-10, -size.Y + 6, 20, size.Y - 1));
            DrawString(ThemeDB.FallbackFont, new Vector2(-12, -size.Y + 2), $"{Item.Points}p", HorizontalAlignment.Center, 24, 8, Palette.White);
        }
    }

    private void DrawKey()
    {
        float bob = Mathf.Round(Mathf.Sin((float)_time * 3f) * 1.5f);
        if (_key is null)
        {
            DrawArc(new Vector2(-3, bob), 2.5f, 0, Mathf.Tau, 12, Palette.White, 1f);
            DrawLine(new Vector2(-0.5f, bob), new Vector2(5, bob), Palette.White);
            return;
        }

        var size = _key.GetSize();
        DrawSetTransform(new Vector2(0, 2), 0, new Vector2(1f, 0.4f));
        DrawCircle(Vector2.Zero, 4f, new Color(Palette.Void, 0.5f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        DrawTexture(_key, new Vector2(-size.X / 2f, -size.Y + 1 + bob));
    }

    private void DrawBrackets(Rect2 r)
    {
        const float l = 3;
        var c = Palette.White;
        foreach (var (corner, dx, dy) in new[] { (r.Position, 1, 1), (new Vector2(r.End.X, r.Position.Y), -1, 1), (new Vector2(r.Position.X, r.End.Y), 1, -1), (r.End, -1, -1) })
        {
            DrawLine(corner, corner + new Vector2(dx * l, 0), c);
            DrawLine(corner, corner + new Vector2(0, dy * l), c);
        }
    }

    private void DrawFallbackChest()
    {
        var body = new Rect2(-7, -4, 14, 9);
        var edge = _highlighted ? Palette.White : Palette.Accent;
        DrawRect(body, Item.Claimed ? Palette.Void : Palette.Wall);
        DrawRect(body, edge, filled: false, width: 1f);
    }

    private static Texture2D? TryLoad(string name)
    {
        var path = $"{Folder}/{name}.png";
        return ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
    }
}
