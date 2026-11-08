using VioVau.Core.Cards;
using VioVau.Core.Dungeon;
using VioVau.Core.Net;
using static VioVau.Core.Tests.TestRuns;

namespace VioVau.Core.Tests;

public class CardTests
{
    private static readonly CardsConfig GameCards =
        CardsConfig.FromJson(File.ReadAllText(Path.Combine(DungeonTests.RepoRoot(), "game", "data", "cards.json")));

    private static readonly DungeonConfig CardDungeon = LootyDungeon with
    {
        RoomWeights = new(LootyDungeon.RoomWeights) { [RoomType.Library] = 2, [RoomType.Merchant] = 2 },
    };

    /// <summary>A started 2-player run whose decks contain only the given cards (in draw order, top last).</summary>
    private static RunState Run(string[] starter, ulong seed = 42, DungeonConfig? dungeon = null, Func<DungeonMap, bool>? where = null)
    {
        var cards = GameCards with { StarterDeck = starter.ToList() };
        dungeon ??= CardDungeon;
        if (where is not null)
            seed = Enumerable.Range(1, 500).Select(i => (ulong)i).First(s => where(DungeonGenerator.Generate(s, dungeon, cards)));

        var run = new RunState(seed, new GameConfig(dungeon, QuietThreat, cards));
        run.Apply(new JoinPlayer(P1, "P1"));
        run.Apply(new JoinPlayer(P2, "P2"));
        Assert.True(run.Apply(new StartRun()).Accepted);
        return run;
    }

    private static int SlotOf(RunState run, PlayerId p, string card) =>
        Array.FindIndex(run.GetPlayer(p)!.Deck!.Hand.ToArray(), c => c?.Value == card);

    private static CommandResult PlayFirst(RunState run, PlayerId p, string card, PlayerId? target = null, Direction? door = null)
    {
        int slot = SlotOf(run, p, card);
        Assert.True(slot >= 0, $"{card} not in hand");
        return run.Apply(new PlayCard(p, slot, target, door));
    }

    private static List<RoomId> Reachable(RunState run) =>
        DungeonGenerator.WalkingDistances(run.Dungeon!.Floors[0], run.Dungeon.GetRoom(run.Dungeon.Entrance)).Keys.ToList();

    // ── Data ────────────────────────────────────────────────────────────────

    [Fact]
    public void GameCards_LoadAndHaveTwelveCards()
    {
        Assert.Equal(12, GameCards.Cards.Count);
        Assert.Equal(6, GameCards.StarterDeck.Count);
    }

    [Theory]
    [InlineData("""{ "starterDeck": ["nope"], "cards": [] }""")]
    [InlineData("""{ "cards": [ { "id": "a", "name": "A", "effects": [] } ] }""")]
    [InlineData("""{ "cards": [ { "id": "a", "effects": [{ "type": "Unlock" }] } ] }""")]
    [InlineData("""{ "cards": [ { "id": "a", "target": "lockedDoor", "effects": [{ "type": "Steal" }] } ] }""")]
    [InlineData("""{ "cards": [ { "id": "a", "effects": [{ "type": "RevealRooms" }] } ] }""")]
    [InlineData("""{ "cards": [ { "id": "a", "effects": [{ "type": "Dash" }] }, { "id": "a", "effects": [{ "type": "Dash" }] } ] }""")]
    public void CardsConfig_RejectsInvalidData(string json) =>
        Assert.Throws<InvalidDataException>(() => CardsConfig.FromJson(json));

    // ── Drawing ─────────────────────────────────────────────────────────────

    [Fact]
    public void Start_DealsAFullHand_AndPrivateDrawEvents()
    {
        var run = Run(["dash", "hush", "lantern", "smoke-veil"]);
        var deck = run.GetPlayer(P1)!.Deck!;
        Assert.Equal(3, deck.HandCount);
        Assert.Single(deck.DrawPile);
    }

    [Fact]
    public void FirstVisit_DrawsIntoTheEmptySlot_RevisitDoesNot()
    {
        var run = Run(["dash", "dash", "dash", "lantern", "lantern"]);
        var entrance = run.Dungeon!.Entrance;
        Assert.True(PlayFirst(run, P1, "dash").Accepted);
        Assert.Equal(2, run.GetPlayer(P1)!.Deck!.HandCount);

        var next = Reachable(run).First(r => r != entrance);
        var events = WalkTo(run, P1, next);
        Assert.Contains(events, e => e is CardDrawn { Player.Value: 1 });
        Assert.Equal(3, run.GetPlayer(P1)!.Deck!.HandCount);

        PlayFirst(run, P1, run.GetPlayer(P1)!.Deck!.Hand.First(c => c is not null)!.Value.Value);
        var back = WalkTo(run, P1, entrance);
        Assert.DoesNotContain(back, e => e is CardDrawn);
    }

