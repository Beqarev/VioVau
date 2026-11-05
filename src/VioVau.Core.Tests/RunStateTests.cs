namespace VioVau.Core.Tests;

public class RunStateTests
{
    private static readonly PlayerId P1 = new(1), P2 = new(2), P3 = new(3), P4 = new(4), P5 = new(5);

    [Fact]
    public void Join_AddsPlayerAndEmitsEvent()
    {
        var run = new RunState(1);
        var result = run.Apply(new JoinPlayer(P1, "  Beqa "));

        Assert.True(result.Accepted);
        Assert.Equal(new PlayerJoined(P1, "Beqa"), Assert.Single(result.Events));
        Assert.Equal("Beqa", run.GetPlayer(P1)!.DisplayName);
    }

    [Fact]
    public void Join_RejectsDuplicate()
    {
        var run = new RunState(1);
        run.Apply(new JoinPlayer(P1, "A"));
        var result = run.Apply(new JoinPlayer(P1, "A again"));

        Assert.False(result.Accepted);
        Assert.Empty(result.Events);
        Assert.Single(run.Players);
    }

    [Fact]
    public void Join_RejectsFifthPlayer()
    {
        var run = new RunState(1);
        foreach (var p in new[] { P1, P2, P3, P4 })
            Assert.True(run.Apply(new JoinPlayer(p, p.ToString())).Accepted);

        var result = run.Apply(new JoinPlayer(P5, "Too many"));
        Assert.False(result.Accepted);
        Assert.Equal(RunState.MaxPlayers, run.Players.Count);
    }

    [Fact]
    public void Join_RejectsBlankName()
    {
        var run = new RunState(1);
        Assert.False(run.Apply(new JoinPlayer(P1, "   ")).Accepted);
    }

    [Fact]
    public void Players_KeepJoinOrder()
    {
        var run = new RunState(1);
        run.Apply(new JoinPlayer(P3, "C"));
        run.Apply(new JoinPlayer(P1, "A"));
        run.Apply(new JoinPlayer(P2, "B"));
        run.Apply(new LeavePlayer(P1));

        Assert.Equal(new[] { P3, P2 }, run.Players.Select(p => p.Id));
    }

    [Fact]
    public void Start_RequiresAPlayer()
    {
        var run = new RunState(1);
        Assert.False(run.Apply(new StartRun()).Accepted);
        Assert.Equal(RunPhase.Lobby, run.Phase);
    }

    [Fact]
    public void Start_MovesToInRunAndLocksLobby()
    {
        var run = new RunState(77);
        run.Apply(new JoinPlayer(P1, "A"));
        var result = run.Apply(new StartRun());

        Assert.True(result.Accepted);
        var started = Assert.IsType<RunStarted>(Assert.Single(result.Events));
        Assert.Equal(77UL, started.Seed);
        Assert.Equal(RunPhase.InRun, run.Phase);

        Assert.False(run.Apply(new JoinPlayer(P2, "Late")).Accepted);
        Assert.False(run.Apply(new LeavePlayer(P1)).Accepted);
        Assert.False(run.Apply(new StartRun()).Accepted);
    }

    [Fact]
    public void Tick_OnlyAdvancesTimeDuringRun()
    {
        var run = new RunState(1);
        run.Tick(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.Zero, run.Elapsed);

        run.Apply(new JoinPlayer(P1, "A"));
        run.Apply(new StartRun());
        run.Tick(TimeSpan.FromSeconds(1.5));
        run.Tick(TimeSpan.FromSeconds(0.5));
        Assert.Equal(TimeSpan.FromSeconds(2), run.Elapsed);
    }

    [Fact]
    public void Tick_RejectsNegativeTime()
    {
        var run = new RunState(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => run.Tick(TimeSpan.FromSeconds(-1)));
    }

    private sealed record UnknownCommand : ICommand;

    [Fact]
    public void Apply_RejectsUnknownCommand()
    {
        Assert.False(new RunState(1).Apply(new UnknownCommand()).Accepted);
    }
}
