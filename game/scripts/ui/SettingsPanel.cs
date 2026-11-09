using Godot;
using VioVau.Game.Settings;

namespace VioVau.Game.UI;

/// <summary>Fullscreen + volume sliders. Used from the main menu and the in-run pause menu.</summary>
public partial class SettingsPanel : Control
{
    private CheckButton _fullscreen = null!;
    private HSlider _master = null!, _music = null!, _sfx = null!;
    private Label _masterValue = null!, _musicValue = null!, _sfxValue = null!;
    private Action? _onBack;
    private bool _suppress;

    public SettingsPanel Init(Action onBack)
    {
        _onBack = onBack;
        return this;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = MenuStyle.Create();

        // Opaque dim so the HUD doesn't bleed through.
        var dim = new ColorRect
        {
            Color = new Color(Palette.Void, 0.88f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        // CenterContainer reliably centers the panel in both the menu and the pause overlay.
        var center = new CenterContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
        };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(280, 0),
            Theme = MenuStyle.Create(),
        };
        center.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);

        var title = new Label { Text = "SETTINGS", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 14);
        title.AddThemeColorOverride("font_color", Palette.White);
        column.AddChild(title);

        _fullscreen = new CheckButton { Text = "Fullscreen", FocusMode = FocusModeEnum.None };
        _fullscreen.Toggled += OnFullscreenToggled;
        column.AddChild(_fullscreen);

        (_master, _masterValue) = AddSlider(column, "Master", v => GameSettings.Instance.SetMasterVolume(v));
        (_music, _musicValue) = AddSlider(column, "Music", v => GameSettings.Instance.SetMusicVolume(v));
        (_sfx, _sfxValue) = AddSlider(column, "SFX", v => GameSettings.Instance.SetSfxVolume(v));

        var back = new Button
        {
            Text = "Back",
            FocusMode = FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 24),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        back.Pressed += () => _onBack?.Invoke();
        Audio.WireButton(back);
        Audio.WireButton(_fullscreen);
        column.AddChild(back);

        RefreshFromSettings();
        GameSettings.Instance.Changed += RefreshFromSettings;
    }

    public override void _ExitTree()
    {
        if (GameSettings.Instance is not null)
            GameSettings.Instance.Changed -= RefreshFromSettings;
    }

    private void RefreshFromSettings()
    {
        var s = GameSettings.Instance;
        _suppress = true;
        _fullscreen.ButtonPressed = s.Fullscreen;
        _master.Value = s.MasterVolume;
        _music.Value = s.MusicVolume;
        _sfx.Value = s.SfxVolume;
        UpdateValueLabels();
        _suppress = false;
    }

    private void OnFullscreenToggled(bool on)
    {
        if (_suppress) return;
        GameSettings.Instance.SetFullscreen(on);
    }

    private (HSlider, Label) AddSlider(VBoxContainer parent, string name, Action<float> onChanged)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        parent.AddChild(row);

        var label = new Label
        {
            Text = name,
            CustomMinimumSize = new Vector2(56, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", 10);
        label.AddThemeColorOverride("font_color", Palette.Dim);
        row.AddChild(label);

        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 1,
            Step = 0.05,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            CustomMinimumSize = new Vector2(140, 18),
            FocusMode = FocusModeEnum.None,
        };
        row.AddChild(slider);

        var value = new Label
        {
            CustomMinimumSize = new Vector2(40, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        value.AddThemeFontSizeOverride("font_size", 10);
        value.AddThemeColorOverride("font_color", Palette.White);
        row.AddChild(value);

        slider.ValueChanged += v =>
        {
            if (_suppress) return;
            value.Text = $"{(int)Math.Round(v * 100)}%";
            onChanged((float)v);
        };

        return (slider, value);
    }

    private void UpdateValueLabels()
    {
        _masterValue.Text = $"{(int)Math.Round(_master.Value * 100)}%";
        _musicValue.Text = $"{(int)Math.Round(_music.Value * 100)}%";
        _sfxValue.Text = $"{(int)Math.Round(_sfx.Value * 100)}%";
    }
}
