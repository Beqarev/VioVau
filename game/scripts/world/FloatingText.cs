using Godot;
using VioVau.Game.UI;

namespace VioVau.Game.World;

/// <summary>A word that rises and fades above someone (e.g. the card they just played). Lives in the world.</summary>
public partial class FloatingText : Node2D
{
    private const double Duration = 1.2;

    private string _text = "";
    private Color _color = Palette.White;
    private double _age;

    public FloatingText Init(Vector2 position, string text, Color color)
    {
        Position = position;
        _text = text;
        _color = color;
        ZIndex = 30;
        Material = Lights.Unshaded;
        return this;
    }

    public override void _Process(double delta)
    {
        _age += delta;
        if (_age >= Duration) QueueFree();
        else QueueRedraw();
    }

    public override void _Draw()
    {
        float t = (float)(_age / Duration);
        float rise = Mathf.Round(-t * 12f);
        var color = new Color(_color, 1f - t * t);
        var font = ThemeDB.FallbackFont;
        // Dark backing so it reads on any floor.
        DrawString(font, new Vector2(-60 + 1, rise + 1), _text, HorizontalAlignment.Center, 120, 8, new Color(Palette.Void, color.A));
        DrawString(font, new Vector2(-60, rise), _text, HorizontalAlignment.Center, 120, 8, color);
    }
}
