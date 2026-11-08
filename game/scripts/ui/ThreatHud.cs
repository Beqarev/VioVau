using Godot;
using VioVau.Core;

namespace VioVau.Game.UI;

/// <summary>
/// Top-left HUD: the shared Threat meter with tier marks, then one row per player with
/// carried points, keys, and their share of recent noise (who the dungeon is listening to).
/// </summary>
public partial class ThreatHud : Control
{
    private const float BarWidth = 120f, BarHeight = 5f, RowHeight = 9f, Pad = 4f;

    private RunView _view = null!;
    private Font _font = null!;

    public ThreatHud Init(RunView view)
    {
        _view = view;
        return this;
    }

    public override void _Ready()
    {
        _font = ThemeDB.FallbackFont;
        MouseFilter = MouseFilterEnum.Ignore;
        Position = new Vector2(4, 4);
        Size = new Vector2(BarWidth + 70, 22 + _view.Players.Count * RowHeight + Pad);
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        var threat = _view.Config.Threat;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(Palette.Void, 0.8f));

        // Header: "THREAT 23  STIRRING"
        int tier = _view.ThreatTier;
        var tierColor = TierColor(tier, threat.TierThresholds.Count);
        DrawString(_font, new Vector2(Pad, 10), "THREAT", HorizontalAlignment.Left, -1, 8, Palette.Dim);
        DrawString(_font, new Vector2(Pad + 34, 10), $"{(int)_view.ThreatLevel}", HorizontalAlignment.Left, -1, 8, Palette.White);
        DrawString(_font, new Vector2(Pad + 54, 10), threat.TierName(tier).ToUpperInvariant(), HorizontalAlignment.Left, -1, 8, tierColor);

        // Meter with tier marks.
        var bar = new Rect2(Pad, 13, BarWidth, BarHeight);
        DrawRect(bar, Palette.Floor);
        float fill = (float)Math.Clamp(_view.ThreatLevel / threat.Max, 0, 1);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * fill, bar.Size.Y)), tierColor);
        foreach (var threshold in threat.TierThresholds.Skip(1))
        {
            float x = bar.Position.X + bar.Size.X * (float)(threshold / threat.Max);
            DrawLine(new Vector2(x, bar.Position.Y - 1), new Vector2(x, bar.End.Y + 1), Palette.Dim);
        }
        DrawRect(bar, Palette.Wall, filled: false, width: 1f);

        // Players.
        float y = 22;
        for (int i = 0; i < _view.Players.Count; i++)
        {
            if (_view.Player(_view.Players[i].Id) is not { } p) continue;
            var color = Palette.PlayerColor(i);
            float baseline = y + 7;

            DrawRect(new Rect2(Pad, y + 1, 5, 5), color);
            var name = p.Info.DisplayName.Length > 9 ? p.Info.DisplayName[..9] : p.Info.DisplayName;
            DrawString(_font, new Vector2(Pad + 8, baseline), name, HorizontalAlignment.Left, -1, 8, color);
            DrawString(_font, new Vector2(Pad + 56, baseline), $"{p.CarriedPoints}p", HorizontalAlignment.Left, -1, 8, Palette.White);
            DrawString(_font, new Vector2(Pad + 84, baseline), $"K{p.Keys}", HorizontalAlignment.Left, -1, 8, p.Keys > 0 ? Palette.White : Palette.Dim);

            // Noise share: the louder you've been lately, the more the dungeon will hunt you.
            var share = new Rect2(Pad + 104, y + 2, 40, 3);
            DrawRect(share, Palette.Floor);
            DrawRect(new Rect2(share.Position, new Vector2(share.Size.X * (float)p.NoiseShare, share.Size.Y)), Palette.Accent);
            DrawString(_font, new Vector2(share.End.X + 3, baseline), $"{Math.Round(p.NoiseShare * 100)}%", HorizontalAlignment.Left, -1, 8, Palette.Dim);

            y += RowHeight;
        }
    }

    /// <summary>Purple at Calm, ramping to stark white as the dungeon wakes up.</summary>
    public static Color TierColor(int tier, int tierCount) =>
        Palette.Accent.Lerp(Palette.White, tierCount <= 1 ? 0 : (float)tier / (tierCount - 1));
}
