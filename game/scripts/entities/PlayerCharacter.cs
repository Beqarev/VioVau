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
/// Drawn with the baked Delver sprites (idle / walking / running × 4 directions), a colored ring
/// under the feet marks which player it is.
/// </summary>
public partial class PlayerCharacter : CharacterBody2D
{
    public const float WalkSpeed = 80f;
    public const float SprintSpeed = 135f;
    private const double SendInterval = 1.0 / 30.0;
    private const float SnapDistance = 96f;
    private const string SpriteFolder = "res://assets/sprites/delver";

    private Vector2 _remoteTarget;
    private double _sendTimer;
    private bool _wasInteracting;
    private string _facing = "down";
    private AnimatedSprite2D? _sprite;
    private PlayerOverlay _overlay = null!;

    public PlayerId Id { get; private set; }
    public string DisplayName { get; private set; } = "";
    public int ColorIndex { get; private set; }
    public InputDevice? Device { get; private set; }
    public bool IsLocal => Device is not null;

    /// <summary>Moving at sprint speed right now (local: from input; remote: as reported by its owner).</summary>
    public bool IsSprinting { get; private set; }

    /// <summary>Best known true position: our own for local players, the last received one for remote players.</summary>
    public Vector2 AuthoritativePosition => IsLocal ? GlobalPosition : _remoteTarget;

    /// <summary>Raised when this local player presses Interact.</summary>
    public event Action<PlayerCharacter>? InteractPressed;

    /// <summary>Button hint drawn above the player (e.g. "E"), or null.</summary>
    public string? Prompt
    {
        get => _overlay.Prompt;
        set => _overlay.Prompt = value;
    }

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

        float headY = -14f;
        if (CharacterSprites.Load(SpriteFolder) is { } art)
        {
            // Stand the sprite's feet on this node's position (+2px so the collision circle sits at the ankles).
            _sprite = new AnimatedSprite2D
            {
                SpriteFrames = art.Frames,
                Centered = false,
                Position = new Vector2(-art.FrameSize / 2f, -art.FeetY + 2),
            };
            AddChild(_sprite);
            _sprite.Play(CharacterSprites.Name("idle", _facing));
            headY = -art.FeetY + 2 + 3; // just above the hood
        }

        _overlay = new PlayerOverlay().Init(ColorIndex, headY);
        AddChild(_overlay);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Device is not null)
        {
            var move = Device.ReadMove();
            if (move.LengthSquared() > 1f) move = move.Normalized();
            bool sprintHeld = Device.ReadSprint();
            Velocity = move * (sprintHeld ? SprintSpeed : WalkSpeed);
            MoveAndSlide();
            IsSprinting = sprintHeld && GetRealVelocity().Length() > WalkSpeed * 0.5f;

            bool interacting = Device.ReadInteract();
            if (interacting && !_wasInteracting) InteractPressed?.Invoke(this);
            _wasInteracting = interacting;

            _sendTimer += delta;
            if (_sendTimer >= SendInterval)
            {
                _sendTimer = 0;
                NetSession.Instance.SendPlayerState(Id, GlobalPosition, Velocity, IsSprinting);
            }
        }
        else
        {
            Position = Position.DistanceTo(_remoteTarget) > SnapDistance
                ? _remoteTarget
                : Position.Lerp(_remoteTarget, 1f - Mathf.Exp(-18f * (float)delta));
        }

        Animate();
    }

    public void ApplyRemoteState(Vector2 position, Vector2 velocity, bool sprinting)
    {
        if (IsLocal) return;
        _remoteTarget = position;
        Velocity = velocity;
        IsSprinting = sprinting;
    }

    /// <summary>Pick idle / walking / running and the facing direction from the current velocity.</summary>
    private void Animate()
    {
        if (_sprite is null) return;

        var v = IsLocal ? GetRealVelocity() : Velocity;
        bool moving = v.Length() > 5f;
        if (moving)
            _facing = Mathf.Abs(v.X) > Mathf.Abs(v.Y) ? (v.X < 0 ? "left" : "right") : (v.Y < 0 ? "up" : "down");

        var animation = CharacterSprites.Name(!moving ? "idle" : IsSprinting ? "running" : "walking", _facing);
        if (_sprite.Animation != animation) _sprite.Play(animation);
        _sprite.SpeedScale = moving && IsSprinting ? 1.4f : 1f;
    }

    public override void _Draw()
    {
        var color = Palette.PlayerColor(ColorIndex);
        if (_sprite is null)
        {
            // Placeholder when the art is missing.
            var body = new Rect2(-5, -6, 10, 12);
            DrawRect(body, Palette.Void);
            DrawRect(body.Grow(-1), color);
            return;
        }

        // Shadow + player-colored ring under the feet (drawn before children, so it sits under the sprite).
        DrawSetTransform(new Vector2(0, 1), 0, new Vector2(1f, 0.45f));
        DrawCircle(Vector2.Zero, 7f, new Color(Palette.Void, 0.6f));
        DrawArc(Vector2.Zero, 7f, 0, Mathf.Tau, 20, color, 1.2f);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
