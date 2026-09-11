using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// WHAT MAY AND MAY NOT APPEAR INSIDE A COLLAPSED QUOTATION.
///
/// <para>
/// Telegram allows INLINE entities inside a <c>&lt;blockquote&gt;</c> and refuses two BLOCK ones: a
/// nested blockquote, and <c>&lt;pre&gt;</c>. Emitting either earns a 400, and a 400 on the mirror
/// path costs the owner a message — the exact failure the plain-text fallback was written for and
/// the exact failure this mode exists not to cause. So the fold body degrades those two and keeps
/// everything else.
/// </para>
/// <para>
/// The other half of the subject is that it is ONE renderer: every inline rule here is asserted to
/// behave identically in both modes, because a second copy of the emphasis scanner is how the two
/// would drift (CLAUDE.md decision 12).
/// </para>
/// </summary>
public class TelegramHtmlRendererFoldModeTests
{
    [Fact]
    public void AFencedBlockInsideTheFold_BecomesCodeLines_BecauseTelegramRefusesPreInAQuotation()
    {
        var html = TelegramHtml_Renderer.Render_ForFoldBody("```\nfoo bar\nbaz\n```");

        Assert.DoesNotContain("<pre>", html, StringComparison.Ordinal);
        Assert.Contains("<code>foo bar</code>", html, StringComparison.Ordinal);
        Assert.Contains("<code>baz</code>", html, StringComparison.Ordinal);
    }

    /// <summary>The ordinary mode is untouched — the degradation is the fold's, not the renderer's.</summary>
    [Fact]
    public void TheSameFencedBlock_OutsideTheFold_IsStillAPreBlock()
    {
        var html = TelegramHtml_Renderer.Render("```\nfoo bar\nbaz\n```");

        Assert.Contains("<pre>foo bar\nbaz</pre>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuotedParagraphInsideTheFold_BecomesPlainLines_BecauseTelegramRefusesANestedBlockquote()
    {
        var html = TelegramHtml_Renderer.Render_ForFoldBody("> the owner asked\n> and asked again");

        Assert.DoesNotContain("<blockquote", html, StringComparison.Ordinal);
        Assert.Contains("the owner asked", html, StringComparison.Ordinal);
        Assert.Contains("and asked again", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE WHOLE POINT OF ONE RENDERER. Bold, italic, strikethrough, code and links are inline
    /// entities, which a blockquote may contain — so the fold must not be an excuse to drop them.
    /// </summary>
    [Fact]
    public void InlineEntities_SurviveTheFold_Unchanged()
    {
        const string MARKDOWN = "**bold** and *italic* and ~~gone~~ and `code` and [link](https://example.com/x)";

        Assert.Equal(TelegramHtml_Renderer.Render(MARKDOWN), TelegramHtml_Renderer.Render_ForFoldBody(MARKDOWN));
    }

    [Fact]
    public void HeadingsAndListsAndSnakeCase_ReadTheSameInBothModes()
    {
        const string MARKDOWN = "## Findings\n- PlanLedger_Parser and PlanLedger_Sections\n1. first\n2. second";

        Assert.Equal(TelegramHtml_Renderer.Render(MARKDOWN), TelegramHtml_Renderer.Render_ForFoldBody(MARKDOWN));
    }

    /// <summary>
    /// ESCAPING IS THE SAFETY STORY AND IT DOES NOT WEAKEN INSIDE THE FOLD. An agent writing
    /// <c>&lt;/blockquote&gt;</c> in prose must not be able to close the quotation the bridge opened.
    /// </summary>
    [Fact]
    public void MarkupAnAgentTyped_IsEscapedInsideTheFold_SoItCannotCloseTheQuotation()
    {
        var html = TelegramHtml_Renderer.Render_ForFoldBody("</blockquote> & <b>not bold</b>");

        Assert.DoesNotContain("</blockquote>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;/blockquote&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&amp;", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void EscapingInsideAFoldedFencedBlock_IsTheSameEscaping()
    {
        var html = TelegramHtml_Renderer.Render_ForFoldBody("```\n<a> & <b>\n```");

        Assert.Contains("<code>&lt;a&gt; &amp; &lt;b&gt;</code>", html, StringComparison.Ordinal);
    }

    /// <summary>An empty entity is a thing Telegram has no reason to accept; the newline draws the gap.</summary>
    [Fact]
    public void ABlankLineInAFoldedFencedBlock_EmitsNoEmptyCodeEntity()
    {
        var html = TelegramHtml_Renderer.Render_ForFoldBody("```\nfoo\n\nbar\n```");

        Assert.DoesNotContain("<code></code>", html, StringComparison.Ordinal);
        Assert.Contains("<code>foo</code>\n\n<code>bar</code>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWrapper_IsSpelledOnce_AndIsTheExpandableTelegramQuotation()
    {
        Assert.Equal(
            "<blockquote expandable>body</blockquote>",
            TelegramHtml_Renderer.Wrap_InExpandableQuote("body"));

        Assert.Equal(
            TelegramHtml_Renderer.EXPANDABLE_QUOTE_OPEN.Length + TelegramHtml_Renderer.EXPANDABLE_QUOTE_CLOSE.Length,
            TelegramHtml_Renderer.ExpandableQuoteOverhead);
    }
}
