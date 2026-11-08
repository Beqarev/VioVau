using System.Text.Json.Serialization;
using VioVau.Core.Cards;
using VioVau.Core.Dungeon;
using VioVau.Core.Enemies;

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
[JsonDerivedType(typeof(EnemySpawned), "enemySpawned")]
[JsonDerivedType(typeof(EnemyKilled), "enemyKilled")]
[JsonDerivedType(typeof(EnemiesStunned), "enemiesStunned")]
[JsonDerivedType(typeof(DecoyPlaced), "decoyPlaced")]
[JsonDerivedType(typeof(PlayerHit), "playerHit")]
[JsonDerivedType(typeof(PlayerDowned), "playerDowned")]
[JsonDerivedType(typeof(PlayerRevived), "playerRevived")]
[JsonDerivedType(typeof(PlayerDied), "playerDied")]
[JsonDerivedType(typeof(LootDropped), "lootDropped")]
[JsonDerivedType(typeof(BagClaimed), "bagClaimed")]
[JsonDerivedType(typeof(RunEnded), "runEnded")]
[JsonDerivedType(typeof(RelicTaken), "relicTaken")]
[JsonDerivedType(typeof(PlayerExtracted), "playerExtracted")]
[JsonDerivedType(typeof(CollapseStarted), "collapseStarted")]
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
public sealed record PlayCard(PlayerId Player, int Slot, PlayerId? TargetPlayer = null, Direction? TargetDoor = null, EnemyId? TargetEnemy = null) : ICommand;

/// <summary>Take a card offered in the player's room (free at a Library, bought at a Merchant).</summary>
public sealed record TakeCard(PlayerId Player, EntityId Offer) : ICommand;

/// <summary>An enemy's attack connected (detected by the AI). <paramref name="At"/> is where the player stood.</summary>
public sealed record HitPlayer(PlayerId Player, EnemyId Enemy, RoomPoint At) : ICommand;

/// <summary>Help a downed ally up (the physics layer checked they're within reach).</summary>
public sealed record RevivePlayer(PlayerId Reviver, PlayerId Target) : ICommand;

/// <summary>Pick up a dead player's loot bag.</summary>
public sealed record ClaimBag(PlayerId Player, EntityId Bag) : ICommand;

/// <summary>Lift a Relic (one at a time).</summary>
public sealed record TakeRelic(PlayerId Player, EntityId Relic) : ICommand;

/// <summary>Escape through the Lobby and bank everything carried.</summary>
public sealed record Extract(PlayerId Player) : ICommand;

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

// Danger

/// <param name="Target">For Strikes: the player the dungeon sent it after (null for initial guards).</param>
public sealed record EnemySpawned(EnemyId Enemy, EnemyKind Kind, RoomId Room, RoomPoint At, PlayerId? Target) : IGameEvent;

public sealed record EnemyKilled(EnemyId Enemy, PlayerId By) : IGameEvent;

/// <summary>Every enemy in the room is stunned (the AI applies it).</summary>
public sealed record EnemiesStunned(PlayerId By, RoomId Room, double Seconds) : IGameEvent;

/// <summary>A noisemaker at the player's feet; enemies in the room chase it instead (the AI applies it).</summary>
public sealed record DecoyPlaced(PlayerId By, RoomId Room, double Seconds) : IGameEvent;

public sealed record PlayerHit(PlayerId Player, EnemyId Enemy, int Hearts) : IGameEvent;

public sealed record PlayerDowned(PlayerId Player, double BleedOutSeconds) : IGameEvent;

public sealed record PlayerRevived(PlayerId Player, PlayerId By, int Hearts) : IGameEvent;

public sealed record PlayerDied(PlayerId Player) : IGameEvent;

/// <summary>A dead player's loot bag appeared (new loot not known from generation).</summary>
public sealed record LootDropped(EntityId Bag, RoomId Room, RoomPoint At, int Points, int Keys, PlayerId From) : IGameEvent;

public sealed record BagClaimed(PlayerId Player, EntityId Bag, int Points, int Keys) : IGameEvent;

public enum RunOutcome
{
    /// <summary>Escaped and banked their points.</summary>
    Escaped,
    /// <summary>Bled out (or went down with nobody left to help).</summary>
    Fell,
    /// <summary>Still inside when the vault collapsed.</summary>
    Sealed,
}

public sealed record PlayerResult(PlayerId Player, RunOutcome Outcome, int Banked, bool HadRelic, bool Winner);

public sealed record RunEnded(string Reason, IReadOnlyList<PlayerResult> Results) : IGameEvent;

public sealed record RelicTaken(PlayerId Player, EntityId Relic, int Points) : IGameEvent;

public sealed record PlayerExtracted(PlayerId Player, int Banked, bool HadRelic) : IGameEvent;

/// <summary>The vault starts to collapse: everyone still inside after <paramref name="Seconds"/> is lost.</summary>
public sealed record CollapseStarted(double Seconds, string Reason) : IGameEvent;
