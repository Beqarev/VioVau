using Godot;
using VioVau.Core;

namespace VioVau.Game.UI;

/// <summary>Loads 32×32 card illustrations from <c>res://assets/sprites/cards/</c>.</summary>
public static class CardArt
{
    private const string Folder = "res://assets/sprites/cards";
    private static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D? Get(CardId id) => Get(id.Value);

    public static Texture2D? Get(string id)
    {
        if (Cache.TryGetValue(id, out var cached)) return cached;
        string path = $"{Folder}/{id}.png";
        if (!ResourceLoader.Exists(path)) return null;
        var tex = GD.Load<Texture2D>(path);
        Cache[id] = tex;
        return tex;
    }
}
