using VioVau.Core.Dungeon;
using VioVau.Core.Enemies;
using VioVau.Core.Net;
using VioVau.Core.Threat;
using static VioVau.Core.Tests.TestRuns;

namespace VioVau.Core.Tests;

public class EndingTests
{
    private static readonly EnemiesConfig NoDanger = EnemiesConfig.Default with { Health = new HealthConfig { BleedOutSeconds = 10 } };

    private static RunState Run(int players = 2, ThreatConfig? threat = null, ulong seed = 42)
    {
        var run = new RunState(seed, new GameConfig(LootyDungeon, threat ?? QuietThreat, null, NoDanger));
        for (int i = 1; i <= players; i++) run.Apply(new JoinPlayer(new PlayerId(i), $"P{i}"));
        Assert.True(run.Apply(new StartRun()).Accepted);
        return run;
    }

    private static List<LootItem> Relics(RunState run) =>
        run.Dungeon!.Loot.Where(l => l.Kind == LootKind.Relic).OrderByDescending(l => l.Points).ToList();

    private static RoomId RelicRoom(RunState run) => Relics(run)[0].Room;

    // ── Relics ──────────────────────────────────────────────────────────────

    [Fact]
    public void RelicChamber_HoldsTheConfiguredRelics()
    {
        var run = Run();
        var relics = Relics(run);
        Assert.Equal(LootyDungeon.RelicValues.OrderByDescending(v => v), relics.Select(r => r.Points));
        Assert.All(relics, r => Assert.Equal(RoomType.RelicChamber, run.Dungeon!.GetRoom(r.Room).Type));
    }

    [Fact]
    public void TakingARelic_AddsPoints_AndSpikesThreat()
    {
        var run = Run();
        var relic = Relics(run)[0];
        WalkTo(run, P1, relic.Room);
        double before = run.Threat.Level;

        var result = run.Apply(new TakeRelic(P1, relic.Id));

        Assert.True(result.Accepted, result.RejectionReason);
        Assert.Equal(new RelicTaken(P1, relic.Id, relic.Points), result.Events[0]);
        Assert.Equal(relic.Points, run.GetPlayer(P1)!.CarriedPoints);
        Assert.True(run.GetPlayer(P1)!.CarryingRelic);
        Assert.Equal(before + QuietThreat.TakeRelic * QuietThreat.RoomMultiplier(RoomType.RelicChamber), run.Threat.Level, 6);
    }

    [Fact]
    public void OnlyOneRelicAtATime_AndEachOnlyOnce()
    {
        var run = Run();
        var (first, second) = (Relics(run)[0], Relics(run)[1]);
        WalkTo(run, P1, first.Room);
        WalkTo(run, P2, first.Room);

        Assert.True(run.Apply(new TakeRelic(P1, first.Id)).Accepted);
        Assert.False(run.Apply(new TakeRelic(P1, second.Id)).Accepted);
        Assert.False(run.Apply(new TakeRelic(P2, first.Id)).Accepted);
        Assert.True(run.Apply(new TakeRelic(P2, second.Id)).Accepted);
    }

    // ── Escaping ────────────────────────────────────────────────────────────

    [Fact]
    public void Escape_OnlyFromTheLobby()
    {
        var run = Run();
        WalkTo(run, P1, RelicRoom(run));
        Assert.False(run.Apply(new Extract(P1)).Accepted);
        WalkTo(run, P1, run.Dungeon!.Entrance);
        Assert.True(run.Apply(new Extract(P1)).Accepted);
    }

