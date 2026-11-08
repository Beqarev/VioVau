using VioVau.Core.Random;

namespace VioVau.Core.Cards;

/// <summary>One player's cards: draw pile, a fixed-slot hand (empty slots are null), and discard pile.</summary>
public sealed class PlayerDeck
{
    private readonly List<CardId> _drawPile = new();
    private readonly List<CardId> _discard = new();
    private readonly CardId?[] _hand;

    public IReadOnlyList<CardId> DrawPile => _drawPile;
    public IReadOnlyList<CardId> DiscardPile => _discard;
    public IReadOnlyList<CardId?> Hand => _hand;

    public int HandCount => _hand.Count(c => c is not null);
    public bool HandFull => HandCount == _hand.Length;

    public PlayerDeck(int handSize, IEnumerable<CardId> starter, GameRng rng)
    {
        _hand = new CardId?[handSize];
        _drawPile.AddRange(starter);
        rng.Shuffle(_drawPile);
    }

    /// <summary>Draws into the first empty slot, reshuffling the discard if the draw pile is empty.</summary>
    /// <returns>The slot and card drawn, and whether a reshuffle happened; null if the hand is full or no cards are left.</returns>
    public (int Slot, CardId Card, bool Reshuffled)? Draw(GameRng rng)
    {
        int slot = Array.IndexOf(_hand, null);
        if (slot < 0) return null;

        bool reshuffled = false;
        if (_drawPile.Count == 0)
        {
            if (_discard.Count == 0) return null;
            _drawPile.AddRange(_discard);
            _discard.Clear();
            rng.Shuffle(_drawPile);
            reshuffled = true;
        }

        var card = _drawPile[^1];
        _drawPile.RemoveAt(_drawPile.Count - 1);
        _hand[slot] = card;
        return (slot, card, reshuffled);
    }

    public CardId? PeekHand(int slot) => slot >= 0 && slot < _hand.Length ? _hand[slot] : null;

    /// <summary>Moves the card in <paramref name="slot"/> to the discard pile.</summary>
    public CardId Discard(int slot)
    {
        var card = _hand[slot] ?? throw new InvalidOperationException($"Slot {slot} is empty.");
        _hand[slot] = null;
        _discard.Add(card);
        return card;
    }

    /// <summary>New cards go to the discard pile and come around on the next reshuffle (standard deckbuilding).</summary>
    public void Gain(CardId card) => _discard.Add(card);
}
