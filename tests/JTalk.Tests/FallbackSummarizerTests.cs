using JTalk.Summarize;
using Xunit;

namespace JTalk.Tests;

public sealed class FallbackSummarizerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   \t\n  ")]
    public void CleanForLlmReturnsEmptyForBlankInput(string input) =>
        Assert.Equal("", FallbackSummarizer.CleanForLlm(input));

    [Fact]
    public void CleanForLlmReplacesFencedCodeBlocks() =>
        Assert.Equal(
            "Fixed it. (code) Done.",
            FallbackSummarizer.CleanForLlm("Fixed it.\n```csharp\nvar x = 1;\n```\nDone."));

    [Fact]
    public void CleanForLlmReplacesUnterminatedFencedCode() =>
        Assert.Equal(
            "Before (code)",
            FallbackSummarizer.CleanForLlm("Before\n```\nvar x = 1;"));

    [Fact]
    public void CleanForLlmUnwrapsInlineCode() =>
        Assert.Equal(
            "Use dotnet build now.",
            FallbackSummarizer.CleanForLlm("Use `dotnet build` now."));

    [Fact]
    public void CleanForLlmDropsImagesAndKeepsLinkText() =>
        Assert.Equal(
            "See the docs here.",
            FallbackSummarizer.CleanForLlm("See ![diagram](http://x/y.png) [the docs](http://x) here."));

    [Fact]
    public void CleanForLlmStripsHeadingsQuotesAndBullets() =>
        Assert.Equal(
            "Title item quote numbered",
            FallbackSummarizer.CleanForLlm("# Title\n- item\n> quote\n2. numbered"));

    [Fact]
    public void CleanForLlmStripsEmphasisTokens() =>
        Assert.Equal(
            "bold and italic and gone",
            FallbackSummarizer.CleanForLlm("**bold** and _italic_ and ~~gone~~"));

    [Fact]
    public void CleanForLlmDropsTableRows() =>
        Assert.Equal(
            "before after",
            FallbackSummarizer.CleanForLlm("before\n| a | b |\n| 1 | 2 |\nafter"));

    [Fact]
    public void CleanForLlmCollapsesWhitespace() =>
        Assert.Equal(
            "one two three",
            FallbackSummarizer.CleanForLlm("one\t\ttwo\n\n  three  "));

    [Fact]
    public void CleanKeepsAtMostTwoSentences() =>
        Assert.Equal("One. Two.", FallbackSummarizer.Clean("One. Two. Three. Four."));

    [Fact]
    public void CleanStopsBeforeExceedingBudget()
    {
        var first = new string('a', 150) + ".";
        var second = new string('b', 100) + ".";

        Assert.Equal(first, FallbackSummarizer.Clean(first + " " + second));
    }

    [Fact]
    public void CleanHardTruncatesWithEllipsisWhenNoSentenceBoundary()
    {
        var result = FallbackSummarizer.Clean(new string('a', 250));

        Assert.Equal(201, result.Length);
        Assert.EndsWith("…", result, StringComparison.Ordinal);
    }

    [Fact]
    public void CleanReturnsEmptyWhenNothingSurvivesCleanup() =>
        Assert.Equal("", FallbackSummarizer.Clean("**__** ~~ ~~"));
}
