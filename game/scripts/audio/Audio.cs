using Godot;
using VioVau.Core;
using VioVau.Core.Cards;
using VioVau.Core.Enemies;
using VioVau.Game.Settings;

namespace VioVau.Game;

/// <summary>
/// Autoload. Music crossfades (D minor exploration layers by Threat, Warden layer, Collapse track)
/// and SFX on the Master / Music / SFX buses owned by <see cref="GameSettings"/>.
/// Louder noise actions play louder — noise is the core mechanic.
/// </summary>
public partial class Audio : Node
{
    public static Audio Instance { get; private set; } = null!;

    private const string MusicDir = "res://assets/audio/music/";
    private const string SfxDir = "res://assets/audio/sfx/";

    private readonly Dictionary<string, AudioStream> _sfx = new();
    private AudioStreamPlayer _menu = null!, _collapse = null!, _sting = null!;
    private AudioStreamPlayer _calm = null!, _restless = null!, _hunting = null!, _warden = null!;
    private readonly List<AudioStreamPlayer> _sfxPool = new();
    private int _sfxPoolI;

    private enum MusicMode { None, Menu, Explore, Collapse }
    private MusicMode _mode = MusicMode.None;
    private float _targetCalm, _targetRestless, _targetHunting, _targetWarden;
    private bool _wardenPresent;
    private double _collapseTickAt = -1;

    public override void _EnterTree() => Instance = this;

    public override void _Ready()
    {
        // Keep playing during the pause menu (tree pause shouldn't mute UI / music).
        ProcessMode = ProcessModeEnum.Always;

        // Buses are created by GameSettings (loads first). Re-apply volumes in case we race.
        GameSettings.Instance?.Apply();

        _menu = MakeMusicPlayer("Menu");
        _calm = MakeMusicPlayer("ExploreCalm");
        _restless = MakeMusicPlayer("ExploreRestless");
        _hunting = MakeMusicPlayer("ExploreHunting");
        _warden = MakeMusicPlayer("Warden");
        _collapse = MakeMusicPlayer("Collapse");
        _sting = MakeMusicPlayer("Sting");
        _sting.Bus = GameSettings.BusMusic;

        LoadLoop(_menu, "menu.ogg");
        LoadLoop(_calm, "explore_calm.ogg");
        LoadLoop(_restless, "explore_restless.ogg");
        LoadLoop(_hunting, "explore_hunting.ogg");
        LoadLoop(_warden, "warden.ogg");
        LoadLoop(_collapse, "collapse.ogg");

        foreach (var name in SfxNames)
        {
            var path = SfxDir + name + ".wav";
            if (!ResourceLoader.Exists(path))
            {
                GD.PushWarning($"[Audio] Missing SFX {path}");
                continue;
            }
            var stream = GD.Load<AudioStream>(path);
            if (stream is null)
            {
                GD.PushWarning($"[Audio] Failed to load SFX {path}");
                continue;
            }
            _sfx[name] = stream;
        }
        GD.Print($"[Audio] Loaded {_sfx.Count}/{SfxNames.Length} SFX, buses Master/Music/SFX ready");

        for (int i = 0; i < 12; i++)
        {
            var p = new AudioStreamPlayer
            {
                Bus = GameSettings.BusSfx,
                Name = $"Sfx{i}",
                ProcessMode = ProcessModeEnum.Always,
            };
            AddChild(p);
            _sfxPool.Add(p);
        }
    }

    private static readonly string[] SfxNames =
    [
        "footstep_walk", "footstep_sprint", "chest_open", "key_pickup", "door_unlock",
        "card_mobility", "card_stealth", "card_utility", "card_combat", "card_greed", "card_social",
        "noise_ring", "sentry_notice", "sentry_windup", "sentry_hit",
        "player_hurt", "player_downed", "player_revived", "player_died",
        "relic_lift", "escape", "tier_rise", "collapse_alarm", "collapse_tick",
        "ui_hover", "ui_click",
    ];

    private AudioStreamPlayer MakeMusicPlayer(string name)
    {
        var p = new AudioStreamPlayer
        {
            Name = name,
            Bus = GameSettings.BusMusic,
            VolumeDb = -80,
            ProcessMode = ProcessModeEnum.Always,
        };
        AddChild(p);
        return p;
    }

    private static void LoadLoop(AudioStreamPlayer player, string file)
    {
        var path = MusicDir + file;
        if (!ResourceLoader.Exists(path))
        {
            GD.PushWarning($"[Audio] Missing {path}");
            return;
        }
        var stream = GD.Load<AudioStream>(path);
        if (stream is AudioStreamOggVorbis ogg) ogg.Loop = true;
        player.Stream = stream;
    }

