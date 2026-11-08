using Godot;
using VioVau.Game.UI;

namespace VioVau.Game.World;

/// <summary>An expanding, fading ring showing a noisy action. Lives in the world so every view sees it.</summary>
public partial class NoiseRing : Node2D
{
    private const double Duration = 0.7;

    private double _age;
    private float _maxRadius;

    public NoiseRing Init(Vector2 position, double amount)
    {
        Position = position;
        _maxRadius = 10f + (float)Math.Min(amount, 12) * 5f;
        ZIndex = 20;
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
        float radius = Mathf.Lerp(4f, _maxRadius, 1f - (1f - t) * (1f - t));
        var color = new Color(Palette.White, 1f - t);
        DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 32, color, 1f);
        if (radius > 8f) DrawArc(Vector2.Zero, radius * 0.6f, 0, Mathf.Tau, 24, new Color(Palette.Accent, (1f - t) * 0.6f), 1f);
    }
}
