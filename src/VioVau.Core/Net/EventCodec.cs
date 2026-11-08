using System.Text.Json;
using VioVau.Core.Dungeon;

namespace VioVau.Core.Net;

/// <summary>Turns batches of events into strings for the network and back. Transport-agnostic.</summary>
public static class EventCodec
{
    private static readonly JsonSerializerOptions Options = DungeonConfig.JsonOptions;

    public static string Encode(IReadOnlyList<IGameEvent> events) =>
        JsonSerializer.Serialize(events.ToArray(), Options);

    public static IReadOnlyList<IGameEvent> Decode(string json) =>
        JsonSerializer.Deserialize<IGameEvent[]>(json, Options)
        ?? throw new InvalidDataException("Event batch is empty.");
}
