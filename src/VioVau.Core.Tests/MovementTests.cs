using VioVau.Core.Dungeon;
using VioVau.Core.Net;

namespace VioVau.Core.Tests;

public class MovementTests
{
    private static readonly PlayerId P1 = new(1), P2 = new(2);

    private static (RunState Run, List<IGameEvent> Events) StartedRun(ulong seed = 42)
    {
        var run = new RunState(seed);
        var events = new List<IGameEvent>();
        events.AddRange(run.Apply(new JoinPlayer(P1, "A")).Events);
        events.AddRange(run.Apply(new JoinPlayer(P2, "B")).Events);
        events.AddRange(run.Apply(new StartRun()).Events);
        return (run, events);
    }

    private static Room OpenNeighbor(RunState run, RoomId from, out Direction dir)
    {
        var floor = run.Dungeon!.Floors[from.Floor];
        var room = run.Dungeon.GetRoom(from);
        dir = DirectionExtensions.All.First(room.IsPassable);
        return floor.Neighbor(room, dir)!;
    }

    [Fact]
    public void Start_PlacesEveryoneInTheLobbyAndRevealsIt()
    {
        var (run, events) = StartedRun();
        var entrance = run.Dungeon!.Entrance;

        Assert.All(run.Players, p => Assert.Equal(entrance, p.Room));
        Assert.Contains(new RoomRevealed(entrance), events);
        Assert.Equal(2, events.OfType<PlayerEnteredRoom>().Count(e => e.Room == entrance && e.FirstVisit));
        Assert.Equal(new[] { entrance }, run.RevealedRooms);
    }

    [Fact]
    public void Move_ThroughOpenExit_EntersAndReveals()
    {
        var (run, _) = StartedRun();
        var target = OpenNeighbor(run, run.Dungeon!.Entrance, out _);

        var result = run.Apply(new MovePlayerToRoom(P1, target.Id));

        Assert.True(result.Accepted, result.RejectionReason);
        Assert.Equal(new IGameEvent[] { new PlayerEnteredRoom(P1, target.Id, true), new RoomRevealed(target.Id) }, result.Events);
        Assert.Equal(target.Id, run.GetPlayer(P1)!.Room);
    }

    [Fact]
    public void Move_IntoAlreadyRevealedRoom_DoesNotRevealAgain_ButIsFirstVisitForThatPlayer()
    {
        var (run, _) = StartedRun();
        var target = OpenNeighbor(run, run.Dungeon!.Entrance, out _);
        run.Apply(new MovePlayerToRoom(P1, target.Id));

        var result = run.Apply(new MovePlayerToRoom(P2, target.Id));

        Assert.Equal(new IGameEvent[] { new PlayerEnteredRoom(P2, target.Id, true) }, result.Events);
    }

    [Fact]
    public void Move_BackToVisitedRoom_IsNotFirstVisit()
    {
        var (run, _) = StartedRun();
        var entrance = run.Dungeon!.Entrance;
        var target = OpenNeighbor(run, entrance, out _);
        run.Apply(new MovePlayerToRoom(P1, target.Id));

        var result = run.Apply(new MovePlayerToRoom(P1, entrance));
        Assert.Equal(new IGameEvent[] { new PlayerEnteredRoom(P1, entrance, false) }, result.Events);
    }

    [Fact]
    public void Move_ThroughWall_IsRejected()
    {
        // Find a seed where the entrance has a walled side with a room behind it.
        for (ulong seed = 1; seed < 100; seed++)
        {
            var (run, _) = StartedRun(seed);
            var entrance = run.Dungeon!.GetRoom(run.Dungeon.Entrance);
            var floor = run.Dungeon.Floors[0];
            var walled = DirectionExtensions.All.FirstOrDefault(d => !entrance.IsPassable(d) && floor.Neighbor(entrance, d) is not null, (Direction)(-1));
            if ((int)walled == -1) continue;

            var result = run.Apply(new MovePlayerToRoom(P1, floor.Neighbor(entrance, walled)!.Id));
            Assert.False(result.Accepted);
            Assert.Equal(entrance.Id, run.GetPlayer(P1)!.Room);
            return;
        }
        Assert.Fail("No seed produced a walled entrance side.");
    }

