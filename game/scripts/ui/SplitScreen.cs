using Godot;
using VioVau.Core;
using VioVau.Game.World;

namespace VioVau.Game.UI;

/// <summary>
/// One view per distinct room that local players occupy: local players in the same room share a screen,
/// and the screen splits (up to 4 ways) when they separate. All views render the same shared world.
///
/// Views render at the window's real resolution (not the 480×270 design size), so pixel sprites stay crisp
/// while lights, particles and camera motion are smooth. The camera slides between rooms instead of cutting.
/// Must live inside a CanvasLayer, otherwise the views would render themselves.
/// </summary>
public partial class SplitScreen : Control
{
    private const float Gap = 1f;

    /// <summary>How quickly the camera catches up when you walk into the next room (higher = snappier).</summary>
    private const float SlideSharpness = 11f;

    private sealed class View
    {
        public required SubViewport Viewport;
        public required Camera2D Camera;
        public required TextureRect Display;
        public Vector2? Focus;
    }

    private readonly List<View> _views = new();
    private World2D _world = null!;
    private float _shake;
    private readonly System.Random _random = new();
    private ulong _lastTicks;

    /// <summary>Shake every view (pixels of offset, design scale), fading out quickly.</summary>
    public void Shake(float strength) => _shake = Math.Max(_shake, strength);

    public SplitScreen Init(World2D world)
    {
        _world = world;
        return this;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Whole-number factor between the design resolution and the window (integer scaling keeps pixels even).</summary>
    private float PixelScale(Vector2 design)
    {
        var window = (Vector2)GetTree().Root.Size;
        return Mathf.Max(1f, Mathf.Floor(Mathf.Min(window.X / design.X, window.Y / design.Y)));
    }

    public void ShowRooms(IReadOnlyList<RoomId> rooms)
    {
        // Inside a CanvasLayer the Control's own Size can still be zero before layout runs; the viewport is the truth.
        var screen = GetViewportRect().Size;
        if (screen.X <= 0 || screen.Y <= 0) return;
        if (Size != screen) Size = screen;

        ulong now = Time.GetTicksMsec();
        float dt = _lastTicks == 0 ? 0f : Mathf.Min(0.1f, (now - _lastTicks) / 1000f);
        _lastTicks = now;

        while (_views.Count < rooms.Count) AddView();
        while (_views.Count > rooms.Count) RemoveLastView();

        float scale = PixelScale(screen);
        var slots = Layout(rooms.Count, screen);
        for (int i = 0; i < rooms.Count; i++)
        {
            var view = _views[i];

            // Letterbox each view to exactly one room, so neighbouring rooms never peek in.
            float zoom = Mathf.Min(slots[i].Size.X / WorldLayout.RoomWidth, slots[i].Size.Y / WorldLayout.RoomHeight);
            var size = (WorldLayout.RoomSize * zoom).Floor();
            var position = (slots[i].Position + (slots[i].Size - size) / 2f).Floor();
            if (view.Display.Position != position) view.Display.Position = position;
            if (view.Display.Size != size) view.Display.Size = size;

            var pixels = new Vector2I(Mathf.RoundToInt(size.X * scale), Mathf.RoundToInt(size.Y * scale));
            if (view.Viewport.Size != pixels) view.Viewport.Size = pixels;
            view.Camera.Zoom = new Vector2(zoom * scale, zoom * scale);

            // Slide toward the room; jump if it's far (view reassigned, teleport, new run).
            var target = WorldLayout.RoomCenter(rooms[i]);
            var focus = view.Focus is { } current && current.DistanceTo(target) < WorldLayout.RoomWidth * 1.5f
                ? current.Lerp(target, 1f - Mathf.Exp(-SlideSharpness * dt))
                : target;
            if (focus.DistanceTo(target) < 0.25f) focus = target;
            view.Focus = focus;

            var jitter = _shake > 0.1f ? new Vector2((float)_random.NextDouble() * 2 - 1, (float)_random.NextDouble() * 2 - 1) * _shake : Vector2.Zero;
            view.Camera.Position = focus + jitter;
        }
    }

    public override void _Process(double delta) => _shake = Mathf.MoveToward(_shake, 0, (float)delta * 12f);

    public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), Palette.Void);

    private void AddView()
    {
        var viewport = new SubViewport
        {
            World2D = _world,
            CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest,
            HandleInputLocally = false,
            Snap2DTransformsToPixel = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        var camera = new Camera2D();
        viewport.AddChild(camera);
        AddChild(viewport);
        camera.MakeCurrent();

        var display = new TextureRect
        {
            Texture = viewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.Nearest,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(display);

        _views.Add(new View { Viewport = viewport, Camera = camera, Display = display });
    }

    private void RemoveLastView()
    {
        var view = _views[^1];
        _views.RemoveAt(_views.Count - 1);
        view.Display.QueueFree();
        view.Viewport.QueueFree();
    }

    private static Rect2[] Layout(int count, Vector2 size)
    {
        float halfW = (size.X - Gap) / 2f, halfH = (size.Y - Gap) / 2f;
        float right = halfW + Gap, bottom = halfH + Gap;

        return count switch
        {
            <= 1 => [new Rect2(Vector2.Zero, size)],
            2 => [new Rect2(0, 0, halfW, size.Y), new Rect2(right, 0, halfW, size.Y)],
            _ =>
            [
                new Rect2(0, 0, halfW, halfH),
                new Rect2(right, 0, halfW, halfH),
                new Rect2(0, bottom, halfW, halfH),
                new Rect2(right, bottom, halfW, halfH),
            ],
        };
    }
}
