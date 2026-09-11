using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Mirroring;

/// <summary>
/// TOO LONG FOR A PHONE IS A SPLITTING PROBLEM, NEVER A REASON THE OWNER GETS NOTHING.
///
/// The mirror used to chunk the MARKDOWN at Telegram's 4096 and then render each chunk to HTML,
/// which grows text — every <c>&lt;</c> becomes five characters — so a legal chunk could become an
/// illegal message, Telegram refuses it with a 400, the plain-text fallback re-sends text that is
/// also over the cap, the send throws, the append is never confirmed and the entry is retried into
/// the same refusal for ever. From the owner's side that is a supervisor that answered and was
/// never heard, which is exactly what they reported on 2026-09-07.
/// </summary>
public class OwnerMessageChunkerTests
{
    /// <summary>An ordinary reply is untouched and unnumbered — "(1/1)" on every message is noise.</summary>
    [Fact]
    public void AMessageThatFits_IsSentAsItIs_WithNoNumbering()
    {
        var chunks = OwnerMessage_Chunker.Chunk_ForOwner("🔴 Sup: done — branch wf-perf ready to merge");

        Assert.Single(chunks);
        Assert.Equal("🔴 Sup: done — branch wf-perf ready to merge", chunks[0]);
    }

    [Fact]
    public void ALongAnswer_IsSplitOnLineBoundaries_AndEveryLineSurvivesInOrder()
    {
        var lines = Enumerable.Range(1, 400).Select(i => $"line {i} — something the supervisor had to say about it").ToList();
        var text = string.Join('\n', lines);

        var chunks = OwnerMessage_Chunker.Chunk_ForOwner(text);

        Assert.True(chunks.Count >= 3, $"a {text.Length}-character answer became {chunks.Count} message(s)");

        var rejoined = string.Join('\n', chunks.Select(Strip_Marker));

        foreach (var line in lines)
            Assert.Contains(line, rejoined);

        Assert.Equal(lines, rejoined.Split('\n').Where(line => line.Length > 0).ToList());
    }

    /// <summary>Numbered so several messages in a row read as one answer rather than as a glitch.</summary>
    [Fact]
    public void EveryPieceOfASplitMessage_SaysWhichPieceItIs()
    {
        var chunks = OwnerMessage_Chunker.Chunk_ForOwner(
            string.Join('\n', Enumerable.Range(1, 400).Select(i => $"line {i} — something the supervisor had to say about it")));

        Assert.True(chunks.Count > 1, "the fixture no longer splits, so this asserts nothing");

        for (var i = 0; i < chunks.Count; i++)
            Assert.StartsWith(OwnerMessage_Chunker.Build_Marker(i + 1, chunks.Count), chunks[i]);
    }

    /// <summary>
    /// THE ONE THE OLD CHUNKER GOT WRONG, WITH THE MEASUREMENT CORRECTED (brief F4, 2026-09-10).
    ///
    /// <para>
    /// The property is unchanged and still the point: text that is almost all escapable characters
    /// must not produce a piece Telegram refuses, because the plain-text fallback re-sends the same
    /// text into the same refusal and the entry wedges.
    /// </para>
    /// <para>
    /// WHAT CHANGED IS WHICH READING OF "THE HTML" IS THE TRUE ONE. This case used to assert
    /// <c>Render(chunk).Length</c> — the raw markup — and its old summary said "Telegram counts the
    /// HTML". Telegram's own wording is "1-4096 characters AFTER ENTITIES PARSING" [documented], so
    /// the tags and the <c>&amp;amp;</c> expansions do not count. Asserting the raw length was
    /// asserting a stricter rule than Telegram's, which is why long formatted reports arrived cut
    /// into numbered pieces that would have fitted whole.
    /// </para>
    /// <para>
    /// Both readings are still asserted, because both still matter: the MARKDOWN length is what the
    /// plain-text fallback sends, and the PARSED length is what the HTML send is measured against.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryPiece_FitsTelegramsCap_MeasuredAfterEntityParsing()
    {
        var text = string.Join('\n', Enumerable.Range(1, 600).Select(i => $"<{i}> & <{i}> & <{i}> & <{i}> & <{i}>"));

        var chunks = OwnerMessage_Chunker.Chunk_ForOwner(text);

        foreach (var chunk in chunks)
        {
            Assert.True(
                chunk.Length <= TelegramMessage_Chunker.TELEGRAM_MAX_MESSAGE_LENGTH,
                $"a chunk of {chunk.Length} markdown characters would be refused by the plain-text fallback");

            var parsed = TelegramText_Ruler.Count_AfterEntityParsing(TelegramHtml_Renderer.Render(chunk));

            Assert.True(
                parsed <= TelegramMessage_Chunker.TELEGRAM_MAX_MESSAGE_LENGTH,
                $"a chunk is {parsed} characters after entity parsing — Telegram refuses it, and the plain-text fallback is what the owner's message was already lost to");
        }
    }

    /// <summary>
    /// AND THE CORRECTION HAS TO BUY SOMETHING, or it is a loosened assertion and nothing else.
    ///
    /// A page of heavily formatted markdown — bold, headings, code spans, links — that fits inside
    /// Telegram's real cap must arrive as ONE message. Under the old raw-markup measurement the
    /// same text was split, and the owner read the second piece starting mid-sentence as a glitch.
    /// </summary>
    [Fact]
    public void AHeavilyFormattedEntryThatReallyFits_IsNotSplit()
    {
        var paragraph = "## What I did\n\n**Merged** `FIN-D-293` into `staging` — the [run](https://example.internal/ci/12345) is green & the diff is small.\n";
        var text = string.Concat(Enumerable.Repeat(paragraph, 25));

        // The premise: it is over the cap when the MARKUP is counted, and under it when Telegram's
        // own measurement is. Without this the case could pass for the boring reason.
        var rendered = TelegramHtml_Renderer.Render(text);
        Assert.True(rendered.Length > TelegramMessage_Chunker.TELEGRAM_MAX_MESSAGE_LENGTH,
            $"the fixture no longer exercises the correction: {rendered.Length} characters of markup");
        Assert.True(TelegramText_Ruler.Count_AfterEntityParsing(rendered) <= TelegramMessage_Chunker.TELEGRAM_MAX_MESSAGE_LENGTH,
            $"the fixture is genuinely too long: {TelegramText_Ruler.Count_AfterEntityParsing(rendered)} after parsing");

        var chunks = OwnerMessage_Chunker.Chunk_ForOwner(text);

        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    /// <summary>A single line longer than a whole message is still delivered, hard-split, never dropped.</summary>
    [Fact]
    public void AWallWithNoLineBreaks_IsStillDelivered()
    {
        var text = new string('x', 12_000);

        var chunks = OwnerMessage_Chunker.Chunk_ForOwner(text);

        Assert.True(chunks.Count >= 3);
        Assert.Equal(text, string.Concat(chunks.Select(Strip_Marker)));
    }

    static string Strip_Marker(string chunk)
    {
        var close = chunk.IndexOf(") ", StringComparison.Ordinal);

        return chunk.StartsWith('(') && close > 0 ? chunk[(close + 2)..] : chunk;
    }
}
