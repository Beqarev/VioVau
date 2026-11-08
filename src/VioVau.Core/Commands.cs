using System.Text.Json.Serialization;
using VioVau.Core.Cards;
using VioVau.Core.Dungeon;

namespace VioVau.Core;

/// <summary>
/// An intent sent to the host ("I want to do X"). Commands are validated by <see cref="RunState.Apply"/>;
/// they never change state on their own.
/// </summary>
public interface ICommand;

/// <summary>Something that happened. Emitted by Core, broadcast to clients, consumed by views.</summary>
/// <remarks>Every event type must be registered here so it can be sent over the network (see <c>EventCodec</c>).</remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(PlayerJoined), "playerJoined")]
[JsonDerivedType(typeof(PlayerLeft), "playerLeft")]
[JsonDerivedType(typeof(RunStarted), "runStarted")]
[JsonDerivedType(typeof(PlayerEnteredRoom), "playerEnteredRoom")]
[JsonDerivedType(typeof(RoomRevealed), "roomRevealed")]
[JsonDerivedType(typeof(ThreatChanged), "threatChanged")]
[JsonDerivedType(typeof(ThreatTierChanged), "threatTierChanged")]
[JsonDerivedType(typeof(NoiseMade), "noiseMade")]
[JsonDerivedType(typeof(ChestOpened), "chestOpened")]
[JsonDerivedType(typeof(KeyPickedUp), "keyPickedUp")]
[JsonDerivedType(typeof(ExitUnlocked), "exitUnlocked")]
[JsonDerivedType(typeof(CardDrawn), "cardDrawn")]
[JsonDerivedType(typeof(DeckChanged), "deckChanged")]
[JsonDerivedType(typeof(CardGained), "cardGained")]
[JsonDerivedType(typeof(HandChanged), "handChanged")]
[JsonDerivedType(typeof(CardPlayed), "cardPlayed")]
[JsonDerivedType(typeof(MovementEffect), "movementEffect")]
[JsonDerivedType(typeof(PlayerSilenced), "playerSilenced")]
[JsonDerivedType(typeof(PlayerResourcesChanged), "playerResourcesChanged")]
[JsonDerivedType(typeof(CardTaken), "cardTaken")]
public interface IGameEvent;

/// <summary>An event only its owner may see (e.g. which card they drew). The host sends it to that player's machine only.</summary>
public interface IPrivateEvent : IGameEvent
{
    PlayerId Owner { get; }
}

public sealed record CommandResult(bool Accepted, string? RejectionReason, IReadOnlyList<IGameEvent> Events)
{
    public static CommandResult Ok(params IGameEvent[] events) => new(true, null, events);
    public static CommandResult Ok(IReadOnlyList<IGameEvent> events) => new(true, null, events);
    public static CommandResult Reject(string reason) => new(false, reason, Array.Empty<IGameEvent>());
}

public sealed record PlayerInfo(PlayerId Id, string DisplayName);

public sealed record NoiseShare(PlayerId Player, double Share);

// ── Commands ────────────────────────────────────────────────────────────────

public sealed record JoinPlayer(PlayerId Player, string DisplayName) : ICommand;
public sealed record LeavePlayer(PlayerId Player) : ICommand;
public sealed record StartRun : ICommand;

/// <summary>A player walked through an exit into an adjacent room (detected by the physics layer).</summary>
public sealed record MovePlayerToRoom(PlayerId Player, RoomId Target) : ICommand;

/// <summary>A player sprinted for this long since the last report (max 1s per report).</summary>
public sealed record ReportSprint(PlayerId Player, TimeSpan Duration) : ICommand;

/// <summary>Open a chest in the player's room. The physics layer has already checked the player is next to it.</summary>
public sealed record OpenChest(PlayerId Player, EntityId Chest) : ICommand;

public sealed record PickUpKey(PlayerId Player, EntityId Key) : ICommand;

/// <summary>Use a key on a locked exit of the player's room.</summary>
public sealed record UnlockExit(PlayerId Player, Direction Side) : ICommand;

/// <summary>Play the card in a hand slot. Targets are found by the physics layer (who's nearby, which door).</summary>
public sealed record PlayCard(PlayerId Player, int Slot, PlayerId? TargetPlayer = null, Direction? TargetDoor = null) : ICommand;

/// <summary>Take a card offered in the player's room (free at a Library, bought at a Merchant).</summary>
public sealed record TakeCard(PlayerId Player, EntityId Offer) : ICommand;

// ── Events ──────────────────────────────────────────────────────────────────

public sealed record PlayerJoined(PlayerId Player, string DisplayName) : IGameEvent;
public sealed record PlayerLeft(PlayerId Player) : IGameEvent;

/// <summary>Carries everything a client needs to build the same dungeon locally.</summary>
public sealed record RunStarted(ulong Seed, IReadOnlyList<PlayerInfo> Players, GameConfig Config) : IGameEvent;

/// <param name="FirstVisit">True the first time this player enters this room (cards are drawn on first visits).</param>
public sealed record PlayerEnteredRoom(PlayerId Player, RoomId Room, bool FirstVisit) : IGameEvent;

/// <summary>A room became known to everyone (shared map knowledge).</summary>
public sealed record RoomRevealed(RoomId Room) : IGameEvent;

/// <summary>Full threat snapshot: sent periodically and after discrete noisy actions.</summary>
public sealed record ThreatChanged(double Level, int Tier, IReadOnlyList<NoiseShare> Shares) : IGameEvent;

public sealed record ThreatTierChanged(int Tier) : IGameEvent;

/// <summary>A discrete noisy action (not continuous sprinting). For feedback: rings, sounds.</summary>
public sealed record NoiseMade(PlayerId Player, double Amount) : IGameEvent;

public sealed record ChestOpened(PlayerId Player, EntityId Chest, int Points) : IGameEvent;
public sealed record KeyPickedUp(PlayerId Player, EntityId Key) : IGameEvent;

/// <summary>The doorway on <paramref name="Side"/> of <paramref name="Room"/> (and its mirror) is now open.</summary>
/// <param name="UsedKey">False when opened by a card (Lockpick).</param>
public sealed record ExitUnlocked(PlayerId Player, RoomId Room, Direction Side, bool UsedKey = true) : IGameEvent;

// Cards — private (owner only)

public sealed record CardDrawn(PlayerId Player, int Slot, CardId Card) : IPrivateEvent
{
    public PlayerId Owner => Player;
}

public sealed record DeckChanged(PlayerId Player, int DrawPile, int DiscardPile) : IPrivateEvent
{
    public PlayerId Owner => Player;
}

public sealed record CardGained(PlayerId Player, CardId Card) : IPrivateEvent
{
    public PlayerId Owner => Player;
}

// Cards — public

/// <summary>How many cards a player holds (everyone sees the count, not the cards).</summary>
public sealed record HandChanged(PlayerId Player, int Count) : IGameEvent;

/// <summary>Played cards are public: everyone sees what you did.</summary>
public sealed record CardPlayed(PlayerId Player, int Slot, CardId Card) : IGameEvent;

/// <summary>Dash/Blink: the owning machine moves the character.</summary>
public sealed record MovementEffect(PlayerId Player, EffectType Kind, double Distance) : IGameEvent;

public sealed record PlayerSilenced(PlayerId Player, double Seconds) : IGameEvent;

/// <summary>Absolute carried points and keys after a transfer (steal, gift, purchase).</summary>
public sealed record PlayerResourcesChanged(PlayerId Player, int CarriedPoints, int Keys) : IGameEvent;

public sealed record CardTaken(PlayerId Player, EntityId Offer, CardId Card, int Price) : IGameEvent;
