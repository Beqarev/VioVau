using System.Globalization;
using System.Text.Json;
using Godot;

namespace VioVau.Game.Tools;

/// <summary>
/// Dev tool: renders a rigged 3D model (.glb) into pixel-art sprite sheets — one sheet per animation,
/// rows = directions (down, left, right, up), columns = frames. Every pixel is snapped to the
/// Violet Vaults palette and given a 1px Void outline, so baked art matches the art bible exactly.
///
/// Run (needs a window; the headless renderer draws nothing):
///   Godot --path game res://tools/SpriteBaker.tscn -- --model=assets/raw/characters/delver.glb --out=assets/sprites/delver
/// Options: --frame=32 --ortho=3.4 --pitch=30 --center=0.8 --contrast=1.45 --anims=Idle:4,Walking_A:6 --preview
/// </summary>
public partial class SpriteBaker : Node
{
    private static readonly (string Name, float Yaw)[] Directions = [("down", 0f), ("left", -90f), ("right", 90f), ("up", 180f)];

    /// <summary>Dark → light purple ramp used for shading. Bone is reserved for the brightest highlights (eyes, glows).</summary>
    private static readonly Color[] Ramp =
    [
        new("1d1030"), new("2a1a45"), new("4b2a7a"), new("6a3fa8"), new("8b5cc8"), new("c8a2ff"), new("e3c7ff"),
    ];
    private static readonly Color Void = new("0d0716");
    private static readonly Color Bone = new("f4f0ff");

    private const string DefaultAnims =
        "Idle:4,Walking_A:6,Running_A:6,Interact:4,PickUp:4,Use_Item:4,Hit_A:3,Death_A:6,Dodge_Forward:5,Cheer:6";

    private Dictionary<string, string> _args = new();

    public override async void _Ready()
    {
        _args = OS.GetCmdlineUserArgs()
            .Where(a => a.StartsWith("--"))
            .Select(a => a[2..].Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "true");

        try
        {
            await Bake();
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PushError($"[Baker] {ex.Message}");
            GetTree().Quit(1);
        }
    }

