using Godot;
using VioVau.Game.Networking;

namespace VioVau.Game.UI;

/// <summary>
/// In-run Esc menu: Resume, Settings, Leave run.
/// Pauses the scene tree when every player is on this machine (solo / couch);
/// online hosts keep simulating so remote peers aren't frozen.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
    public static PauseMenu? Instance { get; private set; }

    private Control _root = null!;
    private PanelContainer _pausePanel = null!;
    private VBoxContainer _main = null!;
    private SettingsPanel? _settings;
    private bool _open;
    private bool _worldPaused;

    public bool IsOpen => _open;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        Layer = 20;
        Visible = false;

        _root = new Control { Name = "Root" };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_root);

        var dim = new ColorRect { Color = new Color(Palette.Void, 0.7f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        _root.AddChild(dim);

        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        _pausePanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(200, 0),
            Theme = MenuStyle.Create(),
        };
        center.AddChild(_pausePanel);

        _main = new VBoxContainer();
        _main.AddThemeConstantOverride("separation", 6);
        _pausePanel.AddChild(_main);

        var title = new Label { Text = "PAUSED", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 14);
        title.AddThemeColorOverride("font_color", Palette.White);
        _main.AddChild(title);

        _main.AddChild(MakeButton("Resume", Close));
        _main.AddChild(MakeButton("Settings", OpenSettings));
        _main.AddChild(MakeButton("Leave run", LeaveRun));
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
        if (_worldPaused && GetTree() is { } tree)
        {
            tree.Paused = false;
            _worldPaused = false;
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        // Esc on keyboards, Start on gamepads. On the scoreboard, Start belongs to "play again" (host).
        bool escape = e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Escape };
        bool start = e is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.Start } && NetSession.Instance.View?.Results is null;
        if (!escape && !start) return;

        GetViewport().SetInputAsHandled();
        if (_settings is not null)
        {
            CloseSettings();
            return;
        }
        if (_open) Close();
        else Open();
    }

    public void Open()
    {
        if (_open) return;
        _open = true;
        Visible = true;
        _pausePanel.Visible = true;
        CloseSettings();

        // Only freeze the world when nobody remote would be left running alone.
        var net = NetSession.Instance;
        bool localOnly = net.Roster.All(r => r.PeerId == net.LocalPeerId);
        if (localOnly)
        {
            GetTree().Paused = true;
            _worldPaused = true;
        }
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        Visible = false;
        CloseSettings();
        if (_worldPaused)
        {
            GetTree().Paused = false;
            _worldPaused = false;
        }
    }

    private void OpenSettings()
    {
        _pausePanel.Visible = false;
        _settings = new SettingsPanel().Init(CloseSettings);
        _root.AddChild(_settings);
    }

    private void CloseSettings()
    {
        _settings?.QueueFree();
        _settings = null;
        if (_open) _pausePanel.Visible = true;
    }

    private void LeaveRun()
    {
        if (_worldPaused)
        {
            GetTree().Paused = false;
            _worldPaused = false;
        }
        _open = false;
        NetSession.Instance.Leave();
    }

    private static Button MakeButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            FocusMode = Control.FocusModeEnum.None,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, 22),
        };
        button.Pressed += onPressed;
        Audio.WireButton(button);
        return button;
    }
}
