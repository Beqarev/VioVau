using Godot;
using VioVau.Core;
using VioVau.Game.Input;
using VioVau.Game.Networking;
using VioVau.Game.UI;
using VioVau.Game.World;

namespace VioVau.Game.Entities;

/// <summary>
/// A player's body. Local players move with physics and stream their position to everyone;
/// remote players are smoothly interpolated toward the latest position received.
/// </summary>
public partial class PlayerCharacter : CharacterBody2D
{
    public const float WalkSpeed = 80f;
    public const float SprintSpeed = 135f;
    private const double SendInterval = 1.0 / 30.0;
    private const float SnapDistance = 96f;

    private Vector2 _remoteTarget;
    private double _sendTimer;

    public PlayerId Id { get; private set; }
    public string DisplayName { get; private set; } = "";
    public int ColorIndex { get; private set; }
    public InputDevice? Device { get; private set; }
    public bool IsLocal => Device is not null;

    /// <summary>Best known true position: our own for local players, the last received one for remote players.</summary>
    public Vector2 AuthoritativePosition => IsLocal ? GlobalPosition : _remoteTarget;

    public PlayerCharacter Init(PlayerId id, string displayName, int colorIndex, InputDevice? device, Vector2 spawn)
    {
        Id = id;
        DisplayName = displayName;
        ColorIndex = colorIndex;
        Device = device;
        Name = $"Player_{id.Value}";
        Position = spawn;
        _remoteTarget = spawn;
        return this;
    }

    public override void _Ready()
    {
        MotionMode = MotionModeEnum.Floating;
        CollisionLayer = PhysicsLayers.Players;
        CollisionMask = PhysicsLayers.Walls; // players pass through each other
        ZIndex = 10;
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 5f } });
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Device is not null)
        {
            var move = Device.ReadMove();
            if (move.LengthSquared() > 1f) move = move.Normalized();
            Velocity = move * (Device.ReadSprint() ? SprintSpeed : WalkSpeed);
            MoveAndSlide();

            _sendTimer += delta;
            if (_sendTimer >= SendInterval)
            {
                _sendTimer = 0;
                NetSession.Instance.SendPlayerState(Id, GlobalPosition, Velocity);
            }
        }
        else
        {
            Position = Position.DistanceTo(_remoteTarget) > SnapDistance
                ? _remoteTarget
                : Position.Lerp(_remoteTarget, 1f - Mathf.Exp(-18f * (float)delta));
        }
    }

    public void ApplyRemoteState(Vector2 position, Vector2 velocity)
    {
        if (IsLocal) return;
        _remoteTarget = position;
        Velocity = velocity;
    }

    public override void _Draw()
    {
        var color = Palette.PlayerColor(ColorIndex);
        var body = new Rect2(-5, -6, 10, 12);
        DrawRect(body, Palette.Void);
        DrawRect(body.Grow(-1), color);
        DrawString(ThemeDB.FallbackFont, new Vector2(-3, -9), (ColorIndex + 1).ToString(),
            HorizontalAlignment.Left, -1, 8, color);
    }
}
