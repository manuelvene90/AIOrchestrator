using AIOrchestratorCoreLib.Mirroring;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Mirroring;

/// <summary>
/// A LONG ENTRY IS FOLDED, NOT SHORTENED — and a short one is not touched at all.
///
/// <para>
/// The fold trades scrolling for one tap: the opening in the clear, the rest inside a collapsed
/// quotation. Everything below is written against the two ways that could go wrong. It could show
/// LESS than the entry said, which would make the bridge a thing that edits the supervisor; or it
/// could produce a message Telegram refuses, which — through the plain-text fallback that would then
/// also be over the cap — is the wedge <see cref="OwnerMessage_Chunker"/> was written to close.
/// </para>
/// </summary>
public class OwnerMessageFolderTests
{
    const int MAX = TelegramMessage_Chunker.TELEGRAM_MAX_MESSAGE_LENGTH;

    /// <summary>
    /// THE REGRESSION GUARD FOR EVERY ORDINARY MESSAGE. The owner reads short entries all day; a fold
    /// on one of them would hide half a reply behind a "show more" for no gain.
    /// </summary>
    [Fact]
    public void AFourHundredCharacterEntry_IsSentExactlyAsBefore_WithNoFold()
    {
        var text = Build_Prose(lines: 5, lineLength: 78);

        Assert.InRange(text.Length, 380, 420);

        var pieces = OwnerMessage_Folder.Fold_ForOwner(text);

        Assert.Single(pieces);
        Assert.Equal(text, pieces[0].Markdown);
        Assert.Equal(TelegramHtml_Renderer.Render(text), pieces[0].Html);
        Assert.DoesNotContain("blockquote", pieces[0].Html, StringComparison.Ordinal);
    }

    [Fact]
    public void AShortEntry_ReadsIdenticallyToTheChunker_SoNothingAboutItChanged()
    {
        const string TEXT = "🔴 Sup: done — branch wf-perf ready to merge";

        var folded = OwnerMessage_Folder.Fold_ForOwner(TEXT);
        var chunked = OwnerMessage_Chunker.Chunk_ForOwner(TEXT);

        Assert.Equal(chunked, [.. folded.Select(piece => piece.Markdown)]);
    }

    [Fact]
    public void ATwoThousandCharacterEntry_ArrivesAsOneMessage_ItsOpeningInTheClearAndTheRestFolded()
    {
        var text = "**GOAL 2: V1 LIVE**\n" + Build_Prose(lines: 25, lineLength: 78);

        Assert.InRange(text.Length, 1_900, 2_100);

        var piece = Assert.Single(OwnerMessage_Folder.Fold_ForOwner(text));

        // TWO LINES IN THE CLEAR when the entry opens on a run of lines with no paragraph break —
        // agents lead with a subject and a verdict, and that pair is what the owner reads to decide.
        Assert.StartsWith("<b>GOAL 2: V1 LIVE</b>\n01 ", piece.Html, StringComparison.Ordinal);
        Assert.Contains("\n<blockquote expandable>02 ", piece.Html, StringComparison.Ordinal);
        Assert.EndsWith("</blockquote>", piece.Html, StringComparison.Ordinal);

        // ONE fold, never two: a second blockquote inside the first is the nesting Telegram refuses.
        Assert.Equal(1, Count_Occurrences(piece.Html, "<blockquote"));
    }

    /// <summary>
    /// THE OPENING IS WHAT THE OWNER READS TO DECIDE, so it must be OUTSIDE the quotation. A fold that
    /// swallowed the subject line would show a phone notification saying nothing at all.
    /// </summary>
    [Fact]
    public void TheOpeningLine_IsOutsideTheQuotation_AndTheRestIsInside()
    {
        var text = "SUBJECT LINE\nfirst body line\n" + Build_Prose(lines: 20, lineLength: 78);

        var piece = Assert.Single(OwnerMessage_Folder.Fold_ForOwner(text));

        var quoteStart = piece.Html.IndexOf("<blockquote expandable>", StringComparison.Ordinal);

        Assert.True(quoteStart > 0, piece.Html);
        Assert.Contains("SUBJECT LINE", piece.Html[..quoteStart], StringComparison.Ordinal);
        Assert.DoesNotContain("SUBJECT LINE", piece.Html[quoteStart..], StringComparison.Ordinal);
    }

    /// <summary>The first paragraph wins when it is shorter than the two-line cap.</summary>
    [Fact]
    public void AOneLineFirstParagraph_IsTheWholeHead_AndTheBlankLineUnderItIsNotFolded()
    {
        var (head, body) = OwnerMessage_Folder.Split_Head("subject\n\nand then the body\nand more of it");

        Assert.Equal("subject", head);
        Assert.Equal("and then the body\nand more of it", body);
    }

