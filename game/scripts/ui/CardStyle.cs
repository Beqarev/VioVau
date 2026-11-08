using Godot;
using VioVau.Core.Cards;

namespace VioVau.Game.UI;

/// <summary>How cards look until real card art exists: a category color and a short name.</summary>
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

    /// <summary>A name that fits in <paramref name="maxChars"/>: "Shared Fortune" → "Shared Fort.".</summary>
    public static string ShortName(string name, int maxChars) =>
        name.Length <= maxChars ? name : name[..Math.Max(1, maxChars - 1)].TrimEnd() + ".";
}
