using VioVau.Core.Cards;
using VioVau.Core.Dungeon;
using VioVau.Core.Enemies;

namespace VioVau.Core;

/// <summary>Card rules: dealing, drawing, playing, effects, and taking offered cards.</summary>
public sealed partial class RunState
{
    private CardsConfig Cards => Config.CardSet;

    // ── Drawing ─────────────────────────────────────────────────────────────

    private void DealStartingHand(PlayerState player, List<IGameEvent> events)
    {
        player.Deck = new PlayerDeck(Cards.HandSize, Cards.StarterDeck.Select(id => new CardId(id)), Rng.Cards);
        for (int i = 0; i < Cards.HandSize; i++) TryDraw(player, events);
    }

    /// <summary>Draws one card if the hand has room and there's anything left to draw.</summary>
    private void TryDraw(PlayerState player, List<IGameEvent> events)
    {
        if (player.Deck?.Draw(Rng.Cards) is not { } drawn) return;
        events.Add(new CardDrawn(player.Id, drawn.Slot, drawn.Card));
        events.Add(new HandChanged(player.Id, player.Deck.HandCount));
        events.Add(DeckState(player));
    }

    /// <summary>One card every <see cref="CardsConfig.DrawIntervalSeconds"/> while the hand isn't full.</summary>
    private void TickDraws(TimeSpan dt, List<IGameEvent> events)
    {
        foreach (var player in Players)
        {
            if (player.Deck is null || !player.Active) continue;
            if (player.Deck.HandFull)
            {
                player.DrawTimer = 0;
                continue;
            }

            player.DrawTimer += dt.TotalSeconds;
            if (player.DrawTimer < Cards.DrawIntervalSeconds) continue;
            player.DrawTimer -= Cards.DrawIntervalSeconds;
            TryDraw(player, events);
        }
    }

    private static DeckChanged DeckState(PlayerState player) =>
        new(player.Id, player.Deck?.DrawPile.Count ?? 0, player.Deck?.DiscardPile.Count ?? 0);

    // ── Playing ─────────────────────────────────────────────────────────────

    private CommandResult Play(PlayCard c)
    {
        if (InRunPlayer(c.Player, out var player, out var room) is { } rejection) return rejection;
        if (player.Deck?.PeekHand(c.Slot) is not { } cardId) return CommandResult.Reject($"Slot {c.Slot} is empty.");
        if (Cards.Get(cardId) is not { } card) return CommandResult.Reject($"Unknown card '{cardId}'.");

        if (CheckTarget(card, player, room, c) is { } targetRejection) return targetRejection;
        var target = c.TargetPlayer is { } t ? GetPlayer(t) : null;
        var enemy = c.TargetEnemy is { } e ? _enemies.GetValueOrDefault(e) : null;

        var events = new List<IGameEvent>();
        player.Deck.Discard(c.Slot);
        events.Add(new CardPlayed(c.Player, c.Slot, cardId));
        events.Add(new HandChanged(c.Player, player.Deck.HandCount));
        events.Add(DeckState(player));

        MakeNoise(c.Player, card.Noise, room, events);
        foreach (var effect in card.Effects) ApplyEffect(effect, player, target, enemy, room, c.TargetDoor, events);

        return CommandResult.Ok(events);
    }

    /// <summary>Rejects a play whose target is missing or pointless, so the card isn't wasted.</summary>
    private CommandResult? CheckTarget(CardDefinition card, PlayerState player, RoomId room, PlayCard c)
    {
        switch (card.Target)
        {
            case CardTarget.LockedDoor:
                if (c.TargetDoor is not { } door || Dungeon!.GetRoom(room).GetExit(door) != ExitState.Locked)
                    return CommandResult.Reject($"{card.Name} needs a locked door in reach.");
                break;

            case CardTarget.NearbyPlayer or CardTarget.OtherPlayer:
                if (c.TargetPlayer is not { } targetId || targetId == player.Id || GetPlayer(targetId) is not { Room: not null, Dead: false } target)
                    return CommandResult.Reject($"{card.Name} needs another player as a target.");
                if (card.Effects.Any(e => e.Type == EffectType.Steal) && target.Keys == 0 && target.CarriedPoints == 0)
                    return CommandResult.Reject($"{target.DisplayName} has nothing to steal.");
                break;

            case CardTarget.NearbyEnemy:
                if (c.TargetEnemy is not { } enemyId || _enemies.GetValueOrDefault(enemyId) is not { Alive: true } enemy)
                    return CommandResult.Reject($"{card.Name} needs an enemy in reach.");
                if (card.Effects.Any(e => e.Type == EffectType.KillEnemy) && !enemy.Killable)
                    return CommandResult.Reject("The Warden cannot be destroyed.");
                break;

            case CardTarget.DownedPlayer:
                if (c.TargetPlayer is not { } downedId || GetPlayer(downedId) is not { Downed: true } downed || downed.Room != room)
                    return CommandResult.Reject($"{card.Name} needs a downed ally in this room.");
                break;
        }
        return null;
    }

