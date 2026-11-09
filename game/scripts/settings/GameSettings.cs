using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace VioVau.Game.Settings;

/// <summary>
/// Autoload. Loads/saves <c>user://settings.json</c> and applies display + audio bus volumes.
/// Creates Master / Music / SFX buses if the project has none yet (audio content lands in task 2).
/// </summary>
public partial class GameSettings : Node
{
    public const string Path = "user://settings.json";
    public const string BusMaster = "Master";
    public const string BusMusic = "Music";
    public const string BusSfx = "SFX";

    public static GameSettings Instance { get; private set; } = null!;

    public bool Fullscreen { get; private set; }
    public float MasterVolume { get; private set; } = 1f;
    public float MusicVolume { get; private set; } = 0.8f;
    public float SfxVolume { get; private set; } = 1f;

    public event Action? Changed;

    public override void _EnterTree()
    {
        Instance = this;
        EnsureAudioBuses();
        Load();
        Apply();
    }

    public void SetFullscreen(bool value)
    {
        if (Fullscreen == value) return;
        Fullscreen = value;
        ApplyDisplay();
        Save();
        Changed?.Invoke();
    }

    public void SetMasterVolume(float value) => SetVolume(nameof(MasterVolume), value);
    public void SetMusicVolume(float value) => SetVolume(nameof(MusicVolume), value);
    public void SetSfxVolume(float value) => SetVolume(nameof(SfxVolume), value);

    private void SetVolume(string which, float value)
    {
        value = Mathf.Clamp(value, 0f, 1f);
        switch (which)
        {
            case nameof(MasterVolume):
                if (Mathf.IsEqualApprox(MasterVolume, value)) return;
                MasterVolume = value;
                break;
            case nameof(MusicVolume):
                if (Mathf.IsEqualApprox(MusicVolume, value)) return;
                MusicVolume = value;
                break;
            case nameof(SfxVolume):
                if (Mathf.IsEqualApprox(SfxVolume, value)) return;
                SfxVolume = value;
                break;
        }
        ApplyVolumes();
        Save();
        Changed?.Invoke();
    }

    public void Apply()
    {
        ApplyDisplay();
        ApplyVolumes();
    }

    private void ApplyDisplay()
    {
        var mode = Fullscreen
            ? DisplayServer.WindowMode.Fullscreen
            : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode)
            DisplayServer.WindowSetMode(mode);
    }

    private void ApplyVolumes()
    {
        SetBusLinear(BusMaster, MasterVolume);
        SetBusLinear(BusMusic, MusicVolume);
        SetBusLinear(BusSfx, SfxVolume);
    }

    private static void SetBusLinear(string name, float linear)
    {
        int idx = AudioServer.GetBusIndex(name);
        if (idx < 0) return;
        AudioServer.SetBusMute(idx, linear <= 0.0001f);
        if (linear > 0.0001f)
            AudioServer.SetBusVolumeDb(idx, Mathf.LinearToDb(linear));
    }

    private void Load()
    {
        if (!Godot.FileAccess.FileExists(Path))
        {
            Save(); // write defaults so user://settings.json exists on first launch
            return;
        }

        try
        {
            using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Read);
            var data = JsonSerializer.Deserialize<SettingsData>(file.GetAsText());
            if (data is null) return;
            Fullscreen = data.Fullscreen;
            MasterVolume = Mathf.Clamp(data.MasterVolume, 0f, 1f);
            MusicVolume = Mathf.Clamp(data.MusicVolume, 0f, 1f);
            SfxVolume = Mathf.Clamp(data.SfxVolume, 0f, 1f);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Settings] Failed to load {Path}: {e.Message}");
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(new SettingsData
            {
                Fullscreen = Fullscreen,
                MasterVolume = MasterVolume,
                MusicVolume = MusicVolume,
                SfxVolume = SfxVolume,
            }, new JsonSerializerOptions { WriteIndented = true });
            using var file = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
            file.StoreString(json);
        }
        catch (Exception e)
        {
            GD.PushWarning($"[Settings] Failed to save {Path}: {e.Message}");
        }
    }

    /// <summary>Godot ships with only Master; add Music and SFX under it for the upcoming audio pass.</summary>
    private static void EnsureAudioBuses()
    {
        EnsureBus(BusMusic, BusMaster);
        EnsureBus(BusSfx, BusMaster);
    }

    private static void EnsureBus(string name, string sendTo)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, name);
        AudioServer.SetBusSend(idx, sendTo);
    }

    private sealed class SettingsData
    {
        [JsonPropertyName("fullscreen")] public bool Fullscreen { get; set; }
        [JsonPropertyName("masterVolume")] public float MasterVolume { get; set; } = 1f;
        [JsonPropertyName("musicVolume")] public float MusicVolume { get; set; } = 0.8f;
        [JsonPropertyName("sfxVolume")] public float SfxVolume { get; set; } = 1f;
    }
}
