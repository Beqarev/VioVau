using Godot;
using VioVau.Game.UI;

namespace VioVau.Game.World;

/// <summary>
/// One-shot particle effects (pixel squares, so they match the art). Each burst frees itself when done.
/// Bright bursts are unshaded so they read as light even in the dark.
/// </summary>
public static class Fx
{
    private static Gradient Fade(Color color)
    {
        var g = new Gradient();
        g.Offsets = [0f, 0.6f, 1f];
        g.Colors = [color, new Color(color, 0.7f), new Color(color, 0f)];
        return g;
    }

    /// <param name="spread">Degrees around <paramref name="direction"/>; 180 = all directions.</param>
    public static CpuParticles2D Burst(Node parent, Vector2 at, Color color, int amount, float speed, float lifetime,
        Vector2? direction = null, float spread = 180f, float gravity = 0f, float size = 1f, bool glow = true)
    {
        var particles = new CpuParticles2D
        {
            Position = at,
            OneShot = true,
            Emitting = false,
            Amount = amount,
            Lifetime = lifetime,
            Explosiveness = 0.9f,
            Direction = direction ?? Vector2.Up,
            Spread = spread,
            InitialVelocityMin = speed * 0.35f,
            InitialVelocityMax = speed,
            Gravity = new Vector2(0, gravity),
            DampingMin = speed * 0.3f,
            DampingMax = speed * 0.6f,
            ScaleAmountMin = size,
            ScaleAmountMax = size * 2f,
            ColorRamp = Fade(color),
            ZIndex = 15,
        };
        if (glow) particles.Material = Lights.Unshaded;
        parent.AddChild(particles);
        particles.Emitting = true;
        particles.Finished += particles.QueueFree;
        return particles;
    }

    public static void ChestOpened(Node parent, Vector2 at)
    {
        Burst(parent, at + new Vector2(0, -6), Palette.White, 18, 55f, 0.8f, Vector2.Up, 50f, gravity: 110f);
        Burst(parent, at + new Vector2(0, -4), Palette.Accent, 10, 25f, 0.6f);
    }

    public static void KeyTaken(Node parent, Vector2 at) => Burst(parent, at + new Vector2(0, -4), Palette.White, 10, 30f, 0.45f);

    public static void RelicTaken(Node parent, Vector2 at)
    {
        Burst(parent, at + new Vector2(0, -10), Palette.White, 40, 80f, 1.1f, Vector2.Up, 70f, gravity: 60f);
        Burst(parent, at + new Vector2(0, -10), Lights.Amethyst, 24, 40f, 1.2f);
    }

    public static void DoorUnlocked(Node parent, Vector2 at) => Burst(parent, at, Lights.Amethyst, 24, 40f, 0.9f);

    public static void Hit(Node parent, Vector2 at)
    {
        Burst(parent, at + new Vector2(0, -8), Palette.White, 12, 70f, 0.35f, size: 1f);
        Burst(parent, at + new Vector2(0, -8), Lights.Amethyst, 8, 40f, 0.5f);
    }

    /// <summary>A Hollow Sentry falls apart into violet mist.</summary>
    public static void EnemyDestroyed(Node parent, Vector2 at) =>
        Burst(parent, at + new Vector2(0, -10), Lights.Amethyst, 30, 35f, 1.3f, Vector2.Up, 180f, gravity: -20f, size: 1.5f);

    /// <summary>Escaping: a column of light rising from the circle.</summary>
    public static void Escaped(Node parent, Vector2 at) =>
        Burst(parent, at, Palette.White, 36, 90f, 1.2f, Vector2.Up, 12f, gravity: -30f);

    /// <summary>Ambient dust drifting through a room (looping; enable only for rooms on screen).</summary>
    public static CpuParticles2D RoomDust(Vector2 roomSize)
    {
        return new CpuParticles2D
        {
            Position = roomSize / 2f,
            Emitting = false,
            Amount = 14,
            Lifetime = 8f,
            Preprocess = 8f,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle,
            EmissionRectExtents = roomSize / 2f - new Vector2(16, 16),
            Direction = new Vector2(0.3f, -1f),
            Spread = 40f,
            InitialVelocityMin = 2f,
            InitialVelocityMax = 6f,
            Gravity = Vector2.Zero,
            ScaleAmountMin = 1f,
            ScaleAmountMax = 1f,
            ColorRamp = AmbientFade(),
            ZIndex = 12,
        };
    }

    private static Gradient AmbientFade()
    {
        var g = new Gradient();
        var c = new Color("c8a2ff");
        g.Offsets = [0f, 0.3f, 0.7f, 1f];
        g.Colors = [new Color(c, 0), new Color(c, 0.45f), new Color(c, 0.45f), new Color(c, 0)];
        return g;
    }

    /// <summary>Dust kicked up by a sprinting player (looping; toggle <c>Emitting</c>).</summary>
    public static CpuParticles2D SprintDust()
    {
        var g = new Gradient();
        g.Offsets = [0f, 1f];
        g.Colors = [new Color(Palette.Dim, 0.6f), new Color(Palette.Dim, 0)];
        return new CpuParticles2D
        {
            Emitting = false,
            Amount = 10,
            Lifetime = 0.45f,
            LocalCoords = false,
            Direction = Vector2.Up,
            Spread = 70f,
            InitialVelocityMin = 6f,
            InitialVelocityMax = 16f,
            Gravity = new Vector2(0, 20),
            ScaleAmountMin = 1f,
            ScaleAmountMax = 2f,
            ColorRamp = g,
            ZIndex = -1,
        };
    }
}
