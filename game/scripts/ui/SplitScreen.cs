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

        var rects = Layout(rooms.Count, screen);
        for (int i = 0; i < rooms.Count; i++)
        {
            var (container, camera) = _views[i];
            if (container.Position != rects[i].Position) container.Position = rects[i].Position;
            if (container.Size != rects[i].Size) container.Size = rects[i].Size;

            float zoom = Mathf.Min(rects[i].Size.X / WorldLayout.RoomWidth, rects[i].Size.Y / WorldLayout.RoomHeight);
            camera.Zoom = new Vector2(zoom, zoom);
            camera.Position = WorldLayout.RoomCenter(rooms[i]);
        }
    }

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
