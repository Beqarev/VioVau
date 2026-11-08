using VioVau.Core.Dungeon;

namespace VioVau.Core.Cards;

public enum CardCategory
{
    Mobility,
    Stealth,
    Utility,
    Combat,
    Greed,
    Social,
}

public enum CardRarity
{
    Common,
    Rare,
    Legendary,
}

/// <summary>What the player must be next to (or have) for the card to be playable. The physics layer finds it.</summary>
public enum CardTarget
{
    None,
    /// <summary>A locked door of the player's room, within reach.</summary>
    LockedDoor,
    /// <summary>Another player within arm's reach.</summary>
    NearbyPlayer,
    /// <summary>Any other player (the nearest one).</summary>
    OtherPlayer,
}

public enum EffectType
{
    /// <summary>Owner bursts forward <c>Amount</c> px (performed by the physics layer).</summary>
    Dash,
    /// <summary>Owner teleports forward up to <c>Amount</c> px (performed by the physics layer).</summary>
    Blink,
    /// <summary>The player makes no noise for <c>Amount</c> seconds.</summary>
    Silence,
    /// <summary>Opens the targeted locked door without using a key.</summary>
    Unlock,
    /// <summary>Reveals every room connected to the player's room by a doorway.</summary>
    RevealAdjacent,
    /// <summary>Reveals every room of the listed <c>Rooms</c> types on the floor.</summary>
    RevealRooms,
    /// <summary>Moves up to <c>Amount</c> of the player's attributed noise onto the target.</summary>
    ShiftNoise,
    /// <summary>Takes a key from the target, or up to <c>Amount</c> points if they have no key.</summary>
    Steal,
    /// <summary>Opens every chest and takes every key in the room (each with its usual noise).</summary>
    ClaimRoomLoot,
    /// <summary>The player's next opened chest gives <c>Amount</c>× points.</summary>
    MultiplyNextChest,
    /// <summary>The player and the target each gain <c>Amount</c> points.</summary>
    GrantPoints,
    /// <summary>Multiplies the player's attributed noise by <c>Amount</c> (e.g. 0.5 halves it). Threat itself is unchanged.</summary>
    ScaleOwnNoise,
}

public sealed record CardEffect
{
    public EffectType Type { get; init; }
    public double Amount { get; init; }
    public List<RoomType> Rooms { get; init; } = [];
}

public sealed record CardDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public CardCategory Category { get; init; }
    public CardRarity Rarity { get; init; } = CardRarity.Common;

    /// <summary>Threat added (and attributed to the player) when played.</summary>
    public double Noise { get; init; }

    /// <summary>Merchant price in points.</summary>
    public int Price { get; init; } = 10;

    public string Text { get; init; } = "";
    public CardTarget Target { get; init; } = CardTarget.None;
    public List<CardEffect> Effects { get; init; } = [];

    public CardId CardId => new(Id);
}

/// <summary>How a room type offers cards: free picks (Library, one per player) or for sale (Merchant).</summary>
public sealed record OfferRule
{
    public int Count { get; init; } = 3;
    public bool Free { get; init; }

    /// <summary>A player may take at most this many offers from one room (0 = unlimited).</summary>
    public int PerPlayerLimit { get; init; }
}

/// <summary>All card data. Loaded from <c>game/data/cards.json</c>.</summary>
public sealed record CardsConfig
{
    public int HandSize { get; init; } = 3;
    public double DrawIntervalSeconds { get; init; } = 30;
    public List<string> StarterDeck { get; init; } = [];
    public List<CardDefinition> Cards { get; init; } = [];
    public Dictionary<RoomType, OfferRule> Offers { get; init; } = new();

    /// <summary>Relative chance of each rarity when rolling offers.</summary>
    public Dictionary<CardRarity, int> RarityWeights { get; init; } = new()
    {
        [CardRarity.Common] = 6,
        [CardRarity.Rare] = 3,
        [CardRarity.Legendary] = 1,
    };

    /// <summary>No cards at all — runs work without a deck (used by default and in non-card tests).</summary>
    public static CardsConfig Default { get; } = new();

    public static CardsConfig FromJson(string json)
    {
        var config = CoreJson.Parse<CardsConfig>(json, "Cards config");
        config.Validate();
        return config;
    }

    public CardDefinition? Get(CardId id) => Cards.FirstOrDefault(c => c.Id == id.Value);

    public CardDefinition Require(CardId id) => Get(id) ?? throw new InvalidDataException($"Unknown card '{id}'.");

    public void Validate()
    {
        if (HandSize is < 1 or > 5) throw new InvalidDataException("handSize must be 1–5.");
        if (DrawIntervalSeconds <= 0) throw new InvalidDataException("drawIntervalSeconds must be positive.");

        var ids = new HashSet<string>();
        foreach (var card in Cards)
        {
            if (string.IsNullOrWhiteSpace(card.Id)) throw new InvalidDataException("Every card needs an id.");
            if (!ids.Add(card.Id)) throw new InvalidDataException($"Duplicate card id '{card.Id}'.");
            if (card.Noise < 0 || card.Price < 0) throw new InvalidDataException($"Card '{card.Id}': noise and price cannot be negative.");
            if (card.Effects.Count == 0) throw new InvalidDataException($"Card '{card.Id}' has no effects.");

            foreach (var effect in card.Effects)
            {
                bool needsTarget = effect.Type is EffectType.Unlock or EffectType.ShiftNoise or EffectType.Steal or EffectType.GrantPoints;
                if (needsTarget && card.Target == CardTarget.None)
                    throw new InvalidDataException($"Card '{card.Id}': {effect.Type} needs a target.");
                if (effect.Type == EffectType.Unlock && card.Target != CardTarget.LockedDoor)
                    throw new InvalidDataException($"Card '{card.Id}': Unlock needs target lockedDoor.");
                if (effect.Type is EffectType.ShiftNoise or EffectType.Steal or EffectType.GrantPoints && card.Target is not (CardTarget.NearbyPlayer or CardTarget.OtherPlayer))
                    throw new InvalidDataException($"Card '{card.Id}': {effect.Type} needs a player target.");
                if (effect.Type == EffectType.RevealRooms && effect.Rooms.Count == 0)
                    throw new InvalidDataException($"Card '{card.Id}': RevealRooms needs rooms.");
                if (effect.Amount < 0) throw new InvalidDataException($"Card '{card.Id}': amounts cannot be negative.");
            }
        }

        foreach (var id in StarterDeck)
            if (!ids.Contains(id)) throw new InvalidDataException($"Starter deck lists unknown card '{id}'.");
        if (Offers.Count > 0 && Cards.Count == 0) throw new InvalidDataException("Offers need cards.");
        if (Offers.Values.Any(o => o.Count < 0 || o.PerPlayerLimit < 0)) throw new InvalidDataException("Offer counts cannot be negative.");
        if (RarityWeights.Values.Any(w => w < 0)) throw new InvalidDataException("rarityWeights cannot be negative.");
    }
}
