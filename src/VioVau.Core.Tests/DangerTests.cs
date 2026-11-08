using VioVau.Core.Cards;
using VioVau.Core.Dungeon;
using VioVau.Core.Enemies;
using VioVau.Core.Net;
using VioVau.Core.Threat;
using static VioVau.Core.Tests.TestRuns;

namespace VioVau.Core.Tests;

public class DangerTests
{
    private static readonly string Data = Path.Combine(DungeonTests.RepoRoot(), "game", "data");
    private static readonly EnemiesConfig GameEnemies = EnemiesConfig.FromJson(File.ReadAllText(Path.Combine(Data, "enemies.json")));
    private static readonly CardsConfig GameCards = CardsConfig.FromJson(File.ReadAllText(Path.Combine(Data, "cards.json")));

    /// <summary>No guards and no strikes unless a test asks for them.</summary>
    private static readonly EnemiesConfig Calm = GameEnemies with { Spawns = new(), StrikeIntervals = [0], WardenTier = -1 };

    private static RunState Run(EnemiesConfig? enemies = null, string[]? starter = null, ThreatConfig? threat = null, ulong seed = 42, int players = 2)
    {
        var cards = GameCards with { StarterDeck = (starter ?? ["dash", "dash", "dash"]).ToList() };
        var run = new RunState(seed, new GameConfig(LootyDungeon, threat ?? QuietThreat, cards, enemies ?? Calm));
        for (int i = 1; i <= players; i++) run.Apply(new JoinPlayer(new PlayerId(i), $"P{i}"));
        Assert.True(run.Apply(new StartRun()).Accepted);
        return run;
    }

    /// <summary>Puts one Sentry in the first active player's room and returns its id.</summary>
    private static EnemyId AddSentry(RunState run)
    {
        var target = run.Players.First(p => p.Active);
        return run.Spawn(EnemyKind.Sentry, target.Room!.Value, new RoomPoint(0.5f, 0.5f), null).Enemy;
    }

    private static readonly RoomPoint Here = new(0.4f, 0.6f);

    private static List<IGameEvent> KnockDown(RunState run, PlayerId player, EnemyId enemy)
    {
        var events = new List<IGameEvent>();
        for (int i = 0; i < 3; i++)
        {
            var result = run.Apply(new HitPlayer(player, enemy, Here));
            Assert.True(result.Accepted, result.RejectionReason);
            events.AddRange(result.Events);
            run.Tick(TimeSpan.FromSeconds(1.5)); // past the invulnerability window
        }
        return events;
    }

    // ── Data ────────────────────────────────────────────────────────────────

    [Fact]
    public void GameData_Loads()
    {
        Assert.True(GameEnemies.Spawns.Count > 0);
        Assert.Equal(16, GameCards.Cards.Count);
    }

    [Theory]
    [InlineData("""{ "spawns": { "Lobby": { "min": 1, "max": 1 } } }""")]
    [InlineData("""{ "spawns": { "Vault": { "min": 2, "max": 1 } } }""")]
    [InlineData("""{ "health": { "hearts": 0 } }""")]
    [InlineData("""{ "health": { "hearts": 3, "reviveHearts": 4 } }""")]
    [InlineData("""{ "strikeIntervals": [0, -5] }""")]
    public void EnemiesConfig_RejectsInvalidData(string json) =>
        Assert.Throws<InvalidDataException>(() => EnemiesConfig.FromJson(json));

    // ── Spawning ────────────────────────────────────────────────────────────

    [Fact]
    public void Guards_SpawnPerRoomRules_NeverInTheLobby_AndDeterministically()
    {
        IReadOnlyList<EnemySpawned> Spawns(ulong seed)
        {
            var run = new RunState(seed, new GameConfig(LootyDungeon, QuietThreat, null, GameEnemies with { StrikeIntervals = [0], WardenTier = -1 }));
            run.Apply(new JoinPlayer(P1, "P1"));
            return run.Apply(new StartRun()).Events.OfType<EnemySpawned>().ToList();
        }

        var a = Spawns(5);
        Assert.Equal(a, Spawns(5));
        Assert.NotEmpty(a);
        var map = DungeonGenerator.Generate(5, LootyDungeon);
        Assert.All(a, s =>
        {
            Assert.NotEqual(map.Entrance, s.Room);
            Assert.True(GameEnemies.Spawns.ContainsKey(map.GetRoom(s.Room).Type));
            Assert.Null(s.Target);
        });
        foreach (var group in a.GroupBy(s => s.Room))
            Assert.InRange(group.Count(), 0, GameEnemies.Spawns[map.GetRoom(group.Key).Type].Max);
    }