    private async Task Bake()
    {
        string modelPath = ProjectSettings.GlobalizePath("res://" + Arg("model", "assets/raw/characters/delver.glb"));
        string outDir = ProjectSettings.GlobalizePath("res://" + Arg("out", "assets/sprites/delver"));
        bool preview = _args.ContainsKey("preview");
        int frame = preview ? 256 : int.Parse(Arg("frame", "32"));
        float ortho = Float("ortho", 3.4f);
        float pitch = Mathf.DegToRad(Float("pitch", 30f));
        float centerY = Float("center", 0.8f);

        var model = LoadModel(modelPath);
        var player = model.FindChildren("*", "AnimationPlayer", true, false).OfType<AnimationPlayer>().FirstOrDefault()
            ?? throw new InvalidOperationException("The model has no AnimationPlayer.");

        // Stage: own 3D world, transparent background, no anti-aliasing (crisp pixels).
        var viewport = new SubViewport
        {
            Size = new Vector2I(frame, frame),
            TransparentBg = true,
            OwnWorld3D = true,
            Msaa3D = Viewport.Msaa.Disabled,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        AddChild(viewport);

        var pivot = new Node3D();
        viewport.AddChild(pivot);
        pivot.AddChild(model);

        var target = new Vector3(0, centerY, 0);
        var camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = ortho, Near = 0.1f, Far = 50f };
        viewport.AddChild(camera);
        camera.Position = target + new Vector3(0, Mathf.Sin(pitch), Mathf.Cos(pitch)) * 10f;
        camera.LookAt(target);

        // Light from the top-left (as the art bible says), plus soft ambient so shadows aren't pure black.
        var light = new DirectionalLight3D { LightEnergy = 1.1f };
        viewport.AddChild(light);
        light.Position = new Vector3(-4, 6, 4);
        light.LookAt(Vector3.Zero);
        viewport.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = Colors.White,
                AmbientLightEnergy = 0.6f,
            },
        });

        DirAccess.MakeDirRecursiveAbsolute(outDir);

        if (preview)
        {
            Pose(player, "Idle", 0);
            pivot.RotationDegrees = new Vector3(0, 20, 0);
            var image = await Capture(viewport);
            image.SavePng($"{outDir}/_preview_raw.png");
            Process(image).SavePng($"{outDir}/_preview_palette.png");
            GD.Print($"[Baker] Preview written to {outDir}");
            return;
        }

        var manifest = new Dictionary<string, object>
        {
            ["frame"] = frame,
            ["directions"] = Directions.Select(d => d.Name).ToArray(),
        };
        var anims = new Dictionary<string, object>();

        foreach (var spec in Arg("anims", DefaultAnims).Split(','))
        {
            var parts = spec.Split(':');
            string name = parts[0];
            int frames = parts.Length > 1 ? int.Parse(parts[1]) : 4;
            if (!player.HasAnimation(name))
            {
                GD.PushWarning($"[Baker] Skipping missing animation {name}");
                continue;
            }

            var animation = player.GetAnimation(name);
            bool loops = animation.LoopMode != Animation.LoopModeEnum.None || name.StartsWith("Idle") || name.StartsWith("Walking") || name.StartsWith("Running");
            var sheet = Image.CreateEmpty(frame * frames, frame * Directions.Length, false, Image.Format.Rgba8);

            for (int row = 0; row < Directions.Length; row++)
            {
                pivot.RotationDegrees = new Vector3(0, Directions[row].Yaw, 0);
                for (int col = 0; col < frames; col++)
                {
                    // Looping animations: evenly spaced, last frame ≠ first. One-shots: include the final pose.
                    double t = loops ? animation.Length * col / frames : animation.Length * col / Math.Max(1, frames - 1);
                    Pose(player, name, t);
                    var image = Process(await Capture(viewport));
                    sheet.BlitRect(image, new Rect2I(0, 0, frame, frame), new Vector2I(col * frame, row * frame));
                }
            }

            string file = $"{Slug(name)}.png";
            sheet.SavePng($"{outDir}/{file}");
            anims[Slug(name)] = new { file, frames, fps = loops ? 8 : 10, loop = loops, source = name };
            GD.Print($"[Baker] {name}: {frames} frames × {Directions.Length} directions → {file}");
        }

        manifest["animations"] = anims;
        File.WriteAllText($"{outDir}/sprites.json", JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"[Baker] Done → {outDir}");
    }

    private static Node3D LoadModel(string path)
    {
        var document = new GltfDocument();
        var state = new GltfState();
        var error = document.AppendFromFile(path, state);
        if (error != Error.Ok) throw new InvalidOperationException($"Could not load {path}: {error}");
        return document.GenerateScene(state) as Node3D ?? throw new InvalidOperationException("The model has no 3D root.");
    }

    private static void Pose(AnimationPlayer player, string animation, double time)
    {
        player.Play(animation);
        player.Pause();
        player.Seek(time, true);
    }

    private async Task<Image> Capture(SubViewport viewport)
    {
        // Two frames: one to apply the pose, one to render it.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = viewport.GetTexture().GetImage();
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    /// <summary>Hard alpha, brightness → palette ramp, then a 1px Void outline around the silhouette.</summary>
    private Image Process(Image source)
    {
        int w = source.GetWidth(), h = source.GetHeight();
        float contrast = Float("contrast", 1.45f);
        float boneAbove = Float("bone", 0.93f);
        var solid = new bool[w, h];
        var result = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = source.GetPixel(x, y);
            if (c.A < 0.5f) continue;
            solid[x, y] = true;

            float lum = Mathf.Clamp((0.299f * c.R + 0.587f * c.G + 0.114f * c.B) * contrast, 0f, 1f);
            var color = lum >= boneAbove ? Bone : Ramp[Math.Min(Ramp.Length - 1, (int)(lum * Ramp.Length))];
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

    /// <summary>"Walking_A" → "walking", "1H_Melee_Attack_Chop" → "1h-melee-attack-chop".</summary>
    private static string Slug(string animation)
    {
        var name = animation.ToLowerInvariant();
        if (name.EndsWith("_a")) name = name[..^2]; // variant suffix only, never "_attack"
        return name.Replace('_', '-');
    }
}
