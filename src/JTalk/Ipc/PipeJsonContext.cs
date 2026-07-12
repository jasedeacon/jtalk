using System.Text.Json.Serialization;

namespace JTalk.Ipc;

// The pipe protocol is newline-delimited, so this context must stay compact
// (never WriteIndented) — that's why it is separate from the config context.
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PipeRequest))]
[JsonSerializable(typeof(PipeResponse))]
public sealed partial class PipeJsonContext : JsonSerializerContext
{
}
