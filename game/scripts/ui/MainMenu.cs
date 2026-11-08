using Godot;
using VioVau.Core;
using VioVau.Game.Input;
using VioVau.Game.Networking;

namespace VioVau.Game.UI;

/// <summary>
/// Title + lobby. Local players join with their device's button, then you Host or Join.
/// Placeholder UI built in code; the real Hub replaces it in M6.
///
/// Dev command-line args (after <c>--</c>): <c>--name=X --host --start-at=N --join=IP --arrows --wasd</c>.
/// </summary>
public partial class MainMenu : Control
{
    private static bool _commandLineHandled;
    private static string _lastAddress = "127.0.0.1";
    private int _autoStartAt;

    private NetSession _net = null!;
    private LineEdit _name = null!, _address = null!;
    private Button _host = null!, _join = null!, _start = null!, _leave = null!, _reset = null!;
    private Label _local = null!, _lobby = null!, _status = null!;

    public override void _Ready()
    {
        _net = NetSession.Instance;
        Build();

        _net.RosterChanged += OnRosterChanged;
        _net.StatusChanged += Refresh;
        LocalPlayers.Changed += Refresh;

        Refresh();
        Callable.From(HandleCommandLine).CallDeferred(); // never change scenes while this one is still being built
    }

    public override void _ExitTree()
    {
        _net.RosterChanged -= OnRosterChanged;
        _net.StatusChanged -= Refresh;
        LocalPlayers.Changed -= Refresh;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (InputDevice.FromJoinEvent(e) is { } device && LocalPlayers.TryAdd(device))
            GetViewport().SetInputAsHandled();
        else if (InputDevice.LeaveKeyFromEvent(e) is { } key && LocalPlayers.Remove(key))
            GetViewport().SetInputAsHandled();
    }

    private void Build()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = new Theme { DefaultFontSize = 10 };

        var background = new ColorRect { Color = Palette.Void };
        background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(background);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        foreach (var side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride($"margin_{side}", 16);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 5);
        margin.AddChild(column);

        column.AddChild(MakeLabel("VIOLET VAULTS", 20, Palette.White));
        column.AddChild(MakeLabel("Join on this machine:  SPACE = WASD (+Shift)   ENTER = Arrows (+.)   (A) = gamepad", 8, Palette.Dim));

        _name = new LineEdit { Text = LocalPlayers.BaseName, PlaceholderText = "Name", MaxLength = 16, CustomMinimumSize = new Vector2(120, 0) };
        _name.TextChanged += text => LocalPlayers.BaseName = text;
        column.AddChild(Row(MakeLabel("Name", 10, Palette.Dim), _name));

        _local = MakeLabel("", 10, Palette.White);
        column.AddChild(_local);

        _host = MakeButton("Host", OnHost);
        _address = new LineEdit { Text = _lastAddress, PlaceholderText = "Host IP", CustomMinimumSize = new Vector2(110, 0) };
        _join = MakeButton("Join", OnJoin);
        _reset = MakeButton("Clear local players", LocalPlayers.Clear);
        column.AddChild(Row(_host, _address, _join, _reset));

        _lobby = MakeLabel("", 10, Palette.White);
        column.AddChild(_lobby);

        _start = MakeButton("Start run", () => _net.StartGame());
        _leave = MakeButton("Leave", () => _net.Leave(returnToMenu: false));
        column.AddChild(Row(_start, _leave));

        _status = MakeLabel("", 8, Palette.Accent);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_status);
    }

    private void OnHost()
    {
        _net.Host(Dev.DevOptions.Port);
        Refresh();
    }

    private void OnJoin()
    {
        _lastAddress = string.IsNullOrWhiteSpace(_address.Text) ? "127.0.0.1" : _address.Text.Trim();
        _net.Join(_lastAddress, Dev.DevOptions.Port);
        Refresh();
    }

    private void OnRosterChanged()
    {
        if (_autoStartAt > 0 && _net.IsHost && _net.Roster.Count >= _autoStartAt)
        {
            _autoStartAt = 0;
            _net.StartGame();
        }
        Refresh();
    }

    private void Refresh()
    {
        bool connected = _net.IsOnline;

        _host.Disabled = connected;
        _join.Disabled = connected;
        _address.Editable = !connected;
        _name.Editable = !connected;
        _reset.Disabled = connected || LocalPlayers.Devices.Count == 0;
        _start.Visible = _net.IsHost;
        _start.Disabled = _net.Roster.Count == 0;
        _leave.Disabled = !connected;

        _local.Text = LocalPlayers.Devices.Count == 0
            ? "Local players: none yet (Host/Join adds Keyboard WASD)"
            : "Local players: " + string.Join("   ", LocalPlayers.Devices.Select((d, i) => $"P{i + 1} {d.Label}"));

        _lobby.Text = connected
            ? $"Lobby ({_net.Roster.Count}/{RunState.MaxPlayers}): " +
              string.Join(", ", _net.Roster.Select(r => r.PeerId == _net.LocalPeerId ? $"{r.Name} (you)" : r.Name))
            : "";

        _status.Text = _net.Status;
    }

    private void HandleCommandLine()
    {
        if (_commandLineHandled) return;
        _commandLineHandled = true;

        var args = OS.GetCmdlineUserArgs();
        string? Value(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}="))?.Split('=', 2)[1];

        if (Value("name") is { } name) _name.Text = LocalPlayers.BaseName = name;
        if (args.Contains("--wasd")) LocalPlayers.TryAdd(new KeyboardWasd());
        if (args.Contains("--arrows")) LocalPlayers.TryAdd(new KeyboardArrows());
        if (int.TryParse(Value("start-at"), out var startAt)) _autoStartAt = startAt;

        if (args.Contains("--host"))
        {
            OnHost();
            OnRosterChanged(); // --start-at=1 starts immediately
        }
        else if (Value("join") is { } address)
        {
            _address.Text = address;
            OnJoin();
        }
    }

    private static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static Button MakeButton(string text, Action onPressed)
    {
        // No keyboard focus: Space/Enter are join keys, not "press the focused button".
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None };
        button.Pressed += onPressed;
        return button;
    }

    private static HBoxContainer Row(params Control[] children)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        foreach (var child in children) row.AddChild(child);
        return row;
    }
}
