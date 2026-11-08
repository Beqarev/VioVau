using VioVau.Core.Dungeon;
using VioVau.Core.Threat;
using static VioVau.Core.Tests.TestRuns;

namespace VioVau.Core.Tests;

public class ThreatTests
{
    private static readonly ThreatConfig Config = ThreatConfig.Default;

    [Theory]
    [InlineData(0, 0)]
    [InlineData(14.9, 0)]
    [InlineData(15, 1)]
    [InlineData(54.99, 2)]
    [InlineData(75, 4)]
    [InlineData(100, 5)]
    public void TierFor_UsesThresholds(double level, int tier) => Assert.Equal(tier, Config.TierFor(level));

    [Fact]
    public void PassiveDrift_RaisesThreatOverTime_Unattributed()
    {
        var threat = new ThreatSystem(Config with { PassivePerSecond = 0.5 });
        threat.AddPlayer(P1);
        threat.Tick(TimeSpan.FromSeconds(10), []);

        Assert.Equal(5, threat.Level, 6);
        Assert.Equal(0, threat.Shares().Single().Share);
    }

    [Fact]
    public void RoomMultipliers_ScalePlayerNoise()
    {
        var threat = new ThreatSystem(Config);
        Assert.Equal(4, threat.AddPlayerNoise(P1, 2, RoomType.EchoChamber, []));
        Assert.Equal(0, threat.AddPlayerNoise(P1, 2, RoomType.HushRoom, []));
        Assert.Equal(2, threat.AddPlayerNoise(P1, 2, RoomType.Corridor, []));
        Assert.Equal(6, threat.Level, 6);
    }

    [Fact]
    public void CrossingThreshold_EmitsOneTierEventWithFinalTier()
    {
        var threat = new ThreatSystem(Config);
        var events = new List<IGameEvent>();
        threat.AddPlayerNoise(P1, 40, RoomType.Corridor, events); // 0 → 40 skips tier 1

        Assert.Equal(new IGameEvent[] { new ThreatTierChanged(2) }, events);
        Assert.Equal(2, threat.Tier);
    }

    [Fact]
    public void Threat_IsClampedAtMax()
    {
        var threat = new ThreatSystem(Config);
        threat.AddPlayerNoise(P1, 500, RoomType.Corridor, []);
        Assert.Equal(Config.Max, threat.Level);
        Assert.Equal(Config.TierThresholds.Count - 1, threat.Tier);
    }

    [Fact]
    public void Shares_ReflectWhoMadeTheNoise()
    {
        var threat = new ThreatSystem(Config);
        threat.AddPlayer(P1);
        threat.AddPlayer(P2);
        threat.AddPlayerNoise(P1, 3, RoomType.Corridor, []);
        threat.AddPlayerNoise(P2, 1, RoomType.Corridor, []);

        var shares = threat.Shares().ToDictionary(s => s.Player, s => s.Share);
        Assert.Equal(0.75, shares[P1], 6);
        Assert.Equal(0.25, shares[P2], 6);
    }

    [Fact]
    public void OldNoise_FadesFromShares_ButThreatStays()
    {
        var threat = new ThreatSystem(Config with { PassivePerSecond = 0, AttributionHalfLifeSeconds = 10 });
        threat.AddPlayer(P1);
        threat.AddPlayer(P2);
        threat.AddPlayerNoise(P1, 10, RoomType.Corridor, []);
        threat.Tick(TimeSpan.FromSeconds(30), []); // P1's noise decays to 1/8
        threat.AddPlayerNoise(P2, 10, RoomType.Corridor, []);

        var shares = threat.Shares().ToDictionary(s => s.Player, s => s.Share);
        Assert.Equal(1.25 / 11.25, shares[P1], 6);
        Assert.Equal(20, threat.Level, 6);
    }

    [Fact]
    public void Tick_SendsSnapshotsAtTheBroadcastInterval()
    {
        var threat = new ThreatSystem(Config with { BroadcastIntervalSeconds = 0.5 });
        int snapshots = 0;
        for (int i = 0; i < 60; i++) // 1 second at 60 fps
        {
            var events = new List<IGameEvent>();
            threat.Tick(TimeSpan.FromSeconds(1.0 / 60), events);
            snapshots += events.OfType<ThreatChanged>().Count();
        }
        Assert.InRange(snapshots, 1, 2);
    }

    [Fact]
    public void RunTick_DriftsThreat()
    {
        var run = Started(threat: ThreatConfig.Default with { PassivePerSecond = 1 });
        var events = run.Tick(TimeSpan.FromSeconds(16)).ToList();

        Assert.Equal(16, run.Threat.Level, 6);
        Assert.Contains(new ThreatTierChanged(1), events);
        Assert.Contains(events, e => e is ThreatChanged);
    }

    [Fact]
    public void Sprinting_AddsAttributedNoise_WithoutFeedbackEvents()
    {
        var run = Started(dungeon: DungeonConfig.Default);
        var result = run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(1)));

        Assert.True(result.Accepted);
        Assert.Empty(result.Events);
        Assert.Equal(QuietThreat.SprintPerSecond, run.Threat.Level, 6);
        Assert.Equal(1, run.Threat.Shares().Single(s => s.Player == P1).Share);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    public void Sprinting_RejectsBogusDurations(double seconds)
    {
        var run = Started(dungeon: DungeonConfig.Default);
        Assert.False(run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(seconds))).Accepted);
    }

    [Fact]
    public void ThreatConfig_LoadsFromGameData()
    {
        var path = Path.Combine(DungeonTests.RepoRoot(), "game", "data", "threat.json");
        var config = ThreatConfig.FromJson(File.ReadAllText(path));
        Assert.Equal(config.TierThresholds.Count, config.TierNames.Count);
    }

    [Theory]
    [InlineData("""{ "tierThresholds": [5, 10] , "tierNames": ["a","b"] }""")]
    [InlineData("""{ "tierThresholds": [0, 10, 10], "tierNames": ["a","b","c"] }""")]
    [InlineData("""{ "tierThresholds": [0, 10], "tierNames": ["a"] }""")]
    [InlineData("""{ "openChest": -1 }""")]
    [InlineData("""{ "max": 50 }""")]
    public void ThreatConfig_RejectsInvalidData(string json)
    {
        Assert.Throws<InvalidDataException>(() => ThreatConfig.FromJson(json));
    }
}
