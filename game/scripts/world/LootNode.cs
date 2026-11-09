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
            QueueRedraw();
            _tooltip?.QueueRedraw();
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

    /// <summary>
    /// Highlight brackets and labels live on their own layer above players and enemies, so the item itself
    /// stays at floor level (players walk in front of lecterns and chests) while its tooltip is never covered.
    /// </summary>
    private Node2D? _tooltip;

    public override void _Ready()
    {
        _tooltip = new Node2D { Name = "Tooltip", ZIndex = 20, Material = Lights.Unshaded };
        _tooltip.Draw += () => DrawTooltip(_tooltip);
        AddChild(_tooltip);

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
        var card = new Rect2(-6, -18, 12, 16);
        DrawRect(card, Palette.Void);
        DrawRect(card.Grow(-1), Palette.Floor);
        if (_card is not null && CardArt.Get(_card.Id) is { } art)
            DrawTextureRect(art, new Rect2(card.Position + new Vector2(2, 2), new Vector2(8, 8)), false);
        else
            DrawRect(new Rect2(card.Position + Vector2.One, new Vector2(10, 2)), color);
        DrawRect(card, _card is null ? Palette.Wall : CardStyle.RarityColor(_card.Rarity), filled: false, width: 1f);

        var font = UiFonts.Regular;
        if (Item.Price > 0)
            DrawString(font, new Vector2(-12, 14), $"{Item.Price}p", HorizontalAlignment.Center, 24, 8, Palette.White);

    }

    // ── Tooltip layer ───────────────────────────────────────────────────────

    private void DrawTooltip(CanvasItem t)
    {
        if (!_highlighted || Item.Claimed) return;
        var font = UiFonts.Regular;

        switch (Item.Kind)
        {
            case LootKind.Chest:
                var size = (Item.Claimed ? _open : _closed)?.GetSize() ?? new Vector2(16, 12);
                DrawBrackets(t, new Rect2(-10, -size.Y + 6, 20, size.Y - 1));
                Panel(t, font, -size.Y - 4, [($"{Item.Points}p", Palette.White)]);
                break;

            case LootKind.Bag:
                DrawBrackets(t, new Rect2(-7, -11, 14, 13));
                Panel(t, font, -16, [(Item.Keys > 0 ? $"{Item.Points}p  K{Item.Keys}" : $"{Item.Points}p", Palette.White)]);
                break;

            case LootKind.Relic:
                DrawBrackets(t, new Rect2(-10, -24, 20, 28));
                Panel(t, font, -28, [($"RELIC  {Item.Points}p", Palette.White), ("very loud  ·  carry one", Palette.Dim)]);
                break;

            case LootKind.Card when _card is not null:
                DrawBrackets(t, new Rect2(-8, -18, 16, 22));
                var title = Item.Price > 0 ? $"{_card.Name}  ·  {Item.Price}p" : $"{_card.Name}  ·  free";
                Panel(t, font, -22, [(title, CardStyle.CategoryColor(_card.Category)), .. Wrap(font, _card.Text, 150).Select(l => (l, Palette.White))]);
                break;
        }
    }

    /// <summary>A dark panel sized to its lines, bottom edge at <paramref name="bottom"/>, centred on the item.</summary>
    private static void Panel(CanvasItem t, Font font, float bottom, IReadOnlyList<(string Text, Color Color)> lines)
    {
        const int size = 8;
        const float lineHeight = 9, pad = 3;
        float width = lines.Max(l => font.GetStringSize(l.Text, HorizontalAlignment.Left, -1, size).X) + pad * 2;
        var rect = new Rect2(-width / 2f, bottom - lines.Count * lineHeight - pad * 2 + 2, width, lines.Count * lineHeight + pad * 2 - 2);
        t.DrawRect(rect, new Color(Palette.Void, 0.9f));
        for (int i = 0; i < lines.Count; i++)
            t.DrawString(font, new Vector2(rect.Position.X, rect.Position.Y + pad + 7 + i * lineHeight), lines[i].Text,
                HorizontalAlignment.Center, rect.Size.X, size, lines[i].Color);
    }

    /// <summary>Greedy word wrap by measured width.</summary>
    private static IEnumerable<string> Wrap(Font font, string text, float maxWidth)
    {
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var candidate = line.Length == 0 ? word : $"{line} {word}";
            if (line.Length > 0 && font.GetStringSize(candidate, HorizontalAlignment.Left, -1, 8).X > maxWidth)
            {
                yield return line;
                line = word;
            }
            else line = candidate;
        }
        if (line.Length > 0) yield return line;
    }

    private static void DrawBrackets(CanvasItem t, Rect2 r)
    {
        const float l = 3;
        var c = Palette.White;
        foreach (var (corner, dx, dy) in new[] { (r.Position, 1, 1), (new Vector2(r.End.X, r.Position.Y), -1, 1), (new Vector2(r.Position.X, r.End.Y), 1, -1), (r.End, -1, -1) })
        {
            t.DrawLine(corner, corner + new Vector2(dx * l, 0), c);
            t.DrawLine(corner, corner + new Vector2(0, dy * l), c);
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
