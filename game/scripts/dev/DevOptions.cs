using Godot;

namespace VioVau.Game.Dev;

/// <summary>
/// Developer command-line flags (after <c>--</c>), for automated play-tests:
/// <c>--autopilot</c> replaces this machine's first local player with a scripted walker;
/// <c>--screenshot-at=SEC[,SEC…] --screenshot-dir=PATH</c> saves screenshots during the run.
/// </summary>
public static class DevOptions
{
    private static readonly string[] Args = OS.GetCmdlineUserArgs();

    public static bool Autopilot => Args.Contains("--autopilot") || AutopilotAll;

    /// <summary><c>--autopilot=all</c>: every local player on this machine is an autopilot (couch tests).</summary>
    public static bool AutopilotAll => Value("autopilot") == "all";

    /// <summary><c>--brutal</c>: 1 heart and fast, sharp-eyed Sentries — for testing downed / death / bags quickly.</summary>
    public static bool Brutal => Args.Contains("--brutal");

    /// <summary><c>--port=N</c>: host/join on this UDP port instead of 7777 (e.g. to test alongside a running game).</summary>
    public static int Port => int.TryParse(Value("port"), out var port) ? port : Networking.NetSession.DefaultPort;

    /// <summary><c>--auto-rematch</c>: the host starts the next run 3s after the scoreboard appears (tests rematches).</summary>
    public static bool AutoRematch => Args.Contains("--auto-rematch");

    /// <summary><c>--seed=N</c>: the host uses this run seed (reproducible dungeons for testing).</summary>
    public static ulong? Seed => ulong.TryParse(Value("seed"), out var seed) ? seed : null;

    public static double[] ScreenshotTimes =>
        Value("screenshot-at")?.Split(',').Select(s => double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : -1).Where(t => t >= 0).ToArray()
        ?? [];

    public static string ScreenshotDir => Value("screenshot-dir") ?? "user://screenshots";

    private static string? Value(string name) =>
        Args.FirstOrDefault(a => a.StartsWith($"--{name}="))?.Split('=', 2)[1];
}
