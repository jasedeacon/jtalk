using System.Text.RegularExpressions;

namespace JTalk.Summarize;

/// <summary>
/// No-API summarizer and universal safety net: strips markdown and code, then
/// keeps the first sentence or two (≤ ~200 chars) so it reads well aloud.
/// </summary>
public static partial class FallbackSummarizer
{
    private const int MaxChars = 200;

    /// <summary>Markdown/code cleanup without truncation — input prep for the LLM summarizers.</summary>
    public static string CleanForLlm(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var cleaned = FencedCode().Replace(text, " (code) ");
        cleaned = InlineCode().Replace(cleaned, "$1");
        cleaned = MarkdownImage().Replace(cleaned, "");
        cleaned = MarkdownLink().Replace(cleaned, "$1");
        cleaned = HeadingOrQuoteOrBullet().Replace(cleaned, "");
        cleaned = EmphasisTokens().Replace(cleaned, "");
        cleaned = TableRow().Replace(cleaned, " ");
        return Whitespace().Replace(cleaned, " ").Trim();
    }

    public static string Clean(string text)
    {
        var cleaned = CleanForLlm(text);
        if (cleaned.Length == 0) return "";

        // First 1-2 sentences up to the budget; hard truncate if there is no boundary.
        var result = "";
        foreach (var sentence in SentenceSplit().Split(cleaned))
        {
            if (sentence.Length == 0) continue;
            if (result.Length > 0 && result.Length + sentence.Length + 1 > MaxChars) break;
            result = result.Length == 0 ? sentence : result + " " + sentence;
            if (CountSentences(result) >= 2) break;
        }
        if (result.Length > MaxChars)
            result = result[..MaxChars].TrimEnd() + "…";
        return result;
    }

    private static int CountSentences(string text) =>
        text.Count(c => c is '.' or '!' or '?');

    [GeneratedRegex(@"```[\s\S]*?(```|$)")]
    private static partial Regex FencedCode();

    [GeneratedRegex(@"`([^`]*)`")]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex MarkdownImage();

    [GeneratedRegex(@"^[ \t]*(#{1,6}[ \t]+|>[ \t]?|[-*+][ \t]+|\d+\.[ \t]+)", RegexOptions.Multiline)]
    private static partial Regex HeadingOrQuoteOrBullet();

    [GeneratedRegex(@"(\*\*|__|\*|_|~~)")]
    private static partial Regex EmphasisTokens();

    [GeneratedRegex(@"^\|.*\|[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex TableRow();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[.!?])\s+")]
    private static partial Regex SentenceSplit();
}
