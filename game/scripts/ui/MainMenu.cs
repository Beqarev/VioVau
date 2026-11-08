using Godot;
using VioVau.Core;
using VioVau.Game.Input;
using VioVau.Game.Networking;

namespace VioVau.Game.UI;

/// <summary>
/// Title screen and lobby. Left: what to do (Play solo / Host / Join / Quit). Right: who's at this machine,
/// or — once connected — the lobby. Local players join with their device's button.
///
/// Dev command-line args (after <c>--</c>): <c>--name=X --host --start-at=N --join=IP --arrows --wasd</c>.
/// </summary>
public partial class MainMenu : Control
{
    private static bool _commandLineHandled;
    private static string _lastAddress = "127.0.0.1";
    private int _autoStartAt;
    private bool _soloPending;

    private NetSession _net = null!;
    private LineEdit _name = null!, _address = null!;
    private Button _solo = null!, _host = null!, _join = null!, _start = null!, _leave = null!, _reset = null!, _quit = null!;
    private Label _panelTitle = null!, _panelBody = null!, _status = null!;

    public override void _Ready()
    {
        _net = NetSession.Instance;
        Build();

        _net.RosterChanged += OnRosterChanged;
        _net.StatusChanged += Refresh;
        LocalPlayers.Changed += Refresh;

        Refresh();
        Callable.From(HandleCommandLine).CallDeferred(); // never change scenes while this one is still being built

        if (OS.GetCmdlineUserArgs().Contains("--menu-shot"))
        {
            // Dev: screenshot the menu (into --screenshot-dir) after it has settled.
            GetTree().CreateTimer(1.5).Timeout += () =>
            {
                DirAccess.MakeDirRecursiveAbsolute(Dev.DevOptions.ScreenshotDir);
                GetViewport().GetTexture().GetImage().SavePng($"{Dev.DevOptions.ScreenshotDir}/menu.png");
            };
        }
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

    // ── Layout ──────────────────────────────────────────────────────────────

    private void Build()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = MenuStyle.Create();
        AddChild(new MenuBackground());

        var title = MakeLabel("VIOLET VAULTS", 30, Palette.White);
        title.Position = new Vector2(24, 18);
        title.AddThemeColorOverride("font_shadow_color", new Color("6a3fa8"));
        title.AddThemeConstantOverride("shadow_offset_x", 0);
        title.AddThemeConstantOverride("shadow_offset_y", 2);
        AddChild(title);

        var tagline = MakeLabel("steal what you can  ·  stay quiet  ·  get out alive", 9, Palette.Dim);
        tagline.Position = new Vector2(26, 56);
        AddChild(tagline);

        // Left: actions.
        var actions = new VBoxContainer { Position = new Vector2(24, 82), CustomMinimumSize = new Vector2(170, 0) };
        actions.AddThemeConstantOverride("separation", 6);
        AddChild(actions);

        _solo = MakeButton("Play solo", OnSolo);
        _host = MakeButton("Host a game", OnHost);
        _address = new LineEdit { Text = _lastAddress, PlaceholderText = "Host's IP address", CustomMinimumSize = new Vector2(0, 20) };
        _join = MakeButton("Join a game", OnJoin);
        _quit = MakeButton("Quit", () => GetTree().Quit());
        foreach (var c in new Control[] { _solo, _host, _join })
        {
            c.CustomMinimumSize = new Vector2(170, 22);
            actions.AddChild(c);
        }
        var joinRow = new HBoxContainer();
        joinRow.AddThemeConstantOverride("separation", 4);
        joinRow.AddChild(MakeLabel("IP", 9, Palette.Dim));
        _address.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        joinRow.AddChild(_address);
        actions.AddChild(joinRow);
        _quit.CustomMinimumSize = new Vector2(170, 20);
        actions.AddChild(_quit);

        // Right: players at this machine / lobby.
        var panel = new PanelContainer { Position = new Vector2(220, 82), CustomMinimumSize = new Vector2(236, 140) };
        AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        panel.AddChild(column);

        _panelTitle = MakeLabel("", 11, Palette.White);
        column.AddChild(_panelTitle);
        _panelBody = MakeLabel("", 9, Palette.White);
        _panelBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _panelBody.CustomMinimumSize = new Vector2(216, 0);
        column.AddChild(_panelBody);

        _name = new LineEdit { Text = LocalPlayers.BaseName, PlaceholderText = "Your name", MaxLength = 16 };
        _name.TextChanged += text => LocalPlayers.BaseName = text;
        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 6);
        nameRow.AddChild(MakeLabel("Name", 9, Palette.Dim));
        _name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        nameRow.AddChild(_name);
        column.AddChild(nameRow);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        _start = MakeButton("Start run", () => _net.StartGame());
        _leave = MakeButton("Leave", () => _net.Leave(returnToMenu: false));
        _reset = MakeButton("Clear players", LocalPlayers.Clear);
        buttons.AddChild(_start);
        buttons.AddChild(_leave);
        buttons.AddChild(_reset);
        column.AddChild(buttons);

        _status = MakeLabel("", 9, Palette.Accent);
        _status.Position = new Vector2(24, 228);
        _status.CustomMinimumSize = new Vector2(432, 0);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        AddChild(_status);

        var controls = MakeLabel("Move WASD / arrows / stick   ·   Sprint Shift / . / RB   ·   Interact E / / / A   ·   Cards 1-3 / K L ; / X Y B", 7, Palette.Wall);
        controls.Position = new Vector2(24, 246);
        AddChild(controls);
    }

    // ── Actions ─────────────────────────────────────────────────────────────

    /// <summary>Host with nobody else and start right away.</summary>
    private void OnSolo()
    {
        if (!_net.Host(Dev.DevOptions.Port)) return;
        _soloPending = true;
        Callable.From(() => { if (_soloPending) _net.StartGame(); }).CallDeferred();
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

        _solo.Disabled = connected;
        _host.Disabled = connected;
        _join.Disabled = connected;
        _address.Editable = !connected;
        _name.Editable = !connected;
        _reset.Visible = !connected && LocalPlayers.Devices.Count > 0;
        _start.Visible = _net.IsHost;
        _start.Disabled = _net.Roster.Count == 0;
        _leave.Visible = connected;

        if (!connected)
        {
            _panelTitle.Text = "AT THIS MACHINE";
            var players = LocalPlayers.Devices.Count == 0
                ? "Nobody yet — Host / Play solo adds Keyboard WASD.\n"
                : string.Join("\n", LocalPlayers.Devices.Select((d, i) => $"P{i + 1}   {d.Label}")) + "\n";
            _panelBody.Text = players + "\nMore players on this machine: press SPACE (WASD), ENTER (arrows) or (A) on a gamepad.";
        }
        else
        {
            _panelTitle.Text = _net.IsHost ? $"YOUR LOBBY   {_net.Roster.Count}/{RunState.MaxPlayers}" : $"LOBBY   {_net.Roster.Count}/{RunState.MaxPlayers}";
            _panelBody.Text = _net.Roster.Count == 0
                ? "Connecting…"
                : string.Join("\n", _net.Roster.Select((r, i) => $"P{i + 1}   {r.Name}{(r.PeerId == _net.LocalPeerId ? "  (you)" : "")}"))
                  + (_net.IsHost ? "\n\nFriends join with your IP address. Start when everyone's in." : "\n\nWaiting for the host to start…");
        }

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
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, Alignment = HorizontalAlignment.Left };
        button.Pressed += onPressed;
        return button;
    }
}
