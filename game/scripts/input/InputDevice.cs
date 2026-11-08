using Godot;

namespace VioVau.Game.Input;

/// <summary>
/// One physical way to control one local player. Devices are polled directly (not via the global
/// InputMap) so up to 4 people can share one machine without fighting over actions.
/// </summary>
public abstract class InputDevice
{
    /// <summary>Stable unique key, e.g. "kb-wasd" or "pad-0".</summary>
    public abstract string Key { get; }
    public abstract string Label { get; }

    /// <summary>Short name of the interact button, for prompts.</summary>
    public abstract string InteractLabel { get; }

    public abstract Vector2 ReadMove();
    public abstract bool ReadSprint();

    /// <summary>Whether the interact button is held (edge detection is done by the character).</summary>
    public abstract bool ReadInteract();

    /// <summary>The device whose "join" button this event is, if any.</summary>
    public static InputDevice? FromJoinEvent(InputEvent e) => e switch
    {
        InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Godot.Key.Space } => new KeyboardWasd(),
        InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Godot.Key.Enter or Godot.Key.KpEnter } => new KeyboardArrows(),
        InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.A } pad => new Gamepad(pad.Device),
        _ => null,
    };

    /// <summary>The device key whose "leave" button this event is, if any (gamepad B).</summary>
    public static string? LeaveKeyFromEvent(InputEvent e) => e switch
    {
        InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.B } pad => new Gamepad(pad.Device).Key,
        _ => null,
    };

    protected static Vector2 Keys(Godot.Key up, Godot.Key down, Godot.Key left, Godot.Key right) => new(
        (Godot.Input.IsPhysicalKeyPressed(right) ? 1 : 0) - (Godot.Input.IsPhysicalKeyPressed(left) ? 1 : 0),
        (Godot.Input.IsPhysicalKeyPressed(down) ? 1 : 0) - (Godot.Input.IsPhysicalKeyPressed(up) ? 1 : 0));
}

public sealed class KeyboardWasd : InputDevice
{
    public override string Key => "kb-wasd";
    public override string Label => "Keyboard WASD";
    public override string InteractLabel => "E";
    public override Vector2 ReadMove() => Keys(Godot.Key.W, Godot.Key.S, Godot.Key.A, Godot.Key.D);
    public override bool ReadSprint() => Godot.Input.IsPhysicalKeyPressed(Godot.Key.Shift);
    public override bool ReadInteract() => Godot.Input.IsPhysicalKeyPressed(Godot.Key.E);
}

public sealed class KeyboardArrows : InputDevice
{
    public override string Key => "kb-arrows";
    public override string Label => "Keyboard Arrows";
    public override string InteractLabel => "/";
    public override Vector2 ReadMove() => Keys(Godot.Key.Up, Godot.Key.Down, Godot.Key.Left, Godot.Key.Right);
    public override bool ReadSprint() => Godot.Input.IsPhysicalKeyPressed(Godot.Key.Period);
    public override bool ReadInteract() => Godot.Input.IsPhysicalKeyPressed(Godot.Key.Slash);
}

public sealed class Gamepad : InputDevice
{
    private const float Deadzone = 0.2f;

    public int Device { get; }

    public Gamepad(int device) => Device = device;

    public override string Key => $"pad-{Device}";
    public override string Label => $"Gamepad {Device + 1}";
    public override string InteractLabel => "A";

    public override Vector2 ReadMove()
    {
        var stick = new Vector2(
            Godot.Input.GetJoyAxis(Device, JoyAxis.LeftX),
            Godot.Input.GetJoyAxis(Device, JoyAxis.LeftY));
        if (stick.Length() < Deadzone) stick = Vector2.Zero;

        var dpad = new Vector2(
            (Pressed(JoyButton.DpadRight) ? 1 : 0) - (Pressed(JoyButton.DpadLeft) ? 1 : 0),
            (Pressed(JoyButton.DpadDown) ? 1 : 0) - (Pressed(JoyButton.DpadUp) ? 1 : 0));

        return dpad != Vector2.Zero ? dpad : stick;
    }

    public override bool ReadSprint() =>
        Pressed(JoyButton.RightShoulder) || Godot.Input.GetJoyAxis(Device, JoyAxis.TriggerRight) > 0.5f;

    public override bool ReadInteract() => Pressed(JoyButton.A) || Pressed(JoyButton.X);

    private bool Pressed(JoyButton button) => Godot.Input.IsJoyButtonPressed(Device, button);
}