    [Fact]
    public void Move_ToNonAdjacentRoom_IsRejected()
    {
        var (run, _) = StartedRun();
        var e = run.Dungeon!.Entrance;
        Assert.False(run.Apply(new MovePlayerToRoom(P1, e with { Y = e.Y - 2 })).Accepted);
        Assert.False(run.Apply(new MovePlayerToRoom(P1, e with { X = e.X + 1, Y = e.Y - 1 })).Accepted);
    }

    [Fact]
    public void Move_OutsideDungeonOrSameRoomOrUnknownPlayer_IsRejected()
    {
        var (run, _) = StartedRun();
        var e = run.Dungeon!.Entrance;
        Assert.False(run.Apply(new MovePlayerToRoom(P1, e with { Y = e.Y + 1 })).Accepted);
        Assert.False(run.Apply(new MovePlayerToRoom(P1, e)).Accepted);
        Assert.False(run.Apply(new MovePlayerToRoom(new PlayerId(9), e with { Y = e.Y - 1 })).Accepted);
    }

    [Fact]
    public void Move_BeforeRunStarts_IsRejected()
    {
        var run = new RunState(1);
        run.Apply(new JoinPlayer(P1, "A"));
        Assert.False(run.Apply(new MovePlayerToRoom(P1, new RoomId(0, 0, 0))).Accepted);
    }

    [Fact]
    public void RunView_BuiltFromEncodedEvents_MatchesHostState()
    {
        // Simulates a client: receives events over the "network" and rebuilds the run.
        var (run, events) = StartedRun(seed: 2024);
        var current = run.Dungeon!.Entrance;
        for (int step = 0; step < 6; step++)
        {
            var next = OpenNeighbor(run, current, out _);
            events.AddRange(run.Apply(new MovePlayerToRoom(P1, next.Id)).Events);
            current = next.Id;
        }

        var received = EventCodec.Decode(EventCodec.Encode(events));
        var start = received.OfType<RunStarted>().Single();
        var view = RunView.FromStart(start);
        foreach (var e in received) view.Apply(e);

        Assert.Equal(run.RevealedRooms.Order(RoomIdComparer), view.RevealedRooms.Order(RoomIdComparer));
        Assert.Equal(run.GetPlayer(P1)!.Room, view.RoomOf(P1));
        Assert.Equal(run.GetPlayer(P2)!.Room, view.RoomOf(P2));
        Assert.Equal(
            run.Dungeon.AllRooms.Select(r => (r.Id, r.Type)),
            view.Dungeon.AllRooms.Select(r => (r.Id, r.Type)));
    }

    [Fact]
    public void EventCodec_RoundTripsEveryEventType()
    {
        IGameEvent[] events =
        [
            new PlayerJoined(P1, "A"),
            new PlayerLeft(P2),
            new RunStarted(7, [new PlayerInfo(P1, "A")], GameConfig.Default),
            new PlayerEnteredRoom(P1, new RoomId(0, 1, 2), true),
            new RoomRevealed(new RoomId(0, 3, 4)),
        ];

        var decoded = EventCodec.Decode(EventCodec.Encode(events));

        Assert.Equal(events.Length, decoded.Count);
        for (int i = 0; i < events.Length; i++)
            Assert.Equal(events[i].GetType(), decoded[i].GetType());
        Assert.Equal(events[0], decoded[0]);
        Assert.Equal(events[3], decoded[3]);
        Assert.Equal(events[4], decoded[4]);
        var started = Assert.IsType<RunStarted>(decoded[2]);
        Assert.Equal(7UL, started.Seed);
        Assert.Equal(DungeonConfig.Default.RoomWeights, started.Config.Dungeon.RoomWeights);
        Assert.Equal(GameConfig.Default.Threat.TierThresholds, started.Config.Threat.TierThresholds);
    }

    private static readonly Comparer<RoomId> RoomIdComparer =
        Comparer<RoomId>.Create((a, b) => (a.Floor, a.Y, a.X).CompareTo((b.Floor, b.Y, b.X)));
}
