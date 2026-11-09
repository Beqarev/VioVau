using Godot;
using VioVau.Core;

namespace VioVau.Game.UI;

/// <summary>End-of-run results over everything: who escaped, who fell, who was sealed in, what they banked, and the winner.</summary>
public partial class Scoreboard : Control
{
    private RunView _view = null!;
    private bool _isHost;
    private double _age;

    public Scoreboard Init(RunView view, bool isHost)
    {
        _view = view;
        _isHost = isHost;
        return this;
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
    }

    public void ShowResults()
    {
        Visible = true;
        _age = 0;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _age += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_view.Results is not { } results) return;
        var font = UiFonts.Regular;
        var screen = GetViewportRect().Size;
        float fade = (float)Math.Min(1, _age / 0.6);

        DrawRect(new Rect2(Vector2.Zero, screen), new Color(Palette.Void, 0.88f * fade));

        var panel = new Rect2(screen.X / 2 - 150, 40, 300, 60 + results.Count * 18);
        DrawRect(panel, Palette.Floor);
        DrawRect(panel, Palette.Accent, filled: false, width: 1f);

        DrawString(font, new Vector2(panel.Position.X, panel.Position.Y + 20), (_view.EndReason ?? "The run is over.").ToUpperInvariant(),
            HorizontalAlignment.Center, panel.Size.X, 12, Palette.White);

        float y = panel.Position.Y + 42;
        int rank = 1;
        foreach (var result in results)
        {
            var info = _view.Players.FirstOrDefault(p => p.Id == result.Player);
            int index = _view.Players.ToList().FindIndex(p => p.Id == result.Player);
            var color = result.Outcome == RunOutcome.Escaped ? Palette.PlayerColor(index) : Palette.Wall;
            float x = panel.Position.X + 14;

            DrawString(font, new Vector2(x, y), $"{rank}.", HorizontalAlignment.Left, -1, 10, Palette.Dim);
            DrawString(font, new Vector2(x + 16, y), info?.DisplayName ?? result.Player.ToString(), HorizontalAlignment.Left, -1, 10, color);
            DrawString(font, new Vector2(x + 110, y), result.Outcome switch
            {
                RunOutcome.Escaped => "escaped",
                RunOutcome.Sealed => "sealed in",
                _ => "fell",
            }, HorizontalAlignment.Left, -1, 10, result.Outcome == RunOutcome.Escaped ? Palette.White : Palette.Dim);
            DrawString(font, new Vector2(x + 180, y), $"{result.Banked}p", HorizontalAlignment.Left, -1, 10, result.Banked > 0 ? Palette.White : Palette.Wall);
            if (result.HadRelic) DrawString(font, new Vector2(x + 218, y), "relic", HorizontalAlignment.Left, -1, 8, Palette.Accent);
            if (result.Winner) DrawString(font, new Vector2(x + 248, y), "WINNER", HorizontalAlignment.Left, -1, 8, Palette.White);

            y += 18;
            rank++;
        }

        var hint = _isHost ? "Enter: play again   ·   Esc: menu" : "Waiting for the host…   ·   Esc: menu";
        DrawString(font, new Vector2(panel.Position.X, panel.End.Y + 16), hint, HorizontalAlignment.Center, panel.Size.X, 8, Palette.Dim);
    }
}
