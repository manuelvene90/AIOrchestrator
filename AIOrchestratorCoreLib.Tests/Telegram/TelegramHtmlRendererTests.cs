using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The renderer that turned an owner's phone from `**GOAL 2: V1 LIVE**` into GOAL 2: V1 LIVE.
///
/// <para>
/// Two properties are asserted, and the second one is the one that keeps a message alive: every
/// construct renders, and NOTHING ELSE produces a tag. Telegram refuses a message whose entities do
/// not parse, so a renderer that emits an unbalanced tag on some sentence an agent happens to write
/// loses that sentence entirely — which is why the unbalanced-marker and snake_case cases below are
/// not tidiness, they are the safety property.
/// </para>
/// </summary>
public class TelegramHtmlRendererTests
{
    const string MOCKUP_MESSAGE = """
        🔴 Sup: two layouts for the settings window — which one?

        ```
        +----------+-------------+
        | rail     | content     |
        +----------+-------------+
        ```
        OPTION: rail on the left
        """;

    [Fact]
    public void Render_TheEntryTheOwnerReportedFromTheirPhone_ArrivesFormatted()
    {
        // Verbatim from the 2026-09-07 report: bold heading, then a bold-label bullet.
        var html = TelegramHtml_Renderer.Render("**GOAL 2: V1 LIVE**\n- **Milestone:** 11 aperte");

        Assert.Equal("<b>GOAL 2: V1 LIVE</b>\n• <b>Milestone:</b> 11 aperte", html);
    }

    [Theory]
    [InlineData("**bold**", "<b>bold</b>")]
    [InlineData("__bold__", "<b>bold</b>")]
    [InlineData("*italic*", "<i>italic</i>")]
    [InlineData("_italic_", "<i>italic</i>")]
    [InlineData("~~gone~~", "<s>gone</s>")]
    [InlineData("***both***", "<b><i>both</i></b>")]
    [InlineData("`code`", "<code>code</code>")]
    [InlineData("a **b** c", "a <b>b</b> c")]
    public void Render_EachInlineConstruct_BecomesItsTelegramTag(string markdown, string expected)
    {
        Assert.Equal(expected, TelegramHtml_Renderer.Render(markdown));
    }

    [Theory]
    [InlineData("# Title", "<b>Title</b>")]
    [InlineData("###### Deep", "<b>Deep</b>")]
    [InlineData("## **Already bold**", "<b><b>Already bold</b></b>")]
    public void Render_AHeading_BecomesABoldLine_BecauseTelegramHasNoHeading(string markdown, string expected)
    {
        Assert.Equal(expected, TelegramHtml_Renderer.Render(markdown));
    }

    [Fact]
    public void Render_ListMarkers_KeepTheShapeTelegramHasNoTagFor()
    {
        var html = TelegramHtml_Renderer.Render("- one\n* two\n1. three\n2. four");

        Assert.Equal("• one\n• two\n1. three\n2. four", html);
    }

    [Fact]
    public void Render_ANestedListKeepsItsIndent_SoTheHierarchyIsStillReadable()
    {
        Assert.Equal("• top\n  • under it", TelegramHtml_Renderer.Render("- top\n  - under it"));
    }

    [Fact]
    public void Render_EscapesTheThreeCharactersTelegramReadsAsMarkup()
    {
        Assert.Equal("a &lt; b &amp; c &gt; d", TelegramHtml_Renderer.Render("a < b & c > d"));
    }

    [Fact]
    public void Render_EscapesInsideCodeToo_BecauseTelegramParsesTheContentOfEveryTag()
    {
        // The one that would otherwise refuse the whole message: an agent pasting a C# generic.
        Assert.Equal(
            "<code>List&lt;string&gt; &amp; more</code>",
            TelegramHtml_Renderer.Render("`List<string> & more`"));
    }

    [Fact]
    public void Render_MarkersInsideCode_AreNotInterpreted()
    {
        Assert.Equal("<code>**not bold**</code>", TelegramHtml_Renderer.Render("`**not bold**`"));
    }

