using Godot;
using VioVau.Core;
using VioVau.Core.Cards;
using VioVau.Game.Input;

namespace VioVau.Game.UI;

/// <summary>
/// Bottom of the screen: each local player's hand of cards (name, category, noise, button), their deck and
/// discard counts, and short messages for that player ("Lockpick needs a locked door in reach").
/// </summary>
public partial class HandHud : Control
{
    public readonly record struct LocalHand(PlayerId Player, int ColorIndex, InputDevice Device);

    private const float CardHeight = 44f, Gap = 3f, Bottom = 3f, ArtSize = 16f;
    private const double MessageSeconds = 2.5;

    private RunView _view = null!;
    private Func<IReadOnlyList<LocalHand>> _hands = () => [];
    private readonly Dictionary<PlayerId, (string Text, double Age)> _messages = new();
    private Font _font = null!;

    public HandHud Init(RunView view, Func<IReadOnlyList<LocalHand>> hands)
    {
        _view = view;
        _hands = hands;
        return this;
    }

    public override void _Ready()
    {
        _font = UiFonts.Regular;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    public void ShowMessage(PlayerId player, string text) => _messages[player] = (text, 0);

    public override void _Process(double delta)
    {
        foreach (var key in _messages.Keys.ToList())
        {
            var (text, age) = _messages[key];
            if (age + delta > MessageSeconds) _messages.Remove(key);
            else _messages[key] = (text, age + delta);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var screen = GetViewportRect().Size;
        var hands = _hands();
        if (hands.Count == 0) return;

        float regionWidth = screen.X / hands.Count;
        for (int i = 0; i < hands.Count; i++)
            DrawHand(hands[i], new Rect2(i * regionWidth, 0, regionWidth, screen.Y));
    }

    private void DrawHand(LocalHand hand, Rect2 region)
    {
        if (_view.Player(hand.Player) is not { } player) return;
        var cards = _view.Config.CardSet;
        int slots = player.Hand.Length;

        float width = Mathf.Min(62f, (region.Size.X - 8 - Gap * (slots - 1)) / slots);
        float total = slots * width + (slots - 1) * Gap;
        // Alone on screen: bottom-left, clear of the south doorway. Split screen: centered under that player's view.
        bool alone = GetViewportRect().Size.X - region.Size.X < 1f;
        float x0 = alone ? region.Position.X + 6 : region.Position.X + (region.Size.X - total) / 2f;
        float y = region.End.Y - Bottom - CardHeight;

        for (int slot = 0; slot < slots; slot++)
        {
            var rect = new Rect2(x0 + slot * (width + Gap), y, width, CardHeight);
            var label = slot < hand.Device.CardLabels.Length ? hand.Device.CardLabels[slot] : "";

            if (player.Hand[slot] is not { } id || cards.Get(id) is not { } card)
            {
                DrawRect(rect, new Color(Palette.Void, 0.6f));
                DrawRect(rect, Palette.Floor, filled: false, width: 1f);
                DrawString(_font, rect.Position + new Vector2(3, CardHeight - 3), label, HorizontalAlignment.Left, -1, 8, Palette.Wall);
                continue;
            }

            var color = CardStyle.CategoryColor(card.Category);
            DrawRect(rect, Palette.Void);
            DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, 3)), color);
            DrawRect(rect, CardStyle.RarityColor(card.Rarity), filled: false, width: 1f);

            // Layout, top to bottom: art · name · [button] ……… noise. Nothing overlaps.
            if (CardArt.Get(card.Id) is { } art)
            {
                float ax = rect.Position.X + (rect.Size.X - ArtSize) / 2f;
                DrawTextureRect(art, new Rect2(ax, rect.Position.Y + 5, ArtSize, ArtSize), false);
            }
            var name = CardStyle.FitName(_font, card, rect.Size.X - 6, 8);
            DrawString(_font, new Vector2(rect.Position.X, rect.Position.Y + 30), name, HorizontalAlignment.Center, rect.Size.X, 8, Palette.White);

            // Button (bottom-left) and noise (bottom-right).
            var button = new Rect2(rect.Position + new Vector2(2, CardHeight - 11), new Vector2(9, 9));
            DrawRect(button, Palette.White);
            DrawString(_font, new Vector2(button.Position.X, button.End.Y - 2), label, HorizontalAlignment.Center, button.Size.X, 8, Palette.Void);
            var noise = card.Noise > 0 ? $"{card.Noise:0.#}" : "-";
            DrawString(_font, new Vector2(rect.Position.X, rect.End.Y - 3), $"{noise}", HorizontalAlignment.Right, rect.Size.X - 3, 8, card.Noise > 0 ? color : Palette.Dim);
        }

        // Deck / discard above the hand, in the player's color.
        var info = $"P{hand.ColorIndex + 1}  deck {player.DrawPile} · discard {player.DiscardPile}";
        DrawString(_font, new Vector2(x0, y - 3), info, HorizontalAlignment.Left, -1, 8, Palette.PlayerColor(hand.ColorIndex));

        if (_messages.TryGetValue(hand.Player, out var message))
        {
            float alpha = (float)Math.Min(1, (MessageSeconds - message.Age) / 0.5);
            var band = new Rect2(region.Position.X + 4, y - 24, region.Size.X - 8, 11);
            DrawRect(band, new Color(Palette.Void, 0.85f * alpha));
            DrawString(_font, new Vector2(band.Position.X, band.End.Y - 2), message.Text, HorizontalAlignment.Center, band.Size.X, 8, new Color(Palette.White, alpha));
        }
    }
}