    [Fact]
    public void Timer_DrawsWhileHandNotFull()
    {
        var run = Run(["dash", "dash", "dash", "dash"]);
        int slot = SlotOf(run, P1, "dash");
        PlayFirst(run, P1, "dash");

        Assert.DoesNotContain(run.Tick(TimeSpan.FromSeconds(29)), e => e is CardDrawn);
        var events = run.Tick(TimeSpan.FromSeconds(2));
        Assert.Contains(new CardDrawn(P1, slot, new CardId("dash")), events);
        Assert.DoesNotContain(events, e => e is CardDrawn { Player.Value: 2 }); // P2's hand is full
    }

    [Fact]
    public void EmptyDeck_ReshufflesTheDiscard()
    {
        var run = Run(["hush", "hush", "hush"]);
        for (int i = 0; i < 3; i++) PlayFirst(run, P1, "hush");
        Assert.Empty(run.GetPlayer(P1)!.Deck!.DrawPile);

        run.Tick(TimeSpan.FromSeconds(31));
        var deck = run.GetPlayer(P1)!.Deck!;
        Assert.Equal(1, deck.HandCount);
        Assert.Equal(2, deck.DrawPile.Count);
        Assert.Empty(deck.DiscardPile);
    }

    // ── Playing ─────────────────────────────────────────────────────────────

    [Fact]
    public void Play_DiscardsPublicly_AndMakesAttributedNoise()
    {
        var run = Run(["dash", "dash", "dash"]);
        var result = PlayFirst(run, P1, "dash");

        Assert.True(result.Accepted);
        Assert.Contains(result.Events, e => e is CardPlayed { Card.Value: "dash" });
        Assert.Contains(new MovementEffect(P1, EffectType.Dash, 72), result.Events);
        Assert.Contains(new NoiseMade(P1, 1), result.Events);
        Assert.Single(run.GetPlayer(P1)!.Deck!.DiscardPile);
        Assert.Equal(1, run.Threat.NoiseOf(P1), 6);
    }

    [Fact]
    public void Play_EmptySlot_IsRejected()
    {
        var run = Run(["dash"]);
        Assert.False(run.Apply(new PlayCard(P1, 2)).Accepted);
        Assert.False(run.Apply(new PlayCard(P1, 7)).Accepted);
    }