    [Fact]
    public void ARunOfLinesWithNoParagraphBreak_KeepsTwoInTheClear()
    {
        var (head, body) = OwnerMessage_Folder.Split_Head("one\ntwo\nthree\nfour");

        Assert.Equal("one\ntwo", head);
        Assert.Equal("three\nfour", body);
    }

    /// <summary>
    /// THE ACCEPTANCE CASE. Nine thousand characters is over the 4096 cap twice over, so the fold and
    /// the chunker have to cooperate: several messages, each one a fold, and between them every line
    /// the supervisor wrote.
    /// </summary>
    [Fact]
    public void ANineThousandCharacterEntry_IsFoldedAndThenChunked_WithEveryLineStillThere()
    {
        var lines = Enumerable.Range(1, 130)
            .Select(i => $"finding {i:D3} — the measurement, what it means, and what it costs to leave alone")
            .ToList();

        var text = "THE SWEEP\n" + string.Join('\n', lines);

        Assert.True(text.Length > 9_000, $"the fixture is only {text.Length} characters");

        var pieces = OwnerMessage_Folder.Fold_ForOwner(text);

        Assert.True(pieces.Count >= 3, $"a {text.Length}-character entry became {pieces.Count} message(s)");

        foreach (var piece in pieces)
        {
            Assert.Contains("<blockquote expandable>", piece.Html, StringComparison.Ordinal);
            Assert.Equal(1, Count_Occurrences(piece.Html, "<blockquote"));
        }

        // NOTHING LOST: every line the entry carried is in one of the pieces, and in order.
        var arrivalOrder = lines
            .Select(line => Index_OfPieceContaining(pieces, line))
            .ToList();

        Assert.DoesNotContain(-1, arrivalOrder);
        Assert.Equal(arrivalOrder.OrderBy(index => index).ToList(), arrivalOrder);
    }

    /// <summary>
    /// EVERY PIECE FITS, MEASURED BOTH WAYS. The HTML is what Telegram counts; the Markdown is what
    /// the plain-text fallback re-sends when Telegram refuses that HTML, and a fallback over the cap
    /// throws, leaves the append unconfirmed and retries the entry into the same refusal for ever.
    /// </summary>
    [Fact]
    public void EveryPieceOfALongEntry_FitsTelegramsCap_AsHtmlANDAsThePlainFallback()
    {
        var text = "THE SWEEP\n" + string.Join('\n', Enumerable.Range(1, 400)
            .Select(i => $"line {i} — something the supervisor had to say about it"));

        foreach (var piece in OwnerMessage_Folder.Fold_ForOwner(text))
        {
            Assert.True(piece.Html.Length <= MAX, $"a piece renders to {piece.Html.Length} characters of HTML");
            Assert.True(piece.Markdown.Length <= MAX, $"a piece's plain fallback is {piece.Markdown.Length} characters");
        }
    }

    /// <summary>
    /// Text that is almost entirely escapable characters is legal Markdown at 4096 and illegal HTML at
    /// 4096 — the case the pre-1i chunker got wrong, re-asked with the fold's own overhead added.
    /// </summary>
    [Fact]
    public void AnEntryOfMostlyEscapableCharacters_StillFits_MEASUREDONTHERENDEREDHTML()
    {
        var text = "THE DIFF\n" + string.Join('\n', Enumerable.Range(1, 600).Select(i => $"<{i}> & <{i}> & <{i}> & <{i}> & <{i}>"));

        foreach (var piece in OwnerMessage_Folder.Fold_ForOwner(text))
        {
            Assert.True(piece.Html.Length <= MAX, $"a piece renders to {piece.Html.Length} characters of HTML");
            Assert.True(piece.Markdown.Length <= MAX, $"a piece's plain fallback is {piece.Markdown.Length} characters");
        }
    }

    /// <summary>
    /// Numbered so several messages in a row read as one answer, and the opening rides on the FIRST
    /// only — repeating the subject on each would read as three separate statements about one thing.
    /// </summary>
    [Fact]
    public void EveryPieceSaysWhichPieceItIs_AndOnlyTheFirstCarriesTheOpening()
    {
        var text = "THE SWEEP\n" + string.Join('\n', Enumerable.Range(1, 400)
            .Select(i => $"line {i} — something the supervisor had to say about it"));

        var pieces = OwnerMessage_Folder.Fold_ForOwner(text);

        Assert.True(pieces.Count > 1, "the fixture no longer splits, so this asserts nothing");

        for (var i = 0; i < pieces.Count; i++)
        {
            Assert.StartsWith(
                OwnerMessage_Chunker.Build_Marker(i + 1, pieces.Count).Trim(),
                pieces[i].Markdown,
                StringComparison.Ordinal);
        }

        Assert.Contains("THE SWEEP", pieces[0].Markdown, StringComparison.Ordinal);

        // A LATER PIECE IS ENTIRELY A FOLD: its head line is its own number and nothing else.
        Assert.StartsWith(
            OwnerMessage_Chunker.Build_Marker(2, pieces.Count).Trim() + "\n",
            pieces[1].Markdown,
            StringComparison.Ordinal);

        var secondHtml = pieces[1].Html;

        Assert.StartsWith(
            OwnerMessage_Chunker.Build_Marker(2, pieces.Count).Trim() + "\n<blockquote expandable>",
            secondHtml,
            StringComparison.Ordinal);
    }

