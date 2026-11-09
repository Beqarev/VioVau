using Godot;
using VioVau.Core.Cards;

namespace VioVau.Game.UI;

/// <summary>Category/rarity colours and short names for HUD cards (art lives in <see cref="CardArt"/>).</summary>
public static class CardStyle
{
    public static Color CategoryColor(CardCategory category) => category switch
    {
        CardCategory.Mobility => new Color("c8a2ff"),
        CardCategory.Stealth => Palette.Dim,
        CardCategory.Utility => Palette.Accent,
        CardCategory.Greed => Palette.White,
        CardCategory.Social => new Color("e3c7ff"),
        _ => Palette.WallEdge,
    };

    public static Color RarityColor(CardRarity rarity) => rarity switch
    {
        CardRarity.Legendary => Palette.White,
        CardRarity.Rare => Palette.Accent,
        _ => Palette.Wall,
    };

    /// <summary>
    /// The longest form of the card's name that fits <paramref name="maxWidth"/> in this font:
    /// full name, then its short name, then a trimmed short name.
    /// </summary>
    public static string FitName(Font font, CardDefinition card, float maxWidth, int size)
    {
        bool Fits(string s) => font.GetStringSize(s, HorizontalAlignment.Left, -1, size).X <= maxWidth;
        if (Fits(card.Name)) return card.Name;
        var shorter = card.ShortName ?? card.Name;
        while (shorter.Length > 2 && !Fits(shorter)) shorter = shorter[..^1];
        return shorter;
    }

    /// <summary>A name that fits in <paramref name="maxChars"/>: "Shared Fortune" → "Shared Fort.".</summary>
    public static string ShortName(string name, int maxChars) =>
        name.Length <= maxChars ? name : name[..Math.Max(1, maxChars - 1)].TrimEnd() + ".";
}
