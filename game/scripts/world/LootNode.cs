using Godot;
using VioVau.Core.Cards;
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
    private Texture2D? _closed, _open, _key, _relic;
    private CardDefinition? _card;

    public LootItem Item { get; private set; } = null!;

    public bool Highlighted
    {
        get => _highlighted;
        set
        {
            if (_highlighted == value) return;
            _highlighted = value;
            ZIndex = value ? 25 : 5; // tooltips above players
            QueueRedraw();
        }
    }

    /// <param name="fancy">Use the ornate vault chest art (chests inside Vaults).</param>
    /// <param name="card">The card's data, for card offers.</param>
    public LootNode Init(LootItem item, bool fancy, CardDefinition? card = null)
    {
        Item = item;
        _fancy = fancy;
        _card = card;
        Name = $"{item.Kind}_{item.Id.Value}";
        Position = WorldLayout.LootPosition(item.Room, item.Position);
        ZIndex = 5;
        return this;
    }

    private PointLight2D? _glow;

    public override void _Ready()
    {
        // Treasure that matters glows (white = value).
        _glow = Item.Kind switch
        {
            LootKind.Relic => Lights.Point(80f, Lights.Bone, 0.9f),
            LootKind.Key => Lights.Point(22f, Lights.Bone, 0.55f),
            LootKind.Bag => Lights.Point(20f, Lights.Bone, 0.4f),
            _ => null,
        };
        if (_glow is not null)
        {
            _glow.Position = new Vector2(0, -8);
            AddChild(_glow);
        }
        _closed = TryLoad(_fancy ? "chest_vault_closed" : "chest_closed");
        _open = TryLoad(_fancy ? "chest_vault_open" : "chest_open");
        _key = TryLoad("key");
        _relic = TryLoad("relic_crown");
        Refresh();
    }

    /// <summary>Call after the item was claimed.</summary>
    public void Refresh()
    {
        if (Item.Kind is LootKind.Key or LootKind.Bag or LootKind.Relic && Item.Claimed) Visible = false;
        if (Item.Claimed && _glow is not null) _glow.Enabled = false;
        SetProcess(Item.Kind is LootKind.Key or LootKind.Bag or LootKind.Relic && !Item.Claimed);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Keys bob gently so they catch the eye; the Relic's light breathes.
        _time += delta;
        if (_glow is not null && Item.Kind == LootKind.Relic) _glow.Energy = 0.8f + 0.25f * Mathf.Sin((float)_time * 2.5f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        switch (Item.Kind)
        {
            case LootKind.Chest: DrawChest(); break;
            case LootKind.Key: DrawKey(); break;
            case LootKind.Card: DrawCardOffer(); break;
            case LootKind.Bag: DrawBag(); break;
            case LootKind.Relic: DrawRelic(); break;
        }
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

    /// <summary>The prize: a crown on a pedestal, glowing and slowly bobbing.</summary>
    private void DrawRelic()
    {
        // Pedestal.
        DrawRect(new Rect2(-8, -2, 16, 6), Palette.Wall);
        DrawRect(new Rect2(-8, -2, 16, 6), Palette.Void, filled: false, width: 1f);
        DrawRect(new Rect2(-8, -2, 16, 1), Palette.WallEdge);

        // Glow (white = value).
        float pulse = 0.5f + 0.5f * Mathf.Sin((float)_time * 2.5f);
        DrawCircle(new Vector2(0, -10), 11f + pulse * 2f, new Color(Palette.White, 0.06f + 0.06f * pulse));
        DrawCircle(new Vector2(0, -10), 7f, new Color(Palette.White, 0.08f));

        float bob = Mathf.Round(Mathf.Sin((float)_time * 2f) * 1.5f);
        if (_relic is not null)
        {
            var size = _relic.GetSize();
            DrawTexture(_relic, new Vector2(-size.X / 2f, -size.Y - 2 + bob));
        }
        else
        {
            DrawRect(new Rect2(-5, -12 + bob, 10, 6), Palette.White);
        }

        if (_highlighted)
        {
            DrawBrackets(new Rect2(-10, -24, 20, 28));
            DrawRect(new Rect2(-46, -44, 92, 18), new Color(Palette.Void, 0.9f));
            DrawString(ThemeDB.FallbackFont, new Vector2(-45, -36), $"RELIC  ·  {Item.Points}p", HorizontalAlignment.Center, 90, 8, Palette.White);
            DrawString(ThemeDB.FallbackFont, new Vector2(-45, -28), "very loud · carry one", HorizontalAlignment.Center, 90, 8, Palette.Dim);
        }
    }

    /// <summary>A fallen player's sack: everything they carried, for whoever gets there first.</summary>
    private void DrawBag()
    {
        DrawSetTransform(new Vector2(0, 3), 0, new Vector2(1f, 0.4f));
        DrawCircle(Vector2.Zero, 6f, new Color(Palette.Void, 0.5f));
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        DrawCircle(new Vector2(0, -3), 5f, Palette.Void);
        DrawCircle(new Vector2(0, -3), 4f, Palette.WallEdge);
        DrawRect(new Rect2(-2, -9, 4, 2), Palette.Void);
        DrawRect(new Rect2(-1, -10, 2, 1), Palette.Wall);
        // A glint of treasure that pulses.
        float glint = 0.6f + 0.4f * Mathf.Sin((float)_time * 4f);
        DrawRect(new Rect2(1, -5, 1, 1), new Color(Palette.White, glint));

        if (_highlighted)
        {
            DrawBrackets(new Rect2(-7, -11, 14, 13));
            var label = Item.Keys > 0 ? $"{Item.Points}p  K{Item.Keys}" : $"{Item.Points}p";
            DrawString(ThemeDB.FallbackFont, new Vector2(-20, -14), label, HorizontalAlignment.Center, 40, 8, Palette.White);
        }
    }

    /// <summary>A card on a lectern (Library) or pedestal (Merchant, with a price tag).</summary>
    private void DrawCardOffer()
    {
        // Lectern.
        DrawRect(new Rect2(-7, -2, 14, 5), Palette.Wall);
        DrawRect(new Rect2(-7, -2, 14, 5), Palette.Void, filled: false, width: 1f);
        DrawRect(new Rect2(-2, 3, 4, 4), Palette.Floor);
        if (Item.Claimed) return;

        var color = _card is null ? Palette.Accent : CardStyle.CategoryColor(_card.Category);
        var card = new Rect2(-5, -15, 10, 13);
        DrawRect(card, Palette.Void);
        DrawRect(card.Grow(-1), Palette.Floor);
        DrawRect(new Rect2(card.Position + Vector2.One, new Vector2(8, 2)), color);
        DrawRect(card, _card is null ? Palette.Wall : CardStyle.RarityColor(_card.Rarity), filled: false, width: 1f);

        var font = ThemeDB.FallbackFont;
        if (Item.Price > 0)
            DrawString(font, new Vector2(-12, 14), $"{Item.Price}p", HorizontalAlignment.Center, 24, 8, Palette.White);

        if (_highlighted && _card is not null)
        {
            DrawBrackets(new Rect2(-8, -18, 16, 22));
            var title = Item.Price > 0 ? $"{_card.Name}  ·  {Item.Price}p" : $"{_card.Name}  ·  free";
            DrawRect(new Rect2(-62, -46, 124, 26), new Color(Palette.Void, 0.9f));
            DrawString(font, new Vector2(-60, -37), title, HorizontalAlignment.Center, 120, 8, color);
            DrawMultilineString(font, new Vector2(-60, -28), _card.Text, HorizontalAlignment.Center, 120, 8, 2, Palette.White);
        }
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
