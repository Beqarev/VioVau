namespace VioVau.Core;

/// <summary>
/// An intent sent to the host ("I want to do X"). Commands are validated by <see cref="RunState.Apply"/>;
/// they never change state on their own.
/// </summary>
public interface ICommand;

/// <summary>Something that happened. Emitted by Core, broadcast to clients, consumed by views.</summary>
public interface IGameEvent;

public sealed record CommandResult(bool Accepted, string? RejectionReason, IReadOnlyList<IGameEvent> Events)
{
    public static CommandResult Ok(params IGameEvent[] events) => new(true, null, events);
    public static CommandResult Reject(string reason) => new(false, reason, Array.Empty<IGameEvent>());
}

// ── Commands ────────────────────────────────────────────────────────────────

public sealed record JoinPlayer(PlayerId Player, string DisplayName) : ICommand;
public sealed record LeavePlayer(PlayerId Player) : ICommand;
public sealed record StartRun : ICommand;

// ── Events ──────────────────────────────────────────────────────────────────

public sealed record PlayerJoined(PlayerId Player, string DisplayName) : IGameEvent;
public sealed record PlayerLeft(PlayerId Player) : IGameEvent;
public sealed record RunStarted(ulong Seed, IReadOnlyList<PlayerId> Players) : IGameEvent;
