using System.Text.Json;
using Godot;

namespace VioVau.Game.World;

/// <summary>
/// Loads a baked character (see tools/SpriteBaker) into <see cref="SpriteFrames"/>: one animation per
/// "{anim}_{direction}" (e.g. "walking_left"). Cached per folder; returns null if the art isn't there,
/// so callers can fall back to placeholder drawing.
/// </summary>
public sealed class CharacterSprites
{
    private static readonly Dictionary<string, CharacterSprites?> Cache = new();

    public SpriteFrames Frames { get; }
    public int FrameSize { get; }

    /// <summary>Pixels from the frame's top to the character's feet; used to stand the sprite on its position.</summary>
    public int FeetY { get; }

    private CharacterSprites(SpriteFrames frames, int frameSize, int feetY)
    {
        Frames = frames;
        FrameSize = frameSize;
        FeetY = feetY;
    }

    public static string Name(string animation, string direction) => $"{animation}_{direction}";

    public static CharacterSprites? Load(string folder)
    {
        if (Cache.TryGetValue(folder, out var cached)) return cached;

        CharacterSprites? result = null;
        try
        {
            result = Build(folder);
        }
        catch (Exception ex)
        {
            GD.PushWarning($"[Sprites] Could not load {folder}: {ex.Message}");
        }
        Cache[folder] = result;
        return result;
    }

    private static CharacterSprites Build(string folder)
    {
        using var manifest = JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"{folder}/sprites.json"));
        var root = manifest.RootElement;
        int size = root.GetProperty("frame").GetInt32();
        var directions = root.GetProperty("directions").EnumerateArray().Select(d => d.GetString()!).ToList();

        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");
        int feetY = size;

        foreach (var anim in root.GetProperty("animations").EnumerateObject())
        {
            var texture = GD.Load<Texture2D>($"{folder}/{anim.Value.GetProperty("file").GetString()}")
                ?? throw new FileNotFoundException($"{folder}/{anim.Name} texture");
            int count = anim.Value.GetProperty("frames").GetInt32();
            double fps = anim.Value.GetProperty("fps").GetDouble();
            bool loop = anim.Value.GetProperty("loop").GetBoolean();

            for (int row = 0; row < directions.Count; row++)
            {
                var name = Name(anim.Name, directions[row]);
                frames.AddAnimation(name);
                frames.SetAnimationSpeed(name, fps);
                frames.SetAnimationLoopMode(name, loop ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
                for (int col = 0; col < count; col++)
                    frames.AddFrame(name, new AtlasTexture { Atlas = texture, Region = new Rect2(col * size, row * size, size, size) });
            }

            if (anim.Name == "idle") feetY = LowestOpaqueRow(texture, size);
        }

        return new CharacterSprites(frames, size, feetY);
    }

    /// <summary>The bottom of the first idle frame's silhouette (its feet).</summary>
    private static int LowestOpaqueRow(Texture2D texture, int size)
    {
        var image = texture.GetImage();
        for (int y = size - 1; y >= 0; y--)
            for (int x = 0; x < size; x++)
                if (image.GetPixel(x, y).A > 0.5f) return y;
        return size - 1;
    }
}
