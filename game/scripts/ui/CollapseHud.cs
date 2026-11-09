using Godot;
using VioVau.Core;

namespace VioVau.Game.UI;

/// <summary>Top-centre countdown once the Collapse has started: "THE VAULT COLLAPSES 1:12". Blinks in the last 15s.</summary>
public partial class CollapseHud : Control
{
    private RunView _view = null!;
    private double _elapsed;
    private bool _running;

    public CollapseHud Init(RunView view)
    {
        _view = view;
        return this;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Seconds left (counted locally from when the Collapse event arrived).</summary>
    public double Remaining => _view.CollapseSeconds is { } total ? Math.Max(0, total - _elapsed) : double.NaN;

    public override void _Process(double delta)
    {
        if (_view.Results is not null && _running)
        {
            _running = false; // the scoreboard takes over: clear the countdown
            QueueRedraw();
        }
        if (_view.CollapseSeconds is null || _view.Results is not null) return;
        _running = true;
        _elapsed += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_running || _view.Results is not null) return;
        double left = Remaining;
        bool urgent = left < 15;
        bool blink = urgent && (int)(left * 4) % 2 == 0;
        var screen = GetViewportRect().Size;
        var band = new Rect2(screen.X / 2 - 70, 14, 140, 18);
        DrawRect(band, new Color(Palette.Void, 0.85f));
        DrawRect(band, urgent ? Palette.White : Palette.Accent, filled: false, width: 1f);
        var text = $"COLLAPSE  {(int)left / 60}:{(int)left % 60:00}";
        DrawString(UiFonts.Regular, new Vector2(band.Position.X, band.Position.Y + 13), text, HorizontalAlignment.Center, band.Size.X, 10,
            blink ? Palette.Accent : Palette.White);
    }
}
