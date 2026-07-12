using System.Text.Json;
using System.Text.Json.Serialization;

namespace JTalk.Config;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JTalkConfig))]
public sealed partial class JTalkJsonContext : JsonSerializerContext
{
}
