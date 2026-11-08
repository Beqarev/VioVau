using System.Text.Json.Serialization;
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
public interface IGameEvent;

public sealed record CommandResult(bool Accepted, string? RejectionReason, IReadOnlyList<IGameEvent> Events)
{
    public static CommandResult Ok(params IGameEvent[] events) => new(true, null, events);
    public static CommandResult Ok(IReadOnlyList<IGameEvent> events) => new(true, null, events);
    public static CommandResult Reject(string reason) => new(false, reason, Array.Empty<IGameEvent>());
}

public sealed record PlayerInfo(PlayerId Id, string DisplayName);

// ── Commands ────────────────────────────────────────────────────────────────

public sealed record JoinPlayer(PlayerId Player, string DisplayName) : ICommand;
public sealed record LeavePlayer(PlayerId Player) : ICommand;
public sealed record StartRun : ICommand;

/// <summary>A player walked through an exit into an adjacent room (detected by the physics layer).</summary>
public sealed record MovePlayerToRoom(PlayerId Player, RoomId Target) : ICommand;

// ── Events ──────────────────────────────────────────────────────────────────

public sealed record PlayerJoined(PlayerId Player, string DisplayName) : IGameEvent;
public sealed record PlayerLeft(PlayerId Player) : IGameEvent;

/// <summary>Carries everything a client needs to build the same dungeon locally.</summary>
public sealed record RunStarted(ulong Seed, IReadOnlyList<PlayerInfo> Players, DungeonConfig Config) : IGameEvent;

/// <param name="FirstVisit">True the first time this player enters this room (cards are drawn on first visits).</param>
public sealed record PlayerEnteredRoom(PlayerId Player, RoomId Room, bool FirstVisit) : IGameEvent;

/// <summary>A room became known to everyone (shared map knowledge).</summary>
public sealed record RoomRevealed(RoomId Room) : IGameEvent;
