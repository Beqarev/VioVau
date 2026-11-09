using Godot;
using VioVau.Core;
using VioVau.Game.Input;
using VioVau.Game.Networking;

namespace VioVau.Game.UI;

/// <summary>
/// Title screen and lobby. Left: Play / Host / Join. Right: who's here (or the lobby).
/// Settings and Quit sit in a footer strip so they never cover status text.
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
    private Button _solo = null!, _host = null!, _join = null!, _start = null!, _leave = null!, _reset = null!, _settings = null!, _quit = null!;
    private Control _joinRow = null!;
    private Label _panelTitle = null!, _panelBody = null!, _status = null!, _hint = null!;
    private SettingsPanel? _settingsPanel;

    public override void _Ready()
    {
        _net = NetSession.Instance;
        Build();
        Audio.Instance.PlayMenu();

        _net.RosterChanged += OnRosterChanged;
        _net.StatusChanged += Refresh;
        LocalPlayers.Changed += Refresh;

        Refresh();
        Callable.From(HandleCommandLine).CallDeferred(); // never change scenes while this one is still being built

        if (OS.GetCmdlineUserArgs().Contains("--menu-shot"))
        {
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

    // ── Layout (480×270) ────────────────────────────────────────────────────

    private void Build()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = MenuStyle.Create();
        AddChild(new MenuBackground());

        var title = MakeLabel("VIOLET VAULTS", 28, Palette.White);
        title.Position = new Vector2(24, 16);
        title.AddThemeColorOverride("font_shadow_color", new Color("6a3fa8"));
        title.AddThemeConstantOverride("shadow_offset_x", 0);
        title.AddThemeConstantOverride("shadow_offset_y", 2);
        AddChild(title);

        var tagline = MakeLabel("steal what you can  ·  stay quiet  ·  get out alive", 8, Palette.Dim);
        tagline.Position = new Vector2(26, 48);
        AddChild(tagline);

        // Left: primary actions only (fits above the footer).
        var actions = new VBoxContainer
        {
            Position = new Vector2(24, 72),
            CustomMinimumSize = new Vector2(168, 0),
        };
        actions.AddThemeConstantOverride("separation", 5);
        AddChild(actions);

        _solo = MakeButton("Play solo", OnSolo, 168, 22);
        _host = MakeButton("Host a game", OnHost, 168, 22);
        _join = MakeButton("Join a game", OnJoin, 168, 22);
        actions.AddChild(_solo);
        actions.AddChild(_host);
        actions.AddChild(_join);

        _joinRow = new HBoxContainer();
        _joinRow.AddThemeConstantOverride("separation", 6);
        _joinRow.AddChild(MakeLabel("IP", 9, Palette.Dim));
        _address = new LineEdit
        {
            Text = _lastAddress,
            PlaceholderText = "Host IP",
            CustomMinimumSize = new Vector2(0, 18),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _joinRow.AddChild(_address);
        actions.AddChild(_joinRow);

        var lobbyButtons = new HBoxContainer();
        lobbyButtons.AddThemeConstantOverride("separation", 6);
        _start = MakeButton("Start run", () => _net.StartGame(), 0, 20);
        _leave = MakeButton("Leave", () => _net.Leave(returnToMenu: false), 0, 20);
        lobbyButtons.AddChild(_start);
        lobbyButtons.AddChild(_leave);
        actions.AddChild(lobbyButtons);

        // Right: who's here / lobby — short copy only.
        var panel = new PanelContainer
        {
            Position = new Vector2(212, 72),
            CustomMinimumSize = new Vector2(244, 130),
        };
        AddChild(panel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 5);
        panel.AddChild(column);

        _panelTitle = MakeLabel("", 11, Palette.White);
        column.AddChild(_panelTitle);

        _panelBody = MakeLabel("", 9, Palette.White);
        _panelBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _panelBody.CustomMinimumSize = new Vector2(220, 0);
        column.AddChild(_panelBody);

        _hint = MakeLabel("", 8, Palette.Dim);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _hint.CustomMinimumSize = new Vector2(220, 0);
        column.AddChild(_hint);

        var nameRow = new HBoxContainer();
        nameRow.AddThemeConstantOverride("separation", 6);
        nameRow.AddChild(MakeLabel("Name", 9, Palette.Dim));
        _name = new LineEdit
        {
            Text = LocalPlayers.BaseName,
            PlaceholderText = "Your name",
            MaxLength = 16,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 18),
        };
        _name.TextChanged += text => LocalPlayers.BaseName = text;
        nameRow.AddChild(_name);
        column.AddChild(nameRow);

        _reset = MakeButton("Clear", LocalPlayers.Clear, 0, 18);
        column.AddChild(_reset);

        // Footer strip: status left, Settings + Quit right — never overlaps the columns.
        var footer = new HBoxContainer
        {
            Position = new Vector2(24, 236),
            CustomMinimumSize = new Vector2(432, 22),
        };
        footer.AddThemeConstantOverride("separation", 8);
        AddChild(footer);

        _status = MakeLabel("", 8, Palette.Accent);
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.AutowrapMode = TextServer.AutowrapMode.Off;
        _status.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _status.CustomMinimumSize = new Vector2(0, 18);
        footer.AddChild(_status);

        _settings = MakeButton("Settings", OpenSettings, 70, 20);
        _quit = MakeButton("Quit", () => GetTree().Quit(), 52, 20);
        footer.AddChild(_settings);
        footer.AddChild(_quit);
    }

    // ── Actions ─────────────────────────────────────────────────────────────

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

    private void OpenSettings()
    {
        if (_settingsPanel is not null) return;
        _settingsPanel = new SettingsPanel().Init(CloseSettings);
        AddChild(_settingsPanel);
    }

    private void CloseSettings()
    {
        _settingsPanel?.QueueFree();
        _settingsPanel = null;
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
        _joinRow.Visible = !connected;
        _address.Editable = !connected;
        _name.Editable = !connected;
        _reset.Visible = !connected && LocalPlayers.Devices.Count > 0;
        _start.Visible = _net.IsHost;
        _start.Disabled = _net.Roster.Count == 0;
        _leave.Visible = connected;

        if (!connected)
        {
            _panelTitle.Text = "AT THIS MACHINE";
            _panelBody.Text = LocalPlayers.Devices.Count == 0
                ? "No one yet."
                : string.Join("\n", LocalPlayers.Devices.Select((d, i) => $"P{i + 1}  {d.Label}"));
            _hint.Text = "Add a player: Space · Enter · gamepad A";
        }
        else
        {
            _panelTitle.Text = _net.IsHost
                ? $"LOBBY  {_net.Roster.Count}/{RunState.MaxPlayers}"
                : $"LOBBY  {_net.Roster.Count}/{RunState.MaxPlayers}";
            _panelBody.Text = _net.Roster.Count == 0
                ? "Connecting…"
                : string.Join("\n", _net.Roster.Select((r, i) =>
                    $"P{i + 1}  {r.Name}{(r.PeerId == _net.LocalPeerId ? "  (you)" : "")}"));
            _hint.Text = _net.IsHost ? "Friends join your IP — then Start run." : "Waiting for the host…";
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
            OnRosterChanged();
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

    private static Button MakeButton(string text, Action onPressed, float width, float height)
    {
        // No keyboard focus: Space/Enter are join keys, not "press the focused button".
        var button = new Button
        {
            Text = text,
            FocusMode = FocusModeEnum.None,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(width, height),
        };
        button.Pressed += onPressed;
        Audio.WireButton(button);
        return button;
    }
}
