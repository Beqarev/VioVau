using System.Globalization;
using Godot;

namespace VioVau.Game.Tools;

/// <summary>
/// Dev tool: turns generated 2D item art (any size) into game-ready sprites: crop to content, shrink so the
/// longest side is <c>--size</c> px, hard alpha, brightness → Violet Vaults palette ramp (Bone kept for the
/// brightest treasure highlights), 1px Void outline. Centered on a <c>--canvas</c>-sized transparent canvas.
///
/// Run (headless is fine):
///   Godot --headless --path game res://tools/ItemImporter.tscn -- --in=assets/raw/items --out=assets/sprites/items
/// Options: --size=18 --canvas=24 --contrast=1.2 --bone=0.88 --only=chest_closed,key
/// </summary>
public partial class ItemImporter : Node
{
    private static readonly Color[] Ramp =
    [
        new("1d1030"), new("2a1a45"), new("4b2a7a"), new("6a3fa8"), new("8b5cc8"), new("c8a2ff"), new("e3c7ff"),
    ];
    private static readonly Color Void = new("0d0716");
    private static readonly Color Bone = new("f4f0ff");

    private Dictionary<string, string> _args = new();

    public override void _Ready()
    {
        _args = OS.GetCmdlineUserArgs()
            .Where(a => a.StartsWith("--"))
            .Select(a => a[2..].Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "true");

        try
        {
            Run();
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError($"[Import] {ex.Message}");
            GetTree().Quit(1);
        }
    }

    private void Run()
    {
        string inDir = ProjectSettings.GlobalizePath("res://" + Arg("in", "assets/raw/items"));
        string outDir = ProjectSettings.GlobalizePath("res://" + Arg("out", "assets/sprites/items"));
        int size = int.Parse(Arg("size", "18"));
        int canvas = int.Parse(Arg("canvas", "24"));
        var only = _args.TryGetValue("only", out var o) ? o.Split(',').ToHashSet() : null;

        DirAccess.MakeDirRecursiveAbsolute(outDir);
        foreach (var file in Directory.GetFiles(inDir, "*.png").Order())
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (only is not null && !only.Contains(name)) continue;

            var source = Image.LoadFromFile(file);
            source.Convert(Image.Format.Rgba8);

            var content = Crop(source);
            // Shrink to fit `size` on the longest side, leaving room for the 1px outline.
            float scale = (size - 2f) / Math.Max(content.GetWidth(), content.GetHeight());
            int w = Math.Max(1, (int)Math.Round(content.GetWidth() * scale));
            int h = Math.Max(1, (int)Math.Round(content.GetHeight() * scale));
            content.Resize(w, h, Image.Interpolation.Lanczos);

            var placed = Image.CreateEmpty(canvas, canvas, false, Image.Format.Rgba8);
            // Centered horizontally, resting on the bottom (so items sit on the floor).
            placed.BlitRect(content, new Rect2I(0, 0, w, h), new Vector2I((canvas - w) / 2, canvas - 1 - h));

            Process(placed).SavePng($"{outDir}/{name}.png");
            GD.Print($"[Import] {name}: {source.GetWidth()}x{source.GetHeight()} → {w}x{h} on {canvas}x{canvas}");
        }
    }

    private static Image Crop(Image image)
    {
        var used = image.GetUsedRect();
        if (used.Size.X <= 0 || used.Size.Y <= 0) throw new InvalidOperationException("Image is empty.");
        return image.GetRegion(used);
    }

    private Image Process(Image source)
    {
        int w = source.GetWidth(), h = source.GetHeight();
        float contrast = Float("contrast", 1.2f);
        float boneAbove = Float("bone", 0.88f);
        var solid = new bool[w, h];
        var result = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = source.GetPixel(x, y);
            if (c.A < 0.5f) continue;
            solid[x, y] = true;

            float lum = Mathf.Clamp((0.299f * c.R + 0.587f * c.G + 0.114f * c.B) * contrast, 0f, 1f);
            var color = lum >= boneAbove ? Bone : lum < 0.08f ? Void : Ramp[Math.Min(Ramp.Length - 1, (int)(lum * Ramp.Length))];
            result.SetPixel(x, y, color);
        }

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (solid[x, y]) continue;
            bool edge = (x > 0 && solid[x - 1, y]) || (x < w - 1 && solid[x + 1, y]) || (y > 0 && solid[x, y - 1]) || (y < h - 1 && solid[x, y + 1]);
            if (edge) result.SetPixel(x, y, Void);
        }
        return result;
    }

    private string Arg(string name, string fallback) => _args.TryGetValue(name, out var v) ? v : fallback;

    private float Float(string name, float fallback) =>
        _args.TryGetValue(name, out var v) && float.TryParse(v, CultureInfo.InvariantCulture, out var f) ? f : fallback;
}
