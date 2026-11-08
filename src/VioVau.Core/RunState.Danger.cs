using VioVau.Core.Dungeon;
using VioVau.Core.Enemies;

namespace VioVau.Core;

/// <summary>
/// Danger rules: which enemies exist, Strikes (the dungeon sends enemies after loud players), the Warden,
/// hits, downed / revive / death, loot bags, and the run ending when nobody is left standing.
/// Enemy movement and attacks are run by the AI in the physics layer; it reports hits as <see cref="HitPlayer"/>.
/// </summary>
public sealed partial class RunState
{
    private readonly Dictionary<EnemyId, EnemyState> _enemies = new();
    private int _nextEnemyId = 1;
    private double _strikeTimer;
    private bool _wardenAwake;

    private EnemiesConfig Enemies => Config.EnemySet;

    public IReadOnlyCollection<EnemyState> EnemyList => _enemies.Values;

    public EnemyState? GetEnemy(EnemyId id) => _enemies.GetValueOrDefault(id);

    /// <summary>Where Strikes come in: just inside the doorway on each side (normalized room point).</summary>
    private static RoomPoint DoorPoint(Direction side) => side switch
    {
        Direction.North => new RoomPoint(0.5f, 0.0f),
        Direction.South => new RoomPoint(0.5f, 1.0f),
        Direction.West => new RoomPoint(0.0f, 0.5f),
        _ => new RoomPoint(1.0f, 0.5f),
    };

    // ── Spawning ────────────────────────────────────────────────────────────

    /// <summary>Guards placed at the start, per room type (never in the Lobby).</summary>
    private void SpawnGuards(List<IGameEvent> events)
    {
        foreach (var room in Dungeon!.AllRooms)
        {
            if (!Enemies.Spawns.TryGetValue(room.Type, out var rule)) continue;
            int count = Rng.Ai.NextInt(rule.Min, rule.Max + 1);
            for (int i = 0; i < count && AliveEnemies < Enemies.MaxEnemies; i++)
            {
                var at = new RoomPoint(0.25f + (float)Rng.Ai.NextDouble() * 0.5f, 0.3f + (float)Rng.Ai.NextDouble() * 0.4f);
                events.Add(Spawn(EnemyKind.Sentry, room.Id, at, target: null));
            }
        }
    }

    private int AliveEnemies => _enemies.Values.Count(e => e.Alive);

    /// <summary>Registers a new enemy. Internal so tests can place enemies directly.</summary>
    internal EnemySpawned Spawn(EnemyKind kind, RoomId room, RoomPoint at, PlayerId? target)
    {
        var enemy = new EnemyState(new EnemyId(_nextEnemyId++), kind);
        _enemies[enemy.Id] = enemy;
        return new EnemySpawned(enemy.Id, kind, room, at, target);
    }

    private void TickDanger(TimeSpan dt, List<IGameEvent> events)
    {
        double seconds = dt.TotalSeconds;

        // Bleeding out.
        bool someoneDied = false;
        foreach (var player in Players.Where(p => p.Downed))
        {
            player.BleedRemaining -= seconds;
            if (player.BleedRemaining > 0) continue;
            Die(player, events);
            someoneDied = true;
        }
        if (someoneDied) CheckRunOver(events);
        if (Phase != RunPhase.InRun) return;

        TickCollapse(seconds, events);
        if (Phase != RunPhase.InRun) return;

        TickStrikes(seconds, events);

        if (!_wardenAwake && Enemies.WardenTier >= 0 && Threat.Tier >= Enemies.WardenTier)
            WakeWarden(events);
    }

    /// <summary>
    /// The heart of the semi-co-op: every few seconds (faster at higher tiers) the dungeon sends a Sentry
    /// through a doorway of one player's room — chosen at random, weighted by how much noise they've made lately.
    /// </summary>
    private void TickStrikes(double seconds, List<IGameEvent> events)
    {
        double interval = Enemies.StrikeInterval(Threat.Tier);
        if (interval <= 0)
        {
            _strikeTimer = 0;
            return;
        }

        _strikeTimer += seconds;
        if (_strikeTimer < interval) return;
        _strikeTimer -= interval;

        if (AliveEnemies >= Enemies.MaxEnemies || PickByNoise() is not { Room: { } room } target) return;

        var doorways = DirectionExtensions.All.Where(d => Dungeon!.GetRoom(room).GetExit(d) != ExitState.None).ToList();
        var side = doorways.Count > 0 ? doorways[Rng.Threat.NextInt(0, doorways.Count)] : Direction.North;
        events.Add(Spawn(EnemyKind.Sentry, room, DoorPoint(side), target.Id));
    }

