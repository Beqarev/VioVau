using Godot;

namespace VioVau.Game.UI;

/// <summary>Brief centered banner when the Threat tier changes: "THE VAULT STIRS".</summary>
public partial class TierBanner : Control
{
    private double _duration = 2.5;

    private string _text = "";
    private Color _color = Palette.White;
    private double _age = double.MaxValue;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <param name="seconds">How long it stays; <see cref="double.PositiveInfinity"/> keeps it up.</param>
    public void Show(string text, Color color, double seconds = 2.5)
    {
        _text = text;
        _color = color;
        _duration = seconds;
        _age = 0;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_age >= _duration) return;
        _age += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_age >= _duration) return;

        // Fade in fast, hold, fade out.
        float alpha = (float)Math.Min(Math.Min(_age / 0.2, 1), Math.Min((_duration - _age) / 0.6, 1));
        var screen = GetViewportRect().Size;
        var band = new Rect2(0, screen.Y * 0.32f, screen.X, 18);
        DrawRect(band, new Color(Palette.Void, 0.85f * alpha));
        DrawString(UiFonts.Regular, new Vector2(0, band.Position.Y + 13), _text,
            HorizontalAlignment.Center, screen.X, 10, new Color(_color, alpha));
    }
}