    [Theory]
    [InlineData("**unclosed bold", "**unclosed bold")]
    [InlineData("a lone * asterisk", "a lone * asterisk")]
    [InlineData("2 * 3 * 4 = 24", "2 * 3 * 4 = 24")]
    [InlineData("****", "****")]
    [InlineData("`unclosed code", "`unclosed code")]
    [InlineData("[label](not a url)", "[label](not a url)")]
    [InlineData("[label](javascript:alert(1))", "[label](javascript:alert(1))")]
    public void Render_AnUnbalancedOrUnsupportedMarker_StaysLiteral(string markdown, string expected)
    {
        // NOT A COSMETIC RULE. A marker that opened a tag with no closer is exactly the message
        // Telegram refuses to parse, and a refused message is a message the owner never reads.
        Assert.Equal(expected, TelegramHtml_Renderer.Render(markdown));
    }

    [Theory]
    [InlineData("PlanLedger_Parser and PlanLedger_Sections")]
    [InlineData("call Build_PerSourceTotals then Build_OrchestrationTotals")]
    [InlineData("snake_case_all_the_way")]
    public void Render_SnakeCaseIdentifiers_AreNeverItalic(string markdown)
    {
        // Agents write identifiers constantly. Reading the underscores as emphasis would italicise
        // half of every technical message and — with an odd count — leave a tag hanging open.
        Assert.Equal(markdown, TelegramHtml_Renderer.Render(markdown));
    }

    [Fact]
    public void Render_AnUnderscoreItalicNextToPunctuation_StillRenders()
    {
        Assert.Equal("(<i>maybe</i>)", TelegramHtml_Renderer.Render("(_maybe_)"));
    }

    [Fact]
    public void Render_AFencedBlock_BecomesPre_AndKeepsTheDrawingCharacterForCharacter()
    {
        var html = TelegramHtml_Renderer.Render(MOCKUP_MESSAGE);

        Assert.Contains("<pre>+----------+-------------+\n| rail     | content     |\n+----------+-------------+</pre>", html);
        Assert.Contains("which one?", html);
        Assert.Contains("OPTION: rail on the left", html);
    }

    [Fact]
    public void Render_AFencedBlock_DropsTheLanguage_AndEscapesItsContent()
    {
        var html = TelegramHtml_Renderer.Render("```csharp\nif (a < b) { }\n```");

        Assert.Equal("<pre>if (a &lt; b) { }</pre>", html);
    }

    [Fact]
    public void Render_AFenceCutByChunking_IsStillClosed()
    {
        // A mockup longer than 4096 characters ALWAYS arrives split like this. An unclosed <pre>
        // would be refused by Telegram, which is the message-losing bug this guards.
        var html = TelegramHtml_Renderer.Render("intro\n```\nhalf a drawing");

        Assert.Equal("intro\n<pre>half a drawing</pre>", html);
    }

    [Fact]
    public void Render_AnHttpLink_BecomesAnAnchor()
    {
        Assert.Equal(
            "see <a href=\"https://example.com/a?x=1&amp;y=2\">the PR</a>",
            TelegramHtml_Renderer.Render("see [the PR](https://example.com/a?x=1&y=2)"));
    }

    [Fact]
    public void Render_ABlockquote_BecomesOneBlockquote_BecauseTelegramRefusesNestedOnes()
    {
        var html = TelegramHtml_Renderer.Render("> he said\n> two lines\nthen prose");

        Assert.Equal("<blockquote>he said\ntwo lines</blockquote>\nthen prose", html);
    }

    [Fact]
    public void Render_PreservesBlankLines_SoParagraphsStayParagraphs()
    {
        Assert.Equal("one\n\ntwo", TelegramHtml_Renderer.Render("one\n\ntwo"));
    }

