using Godot;
using VioVau.Core;
using VioVau.Core.Dungeon;
using VioVau.Core.Enemies;
using VioVau.Game.UI;
using VioVau.Game.World;

namespace VioVau.Game.Entities;

/// <summary>What an enemy's AI may ask the game about (implemented by the game scene, host only).</summary>
public interface IEnemyWorld
{
    DungeonMap Map { get; }

    /// <summary>Players that can be seen and hit (not downed, not dead).</summary>
    IEnumerable<PlayerCharacter> ActivePlayers { get; }

    /// <summary>The Warden's prey: the loudest active player.</summary>
    PlayerCharacter? WardenPrey { get; }

    void ReportHit(EnemyNode enemy, PlayerCharacter target);
}

/// <summary>
/// A Hollow Sentry or the Warden. On the host it runs the AI (patrol → notice → chase through doorways →
/// wind up → chop) and reports hits to Core; on clients it smoothly follows the host's synced state.
/// </summary>
public partial class EnemyNode : CharacterBody2D
{
    public enum Mode
    {
        Idle,
        Walk,
        Run,
        Windup,
        Stunned,
    }

    private const string SpriteFolder = "res://assets/sprites/hollow_sentry";
    private static readonly string[] Facings = ["down", "left", "right", "up"];

    private IEnemyWorld? _world;
    private AiTuning _ai = new();
    private AnimatedSprite2D? _sprite;
    private readonly System.Random _random = new();

    private PlayerCharacter? _target;
    private float _lostTimer, _cooldown, _windup, _stun, _decoyTime, _pause;
    private Vector2? _patrolPoint, _decoy;
    private Vector2 _remoteTarget;
    private int _facing;
    private bool _dying;
    private Mode _audioState;
    private PointLight2D _eyes = null!;

    public EnemyId Id { get; private set; }
    public EnemyKind Kind { get; private set; }
    public Mode State { get; private set; }

    /// <summary>Host runs the AI; clients only display.</summary>
    public bool Authority => _world is not null;

    public int FacingIndex => _facing;

    public EnemyNode Init(EnemyId id, EnemyKind kind, Vector2 position, AiTuning ai, IEnemyWorld? world, PlayerCharacter? initialTarget)
    {
        Id = id;
        Kind = kind;
        Name = $"Enemy_{id.Value}";
        Position = position;
        _remoteTarget = position;
        _ai = ai;
        _world = world;
        _target = initialTarget;
        return this;
    }

