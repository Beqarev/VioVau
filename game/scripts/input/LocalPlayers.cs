using VioVau.Core;

namespace VioVau.Game.Input;

/// <summary>
/// The people sitting at this machine, in slot order. Persists across scenes.
/// Locked while connected — local players are fixed once you host or join.
/// </summary>
public static class LocalPlayers
{
    private static readonly List<InputDevice> _devices = new();

    public static IReadOnlyList<InputDevice> Devices => _devices;
    public static bool Locked { get; set; }
    public static string BaseName { get; set; } = "Player";

    public static event Action? Changed;

    public static bool TryAdd(InputDevice device)
    {
        if (Locked || _devices.Count >= RunState.MaxPlayers || _devices.Any(d => d.Key == device.Key)) return false;
        _devices.Add(device);
        Changed?.Invoke();
        return true;
    }

    public static bool Remove(string key)
    {
        if (Locked || _devices.RemoveAll(d => d.Key == key) == 0) return false;
        Changed?.Invoke();
        return true;
    }

    public static void Clear()
    {
        if (Locked || _devices.Count == 0) return;
        _devices.Clear();
        Changed?.Invoke();
    }

    /// <summary>Make sure there's at least one player (keyboard WASD) before hosting or joining.</summary>
    public static void EnsureOne()
    {
        if (_devices.Count == 0) TryAdd(new KeyboardWasd());
    }

    /// <summary>Display names sent to the host: "Beqa", "Beqa 2", …</summary>
    public static string[] Names()
    {
        var name = string.IsNullOrWhiteSpace(BaseName) ? "Player" : BaseName.Trim();
        return _devices.Select((_, i) => i == 0 ? name : $"{name} {i + 1}").ToArray();
    }

    public static InputDevice? Get(int slot) => slot >= 0 && slot < _devices.Count ? _devices[slot] : null;
}