    [Fact]
    public void Hush_SilencesThePlayer_ThenWearsOff()
    {
        var run = Run(["hush", "hush", "hush"]);
        PlayFirst(run, P1, "hush");

        Assert.Empty(run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(1))).Events);
        Assert.Equal(0, run.Threat.NoiseOf(P1), 6);

        run.Tick(TimeSpan.FromSeconds(9));
        run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(1)));
        Assert.True(run.Threat.NoiseOf(P1) > 0);
    }

    [Fact]
    public void Lockpick_OpensALockedDoorWithoutAKey()
    {
        var run = Run(["lockpick", "lockpick", "lockpick"], where: m => m.AllRooms.Any(r => r.Type == RoomType.Vault));
        var vault = run.Dungeon!.AllRooms.First(r => r.Type == RoomType.Vault);
        var side = DirectionExtensions.All.First(d => vault.GetExit(d) == ExitState.Locked);
        var outside = run.Dungeon.Neighbor(vault.Id, side)!;
        WalkTo(run, P1, outside.Id);

        Assert.False(PlayFirst(run, P1, "lockpick").Accepted); // no door given
        var result = PlayFirst(run, P1, "lockpick", door: side.Opposite());

        Assert.True(result.Accepted, result.RejectionReason);
        Assert.Contains(new ExitUnlocked(P1, outside.Id, side.Opposite(), UsedKey: false), result.Events);
        Assert.Equal(0, run.GetPlayer(P1)!.Keys);
        Assert.True(run.Apply(new MovePlayerToRoom(P1, vault.Id)).Accepted);
    }

    [Fact]
    public void Lantern_RevealsNeighbours_VoidSight_RevealsVaultsAndRelic()
    {
        var run = Run(["lantern", "void-sight", "dash"], where: m => m.AllRooms.Any(r => r.Type == RoomType.Vault));
        var entrance = run.Dungeon!.GetRoom(run.Dungeon.Entrance);

        PlayFirst(run, P1, "lantern");
        foreach (var d in DirectionExtensions.All.Where(d => entrance.GetExit(d) != ExitState.None))
            Assert.Contains(run.Dungeon.Neighbor(entrance.Id, d)!.Id, run.RevealedRooms);

        PlayFirst(run, P1, "void-sight");
        Assert.All(run.Dungeon.AllRooms.Where(r => r.Type is RoomType.Vault or RoomType.RelicChamber),
            r => Assert.Contains(r.Id, run.RevealedRooms));
    }

    [Fact]
    public void Scapegoat_MovesNoise_SmokeVeil_HalvesIt()
    {
        var run = Run(["scapegoat", "smoke-veil", "dash"]);
        run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(1)));
        run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(1)));
        double before = run.Threat.NoiseOf(P1);
        double level = run.Threat.Level;

        Assert.False(PlayFirst(run, P1, "scapegoat").Accepted); // needs a target
        Assert.True(PlayFirst(run, P1, "scapegoat", target: P2).Accepted);
        Assert.Equal(0, run.Threat.NoiseOf(P1), 6);
        Assert.Equal(before, run.Threat.NoiseOf(P2), 6);
        Assert.Equal(level, run.Threat.Level, 6);

        run.Apply(new ReportSprint(P1, TimeSpan.FromSeconds(1)));
        double mine = run.Threat.NoiseOf(P1);
        PlayFirst(run, P1, "smoke-veil");
        Assert.Equal(mine / 2, run.Threat.NoiseOf(P1), 6);
    }

    [Fact]
    public void Pickpocket_TakesAKeyFirst_ThenPoints_AndNeedsSomethingToSteal()
    {
        var run = Run(["pickpocket", "pickpocket", "pickpocket"]);
        Assert.False(PlayFirst(run, P1, "pickpocket", target: P2).Accepted); // P2 has nothing

        var key = run.Dungeon!.Loot.First(l => l.Kind == LootKind.Key && Reachable(run).Contains(l.Room));
        WalkTo(run, P2, key.Room);
        run.Apply(new PickUpKey(P2, key.Id));
        run.GetPlayer(P2)!.CarriedPoints = 10;

        var result = PlayFirst(run, P1, "pickpocket", target: P2);
        Assert.True(result.Accepted);
        Assert.Contains(new PlayerResourcesChanged(P2, 10, 0), result.Events);
        Assert.Equal(1, run.GetPlayer(P1)!.Keys);

        PlayFirst(run, P1, "pickpocket", target: P2);
        Assert.Equal(4, run.GetPlayer(P2)!.CarriedPoints);
        Assert.Equal(6, run.GetPlayer(P1)!.CarriedPoints);
    }

    [Fact]
    public void LootMagnet_ClaimsEverythingInTheRoom_WithEachChestsNoise()
    {
        var run = Run(["loot-magnet", "dash", "dash"], where: m =>
            m.AllRooms.Any(r => r.Type == RoomType.GuardPost));
        var room = run.Dungeon!.AllRooms.Where(r => Reachable(run).Contains(r.Id))
            .First(r => run.Dungeon.LootIn(r.Id).Count(l => l.Kind == LootKind.Chest) >= 1);
        var items = run.Dungeon.LootIn(room.Id).Where(l => l.Kind is LootKind.Chest or LootKind.Key).ToList();
        int chests = items.Count(l => l.Kind == LootKind.Chest);
        WalkTo(run, P1, room.Id);
        double before = run.Threat.Level;

        var result = PlayFirst(run, P1, "loot-magnet");

        Assert.True(result.Accepted);
        Assert.All(items, l => Assert.True(l.Claimed));
        Assert.Equal(items.Where(l => l.Kind == LootKind.Chest).Sum(l => l.Points), run.GetPlayer(P1)!.CarriedPoints);
        double multiplier = QuietThreat.RoomMultiplier(room.Type);
        Assert.Equal(before + (2 + chests * QuietThreat.OpenChest) * multiplier, run.Threat.Level, 6);
    }

    [Fact]
    public void GildedGreed_DoublesOnlyTheNextChest()
    {
        var run = Run(["gilded-greed", "dash", "dash"]);
        var chests = run.Dungeon!.Loot.Where(l => l.Kind == LootKind.Chest && Reachable(run).Contains(l.Room)).Take(2).ToList();
        PlayFirst(run, P1, "gilded-greed");

        WalkTo(run, P1, chests[0].Room);
        run.Apply(new OpenChest(P1, chests[0].Id));
        Assert.Equal(chests[0].Points * 2, run.GetPlayer(P1)!.CarriedPoints);

        WalkTo(run, P1, chests[1].Room);
        run.Apply(new OpenChest(P1, chests[1].Id));
        Assert.Equal(chests[0].Points * 2 + chests[1].Points, run.GetPlayer(P1)!.CarriedPoints);
    }

    [Fact]
    public void SharedFortune_GivesBothPlayersPoints()
    {
        var run = Run(["shared-fortune", "dash", "dash"]);
        var result = PlayFirst(run, P1, "shared-fortune", target: P2);
        Assert.Contains(new PlayerResourcesChanged(P1, 5, 0), result.Events);
        Assert.Contains(new PlayerResourcesChanged(P2, 5, 0), result.Events);
    }

    // ── Offers ──────────────────────────────────────────────────────────────

    [Fact]
    public void Library_GivesOneFreeCardPerPlayer()
    {
        var run = Run(["dash", "dash", "dash"], where: m => m.Loot.Any(l => l.Kind == LootKind.Card && m.GetRoom(l.Room).Type == RoomType.Library));
        var offers = run.Dungeon!.Loot.Where(l => l.Kind == LootKind.Card && run.Dungeon.GetRoom(l.Room).Type == RoomType.Library)
            .GroupBy(l => l.Room).First(g => Reachable(run).Contains(g.Key)).ToList();
        Assert.Equal(3, offers.Count);
        Assert.All(offers, o => Assert.Equal(0, o.Price));

        WalkTo(run, P1, offers[0].Room);
        var result = run.Apply(new TakeCard(P1, offers[0].Id));
        Assert.True(result.Accepted, result.RejectionReason);
        Assert.Contains(new CardGained(P1, offers[0].Card!.Value), result.Events);
        Assert.Contains(offers[0].Card!.Value, run.GetPlayer(P1)!.Deck!.DiscardPile);

        Assert.False(run.Apply(new TakeCard(P1, offers[1].Id)).Accepted); // one per player
        WalkTo(run, P2, offers[0].Room);
        Assert.False(run.Apply(new TakeCard(P2, offers[0].Id)).Accepted); // already taken
        Assert.True(run.Apply(new TakeCard(P2, offers[1].Id)).Accepted);
    }

    [Fact]
    public void Merchant_SellsForCarriedPoints()
    {
        var run = Run(["dash", "dash", "dash"], where: m => m.Loot.Any(l => l.Kind == LootKind.Card && m.GetRoom(l.Room).Type == RoomType.Merchant));
        var offer = run.Dungeon!.Loot.Where(l => l.Kind == LootKind.Card && run.Dungeon.GetRoom(l.Room).Type == RoomType.Merchant)
            .First(l => Reachable(run).Contains(l.Room));
        Assert.True(offer.Price > 0);
        WalkTo(run, P1, offer.Room);

        Assert.False(run.Apply(new TakeCard(P1, offer.Id)).Accepted); // broke
        run.GetPlayer(P1)!.CarriedPoints = offer.Price + 3;
        var result = run.Apply(new TakeCard(P1, offer.Id));
        Assert.True(result.Accepted);
        Assert.Contains(new PlayerResourcesChanged(P1, 3, 0), result.Events);
    }

    // ── Privacy and mirroring ───────────────────────────────────────────────

    [Fact]
    public void PrivateEvents_OnlyReachTheirOwner_AndRunViewKnowsOnlyOwnHand()
    {
        var cards = GameCards with { StarterDeck = ["dash", "hush", "lantern", "lockpick"] };
        var run = new RunState(7, new GameConfig(CardDungeon, QuietThreat, cards));
        var all = new List<IGameEvent>();
        all.AddRange(run.Apply(new JoinPlayer(P1, "P1")).Events);
        all.AddRange(run.Apply(new JoinPlayer(P2, "P2")).Events);
        all.AddRange(run.Apply(new StartRun()).Events);
        all.AddRange(run.Apply(new PlayCard(P2, 0)).Events);

        // P1's machine receives only what it may see, over the wire.
        var forP1 = EventCodec.Decode(EventCodec.Encode(EventVisibility.For(all, p => p == P1)));
        Assert.DoesNotContain(forP1, e => e is CardDrawn { Player.Value: 2 });
        Assert.DoesNotContain(forP1, e => e is DeckChanged { Player.Value: 2 });
        Assert.Contains(forP1, e => e is CardPlayed { Player.Value: 2 }); // plays are public

        var view = RunView.FromStart(forP1.OfType<RunStarted>().Single());
        foreach (var e in forP1) view.Apply(e);

        Assert.Equal(run.GetPlayer(P1)!.Deck!.Hand, view.Player(P1)!.Hand);
        Assert.All(view.Player(P2)!.Hand, c => Assert.Null(c));
        Assert.Equal(2, view.Player(P2)!.HandCount);
        Assert.Equal(run.GetPlayer(P1)!.Deck!.DrawPile.Count, view.Player(P1)!.DrawPile);
    }

    [Fact]
    public void Generation_WithCards_DoesNotChangeTheLayoutOrOtherLoot()
    {
        var without = DungeonGenerator.Generate(9, CardDungeon);
        var with = DungeonGenerator.Generate(9, CardDungeon, GameCards);
        Assert.Equal(
            without.Loot.OrderBy(l => l.Id.Value).Select(l => (l.Id, l.Room, l.Position)),
            with.Loot.Where(l => l.Kind != LootKind.Card).OrderBy(l => l.Id.Value).Select(l => (l.Id, l.Room, l.Position)));
        Assert.Equal(without.AllRooms.Select(r => r.Type), with.AllRooms.Select(r => r.Type));
    }
}