    public override void _Process(double delta)
    {
        float k = 1f - Mathf.Exp(-3.5f * (float)delta);
        LerpVol(_calm, _mode == MusicMode.Explore ? _targetCalm : 0, k);
        LerpVol(_restless, _mode == MusicMode.Explore ? _targetRestless : 0, k);
        LerpVol(_hunting, _mode == MusicMode.Explore ? _targetHunting : 0, k);
        LerpVol(_warden, _mode == MusicMode.Explore && _wardenPresent ? _targetWarden : 0, k);
        LerpVol(_collapse, _mode == MusicMode.Collapse ? 1f : 0, k);
        LerpVol(_menu, _mode == MusicMode.Menu ? 1f : 0, k);
    }

    private static void LerpVol(AudioStreamPlayer p, float linear, float k)
    {
        if (p.Stream is null) return;
        float targetDb = linear <= 0.001f ? -80f : Mathf.LinearToDb(Mathf.Clamp(linear, 0.001f, 1f));
        p.VolumeDb = Mathf.Lerp(p.VolumeDb, targetDb, k);
        if (linear > 0.01f && !p.Playing) p.Play();
        if (linear <= 0.001f && p.VolumeDb < -70f && p.Playing) p.Stop();
    }

    // ── Music control ───────────────────────────────────────────────────────

    public void PlayMenu()
    {
        _mode = MusicMode.Menu;
        StopExplorePlayers(keepSilent: true);
        if (_collapse.Playing) _collapse.Stop();
        if (!_menu.Playing) _menu.Play();
        _menu.VolumeDb = Mathf.LinearToDb(1f);
    }

    public void PlayExplore(int threatTier, bool wardenPresent)
    {
        _mode = MusicMode.Explore;
        _wardenPresent = wardenPresent;
        SetExploreTargets(threatTier);
        _targetWarden = wardenPresent ? 0.85f : 0f;
        EnsureExplorePlaying();
        if (_menu.Playing) { /* fades out via _Process */ }
        if (_collapse.Playing) { /* fades out */ }
    }

    public void SetThreatTier(int tier)
    {
        if (_mode != MusicMode.Explore) return;
        SetExploreTargets(tier);
    }

    public void SetWardenPresent(bool present)
    {
        _wardenPresent = present;
        _targetWarden = present && _mode == MusicMode.Explore ? 0.85f : 0f;
        if (present && _mode == MusicMode.Explore && !_warden.Playing) _warden.Play();
    }

    public void PlayCollapse()
    {
        _mode = MusicMode.Collapse;
        _collapseTickAt = -1;
        if (!_collapse.Playing) _collapse.Play();
        _collapse.VolumeDb = 0;
        PlaySfx("collapse_alarm", volume: 1.1f);
    }

    public void TickCollapse(double secondsLeft)
    {
        if (_mode != MusicMode.Collapse || secondsLeft > 15 || secondsLeft <= 0) return;
        // One tick per whole second in the last 15s.
        double bucket = Math.Floor(secondsLeft);
        if (bucket == _collapseTickAt) return;
        _collapseTickAt = bucket;
        PlaySfx("collapse_tick", volume: secondsLeft < 5 ? 1.2f : 0.85f);
    }

    public void PlayVictory()
    {
        _mode = MusicMode.None;
        StopAllMusic();
        PlaySting("victory.ogg");
    }

    public void PlayDeath()
    {
        _mode = MusicMode.None;
        StopAllMusic();
        PlaySting("death.ogg");
    }

    public void StopMusic()
    {
        _mode = MusicMode.None;
        StopAllMusic();
    }

    private void SetExploreTargets(int tier)
    {
        // Tier names: 0 Calm, 1 Stirring, 2 Restless, 3 Hunting, 4 Furious, 5 Collapse
        (_targetCalm, _targetRestless, _targetHunting) = tier switch
        {
            <= 0 => (1.0f, 0.0f, 0.0f),
            1 => (0.75f, 0.35f, 0.0f),
            2 => (0.25f, 1.0f, 0.2f),
            3 => (0.0f, 0.4f, 1.0f),
            _ => (0.0f, 0.2f, 1.0f),
        };
    }