    [Fact]
    public void Render_EveryOpenedTagIsClosed_AcrossAMessyRealisticEntry()
    {
        var messy = "🔴 Sup: **status** _for_ a < b, `x_y`, [link](https://x.io), 2 * 3, "
            + "unmatched ** and __ and ~~ and `, snake_case_name\n"
            + "- **done:** merged\n"
            + "> quoted _line_\n"
            + "```\ndraw < ing\n```";

        var html = TelegramHtml_Renderer.Render(messy);

        Assert.Equal(Count_Occurrences(html, "<b>"), Count_Occurrences(html, "</b>"));
        Assert.Equal(Count_Occurrences(html, "<i>"), Count_Occurrences(html, "</i>"));
        Assert.Equal(Count_Occurrences(html, "<s>"), Count_Occurrences(html, "</s>"));
        Assert.Equal(Count_Occurrences(html, "<code>"), Count_Occurrences(html, "</code>"));
        Assert.Equal(Count_Occurrences(html, "<pre>"), Count_Occurrences(html, "</pre>"));
        Assert.Equal(Count_Occurrences(html, "<blockquote>"), Count_Occurrences(html, "</blockquote>"));
        Assert.Equal(Count_Occurrences(html, "<a href="), Count_Occurrences(html, "</a>"));
    }

    [Fact]
    public void Chunk_ThenRender_NeverSplitsInsideATag()
    {
        // THE ORDER IS THE POINT: the bridge chunks the MARKDOWN and renders each chunk, so a
        // boundary can only ever fall between lines. Rendering first and splitting the HTML would
        // cut a <b> in half and lose the message to a parse refusal.
        var entry = string.Join('\n', Enumerable.Range(0, 400).Select(i => $"- **item {i}:** a reasonably long line of prose about it"));

        var chunks = TelegramMessage_Chunker.Chunk(entry);

        Assert.True(chunks.Count > 1);

        foreach (var chunk in chunks)
        {
            var html = TelegramHtml_Renderer.Render(chunk);

            Assert.Equal(Count_Occurrences(html, "<b>"), Count_Occurrences(html, "</b>"));
            Assert.DoesNotContain("<b</b>", html);
            Assert.StartsWith("• ", html);
        }
    }

    [Theory]
    [InlineData("**`x`**")]
    [InlineData("*`x`*")]
    [InlineData("~~`x`~~")]
    public void Render_ACodeSpanWrappedInEmphasisMarkers_NeverNestsCodeInsideTheEmphasisTag(string markdown)
    {
        // Bot API: bold/italic/underline/strikethrough/spoiler "can contain and can be part of any
        // other entities, EXCEPT pre and code" — <b><code> (either direction) earns a 400, and
        // TelegramProse_Sender then falls back to plain text for the whole message. The wrapping
        // tag is dropped rather than nested; the code span itself still renders.
        var html = TelegramHtml_Renderer.Render(markdown);

        Assert.Equal("<code>x</code>", html);
        Assert.DoesNotContain("<b><code", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<code><b", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<i><code", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<code><i", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<s><code", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<code><s", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// This renderer has no underline or spoiler markup at all — <c>__</c> maps to <c>&lt;b&gt;</c>
    /// (a second bold spelling), not <c>&lt;u&gt;</c>, and no spoiler marker is recognised. So the
    /// three cases above are the whole set; there is nothing further to guard here.
    /// </summary>
    [Fact]
    public void Render_DoubleUnderscoreWrappingACodeSpan_AlsoDropsTheWrappingTag_BecauseItIsBoldNotUnderline()
    {
        Assert.Equal("<code>x</code>", TelegramHtml_Renderer.Render("__`x`__"));
    }

    [Fact]
    public void Render_ACodeSpanBesideEmphasis_RendersBoth_BecauseTheCodeIsNotNestedInsideTheRun()
    {
        var html = TelegramHtml_Renderer.Render("**bold** `code` **more**");

        Assert.Equal("<b>bold</b> <code>code</code> <b>more</b>", html);
        Assert.DoesNotContain("<b><code", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<code><b", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_NestedBoldInsideItalic_IsUnchanged_WhenNoCodeIsInvolved()
    {
        Assert.Equal("<i>text <b>bold</b> text</i>", TelegramHtml_Renderer.Render("*text __bold__ text*"));
    }

    [Fact]
    public void Render_AFencedBlockContainingWhatLooksLikeBoldMarkers_IsUntouched()
    {
        Assert.Equal("<pre>**not bold**</pre>", TelegramHtml_Renderer.Render("```\n**not bold**\n```"));
    }

    static int Count_Occurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