    /// <summary>A random active player, weighted by attributed noise (uniform if nobody has made any).</summary>
    public PlayerState? PickByNoise()
    {
        var candidates = Players.Where(p => p.Active && p.Room is not null).ToList(); // escaped players are out of reach
        if (candidates.Count == 0) return null;

        var weights = candidates.Select(p => Threat.NoiseOf(p.Id)).ToList();
        double total = weights.Sum();
        if (total <= 1e-9) return candidates[Rng.Threat.NextInt(0, candidates.Count)];

        double roll = Rng.Threat.NextDouble() * total;
        for (int i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0) return candidates[i];
        }
        return candidates[^1];
    }

    /// <summary>The loudest active player (the Warden's prey).</summary>
    public PlayerState? Loudest() =>
        Players.Where(p => p.Active && p.Room is not null).OrderByDescending(p => Threat.NoiseOf(p.Id)).ThenBy(p => p.Id.Value).FirstOrDefault();

    /// <summary>The Warden appears once, in the reachable room farthest from its prey.</summary>
    private void WakeWarden(List<IGameEvent> events)
    {
        if (Loudest() is not { Room: { } preyRoom } prey) return;
        _wardenAwake = true;

        var floor = Dungeon!.Floors[preyRoom.Floor];
        var distances = DungeonGenerator.WalkingDistances(floor, Dungeon.GetRoom(preyRoom));
        var lair = distances.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Y).ThenBy(kv => kv.Key.X).First().Key;
        events.Add(Spawn(EnemyKind.Warden, lair, new RoomPoint(0.5f, 0.5f), prey.Id));
    }

    // ── Hits, downed, death ─────────────────────────────────────────────────

    private CommandResult Hit(HitPlayer c)
    {
        if (InRunPlayer(c.Player, out var player, out _) is { } rejection) return rejection;
        if (_enemies.GetValueOrDefault(c.Enemy) is not { Alive: true }) return CommandResult.Reject($"{c.Enemy} is not alive.");
        if (Elapsed < player.InvulnerableUntil) return CommandResult.Reject($"{player.DisplayName} is invulnerable.");

        player.Hearts--;
        player.LastPoint = c.At;
        GrantInvulnerability(player, Enemies.Health.InvulnerableSeconds);

        var events = new List<IGameEvent> { new PlayerHit(c.Player, c.Enemy, player.Hearts) };
        if (player.Hearts <= 0)
        {
            player.Downed = true;
            player.BleedRemaining = Enemies.Health.BleedOutSeconds;
            events.Add(new PlayerDowned(c.Player, Enemies.Health.BleedOutSeconds));
            CheckRunOver(events);
        }
        return CommandResult.Ok(events);
    }

    private CommandResult Revive(RevivePlayer c)
    {
        if (InRunPlayer(c.Reviver, out var reviver, out var room) is { } rejection) return rejection;
        if (GetPlayer(c.Target) is not { Downed: true } target) return CommandResult.Reject("Nobody to revive.");
        if (target.Room != room) return CommandResult.Reject($"{target.DisplayName} is in another room.");

        var events = new List<IGameEvent>();
        RaiseUp(target, reviver, events);
        MakeNoise(reviver.Id, Config.Threat.Revive, room, events);
        return CommandResult.Ok(events);
    }

    private void RaiseUp(PlayerState target, PlayerState by, List<IGameEvent> events)
    {
        target.Downed = false;
        target.Hearts = Enemies.Health.ReviveHearts;
        GrantInvulnerability(target, Enemies.Health.InvulnerableSeconds);
        events.Add(new PlayerRevived(target.Id, by.Id, target.Hearts));
    }

    private void GrantInvulnerability(PlayerState player, double seconds)
    {
        var until = Elapsed + TimeSpan.FromSeconds(seconds);
        if (until > player.InvulnerableUntil) player.InvulnerableUntil = until;
    }

    /// <summary>Death: everything carried drops in a bag anyone can pick up.</summary>
    private void Die(PlayerState player, List<IGameEvent> events)
    {
        player.Downed = false;
        player.Dead = true;

        if ((player.CarriedPoints > 0 || player.Keys > 0) && player.Room is { } room)
        {
            var bag = new LootItem(Dungeon!.NextEntityId(), LootKind.Bag, room, player.LastPoint, player.CarriedPoints) { Keys = player.Keys };
            Dungeon.AddLoot(bag);
            events.Add(new LootDropped(bag.Id, room, bag.Position, bag.Points, bag.Keys, player.Id));
        }
        player.CarriedPoints = 0;
        player.Keys = 0;
        player.CarryingRelic = false;
        player.Outcome = RunOutcome.Fell;
        events.Add(new PlayerDied(player.Id));
    }

    /// <summary>
    /// The run is over when nobody is left inside. If everyone still inside is downed, nobody can revive them
    /// (escaped players don't come back): they fall, and the run is over.
    /// </summary>
    private void CheckRunOver(List<IGameEvent> events)
    {
        if (Phase != RunPhase.InRun) return;
        if (Players.Any(p => p.Active)) return;
        foreach (var downed in Players.Where(p => p.Inside && p.Downed).ToList()) Die(downed, events);
        EndRun(events);
    }

    private CommandResult Claim(ClaimBag c)
    {
        if (InRunPlayer(c.Player, out var player, out var room) is { } rejection) return rejection;
        if (Dungeon!.GetLoot(c.Bag) is not { Kind: LootKind.Bag } bag) return CommandResult.Reject($"{c.Bag} is not a bag.");
        if (bag.Room != room) return CommandResult.Reject($"{c.Bag} is not in {c.Player}'s room.");
        if (bag.Claimed) return CommandResult.Reject($"{c.Bag} was already taken.");

        bag.Claimed = true;
        player.CarriedPoints += bag.Points;
        player.Keys += bag.Keys;
        return CommandResult.Ok(new BagClaimed(c.Player, c.Bag, bag.Points, bag.Keys));
    }
}