    private void EnsureExplorePlaying()
    {
        foreach (var p in new[] { _calm, _restless, _hunting, _warden })
        {
            if (p.Stream is not null && !p.Playing) p.Play();
        }
        // Keep layers phase-aligned: restart quieter ones at the loudest layer's position.
        float pos = _calm.Playing ? _calm.GetPlaybackPosition()
            : _restless.Playing ? _restless.GetPlaybackPosition()
            : _hunting.GetPlaybackPosition();
        foreach (var p in new[] { _calm, _restless, _hunting })
        {
            if (p.Playing) p.Seek(pos);
        }
    }

    private void StopExplorePlayers(bool keepSilent)
    {
        foreach (var p in new[] { _calm, _restless, _hunting, _warden })
        {
            if (keepSilent) p.VolumeDb = -80;
            else if (p.Playing) p.Stop();
        }
    }

    private void StopAllMusic()
    {
        foreach (var p in new[] { _menu, _calm, _restless, _hunting, _warden, _collapse })
            if (p.Playing) p.Stop();
    }

    private void PlaySting(string file)
    {
        var path = MusicDir + file;
        if (!ResourceLoader.Exists(path)) return;
        _sting.Stream = GD.Load<AudioStream>(path);
        _sting.VolumeDb = 0;
        _sting.Play();
    }

    // ── SFX ─────────────────────────────────────────────────────────────────

    public void PlaySfx(string name, float volume = 1f)
    {
        if (!_sfx.TryGetValue(name, out var stream)) return;
        var p = NextSfx();
        p.Stream = stream;
        p.VolumeDb = Mathf.LinearToDb(Mathf.Clamp(volume, 0.05f, 2f));
        p.PitchScale = 1f;
        p.Play();
    }

    /// <summary>
    /// How loud a world position is for this machine's listeners (0..1). Set by the game scene: full in a room
    /// with one of your players, quieter next door, faint further away. Null = everything full volume (menus).
    /// </summary>
    public Func<Vector2, float>? Hearing { get; set; }

    /// <summary>
    /// World SFX. Non-positional players (split screen has several cameras, so <see cref="AudioStreamPlayer2D"/>
    /// has no single listener); instead <see cref="Hearing"/> fades sounds by how far away they happen.
    /// <paramref name="noiseAmount"/> still boosts loudness — louder actions sound louder.
    /// </summary>
    public void PlaySfxAt(Node parent, string name, Vector2 globalPosition, float noiseAmount = 0f, float volume = 1f)
    {
        _ = parent;
        float distance = Hearing?.Invoke(globalPosition) ?? 1f;
        if (distance < 0.02f) return;
        PlaySfx(name, volume * NoiseToLinear(noiseAmount) * distance);
    }

    /// <summary>Map Core noise amount → loudness. Small noise stays quiet; Relic (12) hits hard.</summary>
    public static float NoiseToLinear(float amount) =>
        Mathf.Clamp(0.45f + amount * 0.12f, 0.35f, 2.2f);

    public void PlayCard(CardCategory category, Vector2? at = null, Node? parent = null)
    {
        string name = category switch
        {
            CardCategory.Mobility => "card_mobility",
            CardCategory.Stealth => "card_stealth",
            CardCategory.Utility => "card_utility",
            CardCategory.Combat => "card_combat",
            CardCategory.Greed => "card_greed",
            CardCategory.Social => "card_social",
            _ => "card_utility",
        };
        if (at is { } pos && parent is not null) PlaySfxAt(parent, name, pos);
        else PlaySfx(name);
    }

    public void PlayUiHover() => PlaySfx("ui_hover", 0.55f);
    public void PlayUiClick() => PlaySfx("ui_click", 0.7f);

    public void PlayFootstep(bool sprint, Vector2 at, Node parent)
    {
        // Quiet stone/dust thuds; slight pitch jitter so they don't feel robotic.
        string name = sprint ? "footstep_sprint" : "footstep_walk";
        if (!_sfx.TryGetValue(name, out var stream)) return;
        var p = NextSfx();
        p.Stream = stream;
        p.PitchScale = sprint
            ? (float)GD.RandRange(0.92, 1.08)
            : (float)GD.RandRange(0.88, 1.06);
        p.VolumeDb = Mathf.LinearToDb(sprint ? 0.28f : 0.18f);
        p.Play();
        _ = at;
        _ = parent;
    }

    private AudioStreamPlayer NextSfx()
    {
        var p = _sfxPool[_sfxPoolI];
        _sfxPoolI = (_sfxPoolI + 1) % _sfxPool.Count;
        return p;
    }

    /// <summary>Wire a button's mouse/focus signals for UI clicks (call once per button).</summary>
    public static void WireButton(BaseButton button)
    {
        button.MouseEntered += () => Instance?.PlayUiHover();
        button.Pressed += () => Instance?.PlayUiClick();
    }
}
