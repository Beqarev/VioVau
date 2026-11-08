using Godot;
using VioVau.Game.World;

namespace VioVau.Game.UI;

/// <summary>Behind the menu: the vault in the dark — a breathing violet glow, drifting motes, glinting crystals.</summary>
public partial class MenuBackground : Control
{
    private readonly (Vector2 Pos, float Speed, float Phase)[] _motes;
    private readonly (Vector2 Pos, float Phase)[] _crystals;
    private double _time;

    public MenuBackground()
    {
        var random = new System.Random(7);
        _motes = Enumerable.Range(0, 60)
            .Select(_ => (new Vector2((float)random.NextDouble() * 480, (float)random.NextDouble() * 270), 2f + (float)random.NextDouble() * 6, (float)random.NextDouble() * 10))
            .ToArray();
        _crystals = Enumerable.Range(0, 9)
            .Select(i => (new Vector2(20 + i * 52 + (float)random.NextDouble() * 20, 268 - (float)random.NextDouble() * 4), (float)random.NextDouble() * 6))
            .ToArray();
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        QueueRedraw();
    }

    private void Glow(Vector2 center, float radius, Color color) =>
        DrawTextureRect(Lights.Radial, new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2), false, color);

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        DrawRect(new Rect2(Vector2.Zero, size), Palette.Void);

        // A slow, breathing glow behind the title (the smooth light texture: no banding).
        float breathe = 0.5f + 0.5f * Mathf.Sin((float)_time * 0.8f);
        Glow(new Vector2(size.X * 0.3f, size.Y * 0.2f), 190f, new Color(0.42f, 0.25f, 0.66f, 0.22f + 0.06f * breathe));
        Glow(new Vector2(size.X * 0.85f, size.Y * 1.0f), 160f, new Color(0.35f, 0.18f, 0.6f, 0.18f));

        // Floor glow and crystals along the bottom.
        for (int i = 4; i >= 1; i--)
            DrawRect(new Rect2(0, size.Y - i * 10, size.X, i * 10), new Color(0.3f, 0.16f, 0.5f, 0.05f));
        foreach (var (pos, phase) in _crystals)
        {
            float glint = 0.6f + 0.4f * Mathf.Sin((float)_time * 1.5f + phase);
            DrawColoredPolygon([pos + new Vector2(-3, 0), pos + new Vector2(0, -9), pos + new Vector2(3, 0)], new Color(Palette.Accent, glint));
            DrawColoredPolygon([pos + new Vector2(2, 0), pos + new Vector2(5, -6), pos + new Vector2(7, 0)], new Color("6a3fa8"));
            DrawRect(new Rect2(pos + new Vector2(0, -9), Vector2.One), new Color(Palette.White, glint));
            Glow(pos + new Vector2(1, -4), 16f, new Color(0.6f, 0.4f, 0.9f, 0.35f * glint));
        }

        // Drifting dust (in front of the glows).
        foreach (var (pos, speed, phase) in _motes)
        {
            float t = (float)_time;
            var p = new Vector2((pos.X + t * speed * 0.4f) % size.X, (pos.Y - t * speed + 1000) % size.Y);
            float alpha = 0.15f + 0.25f * (0.5f + 0.5f * Mathf.Sin(t + phase));
            DrawRect(new Rect2(p.Round(), Vector2.One), new Color("c8a2ff") with { A = alpha });
        }
    }
}