    public override void _Ready()
    {
        MotionMode = MotionModeEnum.Floating;
        CollisionLayer = PhysicsLayers.Enemies;
        CollisionMask = PhysicsLayers.Walls;
        ZIndex = 9;
        AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = Kind == EnemyKind.Warden ? 7f : 5f } });

        // In the dark you see their eyes first.
        _eyes = Lights.Point(Kind == EnemyKind.Warden ? 26f : 16f, Lights.Bone, 0.6f);
        _eyes.Position = new Vector2(0, (Kind == EnemyKind.Warden ? -24 : -15));
        AddChild(_eyes);

        if (CharacterSprites.Load(SpriteFolder) is { } art)
        {
            float scale = Kind == EnemyKind.Warden ? 1.6f : 1f;
            _sprite = new AnimatedSprite2D
            {
                SpriteFrames = art.Frames,
                Centered = false,
                Scale = new Vector2(scale, scale),
                Position = new Vector2(-art.FrameSize / 2f * scale, (-art.FeetY + 2) * scale),
                // The Warden is a darker, larger shape.
                Modulate = Kind == EnemyKind.Warden ? new Color(0.55f, 0.45f, 0.75f) : Colors.White,
            };
            AddChild(_sprite);
        }
    }

    // ── Commands from the game ──────────────────────────────────────────────

    public void Stun(double seconds) => _stun = Math.Max(_stun, (float)seconds * (Kind == EnemyKind.Warden ? 0.4f : 1f));

    public void Distract(Vector2 decoy, double seconds)
    {
        if (Kind == EnemyKind.Warden) return; // the Warden isn't fooled
        _decoy = decoy;
        _decoyTime = (float)seconds;
    }

    /// <summary>Client-side: the host's latest state.</summary>
    public void ApplyRemoteState(Vector2 position, int packed)
    {
        if (Authority) return;
        _remoteTarget = position;
        State = (Mode)(packed / 4);
        _facing = packed % 4;
    }

    public int PackedState => (int)State * 4 + _facing;

    public void Die()
    {
        _dying = true;
        _eyes.Enabled = false;
        SetPhysicsProcess(false);
        _sprite?.Play($"death_{Facings[_facing]}");
        var tween = CreateTween();
        tween.TweenInterval(0.8);
        tween.TweenProperty(this, "modulate:a", 0f, 0.4);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    // ── AI (host) ───────────────────────────────────────────────────────────

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (!Authority)
        {
            Position = Position.DistanceTo(_remoteTarget) > 96 ? _remoteTarget : Position.Lerp(_remoteTarget, 1f - Mathf.Exp(-15f * dt));
            Animate(Vector2.Zero);
            EmitAiSounds(); // from the synced state, so clients hear Sentries too
            return;
        }

        Velocity = Vector2.Zero;
        _cooldown -= dt;

        if (_stun > 0)
        {
            _stun -= dt;
            State = Mode.Stunned;
        }
        else if (_windup > 0)
        {
            _windup -= dt;
            State = Mode.Windup;
            if (_windup <= 0) FinishAttack();
        }
        else if (_decoyTime > 0 && _decoy is { } decoy)
        {
            _decoyTime -= dt;
            Steer(decoy, _ai.ChaseSpeed, Mode.Run);
        }
        else
        {
            Think(dt);
        }

        MoveAndSlide();
        Animate(Velocity);
        EmitAiSounds();
        QueueRedraw();
    }

    /// <summary>
    /// Sounds from state changes only (every machine sees the same states): starting a chase = "noticed you",
    /// entering the wind-up = the chop is coming. Runs on the host and on clients.
    /// </summary>
    private void EmitAiSounds()
    {
        bool chasingNow = State is Mode.Run or Mode.Windup;
        bool chasingBefore = _audioState is Mode.Run or Mode.Windup;
        if (chasingNow && !chasingBefore)
            Audio.Instance?.PlaySfxAt(this, "sentry_notice", GlobalPosition, volume: 0.85f);
        if (State == Mode.Windup && _audioState != Mode.Windup)
            Audio.Instance?.PlaySfxAt(this, "sentry_windup", GlobalPosition, volume: 1f);
        _audioState = State;
    }

    private void Think(float dt)
    {
        var world = _world!;
        var me = GlobalPosition;

        if (Kind == EnemyKind.Warden)
        {
            _target = world.WardenPrey;
        }
        else if (Notice(world) is { } seen)
        {
            _target = seen;
            _lostTimer = 0;
        }
        else if (_target is not null)
        {
            _lostTimer += dt;
            if (_lostTimer > _ai.LoseAfterSeconds || !world.ActivePlayers.Contains(_target)) _target = null;
        }

        if (_target is { } target && world.ActivePlayers.Contains(target))
        {
            float distance = me.DistanceTo(target.AuthoritativePosition);
            if (distance <= _ai.AttackRange && _cooldown <= 0)
            {
                _windup = _ai.WindupSeconds;
                FaceToward(target.AuthoritativePosition - me);
                State = Mode.Windup;
                return;
            }
            Steer(target.AuthoritativePosition, _ai.ChaseSpeed, Mode.Run);
            return;
        }

        Patrol(dt);
    }

    /// <summary>Who this Sentry notices: sight in its own room, farther for sprinters, very close for the Hushed.</summary>
    private PlayerCharacter? Notice(IEnemyWorld world)
    {
        var me = GlobalPosition;
        var myRoom = WorldLayout.RoomAt(0, me);
        PlayerCharacter? best = null;
        float bestDistance = float.MaxValue;

        foreach (var player in world.ActivePlayers)
        {
            var at = player.AuthoritativePosition;
            float d = me.DistanceTo(at);
            float radius = player.IsHushed ? _ai.HushedRadius : player.IsSprinting ? _ai.HearingRadius : _ai.SightRadius;
            bool sameRoom = WorldLayout.RoomAt(0, at) == myRoom;
            // Walls block sight, but noise carries a little through doorways.
            if (d > radius || (!sameRoom && d > radius * 0.5f)) continue;
            if (d < bestDistance)
            {
                best = player;
                bestDistance = d;
            }
        }
        return best;
    }

    private void FinishAttack()
    {
        _cooldown = _ai.CooldownSeconds;
        if (_target is { } target && _world!.ActivePlayers.Contains(target) &&
            GlobalPosition.DistanceTo(target.AuthoritativePosition) <= _ai.HitRange)
        {
            _world.ReportHit(this, target);
        }
    }

    private void Patrol(float dt)
    {
        if (_pause > 0)
        {
            _pause -= dt;
            State = Mode.Idle;
            return;
        }

        var room = WorldLayout.RoomAt(0, GlobalPosition);
        if (_patrolPoint is not { } point || WorldLayout.RoomAt(0, point) != room || GlobalPosition.DistanceTo(point) < 4f)
        {
            if (_patrolPoint is not null) _pause = 0.6f + (float)_random.NextDouble() * 1.2f;
            var origin = WorldLayout.RoomOrigin(room);
            _patrolPoint = origin + new Vector2(40 + (float)_random.NextDouble() * (WorldLayout.RoomWidth - 80), 40 + (float)_random.NextDouble() * (WorldLayout.RoomHeight - 80));
            State = Mode.Idle;
            return;
        }
        Steer(point, _ai.PatrolSpeed, Mode.Walk);
    }

    private void Steer(Vector2 goal, float speed, Mode moving)
    {
        var waypoint = RoomNavigator.NextWaypoint(_world!.Map, GlobalPosition, goal);
        if (waypoint is not { } next || GlobalPosition.DistanceTo(next) < 2f)
        {
            State = Mode.Idle;
            return;
        }
        Velocity = (next - GlobalPosition).Normalized() * speed;
        State = moving;
    }

    // ── Presentation ────────────────────────────────────────────────────────

    private void FaceToward(Vector2 v)
    {
        if (v.LengthSquared() < 0.01f) return;
        _facing = Mathf.Abs(v.X) > Mathf.Abs(v.Y) ? (v.X < 0 ? 1 : 2) : (v.Y < 0 ? 3 : 0);
    }

    private void Animate(Vector2 velocity)
    {
        if (_sprite is null || _dying) return;
        if (velocity.LengthSquared() > 1f) FaceToward(velocity);
        _eyes.Enabled = _facing != 3; // facing away: no eyes
        _eyes.Energy = State switch { Mode.Windup => 1.6f, Mode.Stunned => 0.15f, Mode.Run => 0.9f, _ => 0.6f };

        string anim = State switch
        {
            Mode.Walk => "walking",
            Mode.Run => "running",
            Mode.Windup => "1h-melee-attack-chop",
            _ => "idle",
        };
        var name = $"{anim}_{Facings[_facing]}";
        if (_sprite.Animation != name) _sprite.Play(name);
        _sprite.SpeedScale = State == Mode.Stunned ? 0f : 1f;
        var tint = Kind == EnemyKind.Warden ? new Color(0.55f, 0.45f, 0.75f) : Colors.White;
        _sprite.Modulate = State == Mode.Stunned ? tint.Darkened(0.4f) : State == Mode.Windup ? tint.Lightened(0.5f) : tint;
        if (!Authority) QueueRedraw();
    }

    public override void _Draw()
    {
        // Glowing white eyes (danger is white): two pixels in the visor, flaring during the wind-up.
        float scale = Kind == EnemyKind.Warden ? 1.6f : 1f;
        if (_facing != 3 && !_dying)
        {
            var eyes = new Vector2(0, -15 * scale);
            var spread = _facing switch { 1 => new Vector2(-2, 0), 2 => new Vector2(2, 0), _ => Vector2.Zero };
            var color = State == Mode.Stunned ? Palette.Dim : Palette.White;
            float size = State == Mode.Windup ? 2f : 1f;
            DrawRect(new Rect2(eyes + spread + new Vector2(-2 * scale, 0), new Vector2(size, size)), color);
            if (_facing == 0) DrawRect(new Rect2(eyes + new Vector2(1 * scale, 0), new Vector2(size, size)), color);
        }

        if (State == Mode.Stunned)
        {
            float t = Time.GetTicksMsec() / 150f;
            for (int i = 0; i < 3; i++)
            {
                var p = new Vector2(Mathf.Cos(t + i * 2.1f) * 6, -24 * scale + Mathf.Sin(t + i * 2.1f) * 2);
                DrawRect(new Rect2(p, new Vector2(1, 1)), Palette.White);
            }
        }
        else if (State == Mode.Windup)
        {
            DrawString(UiFonts.Regular, new Vector2(-2, -26 * scale), "!", HorizontalAlignment.Left, -1, 8, Palette.White);
        }
    }
}