    [Fact]
    public void FirstEscape_BanksAndStartsTheCollapse_Once()
    {
        var run = Run();
        run.GetPlayer(P1)!.CarriedPoints = 42;

        var result = run.Apply(new Extract(P1));

        Assert.Contains(new PlayerExtracted(P1, 42, false), result.Events);
        var collapse = Assert.Single(result.Events.OfType<CollapseStarted>());
        Assert.Equal(QuietThreat.CollapseSeconds, collapse.Seconds);
        Assert.Equal(RunPhase.InRun, run.Phase); // P2 is still inside

        var p1 = run.GetPlayer(P1)!;
        Assert.True(p1.Extracted);
        Assert.Equal(42, p1.Banked);
        Assert.False(run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(0.5))).Accepted); // escaped players are out

        run.Tick(TimeSpan.FromSeconds(10));
        Assert.Equal(QuietThreat.CollapseSeconds - 10, run.CollapseRemaining!.Value, 6);
    }

    [Fact]
    public void MaxedThreat_StartsTheCollapse()
    {
        var run = Run();
        run.Threat.AddPlayerNoise(P1, 1000, RoomType.Corridor, []);
        var events = run.Tick(TimeSpan.FromSeconds(0.1));
        Assert.Contains(events, e => e is CollapseStarted);
    }

    [Fact]
    public void EveryoneEscaping_EndsTheRun_RichestWins()
    {
        var run = Run();
        run.GetPlayer(P1)!.CarriedPoints = 30;
        run.GetPlayer(P2)!.CarriedPoints = 55;
        run.Apply(new Extract(P1));
        var result = run.Apply(new Extract(P2));

        var ended = Assert.Single(result.Events.OfType<RunEnded>());
        Assert.Equal(RunPhase.Ended, run.Phase);
        Assert.Equal(new[] { P2, P1 }, ended.Results.Select(r => r.Player));
        Assert.True(ended.Results[0].Winner);
        Assert.False(ended.Results[1].Winner);
        Assert.All(ended.Results, r => Assert.Equal(RunOutcome.Escaped, r.Outcome));
    }

    [Fact]
    public void TiedBanks_ShareTheWin()
    {
        var run = Run();
        run.GetPlayer(P1)!.CarriedPoints = 20;
        run.GetPlayer(P2)!.CarriedPoints = 20;
        run.Apply(new Extract(P1));
        var ended = run.Apply(new Extract(P2)).Events.OfType<RunEnded>().Single();
        Assert.All(ended.Results, r => Assert.True(r.Winner));
    }

    [Fact]
    public void CollapseRunningOut_SealsTheRestInside()
    {
        var run = Run();
        run.GetPlayer(P1)!.CarriedPoints = 10;
        run.GetPlayer(P2)!.CarriedPoints = 99;
        run.Apply(new Extract(P1));

        var events = run.Tick(TimeSpan.FromSeconds(QuietThreat.CollapseSeconds + 1));

        var ended = Assert.Single(events.OfType<RunEnded>());
        var p2 = ended.Results.Single(r => r.Player == P2);
        Assert.Equal(RunOutcome.Sealed, p2.Outcome);
        Assert.Equal(0, p2.Banked);
        Assert.True(ended.Results.Single(r => r.Player == P1).Winner);
    }

    [Fact]
    public void EscapingWithARelic_IsRecorded()
    {
        var run = Run(players: 1);
        var relic = Relics(run)[0];
        WalkTo(run, P1, relic.Room);
        run.Apply(new TakeRelic(P1, relic.Id));
        WalkTo(run, P1, run.Dungeon!.Entrance);

        var ended = run.Apply(new Extract(P1)).Events.OfType<RunEnded>().Single();
        var me = Assert.Single(ended.Results);
        Assert.True(me.HadRelic);
        Assert.Equal(relic.Points, me.Banked);
    }

    [Fact]
    public void LastPlayerInside_GoingDownAfterOthersEscaped_EndsTheRun()
    {
        var run = new RunState(42, new GameConfig(LootyDungeon, QuietThreat, null,
            EnemiesConfig.Default with { Health = new HealthConfig { Hearts = 1 } }));
        run.Apply(new JoinPlayer(P1, "P1"));
        run.Apply(new JoinPlayer(P2, "P2"));
        run.Apply(new StartRun());
        run.Apply(new Extract(P1));
        var sentry = run.Spawn(EnemyKind.Sentry, run.Dungeon!.Entrance, new RoomPoint(0.5f, 0.5f), null).Enemy;

        var events = run.Apply(new HitPlayer(P2, sentry, new RoomPoint(0.5f, 0.5f))).Events;

        Assert.Contains(new PlayerDied(P2), events);
        var ended = Assert.Single(events.OfType<RunEnded>());
        Assert.Equal(RunOutcome.Fell, ended.Results.Single(r => r.Player == P2).Outcome);
    }

    [Fact]
    public void RunView_MirrorsRelicsEscapesCollapseAndResults()
    {
        var run = new RunState(42, new GameConfig(LootyDungeon, QuietThreat, null, NoDanger));
        var events = new List<IGameEvent>();
        events.AddRange(run.Apply(new JoinPlayer(P1, "P1")).Events);
        events.AddRange(run.Apply(new JoinPlayer(P2, "P2")).Events);
        events.AddRange(run.Apply(new StartRun()).Events);

        var relic = Relics(run)[0];
        events.AddRange(WalkTo(run, P1, relic.Room));
        events.AddRange(run.Apply(new TakeRelic(P1, relic.Id)).Events);
        events.AddRange(WalkTo(run, P1, run.Dungeon!.Entrance));
        events.AddRange(run.Apply(new Extract(P1)).Events);
        events.AddRange(run.Tick(TimeSpan.FromSeconds(QuietThreat.CollapseSeconds + 1)));

        var view = RunView.FromStart(events.OfType<RunStarted>().Single());
        foreach (var e in EventCodec.Decode(EventCodec.Encode(events))) view.Apply(e);

        Assert.True(view.Player(P1)!.Extracted);
        Assert.Equal(relic.Points, view.Player(P1)!.Banked);
        Assert.Equal(QuietThreat.CollapseSeconds, view.CollapseSeconds);
        Assert.True(view.Dungeon.GetLoot(relic.Id)!.Claimed);
        Assert.Equal(run.Results, view.Results);
    }

    [Fact]
    public void Configs_RejectBadRelicsAndCollapse()
    {
        Assert.Throws<InvalidDataException>(() => (LootyDungeon with { RelicValues = [0] }).Validate());
        Assert.Throws<InvalidDataException>(() => ThreatConfig.FromJson("""{ "collapseSeconds": 0 }"""));
    }
}
