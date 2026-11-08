using VioVau.Core.Dungeon;
using VioVau.Core.Net;
using static VioVau.Core.Tests.TestRuns;

namespace VioVau.Core.Tests;

public class InteractionTests
{
    private static LootItem FirstReachable(RunState run, LootKind kind)
    {
        var reachable = DungeonGenerator.WalkingDistances(run.Dungeon!.Floors[0], run.Dungeon.GetRoom(run.Dungeon.Entrance));
        return run.Dungeon.Loot
            .Where(l => l.Kind == kind && reachable.ContainsKey(l.Room) && run.Dungeon.GetRoom(l.Room).Type is not (RoomType.EchoChamber or RoomType.HushRoom))
            .OrderBy(l => reachable[l.Room])
            .First();
    }

    [Fact]
    public void OpenChest_GivesPointsAndMakesNoise()
    {
        var run = Started();
        var chest = FirstReachable(run, LootKind.Chest);
        WalkTo(run, P1, chest.Room);

        var result = run.Apply(new OpenChest(P1, chest.Id));

        Assert.True(result.Accepted, result.RejectionReason);
        Assert.Equal(new ChestOpened(P1, chest.Id, chest.Points), result.Events[0]);
        Assert.Contains(new NoiseMade(P1, QuietThreat.OpenChest), result.Events);
        Assert.IsType<ThreatChanged>(result.Events[^1]);
        Assert.Equal(chest.Points, run.GetPlayer(P1)!.CarriedPoints);
        Assert.Equal(QuietThreat.OpenChest, run.Threat.Level, 6);
        Assert.True(chest.Claimed);
    }

    [Fact]
    public void OpenChest_FromAnotherRoom_OrTwice_IsRejected()
    {
        var run = Started();
        var chest = FirstReachable(run, LootKind.Chest);
        Assert.False(run.Apply(new OpenChest(P1, chest.Id)).Accepted); // still in the Lobby

        WalkTo(run, P1, chest.Room);
        Assert.True(run.Apply(new OpenChest(P1, chest.Id)).Accepted);
        Assert.False(run.Apply(new OpenChest(P1, chest.Id)).Accepted);
        Assert.Equal(chest.Points, run.GetPlayer(P1)!.CarriedPoints);
    }

    [Fact]
    public void OpenChest_WithAKeyId_IsRejected()
    {
        var run = Started();
        var key = FirstReachable(run, LootKind.Key);
        WalkTo(run, P1, key.Room);
        Assert.False(run.Apply(new OpenChest(P1, key.Id)).Accepted);
    }

    [Fact]
    public void EchoChamber_DoublesChestNoise_HushRoomSilencesIt()
    {
        var run = StartedWhere(m =>
            m.AllRooms.Any(r => r.Type == RoomType.EchoChamber && r.IsPassable(DirectionExtensions.All.First(r.IsPassable))) &&
            m.Loot.Any(l => l.Kind == LootKind.Chest && m.GetRoom(l.Room).Type == RoomType.EchoChamber) &&
            m.Loot.Any(l => l.Kind == LootKind.Chest && m.GetRoom(l.Room).Type == RoomType.HushRoom));
        var reachable = DungeonGenerator.WalkingDistances(run.Dungeon!.Floors[0], run.Dungeon.GetRoom(run.Dungeon.Entrance));

        var echo = run.Dungeon.Loot.First(l => l.Kind == LootKind.Chest && reachable.ContainsKey(l.Room) && run.Dungeon.GetRoom(l.Room).Type == RoomType.EchoChamber);
        var hush = run.Dungeon.Loot.First(l => l.Kind == LootKind.Chest && reachable.ContainsKey(l.Room) && run.Dungeon.GetRoom(l.Room).Type == RoomType.HushRoom);

        WalkTo(run, P1, echo.Room);
        Assert.Contains(new NoiseMade(P1, QuietThreat.OpenChest * 2), run.Apply(new OpenChest(P1, echo.Id)).Events);

        WalkTo(run, P2, hush.Room);
        var hushResult = run.Apply(new OpenChest(P2, hush.Id));
        Assert.True(hushResult.Accepted);
        Assert.DoesNotContain(hushResult.Events, e => e is NoiseMade);
        Assert.Equal(QuietThreat.OpenChest * 2, run.Threat.Level, 6);
    }

    [Fact]
    public void PickUpKey_AddsKey_Once()
    {
        var run = Started();
        var key = FirstReachable(run, LootKind.Key);
        WalkTo(run, P1, key.Room);

        var result = run.Apply(new PickUpKey(P1, key.Id));
        Assert.True(result.Accepted, result.RejectionReason);
        Assert.Equal(new KeyPickedUp(P1, key.Id), result.Events[0]);
        Assert.Equal(1, run.GetPlayer(P1)!.Keys);

        Assert.False(run.Apply(new PickUpKey(P2, key.Id)).Accepted);
    }

