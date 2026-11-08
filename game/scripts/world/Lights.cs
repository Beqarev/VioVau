using Godot;

namespace VioVau.Game.World;

/// <summary>
/// Shared lighting pieces: one soft radial texture for every light, a factory for point lights, and the
/// unshaded material for things that must stay readable in the dark (labels, rings, prompts).
/// </summary>
public static class Lights
{
    /// <summary>Lavender-white lantern light.</summary>
    public static readonly Color Lantern = new("efe6ff");
    public static readonly Color Amethyst = new("9a63e0");
    public static readonly Color Bone = new("f4f0ff");

    private const int TextureSize = 256;
    private static GradientTexture2D? _radial;

    /// <summary>White at the centre fading smoothly to nothing at the edge.</summary>
    public static GradientTexture2D Radial => _radial ??= CreateRadial();

    private static GradientTexture2D CreateRadial()
    {
        var gradient = new Gradient();
        // A bright core, a long soft shoulder, and a gentle tail: reads as lantern light rather than a spotlight.
        gradient.Offsets = [0f, 0.2f, 0.55f, 0.8f, 1f];
        gradient.Colors = [new Color(1, 1, 1, 1), new Color(1, 1, 1, 0.8f), new Color(1, 1, 1, 0.38f), new Color(1, 1, 1, 0.1f), new Color(1, 1, 1, 0)];
        return new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1f, 0.5f),
            Width = TextureSize,
            Height = TextureSize,
        };
    }

    /// <param name="radius">Reach in world pixels.</param>
    public static PointLight2D Point(float radius, Color color, float energy, bool shadows = false)
    {
        var light = new PointLight2D
        {
            Texture = Radial,
            TextureScale = radius * 2f / TextureSize,
            Color = color,
            Energy = energy,
            ShadowEnabled = shadows,
        };
        if (shadows)
        {
            light.ShadowFilter = Light2D.ShadowFilterEnum.Pcf5;
            light.ShadowFilterSmooth = 1.5f;
            light.ShadowColor = new Color(0, 0, 0, 1);
        }
        return light;
    }

    private static CanvasItemMaterial? _unshaded;

    /// <summary>Ignores lights: for UI-like things drawn in the world (floating text, rings, prompts).</summary>
    public static CanvasItemMaterial Unshaded => _unshaded ??= new CanvasItemMaterial { LightMode = CanvasItemMaterial.LightModeEnum.Unshaded };

    /// <summary>Ambient darkness by Threat tier: dim violet when calm, near-black when the vault is furious.</summary>
    public static Color Ambient(int tier, int tierCount)
    {
        var calm = new Color(0.40f, 0.34f, 0.56f);
        var furious = new Color(0.14f, 0.10f, 0.22f);
        float t = tierCount <= 1 ? 0 : Mathf.Clamp((float)tier / (tierCount - 1), 0, 1);
        return calm.Lerp(furious, t);
    }
}
