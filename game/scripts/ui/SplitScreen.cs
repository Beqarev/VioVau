using Godot;
using VioVau.Core;
using VioVau.Game.World;

namespace VioVau.Game.UI;

/// <summary>
/// One view per distinct room that local players occupy: local players in the same room share a screen,
/// and the screen splits (up to 4 ways) when they separate. All views render the same shared world.
/// Must live inside a CanvasLayer, otherwise the views would render themselves.
/// </summary>
public partial class SplitScreen : Control
{
    private const float Gap = 1f;

    private readonly List<(SubViewportContainer Container, Camera2D Camera)> _views = new();
    private World2D _world = null!;
    private float _shake;
    private readonly System.Random _random = new();

    /// <summary>Shake every view (pixels of offset), fading out quickly.</summary>
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

    public void ShowRooms(IReadOnlyList<RoomId> rooms)
    {
        // Inside a CanvasLayer the Control's own Size can still be zero before layout runs; the viewport is the truth.
        var screen = GetViewportRect().Size;
        if (screen.X <= 0 || screen.Y <= 0) return;
        if (Size != screen) Size = screen;

        while (_views.Count < rooms.Count) AddView();
        while (_views.Count > rooms.Count) RemoveLastView();

        var slots = Layout(rooms.Count, screen);
        for (int i = 0; i < rooms.Count; i++)
        {
            var (container, camera) = _views[i];

            // Letterbox each view to exactly one room, so neighbouring rooms never peek in.
            float zoom = Mathf.Min(slots[i].Size.X / WorldLayout.RoomWidth, slots[i].Size.Y / WorldLayout.RoomHeight);
            var size = (WorldLayout.RoomSize * zoom).Floor();
            var position = (slots[i].Position + (slots[i].Size - size) / 2f).Floor();
            if (container.Position != position) container.Position = position;
            if (container.Size != size) container.Size = size;

            camera.Zoom = new Vector2(zoom, zoom);
            var jitter = _shake > 0.1f ? new Vector2((float)_random.NextDouble() * 2 - 1, (float)_random.NextDouble() * 2 - 1) * _shake : Vector2.Zero;
            camera.Position = WorldLayout.RoomCenter(rooms[i]) + jitter.Round();
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
        };
        var camera = new Camera2D();
        viewport.AddChild(camera);

        var container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        container.AddChild(viewport);
        AddChild(container);
        camera.MakeCurrent();

        _views.Add((container, camera));
    }

    private void RemoveLastView()
    {
        var (container, _) = _views[^1];
        _views.RemoveAt(_views.Count - 1);
        container.QueueFree();
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