    private void ApplyEffect(CardEffect effect, PlayerState player, PlayerState? target, EnemyState? enemy, RoomId room, Direction? door, List<IGameEvent> events)
    {
        switch (effect.Type)
        {
            case EffectType.Dash or EffectType.Blink:
                if (effect.Type == EffectType.Dash) GrantInvulnerability(player, Enemies.Health.DashInvulnerableSeconds);
                events.Add(new MovementEffect(player.Id, effect.Type, effect.Amount));
                break;

            case EffectType.StunEnemies:
                events.Add(new EnemiesStunned(player.Id, room, effect.Amount));
                break;

            case EffectType.Decoy:
                events.Add(new DecoyPlaced(player.Id, room, effect.Amount));
                break;

            case EffectType.KillEnemy when enemy is { Alive: true, Killable: true }:
                enemy.Alive = false;
                events.Add(new EnemyKilled(enemy.Id, player.Id));
                break;

            case EffectType.Revive when target is { Downed: true }:
                RaiseUp(target, player, events);
                break;

            case EffectType.Silence:
                var until = Elapsed + TimeSpan.FromSeconds(effect.Amount);
                if (until > player.SilencedUntil) player.SilencedUntil = until;
                events.Add(new PlayerSilenced(player.Id, effect.Amount));
                break;

            case EffectType.Unlock when door is { } side:
                Dungeon!.SetDoorway(room, side, ExitState.Open);
                events.Add(new ExitUnlocked(player.Id, room, side, UsedKey: false));
                break;

            case EffectType.RevealAdjacent:
                var here = Dungeon!.GetRoom(room);
                foreach (var d in DirectionExtensions.All.Where(d => here.GetExit(d) != ExitState.None))
                    events.AddRange(Reveal(Dungeon.Neighbor(room, d)!.Id));
                break;

            case EffectType.RevealRooms:
                foreach (var r in Dungeon!.Floors[room.Floor].Rooms.Where(r => effect.Rooms.Contains(r.Type)))
                    events.AddRange(Reveal(r.Id));
                break;

            case EffectType.ShiftNoise when target is not null:
                Threat.TransferNoise(player.Id, target.Id, effect.Amount);
                events.Add(Threat.Snapshot());
                break;

            case EffectType.ScaleOwnNoise:
                Threat.ScaleNoise(player.Id, effect.Amount);
                events.Add(Threat.Snapshot());
                break;

            case EffectType.Steal when target is not null:
                if (target.Keys > 0)
                {
                    target.Keys--;
                    player.Keys++;
                }
                else
                {
                    int taken = Math.Min(target.CarriedPoints, (int)effect.Amount);
                    target.CarriedPoints -= taken;
                    player.CarriedPoints += taken;
                }
                events.Add(Resources(target));
                events.Add(Resources(player));
                break;

            case EffectType.GrantPoints when target is not null:
                player.CarriedPoints += (int)effect.Amount;
                target.CarriedPoints += (int)effect.Amount;
                events.Add(Resources(player));
                events.Add(Resources(target));
                break;

            case EffectType.MultiplyNextChest:
                player.NextChestMultiplier = effect.Amount;
                break;

            case EffectType.ClaimRoomLoot:
                foreach (var item in Dungeon!.LootIn(room).Where(l => !l.Claimed).OrderBy(l => l.Id.Value).ToList())
                {
                    if (item.Kind == LootKind.Chest) ClaimChest(player, item, room, events);
                    else if (item.Kind == LootKind.Key) ClaimKey(player, item, room, events);
                }
                break;
        }
    }

    private static PlayerResourcesChanged Resources(PlayerState p) => new(p.Id, p.CarriedPoints, p.Keys);

    // ── Taking offered cards ────────────────────────────────────────────────

    private CommandResult Take(TakeCard c)
    {
        if (InRunPlayer(c.Player, out var player, out var room) is { } rejection) return rejection;
        if (Dungeon!.GetLoot(c.Offer) is not { Kind: LootKind.Card, Card: { } card } offer) return CommandResult.Reject($"{c.Offer} is not a card offer.");
        if (offer.Room != room) return CommandResult.Reject($"{c.Offer} is not in {c.Player}'s room.");
        if (offer.Claimed) return CommandResult.Reject($"{c.Offer} was already taken.");
        if (player.Deck is null) return CommandResult.Reject($"{c.Player} has no deck.");

        var rule = Cards.Offers.GetValueOrDefault(Dungeon.GetRoom(room).Type) ?? new OfferRule();
        int taken = player.OffersTaken.GetValueOrDefault(room);
        if (rule.PerPlayerLimit > 0 && taken >= rule.PerPlayerLimit)
            return CommandResult.Reject($"{player.DisplayName} already took a card here.");
        if (player.CarriedPoints < offer.Price)
            return CommandResult.Reject($"{player.DisplayName} can't afford it ({offer.Price}p).");

        offer.Claimed = true;
        player.OffersTaken[room] = taken + 1;
        player.CarriedPoints -= offer.Price;
        player.Deck.Gain(card);

        var events = new List<IGameEvent> { new CardTaken(c.Player, c.Offer, card, offer.Price) };
        if (offer.Price > 0) events.Add(Resources(player));
        events.Add(new CardGained(c.Player, card));
        events.Add(DeckState(player));
        return CommandResult.Ok(events);
    }
}
