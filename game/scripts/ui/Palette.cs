using Godot;
using VioVau.Core.Dungeon;

namespace VioVau.Game.UI;

/// <summary>The strict Violet Vaults palette: void black, deep purples, stark white for value and danger.</summary>
public static class Palette
{
    public static readonly Color Void = new("0d0716");
    public static readonly Color Floor = new("1d1030");
    public static readonly Color FloorLine = new("241439");
    public static readonly Color Wall = new("4b2a7a");
    public static readonly Color WallEdge = new("6a3fa8");
    public static readonly Color Accent = new("8b5cc8");
    public static readonly Color Dim = new("7d6a9e");
    public static readonly Color White = new("f4f0ff");

    public static readonly Color[] Players =
    [
        new("f4f0ff"),
        new("c8a2ff"),
        new("9f6bff"),
        new("e3c7ff"),
    ];

    public static Color PlayerColor(int index) => Players[index % Players.Length];

    public static Color RoomFloor(RoomType type) => type switch
    {
        RoomType.Lobby => new Color("2a1a45"),
        RoomType.RelicChamber => new Color("33204f"),
        RoomType.EchoChamber => new Color("231238"),
        RoomType.HushRoom => new Color("140c22"),
        _ => Floor,
    };
}
