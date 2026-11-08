using Godot;
using VioVau.Game.UI;

namespace VioVau.Game.Entities;

/// <summary>Drawn above a player's sprite: their number and, for local players, the interact button hint.</summary>
public partial class PlayerOverlay : Node2D
{
    private int _colorIndex;
    private float _headY;
    private string? _prompt;

    public string? Prompt
    {
        get => _prompt;
        set
        {
            if (_prompt == value) return;
            _prompt = value;
            QueueRedraw();
        }
    }

    public PlayerOverlay Init(int colorIndex, float headY)
    {
        _colorIndex = colorIndex;
        _headY = headY;
        ZIndex = 1;
        return this;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var color = Palette.PlayerColor(_colorIndex);
        DrawString(font, new Vector2(-3, _headY - 2), (_colorIndex + 1).ToString(), HorizontalAlignment.Left, -1, 8, color);

        if (_prompt is not null)
        {
            var box = new Rect2(6, _headY - 10, 9, 9);
            DrawRect(box, Palette.White);
            DrawRect(box, Palette.Void, filled: false, width: 1f);
            DrawString(font, new Vector2(box.Position.X, box.End.Y - 2), _prompt, HorizontalAlignment.Center, box.Size.X, 8, Palette.Void);
        }
    }
}
