using VioVau.Core.Cards;
using VioVau.Core.Dungeon;

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

    internal PlayerView(PlayerInfo info, int handSize)
    {
        Info = info;
        Hand = new CardId?[handSize];
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

    public double ThreatLevel { get; private set; }
    public int ThreatTier { get; private set; }

    private RunView(RunStarted started)
    {
        Seed = started.Seed;
        Config = started.Config;
        Players = started.Players;
        Dungeon = DungeonGenerator.Generate(started.Seed, started.Config.Dungeon, started.Config.CardSet);
        _players = started.Players.ToDictionary(p => p.Id, p => new PlayerView(p, started.Config.CardSet.HandSize));
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
        }
    }
}