    [Fact]
    public void Unlock_WithoutKey_IsRejected_WithKey_OpensBothSidesAndConsumesKey()
    {
        var run = StartedWhere(m => m.AllRooms.Any(r => r.Type == RoomType.Vault));
        var map = run.Dungeon!;
        var vault = map.AllRooms.First(r => r.Type == RoomType.Vault);
        var lockedSide = DirectionExtensions.All.First(d => vault.GetExit(d) == ExitState.Locked);
        var outside = map.Neighbor(vault.Id, lockedSide)!;
        var doorSide = lockedSide.Opposite(); // the vault door as seen from outside

        // Fetch a key, go to the room in front of the vault.
        var key = FirstReachable(run, LootKind.Key);
        WalkTo(run, P1, outside.Id);
        Assert.False(run.Apply(new UnlockExit(P1, doorSide)).Accepted);
        Assert.False(run.Apply(new MovePlayerToRoom(P1, vault.Id)).Accepted);

        WalkTo(run, P1, key.Room);
        run.Apply(new PickUpKey(P1, key.Id));
        WalkTo(run, P1, outside.Id);

        var result = run.Apply(new UnlockExit(P1, doorSide));
        Assert.True(result.Accepted, result.RejectionReason);
        Assert.Equal(new ExitUnlocked(P1, outside.Id, doorSide), result.Events[0]);
        Assert.Equal(ExitState.Open, outside.GetExit(doorSide));
        Assert.Equal(ExitState.Open, vault.GetExit(lockedSide));
        Assert.Equal(0, run.GetPlayer(P1)!.Keys);

        Assert.True(run.Apply(new MovePlayerToRoom(P1, vault.Id)).Accepted);
        Assert.False(run.Apply(new UnlockExit(P1, lockedSide)).Accepted); // already open
    }

    [Fact]
    public void InteractionCommands_BeforeTheRun_AreRejected()
    {
        var run = new RunState(1);
        run.Apply(new JoinPlayer(P1, "A"));
        Assert.False(run.Apply(new OpenChest(P1, new EntityId(1))).Accepted);
        Assert.False(run.Apply(new PickUpKey(P1, new EntityId(1))).Accepted);
        Assert.False(run.Apply(new UnlockExit(P1, Direction.North)).Accepted);
        Assert.False(run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(0.5))).Accepted);
    }

    [Fact]
    public void RunView_MirrorsLootDoorsAndThreat()
    {
        // Start a host run by hand so the start events (RunStarted etc.) are captured too.
        ulong seed = Enumerable.Range(1, 500).Select(i => (ulong)i)
            .First(s => DungeonGenerator.Generate(s, LootyDungeon).AllRooms.Any(r => r.Type == RoomType.Vault));
        var run = new RunState(seed, new GameConfig(LootyDungeon, QuietThreat));
        var events = new List<IGameEvent>();
        events.AddRange(run.Apply(new JoinPlayer(P1, "P1")).Events);
        events.AddRange(run.Apply(new JoinPlayer(P2, "P2")).Events);
        events.AddRange(run.Apply(new StartRun()).Events);

        var map = run.Dungeon!;
        var vault = map.AllRooms.First(r => r.Type == RoomType.Vault);
        var lockedSide = DirectionExtensions.All.First(d => vault.GetExit(d) == ExitState.Locked);
        var outside = map.Neighbor(vault.Id, lockedSide)!;

        var key = FirstReachable(run, LootKind.Key);
        events.AddRange(WalkTo(run, P1, key.Room));
        events.AddRange(run.Apply(new PickUpKey(P1, key.Id)).Events);
        var chest = FirstReachable(run, LootKind.Chest);
        events.AddRange(WalkTo(run, P1, chest.Room));
        events.AddRange(run.Apply(new OpenChest(P1, chest.Id)).Events);
        events.AddRange(WalkTo(run, P1, outside.Id));
        events.AddRange(run.Apply(new UnlockExit(P1, lockedSide.Opposite())).Events);
        events.AddRange(run.Apply(new ReportSprint(P2, TimeSpan.FromSeconds(1))).Events);
        events.AddRange(run.Tick(TimeSpan.FromSeconds(1)));

        var received = EventCodec.Decode(EventCodec.Encode(events));
        var view = RunView.FromStart(received.OfType<RunStarted>().Single());
        foreach (var e in received) view.Apply(e);

        var p1 = view.Player(P1)!;
        Assert.Equal(run.GetPlayer(P1)!.CarriedPoints, p1.CarriedPoints);
        Assert.Equal(run.GetPlayer(P1)!.Keys, p1.Keys);
        Assert.Equal(ExitState.Open, view.Dungeon.GetRoom(outside.Id).GetExit(lockedSide.Opposite()));
        Assert.Equal(ExitState.Open, view.Dungeon.GetRoom(vault.Id).GetExit(lockedSide));
        Assert.True(view.Dungeon.GetLoot(chest.Id)!.Claimed);
        Assert.True(view.Dungeon.GetLoot(key.Id)!.Claimed);
        Assert.Equal(run.Threat.Level, view.ThreatLevel, 6);
        Assert.Equal(run.Threat.Tier, view.ThreatTier);
        Assert.Equal(
            run.Threat.Shares().Select(s => Math.Round(s.Share, 6)),
            new[] { P1, P2 }.Select(p => Math.Round(view.Player(p)!.NoiseShare, 6)));
    }
}
