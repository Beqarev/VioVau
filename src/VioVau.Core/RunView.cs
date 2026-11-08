using VioVau.Core.Cards;
using VioVau.Core.Dungeon;
using VioVau.Core.Enemies;

namespace VioVau.Core;

/// <summary>What everyone can see about a player.</summary>
public sealed class PlayerView
{
    public PlayerInfo Info { get; }
    public RoomId? Room { get; internal set; }
    public int CarriedPoints { get; internal set; }
    public int Keys { get; internal set; }
    public double NoiseShare { get; internal set; }

    /// <summary>Cards held — public.</summary>
    public int HandCount { get; internal set; }

    /// <summary>The actual cards per slot. Only known for this machine's own players (null otherwise).</summary>
    public CardId?[] Hand { get; }

    public int DrawPile { get; internal set; }
    public int DiscardPile { get; internal set; }

    public int Hearts { get; internal set; }
    public bool Downed { get; internal set; }
    public bool Dead { get; internal set; }
    public bool Active => !Downed && !Dead;

    /// <summary>Bleed-out time when last downed (the view counts down locally).</summary>
    public double BleedOutSeconds { get; internal set; }

    internal PlayerView(PlayerInfo info, int handSize, int hearts)
    {
        Info = info;
        Hand = new CardId?[handSize];
        Hearts = hearts;
    }
}

/// <summary>
/// Read-only picture of a run, rebuilt purely from events. Every peer (host included) keeps one,
/// and all visuals read from it — so host and clients render from the same code path.
/// </summary>
public sealed class RunView
{
    private readonly Dictionary<PlayerId, PlayerView> _players;
    private readonly HashSet<RoomId> _revealed = new();

    public ulong Seed { get; }
    public GameConfig Config { get; }

    /// <summary>This peer's own copy of the map; door and loot changes are applied from events.</summary>
    public DungeonMap Dungeon { get; }

    public IReadOnlyList<PlayerInfo> Players { get; }
    public IReadOnlySet<RoomId> RevealedRooms => _revealed;

    private readonly Dictionary<EnemyId, EnemySpawned> _enemies = new();

    /// <summary>
    /// Living enemies and how they appeared. Kept so a scene that loads after the spawn events (e.g. the
    /// guards placed at run start) can still create them.
    /// </summary>
    public IReadOnlyDictionary<EnemyId, EnemySpawned> Enemies => _enemies;

    /// <summary>Set when the run is over.</summary>
    public string? EndReason { get; private set; }

    public double ThreatLevel { get; private set; }
    public int ThreatTier { get; private set; }

    private RunView(RunStarted started)
    {
        Seed = started.Seed;
        Config = started.Config;
        Players = started.Players;
        Dungeon = DungeonGenerator.Generate(started.Seed, started.Config.Dungeon, started.Config.CardSet);
        _players = started.Players.ToDictionary(p => p.Id, p => new PlayerView(p, started.Config.CardSet.HandSize, started.Config.EnemySet.Health.Hearts));
    }

    public static RunView FromStart(RunStarted started) => new(started);

    public PlayerView? Player(PlayerId id) => _players.GetValueOrDefault(id);

    public RoomId? RoomOf(PlayerId player) => Player(player)?.Room;

    public bool IsRevealed(RoomId room) => _revealed.Contains(room);

    public void Apply(IGameEvent e)
    {
        switch (e)
        {
            case PlayerEnteredRoom entered when Player(entered.Player) is { } p:
                p.Room = entered.Room;
                break;

            case RoomRevealed revealed:
                _revealed.Add(revealed.Room);
                break;

            case ThreatChanged threat:
                ThreatLevel = threat.Level;
                ThreatTier = threat.Tier;
                foreach (var share in threat.Shares)
                    if (Player(share.Player) is { } sharer) sharer.NoiseShare = share.Share;
                break;

            case ThreatTierChanged tier:
                ThreatTier = tier.Tier;
                break;

            case ChestOpened opened:
                if (Dungeon.GetLoot(opened.Chest) is { } chest) chest.Claimed = true;
                if (Player(opened.Player) is { } opener) opener.CarriedPoints += opened.Points;
                break;

            case KeyPickedUp picked:
                if (Dungeon.GetLoot(picked.Key) is { } key) key.Claimed = true;
                if (Player(picked.Player) is { } picker) picker.Keys++;
                break;

            case ExitUnlocked unlocked:
                Dungeon.SetDoorway(unlocked.Room, unlocked.Side, ExitState.Open);
                if (unlocked.UsedKey && Player(unlocked.Player) is { } unlocker) unlocker.Keys--;
                break;

            case CardDrawn drawn when Player(drawn.Player) is { } drawer && drawn.Slot < drawer.Hand.Length:
                drawer.Hand[drawn.Slot] = drawn.Card;
                break;

            case CardPlayed played when Player(played.Player) is { } player && played.Slot < player.Hand.Length:
                player.Hand[played.Slot] = null;
                break;

            case HandChanged hand when Player(hand.Player) is { } holder:
                holder.HandCount = hand.Count;
                break;

            case DeckChanged deck when Player(deck.Player) is { } owner:
                owner.DrawPile = deck.DrawPile;
                owner.DiscardPile = deck.DiscardPile;
                break;

            case PlayerResourcesChanged resources when Player(resources.Player) is { } rich:
                rich.CarriedPoints = resources.CarriedPoints;
                rich.Keys = resources.Keys;
                break;

            case CardTaken taken:
                if (Dungeon.GetLoot(taken.Offer) is { } offer) offer.Claimed = true;
                break;

            case EnemySpawned spawned:
                _enemies[spawned.Enemy] = spawned;
                break;

            case EnemyKilled killed:
                _enemies.Remove(killed.Enemy);
                break;

            case PlayerHit hit when Player(hit.Player) is { } hurt:
                hurt.Hearts = hit.Hearts;
                break;

            case PlayerDowned downed when Player(downed.Player) is { } down:
                down.Downed = true;
                down.BleedOutSeconds = downed.BleedOutSeconds;
                break;

            case PlayerRevived revived when Player(revived.Player) is { } up:
                up.Downed = false;
                up.Hearts = revived.Hearts;
                break;

            case PlayerDied died when Player(died.Player) is { } dead:
                dead.Downed = false;
                dead.Dead = true;
                dead.Hearts = 0;
                dead.CarriedPoints = 0;
                dead.Keys = 0;
                break;

            case LootDropped dropped:
                Dungeon.AddLoot(new LootItem(dropped.Bag, LootKind.Bag, dropped.Room, dropped.At, dropped.Points) { Keys = dropped.Keys });
                break;

            case BagClaimed claimed:
                if (Dungeon.GetLoot(claimed.Bag) is { } bag) bag.Claimed = true;
                if (Player(claimed.Player) is { } looter)
                {
                    looter.CarriedPoints += claimed.Points;
                    looter.Keys += claimed.Keys;
                }
                break;

            case RunEnded ended:
                EndReason = ended.Reason;
                break;
        }
    }
}
