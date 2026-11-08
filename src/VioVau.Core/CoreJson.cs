using System.Text.Json;
using System.Text.Json.Serialization;

namespace VioVau.Core;

/// <summary>Shared JSON settings for game data files and network messages.</summary>
public static class CoreJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T Parse<T>(string json, string what) where T : class =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException($"{what} is empty.");
}
