using Godot;
using VioVau.Game.UI;

namespace VioVau.Game.World;

/// <summary>The Decoy card's noisemaker: a little clockwork box pulsing sound rings until it runs down.</summary>
public partial class DecoyNode : Node2D
{
    private double _remaining;
    private double _age;

    public DecoyNode Init(Vector2 position, double seconds)
    {
        Position = position;
        _remaining = seconds;
        ZIndex = 4;
        return this;
    }

    public override void _Process(double delta)
    {
        _age += delta;
        _remaining -= delta;
        if (_remaining <= 0) QueueFree();
        else QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(-3, -3, 6, 5), Palette.Wall);
        DrawRect(new Rect2(-3, -3, 6, 5), Palette.Void, filled: false, width: 1f);
        DrawRect(new Rect2(-1, -5, 2, 2), Palette.White);

        float t = (float)(_age % 0.8 / 0.8);
        DrawArc(Vector2.Zero, 4 + t * 16, 0, Mathf.Tau, 24, new Color(Palette.White, 1 - t), 1f);
    }
}
