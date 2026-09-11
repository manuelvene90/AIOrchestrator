using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE TWO THINGS THE APP COUNTED ITS OWN WAY AND TELEGRAM COUNTS ANOTHER — brief F4.
///
/// Telegram's caps are "characters after entities parsing" [documented], in UTF-16 code units. The
/// app measured raw markup for the first and ignored the second entirely.
/// </summary>
public class TelegramTextRulerTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("plain", 5)]
    [InlineData("<b>bold</b>", 4)]
    [InlineData("<blockquote expandable>x</blockquote>", 1)]
    [InlineData("&amp;", 1)]
    [InlineData("&lt;&gt;", 2)]
    [InlineData("a &amp; b", 5)]
    [InlineData("<a href=\"https://example.internal/a/very/long/url\">link</a>", 4)]
    [InlineData("<pre>code &lt;here&gt;</pre>", 11)]
    public void TagsAndEntities_CostWhatTheOwnerActuallySees(string html, int expected)
    {
        Assert.Equal(expected, TelegramText_Ruler.Count_AfterEntityParsing(html));
    }

    /// <summary>A bare ampersand that is not an entity is one character, not nothing.</summary>
    [Fact]
    public void ABareAmpersand_IsOneCharacter()
    {
        Assert.Equal(3, TelegramText_Ruler.Count_AfterEntityParsing("a&b"));
    }

    /// <summary>
    /// DEFENCE, NOT A CASE — the renderer escapes every literal '&lt;' it emits, so an unclosed one
    /// cannot reach here. It must still not run off the end or under-count.
    /// </summary>
    [Fact]
    public void AnUnclosedTag_IsCountedConservatively_NeverShorterThanTheTruth()
    {
        // "ab" (2) plus the whole unterminated remainder "<cdef" (5) = 7 — an over-count, which is
        // the safe direction: it can only make the app split sooner, never send something too long.
        Assert.Equal(7, TelegramText_Ruler.Count_AfterEntityParsing("ab<cdef"));
    }

    /// <summary>
    /// THE PROPERTY THAT MAKES THE CORRECTION SAFE: parsed length can never EXCEED raw length, so
    /// swapping the measurement can only ever split less, never send something too long.
    /// </summary>
    [Fact]
    public void ParsedLength_IsNeverGreaterThanRawLength()
    {
        string[] samples =
        [
            "**bold** and `code` and > a quote",
            "## Heading\n- one\n- two\n\n```\nfenced & <escaped>\n```",
            "a & b < c > d",
            "[link](https://example.internal/x?y=1&z=2)",
            "plain text with an emoji 🎯 and more",
        ];

        foreach (var sample in samples)
        {
            var rendered = TelegramHtml_Renderer.Render(sample);

            Assert.True(
                TelegramText_Ruler.Count_AfterEntityParsing(rendered) <= rendered.Length,
                $"parsed length exceeded raw length for '{sample}' — {TelegramText_Ruler.Describe_Length(rendered)}");
        }
    }

    // ---- the cut ----

    [Fact]
    public void ACutBetweenTheTwoHalvesOfAnEmoji_IsMovedBackByOne()
    {
        var text = "ab🎯cd";

        // 🎯 occupies units 2 and 3; a cut at 3 would sever it.
        Assert.Equal(2, TelegramText_Ruler.Move_OffSurrogatePair(text, 3));

        // Cuts that are already whole are left exactly where they were.
        Assert.Equal(2, TelegramText_Ruler.Move_OffSurrogatePair(text, 2));
        Assert.Equal(4, TelegramText_Ruler.Move_OffSurrogatePair(text, 4));
    }

    [Fact]
    public void MoveOffSurrogatePair_NeverGoesOutOfRange()
    {
        var text = "ab🎯cd";

        Assert.Equal(0, TelegramText_Ruler.Move_OffSurrogatePair(text, 0));
        Assert.Equal(text.Length, TelegramText_Ruler.Move_OffSurrogatePair(text, text.Length));
        Assert.Equal(-5, TelegramText_Ruler.Move_OffSurrogatePair(text, -5));
    }

    /// <summary>
    /// THE FAILURE THE GUARD EXISTS FOR, WRITTEN AS ITS OWN ASSERTION: a chunker without it produces
    /// a piece ending in half an emoji, which Telegram answers with a 400 — and the plain-text
    /// fallback re-sends the same broken text into the same 400, so the entry never arrives at all.
    /// </summary>
    [Fact]
    public void AHardSplitLandingInsideAnEmoji_LeavesNoLoneSurrogate()
    {
        // Every unit is meaningful. "ab" is 2 units and each 🎯 is 2, so unit 100 is the HIGH half of
        // an emoji and unit 101 its LOW half — a hard split at 101 severs exactly that pair. Sixty
        // emoji rather than the minimum, so the text is comfortably longer than the budget and the
        // chunker really reaches its hard-split branch.
        var text = "ab" + string.Concat(Enumerable.Repeat("🎯", 60));

        Assert.True(char.IsHighSurrogate(text[100]), "the fixture no longer straddles the split point");
        Assert.True(char.IsLowSurrogate(text[101]), "the fixture no longer straddles the split point");

        var chunks = TelegramMessage_Chunker.Chunk(text, maxLength: 101);

        Assert.True(chunks.Count > 1, "the fixture no longer splits");

        foreach (var chunk in chunks)
            Assert.False(TelegramText_Ruler.Has_LoneSurrogate(chunk), $"a chunk ends or starts mid-emoji: {TelegramText_Ruler.Describe_Length(chunk)}");

        Assert.Equal(text, string.Concat(chunks));
    }

    /// <summary>A wall of emoji with no line breaks at all — every split is a hard one.</summary>
    [Fact]
    public void AWallOfEmoji_IsSplitWithoutBreakingASingleOne()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 3_000));

        var chunks = TelegramMessage_Chunker.Chunk(text);

        Assert.True(chunks.Count > 1);

        foreach (var chunk in chunks)
            Assert.False(TelegramText_Ruler.Has_LoneSurrogate(chunk));

        Assert.Equal(text, string.Concat(chunks));
    }

    /// <summary>
    /// The caption path halves rather than splits, and it can halve straight through an emoji. A
    /// refused caption fails the WHOLE sendDocument — the owner loses the file, not the sentence.
    /// </summary>
    [Fact]
    public void ACaptionTrimmedThroughAnEmoji_LeavesNoLoneSurrogate()
    {
        var caption = OwnerDocument_Builder.Build_CaptionHtml(string.Concat(Enumerable.Repeat("🎯", 4_000)) + "\nbody");

        Assert.False(TelegramText_Ruler.Has_LoneSurrogate(caption), $"the caption ends mid-emoji: {TelegramText_Ruler.Describe_Length(caption)}");
        Assert.True(TelegramText_Ruler.Count_AfterEntityParsing(caption) <= OwnerDocument_Builder.CAPTION_LIMIT);
    }

    [Fact]
    public void Truncate_WholeCharacters_NeverSplitsAPair_AndHandlesTheDegenerateCases()
    {
        Assert.Equal("ab", TelegramText_Ruler.Truncate_WholeCharacters("ab🎯cd", 3));
        Assert.Equal("ab🎯", TelegramText_Ruler.Truncate_WholeCharacters("ab🎯cd", 4));
        Assert.Equal("ab🎯cd", TelegramText_Ruler.Truncate_WholeCharacters("ab🎯cd", 99));
        Assert.Equal("", TelegramText_Ruler.Truncate_WholeCharacters("🎯", 1));
        Assert.Equal("", TelegramText_Ruler.Truncate_WholeCharacters("abc", 0));
    }

    [Fact]
    public void Has_LoneSurrogate_SeesBothHalvesGoingMissing()
    {
        Assert.False(TelegramText_Ruler.Has_LoneSurrogate("ab🎯cd"));
        Assert.True(TelegramText_Ruler.Has_LoneSurrogate("ab\ud83c"));
        Assert.True(TelegramText_Ruler.Has_LoneSurrogate("\udfafcd"));
    }
}