    /// <summary>The owner's own off switch: the key set to 0 must give back exactly the pre-fold shape.</summary>
    [Fact]
    public void AZeroThreshold_DisablesTheFold_AndTheEntryArrivesExactlyAsTheChunkerSendsIt()
    {
        var text = "THE SWEEP\n" + string.Join('\n', Enumerable.Range(1, 400)
            .Select(i => $"line {i} — something the supervisor had to say about it"));

        var pieces = OwnerMessage_Folder.Fold_ForOwner(text, foldThreshold: 0);

        Assert.Equal(OwnerMessage_Chunker.Chunk_ForOwner(text), [.. pieces.Select(piece => piece.Markdown)]);

        foreach (var piece in pieces)
            Assert.DoesNotContain("blockquote", piece.Html, StringComparison.Ordinal);
    }

    /// <summary>
    /// AN ENTRY WITH NO SECOND PART IS NOT FOLDED. One long paragraph wrapped whole in a quotation
    /// would show the owner a "show more" and no message — which is the fold becoming a way to lose one.
    /// </summary>
    [Fact]
    public void AWallWithNoLineBreaks_IsNotFolded_AndIsStillDeliveredWhole()
    {
        var text = new string('x', 12_000);

        var pieces = OwnerMessage_Folder.Fold_ForOwner(text);

        foreach (var piece in pieces)
            Assert.DoesNotContain("blockquote", piece.Html, StringComparison.Ordinal);

        Assert.Equal(text, string.Concat(pieces.Select(piece => Strip_Marker(piece.Markdown))));
    }

    /// <summary>
    /// An opening too long to leave room for a body degrades to the unfolded path rather than
    /// producing pieces the fold cannot shrink under the cap.
    /// </summary>
    [Fact]
    public void AnOpeningLineLongerThanAMessage_DegradesToTheUnfoldedPath()
    {
        var text = new string('y', 5_000) + "\nand then a body line\nand another";

        foreach (var piece in OwnerMessage_Folder.Fold_ForOwner(text))
            Assert.DoesNotContain("blockquote", piece.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyEntry_ProducesNothing()
    {
        Assert.Empty(OwnerMessage_Folder.Fold_ForOwner(string.Empty));
    }

    /// <summary>
    /// A fenced mockup that lands inside the fold keeps its monospacing as <c>&lt;code&gt;</c> lines
    /// and never as the <c>&lt;pre&gt;</c> Telegram refuses in a quotation.
    /// </summary>
    [Fact]
    public void AFencedMockupInsideTheFold_ArrivesAsCodeLines_NeverAsPre()
    {
        var text = "THE MOCKUP\nhere is the shape:\n```\n+----+\n| ok |\n+----+\n```\n" + Build_Prose(lines: 15, lineLength: 78);

        var piece = Assert.Single(OwnerMessage_Folder.Fold_ForOwner(text));

        Assert.DoesNotContain("<pre>", piece.Html, StringComparison.Ordinal);
        Assert.Contains("<code>| ok |</code>", piece.Html, StringComparison.Ordinal);
    }

    static string Build_Prose(int lines, int lineLength)
    {
        return string.Join('\n', Enumerable.Range(1, lines).Select(i => $"{i:D2} " + new string('a', lineLength - 3)));
    }

    static int Index_OfPieceContaining(IReadOnlyList<(string Markdown, string Html)> pieces, string fragment)
    {
        for (var i = 0; i < pieces.Count; i++)
        {
            if (pieces[i].Html.Contains(fragment, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    static int Count_Occurrences(string text, string fragment)
    {
        var count = 0;
        var position = 0;

        while (true)
        {
            var found = text.IndexOf(fragment, position, StringComparison.Ordinal);

            if (found < 0)
                return count;

            count++;
            position = found + fragment.Length;
        }
    }

    static string Strip_Marker(string markdown)
    {
        var close = markdown.IndexOf(") ", StringComparison.Ordinal);

        return markdown.StartsWith('(') && close > 0 ? markdown[(close + 2)..] : markdown;
    }
}
