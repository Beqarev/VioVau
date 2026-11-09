using Godot;

namespace VioVau.Game.UI;

/// <summary>OFL Silkscreen pixel font for HUD and menus (readable at 480×270 design scale).</summary>
public static class UiFonts
{
    private const string RegularPath = "res://assets/fonts/Silkscreen-Regular.ttf";
    private const string BoldPath = "res://assets/fonts/Silkscreen-Bold.ttf";

    private static Font? _regular;
    private static Font? _bold;

    public static Font Regular => _regular ??= Load(RegularPath) ?? ThemeDB.FallbackFont;
    public static Font Bold => _bold ??= Load(BoldPath) ?? Regular;

    private static FontFile? Load(string path)
    {
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"[Fonts] Missing {path}");
            return null;
        }
        return GD.Load<FontFile>(path);
    }
}
