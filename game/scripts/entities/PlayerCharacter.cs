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
    public const float CrawlSpeed = 22f;
    private const double SendInterval = 1.0 / 30.0;
    private const float SnapDistance = 96f;
    private const string SpriteFolder = "res://assets/sprites/delver";

    private Vector2 _remoteTarget;
    private double _sendTimer;
    private bool _wasInteracting;
    private string _facing = "down";
    private readonly bool[] _wasCard = new bool[3];
    private Vector2 _dashVelocity;
    private double _dashTime;
    private double _hushTime;
    private double _hurtTime;
    private AnimatedSprite2D? _sprite;
    private PlayerOverlay _overlay = null!;
    private PointLight2D _lantern = null!;
    private CpuParticles2D _dust = null!;
    private double _flicker;

    public PlayerId Id { get; private set; }
    public string DisplayName { get; private set; } = "";
    public int ColorIndex { get; private set; }
    public InputDevice? Device { get; private set; }
    public bool IsLocal => Device is not null;

    /// <summary>Moving at sprint speed right now (local: from input; remote: as reported by its owner).</summary>
    public bool IsSprinting { get; private set; }

    /// <summary>Under Hush (enemies only notice them up close).</summary>
    public bool IsHushed => _hushTime > 0;

    /// <summary>At 0 hearts: crawling, can't act.</summary>
    public bool IsDowned { get; private set; }

    public bool IsDead { get; private set; }

    /// <summary>Escaped through the Lobby: out of the dungeon.</summary>
    public bool IsEscaped { get; private set; }

    /// <summary>Still in the dungeon (not dead, not escaped).</summary>
    public bool IsInside => !IsDead && !IsEscaped;

    /// <summary>Can be seen, hit and targeted.</summary>
    public bool IsActive => IsInside && !IsDowned;

    /// <summary>Best known true position: our own for local players, the last received one for remote players.</summary>
    public Vector2 AuthoritativePosition => IsLocal ? GlobalPosition : _remoteTarget;

    /// <summary>Raised when this local player presses Interact.</summary>
    public event Action<PlayerCharacter>? InteractPressed;

    /// <summary>Raised when this local player presses a card button (slot 0–2).</summary>
    public event Action<PlayerCharacter, int>? CardPressed;

    /// <summary>Unit vector of the way the character faces.</summary>
    public Vector2 FacingVector => _facing switch
    {
        "left" => Vector2.Left,
        "right" => Vector2.Right,
        "up" => Vector2.Up,
        _ => Vector2.Down,
    };

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
        _overlay.Material = Lights.Unshaded;
        AddChild(_overlay);

        // The lantern: the main light in the vault. Walls cast shadows from it.
        _lantern = Lights.Point(135f, Lights.Lantern, 1.5f, shadows: true);
        _lantern.Position = new Vector2(0, -6);
        AddChild(_lantern);

        _dust = Fx.SprintDust();
        _dust.Position = new Vector2(0, 1);
        AddChild(_dust);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Device is not null)
        {
            var move = Device.ReadMove();
            if (move.LengthSquared() > 1f) move = move.Normalized();
            bool sprintHeld = Device.ReadSprint() && !IsDowned;
            Velocity = move * (IsDowned ? CrawlSpeed : sprintHeld ? SprintSpeed : WalkSpeed);
            if (_dashTime > 0)
            {
                _dashTime -= delta;
                Velocity = _dashVelocity;
            }
            MoveAndSlide();
            IsSprinting = sprintHeld && GetRealVelocity().Length() > WalkSpeed * 0.5f;

            bool interacting = Device.ReadInteract() && !IsDowned;
            if (interacting && !_wasInteracting) InteractPressed?.Invoke(this);
            _wasInteracting = interacting;

            for (int slot = 0; slot < _wasCard.Length; slot++)
            {
                bool held = Device.ReadCard(slot) && !IsDowned;
                if (held && !_wasCard[slot]) CardPressed?.Invoke(this, slot);
                _wasCard[slot] = held;
            }

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

        _dust.Emitting = IsSprinting && IsActive;

        // Lantern: a gentle flicker; dimmer while Hushed, a faint glow while downed.
        _flicker += delta;
        float flicker = 0.94f + 0.04f * Mathf.Sin((float)_flicker * 7.3f) + 0.02f * Mathf.Sin((float)_flicker * 17.1f);
        _lantern.Energy = (IsDowned ? 0.6f : IsHushed ? 0.8f : 1.5f) * flicker;

        if (_hushTime > 0) _hushTime -= delta;
        if (_hurtTime > 0) _hurtTime -= delta;
        // Hushed: faded. Hurt: blinking while invulnerable.
        bool blink = _hurtTime > 0 && (int)(_hurtTime * 12) % 2 == 0;
        Modulate = new Color(1, 1, 1, blink ? 0.25f : IsHushed ? 0.55f : 1f);

        Animate();
    }

    /// <summary>Dash card: a short burst in the facing direction (local players only; others see it via position sync).</summary>
    public void Dash(float distance)
    {
        if (!IsLocal) return;
        const double duration = 0.18;
        _dashVelocity = FacingVector * (float)(distance / duration);
        _dashTime = duration;
    }

    /// <summary>Blink card: teleport forward, stopping short of the first wall in the way.</summary>
    public void Blink(float distance)
    {
        if (!IsLocal) return;
        var from = GlobalPosition;
        var to = from + FacingVector * distance;
        var query = PhysicsRayQueryParameters2D.Create(from, to, PhysicsLayers.Walls);
        var hit = GetWorld2D().DirectSpaceState.IntersectRay(query);
        if (hit.Count > 0) to = (Vector2)hit["position"] - FacingVector * 7f;
        GlobalPosition = to;
    }

    public void ShowHurt(double seconds) => _hurtTime = seconds;

    private float _reviveProgress;

    /// <summary>Progress ring over a downed player while someone revives them (0 hides it).</summary>
    public void ShowReviveProgress(float progress)
    {
        _reviveProgress = progress;
        QueueRedraw();
    }

    public void SetDowned(bool downed)
    {
        IsDowned = downed;
        _dashTime = 0;
    }

    /// <summary>Gone: hidden, no collision, no input, no sync.</summary>
    public void SetDead()
    {
        IsDead = true;
        IsDowned = false;
        Leave();
    }

    /// <summary>Out through the Lobby.</summary>
    public void SetEscaped()
    {
        IsEscaped = true;
        Leave();
    }

    private void Leave()
    {
        Visible = false;
        SetPhysicsProcess(false);
        CollisionLayer = 0;
    }

    /// <summary>A small white crown floats over Relic carriers so everyone knows who has one.</summary>
    public bool CarryingRelic
    {
        get => _overlay.CarryingRelic;
        set => _overlay.CarryingRelic = value;
    }

    /// <summary>Hush: shown as a faded character while silent (everyone sees it).</summary>
    public void ShowHushed(double seconds) => _hushTime = Math.Max(_hushTime, seconds);

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

        if (IsDowned)
        {
            // Lying on the floor (the death animation holds its last frame).
            var down = CharacterSprites.Name("death", _facing);
            if (_sprite.Animation != down) _sprite.Play(down);
            _sprite.SpeedScale = 1f;
            return;
        }

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

        if (IsDowned && _reviveProgress > 0)
            DrawArc(new Vector2(0, -6), 10f, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * _reviveProgress, 24, Palette.White, 1.5f);

        // Shadow + player-colored ring under the feet (drawn before children, so it sits under the sprite).
        DrawSetTransform(new Vector2(0, 1), 0, new Vector2(1f, 0.45f));
        DrawCircle(Vector2.Zero, 7f, new Color(Palette.Void, 0.6f));
        DrawArc(Vector2.Zero, 7f, 0, Mathf.Tau, 20, color, 1.2f);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }
}
