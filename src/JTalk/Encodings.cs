using System.Text;

namespace JTalk;

/// <summary>Shared encodings: config and pipe I/O must never emit a BOM.</summary>
internal static class Encodings
{
    public static readonly UTF8Encoding Utf8NoBom = new(false);
}
