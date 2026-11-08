using VioVau.Core.Dungeon;

namespace VioVau.Core;

/// <summary>
/// How a run ends: lift a Relic, escape through the Lobby to bank your points, and get out before the
/// Collapse (started by the first escape or by maxed-out Threat) seals everyone else inside.
/// </summary>
public sealed partial class RunState
{
    /// <summary>Seconds until the vault seals, once the Collapse has started.</summary>
    public double? CollapseRemaining { get; private set; }

    public IReadOnlyList<PlayerResult>? Results { get; private set; }

    private CommandResult LiftRelic(TakeRelic c)
    {
        if (InRunPlayer(c.Player, out var player, out var room) is { } rejection) return rejection;
        if (Dungeon!.GetLoot(c.Relic) is not { Kind: LootKind.Relic } relic) return CommandResult.Reject($"{c.Relic} is not a relic.");
        if (relic.Room != room) return CommandResult.Reject($"{c.Relic} is not in {c.Player}'s room.");
        if (relic.Claimed) return CommandResult.Reject("Someone already took it.");
        if (player.CarryingRelic) return CommandResult.Reject("You can only carry one Relic.");

        relic.Claimed = true;
        player.CarryingRelic = true;
        player.CarriedPoints += relic.Points;

        var events = new List<IGameEvent> { new RelicTaken(c.Player, c.Relic, relic.Points) };
        MakeNoise(c.Player, Config.Threat.TakeRelic, room, events);
        return CommandResult.Ok(events);
    }

    private CommandResult Escape(Extract c)
    {
        if (InRunPlayer(c.Player, out var player, out var room) is { } rejection) return rejection;
        if (room != Dungeon!.Entrance) return CommandResult.Reject("You can only escape from the Lobby.");

        player.Extracted = true;
        player.Banked = player.CarriedPoints;
        player.Outcome = RunOutcome.Escaped;

        var events = new List<IGameEvent> { new PlayerExtracted(c.Player, player.Banked, player.CarryingRelic) };
        StartCollapse($"{player.DisplayName} escaped. The vault is collapsing!", events);
        CheckRunOver(events);
        return CommandResult.Ok(events);
    }

    private void StartCollapse(string reason, List<IGameEvent> events)
    {
        if (CollapseRemaining is not null || Phase != RunPhase.InRun) return;
        CollapseRemaining = Config.Threat.CollapseSeconds;
        events.Add(new CollapseStarted(Config.Threat.CollapseSeconds, reason));
    }

    private void TickCollapse(double seconds, List<IGameEvent> events)
    {
        // Maxed-out Threat brings the vault down too.
        if (CollapseRemaining is null && Threat.Tier >= Config.Threat.TierThresholds.Count - 1)
            StartCollapse("The vault can't hold any more. It's collapsing!", events);

        if (CollapseRemaining is not { } remaining) return;
        remaining -= seconds;
        CollapseRemaining = Math.Max(0, remaining);
        if (remaining > 0) return;

        // Sealed in: everyone still inside is lost with what they carried.
        foreach (var player in Players.Where(p => p.Inside))
        {
            player.Downed = false;
            player.Dead = true;
            player.CarriedPoints = 0;
            player.CarryingRelic = false;
            player.Outcome = RunOutcome.Sealed;
        }
        EndRun(events);
    }

    /// <summary>Final results: escaped players keep what they banked; the richest escapee wins (ties share it).</summary>
    private void EndRun(List<IGameEvent> events)
    {
        if (Phase != RunPhase.InRun) return;
        Phase = RunPhase.Ended;

        int best = Players.Where(p => p.Extracted).Select(p => p.Banked).DefaultIfEmpty(0).Max();
        Results = Players
            .Select(p => new PlayerResult(
                p.Id,
                p.Outcome ?? RunOutcome.Fell,
                p.Extracted ? p.Banked : 0,
                p.Extracted && p.CarryingRelic,
                Winner: p.Extracted && best > 0 && p.Banked == best))
            .OrderByDescending(r => r.Banked)
            .ThenBy(r => r.Player.Value)
            .ToList();

        string reason = Players.Any(p => p.Extracted) ? "The vault is sealed." : "Everyone fell in the vault.";
        events.Add(new RunEnded(reason, Results));
    }
}