    [Fact]
    public void NoStrikes_WhileCalm()
    {
        var run = Run(GameEnemies with { Spawns = new(), WardenTier = -1 });
        var events = run.Tick(TimeSpan.FromSeconds(120));
        Assert.DoesNotContain(events, e => e is EnemySpawned);
    }

    [Fact]
    public void Strikes_SpawnAtADoorwayOfTheTargetsRoom_OnTheTierInterval()
    {
        var enemies = GameEnemies with { Spawns = new(), WardenTier = -1, StrikeIntervals = [0, 10] };
        var run = Run(enemies, threat: QuietThreat with { PassivePerSecond = 0 });
        run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(1)));
        run.Threat.AddPlayerNoise(P1, 20, RoomType.Corridor, []); // reach tier 1

        Assert.DoesNotContain(run.Tick(TimeSpan.FromSeconds(9)), e => e is EnemySpawned);
        var strike = Assert.Single(run.Tick(TimeSpan.FromSeconds(1.5)).OfType<EnemySpawned>());

        Assert.Equal(P1, strike.Target);
        Assert.Equal(run.GetPlayer(P1)!.Room, strike.Room);
        Assert.True(strike.At.X is 0 or 1 || strike.At.Y is 0 or 1, "strikes come in through a doorway");
    }

    [Fact]
    public void Strikes_FavourTheLoudPlayer()
    {
        var run = Run();
        run.Threat.AddPlayerNoise(P1, 90, RoomType.Corridor, []);
        run.Threat.AddPlayerNoise(P2, 10, RoomType.Corridor, []);

        int p1 = Enumerable.Range(0, 1000).Count(_ => run.PickByNoise()!.Id == P1);
        Assert.InRange(p1, 850, 950);
    }

    [Fact]
    public void Strikes_IgnoreDownedPlayers_AndRespectTheCap()
    {
        var run = Run(GameEnemies with { Spawns = new(), WardenTier = -1, StrikeIntervals = [5], MaxEnemies = 2 });
        var sentry = AddSentry(run);
        KnockDown(run, P1, sentry);
        Assert.All(Enumerable.Range(0, 50), _ => Assert.Equal(P2, run.PickByNoise()!.Id));

        var spawned = run.Tick(TimeSpan.FromSeconds(30)).OfType<EnemySpawned>().ToList();
        Assert.Single(spawned); // 1 existing + 1 = cap of 2
    }

    [Fact]
    public void Warden_AwakensOnce_AtItsTier_FarFromItsPrey()
    {
        var run = Run(GameEnemies with { Spawns = new(), StrikeIntervals = [0], WardenTier = 2 }, threat: QuietThreat);
        run.Threat.AddPlayerNoise(P2, 40, RoomType.Corridor, []); // tier 2, P2 loudest

        var warden = Assert.Single(run.Tick(TimeSpan.FromSeconds(0.1)).OfType<EnemySpawned>());
        Assert.Equal(EnemyKind.Warden, warden.Kind);
        Assert.Equal(P2, warden.Target);
        Assert.NotEqual(run.Dungeon!.Entrance, warden.Room);
        Assert.DoesNotContain(run.Tick(TimeSpan.FromSeconds(60)), e => e is EnemySpawned { Kind: EnemyKind.Warden });
    }

    // ── Health ──────────────────────────────────────────────────────────────

    [Fact]
    public void Hit_CostsAHeart_ThenBriefInvulnerability()
    {
        var run = Run();
        var sentry = AddSentry(run);

        var hit = run.Apply(new HitPlayer(P1, sentry, Here));
        Assert.Equal(new IGameEvent[] { new PlayerHit(P1, sentry, 2) }, hit.Events);
        Assert.False(run.Apply(new HitPlayer(P1, sentry, Here)).Accepted);

        run.Tick(TimeSpan.FromSeconds(1.3));
        Assert.True(run.Apply(new HitPlayer(P1, sentry, Here)).Accepted);
        Assert.Equal(1, run.GetPlayer(P1)!.Hearts);
    }

    [Fact]
    public void ZeroHearts_Downs_AndDownedCantAct()
    {
        var run = Run(starter: ["dash", "dash", "dash"]);
        var events = KnockDown(run, P1, AddSentry(run));

        Assert.Contains(new PlayerDowned(P1, GameEnemies.Health.BleedOutSeconds), events);
        var p1 = run.GetPlayer(P1)!;
        Assert.True(p1.Downed);
        Assert.False(run.Apply(new PlayCard(P1, 0)).Accepted);
        Assert.False(run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(0.5))).Accepted);
        var neighbour = run.Dungeon!.Neighbor(run.Dungeon.Entrance, DirectionExtensions.All.First(run.Dungeon.GetRoom(run.Dungeon.Entrance).IsPassable))!;
        Assert.True(run.Apply(new MovePlayerToRoom(P1, neighbour.Id)).Accepted); // crawling still moves
    }

    [Fact]
    public void Revive_RestoresOneHeart_AndMakesNoise()
    {
        var run = Run();
        KnockDown(run, P1, AddSentry(run));
        double before = run.Threat.Level;

        var result = run.Apply(new RevivePlayer(P2, P1));

        Assert.True(result.Accepted, result.RejectionReason);
        Assert.Contains(new PlayerRevived(P1, P2, 1), result.Events);
        Assert.False(run.GetPlayer(P1)!.Downed);
        Assert.Equal(before + QuietThreat.Revive, run.Threat.Level, 6);
    }

    [Fact]
    public void Revive_FromAnotherRoom_IsRejected()
    {
        var run = Run();
        KnockDown(run, P1, AddSentry(run));
        var other = run.Dungeon!.Neighbor(run.Dungeon.Entrance, DirectionExtensions.All.First(run.Dungeon.GetRoom(run.Dungeon.Entrance).IsPassable))!;
        WalkTo(run, P2, other.Id);
        Assert.False(run.Apply(new RevivePlayer(P2, P1)).Accepted);
    }

    [Fact]
    public void BleedingOut_KillsAndDropsABag_AnyoneCanClaim()
    {
        var run = Run();
        var p1 = run.GetPlayer(P1)!;
        p1.CarriedPoints = 25;
        p1.Keys = 1;
        KnockDown(run, P1, AddSentry(run));

        var events = run.Tick(TimeSpan.FromSeconds(GameEnemies.Health.BleedOutSeconds + 1)).ToList();
        Assert.Contains(new PlayerDied(P1), events);
        var dropped = Assert.Single(events.OfType<LootDropped>());
        Assert.Equal((25, 1, Here), (dropped.Points, dropped.Keys, dropped.At));
        Assert.Equal(0, p1.CarriedPoints);
        Assert.Equal(RunPhase.InRun, run.Phase); // P2 is still standing

        var claim = run.Apply(new ClaimBag(P2, dropped.Bag));
        Assert.True(claim.Accepted, claim.RejectionReason);
        Assert.Equal((25, 1), (run.GetPlayer(P2)!.CarriedPoints, run.GetPlayer(P2)!.Keys));
        Assert.False(run.Apply(new ClaimBag(P2, dropped.Bag)).Accepted);
        Assert.False(run.Apply(new MovePlayerToRoom(P1, run.Dungeon!.Entrance)).Accepted); // the dead don't move
    }

    [Fact]
    public void LastPlayerStanding_GoingDown_EndsTheRun()
    {
        var run = Run();
        var sentry = AddSentry(run);
        KnockDown(run, P1, sentry);
        var events = KnockDown(run, P2, sentry);

        Assert.Contains(events, e => e is RunEnded);
        Assert.Equal(RunPhase.Ended, run.Phase);
        Assert.All(run.Players, p => Assert.True(p.Dead));
    }

    // ── Cards ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dash_GivesBriefInvulnerability()
    {
        var run = Run(starter: ["dash", "dash", "dash"]);
        var sentry = AddSentry(run);
        run.Apply(new PlayCard(P1, 0));
        Assert.False(run.Apply(new HitPlayer(P1, sentry, Here)).Accepted);
        run.Tick(TimeSpan.FromSeconds(0.5));
        Assert.True(run.Apply(new HitPlayer(P1, sentry, Here)).Accepted);
    }

    [Fact]
    public void Shatter_KillsASentry_ButNotTheWarden()
    {
        var run = Run(starter: ["shatter", "shatter", "shatter"]);
        var sentry = AddSentry(run);

        Assert.False(run.Apply(new PlayCard(P1, 0)).Accepted); // no target
        var result = run.Apply(new PlayCard(P1, 0, TargetEnemy: sentry));
        Assert.True(result.Accepted, result.RejectionReason);
        Assert.Contains(new EnemyKilled(sentry, P1), result.Events);
        Assert.False(run.GetEnemy(sentry)!.Alive);
        Assert.False(run.Apply(new HitPlayer(P2, sentry, Here)).Accepted); // dead enemies don't hit

        var warden = run.Spawn(EnemyKind.Warden, run.Dungeon!.Entrance, new RoomPoint(0.5f, 0.5f), null).Enemy;
        Assert.False(run.Apply(new PlayCard(P1, 1, TargetEnemy: warden)).Accepted);
    }

    [Fact]
    public void FlashPowder_AndDecoy_TellTheAiWhatToDo()
    {
        var run = Run(starter: ["flash-powder", "decoy", "flash-powder"]);
        var room = run.GetPlayer(P1)!.Room!.Value;

        var flash = run.Apply(new PlayCard(P1, Array.FindIndex(run.GetPlayer(P1)!.Deck!.Hand.ToArray(), c => c?.Value == "flash-powder")));
        Assert.Contains(new EnemiesStunned(P1, room, 3), flash.Events);

        var decoy = run.Apply(new PlayCard(P1, Array.FindIndex(run.GetPlayer(P1)!.Deck!.Hand.ToArray(), c => c?.Value == "decoy")));
        Assert.Contains(new DecoyPlaced(P1, room, 8), decoy.Events);
    }

    [Fact]
    public void Rally_RevivesADownedAllyInTheRoom()
    {
        var run = Run(starter: ["rally", "rally", "rally"]);
        Assert.False(run.Apply(new PlayCard(P2, 0, TargetPlayer: P1)).Accepted); // nobody is down

        KnockDown(run, P1, AddSentry(run));
        var result = run.Apply(new PlayCard(P2, 0, TargetPlayer: P1));
        Assert.True(result.Accepted, result.RejectionReason);
        Assert.True(run.GetPlayer(P1)!.Active);
    }

    // ── Mirroring ───────────────────────────────────────────────────────────

    [Fact]
    public void RunView_MirrorsEnemiesHealthDeathAndBags()
    {
        var run = new RunState(11, new GameConfig(LootyDungeon, QuietThreat, GameCards with { StarterDeck = ["dash"] }, GameEnemies with { StrikeIntervals = [0], WardenTier = -1 }));
        var events = new List<IGameEvent>();
        events.AddRange(run.Apply(new JoinPlayer(P1, "P1")).Events);
        events.AddRange(run.Apply(new JoinPlayer(P2, "P2")).Events);
        events.AddRange(run.Apply(new StartRun()).Events);
        run.GetPlayer(P1)!.CarriedPoints = 7;

        var sentry = run.EnemyList.First().Id;
        for (int i = 0; i < 3; i++)
        {
            events.AddRange(run.Apply(new HitPlayer(P1, sentry, Here)).Events);
            events.AddRange(run.Tick(TimeSpan.FromSeconds(1.5)));
        }
        events.AddRange(run.Tick(TimeSpan.FromSeconds(30)));

        var view = RunView.FromStart(events.OfType<RunStarted>().Single());
        foreach (var e in EventCodec.Decode(EventCodec.Encode(events))) view.Apply(e);

        Assert.Equal(run.EnemyList.Count(e => e.Alive), view.Enemies.Count);
        Assert.True(view.Player(P1)!.Dead);
        Assert.Equal(3, view.Player(P2)!.Hearts);
        var bag = Assert.Single(view.Dungeon.Loot, l => l.Kind == LootKind.Bag);
        Assert.Equal(7, bag.Points);
    }
}
