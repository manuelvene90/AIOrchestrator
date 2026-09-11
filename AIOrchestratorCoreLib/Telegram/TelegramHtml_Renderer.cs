using System.Text;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Markdown → Telegram-HTML, for every piece of AGENT-WRITTEN prose that reaches the owner's phone.
///
/// <para>
/// WHY IT EXISTS (owner, 2026-09-07, from their phone): a supervisor entry arrived reading
/// <c>**GOAL 2: V1 LIVE**</c> and <c>- **Milestone:** 11 aperte</c> — the markers shown literally,
/// the emphasis lost, and a bullet list rendered as a run of hyphens. The mirror sent through
/// <c>sendMessage</c> with no <c>parse_mode</c>, so Telegram had no reason to read the markers as
/// anything. Agents write Markdown because that is how they write; the bridge is the piece that
/// has to render it.
/// </para>
/// <para>
/// TELEGRAM'S HTML IS NOT A SUBSET OF HTML — it is a fixed list of tags, and there is no such thing
/// as a heading, a list, or an indentation. So a heading becomes a bold line and a list marker
/// becomes a bullet CHARACTER: the shape survives even though the tag does not exist.
/// <c>&lt;blockquote&gt;</c> does exist (Bot API 7.0) and is used; nothing may nest inside another
/// blockquote, so quote lines are merged into one.
/// </para>
/// <para>
/// EVERYTHING NOT RECOGNISED IS ESCAPED, and an unbalanced marker is text like any other. That is
/// the whole safety story: this renderer can only ever emit tags it opened and closed itself, so a
/// message cannot be lost to a malformed-entity refusal because of something an agent typed. The
/// send path still carries a plain-text fallback (see <see cref="TelegramProse_Sender"/>) — belt as
/// well as braces, because a refusal here costs the owner a message.
/// </para>
/// <para>
/// IT HAS TWO MODES AND IS STILL ONE RENDERER. <see cref="Render_ForFoldBody"/> is the same scanner
/// with the two BLOCK constructs an expandable quotation cannot contain degraded rather than emitted
/// — see its own summary. A second renderer for the fold would be a second copy of every inline rule,
/// which is the drift CLAUDE.md decision 12 was written about.
/// </para>
/// </summary>
public static class TelegramHtml_Renderer
{
    /// <summary>The bullet a <c>- </c> or <c>* </c> marker becomes. Telegram has no list tag.</summary>
    const string BULLET = "• ";

    /// <summary>
    /// The collapsed-by-default quotation (Bot API 7.4): the phone shows the first few lines with a
    /// "show more", and the owner expands only what they want to read. The tag is spelled ONCE, here,
    /// because the folder must never assemble Telegram markup of its own — this file's whole safety
    /// story is that it emits only tags it opened and closed itself.
    /// </summary>
    public const string EXPANDABLE_QUOTE_OPEN = "<blockquote expandable>";

    public const string EXPANDABLE_QUOTE_CLOSE = "</blockquote>";

    /// <summary>What wrapping a body in the fold costs, so a caller can budget for it without guessing.</summary>
    public static int ExpandableQuoteOverhead => EXPANDABLE_QUOTE_OPEN.Length + EXPANDABLE_QUOTE_CLOSE.Length;

    public static string Render(string markdown)
    {
        return Render_Core(markdown, insideFold: false);
    }

    /// <summary>
    /// THE SAME RENDERER, in the mode an expandable quotation allows — not a second renderer, for the
    /// reason CLAUDE.md decision 12 states about every formatter in this codebase.
    ///
    /// <para>
    /// Telegram permits INLINE entities inside a <c>&lt;blockquote&gt;</c> (bold, italic, code,
    /// links) and refuses two BLOCK ones: another blockquote, and <c>&lt;pre&gt;</c>. A fenced block
    /// therefore degrades to one <c>&lt;code&gt;</c> per line — monospacing survives, the block
    /// container does not — and a nested quote degrades to plain text. Emitting either would earn a
    /// 400, and a 400 on this path costs the owner the message.
    /// </para>
    /// </summary>
    public static string Render_ForFoldBody(string markdown)
    {
        return Render_Core(markdown, insideFold: true);
    }

    /// <summary>
    /// Wraps an ALREADY-RENDERED body in the fold. Takes HTML rather than Markdown so the caller
    /// cannot accidentally hand it text rendered by <see cref="Render"/> — which would carry the
    /// <c>&lt;pre&gt;</c> and nested <c>&lt;blockquote&gt;</c> the fold refuses.
    /// </summary>
    public static string Wrap_InExpandableQuote(string foldBodyHtml)
    {
        return EXPANDABLE_QUOTE_OPEN + foldBodyHtml + EXPANDABLE_QUOTE_CLOSE;
    }

    static string Render_Core(string markdown, bool insideFold)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var html = new StringBuilder();
        var lineIndex = 0;
        var first = true;

        while (lineIndex < lines.Length)
        {
            if (!first)
                html.Append('\n');

            first = false;

            if (Is_Fence(lines[lineIndex]))
            {
                lineIndex = Append_FencedBlock(html, lines, lineIndex, insideFold);
                continue;
            }

            if (Is_Quote(lines[lineIndex]))
            {
                lineIndex = Append_Quote(html, lines, lineIndex, insideFold);
                continue;
            }

            Append_Line(html, lines[lineIndex]);
            lineIndex++;
        }

        return html.ToString();
    }

    /// <summary>
    /// A fence line is <c>```</c> with anything after it — the language, which Telegram's
    /// <c>&lt;pre&gt;</c> has nowhere to put and which is therefore dropped rather than shown.
    /// </summary>
    static bool Is_Fence(string line)
    {
        return line.TrimStart().StartsWith("```", StringComparison.Ordinal);
    }

    static bool Is_Quote(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith('>');
    }

    /// <summary>
    /// AN UNCLOSED FENCE IS STILL A BLOCK, to the end of the text. The mirror chunks a long entry
    /// at 4096 characters on a line boundary, so a mockup longer than one message ALWAYS arrives
    /// with its opening fence in one chunk and its closing fence in the next. Refusing to render
    /// the half without a partner would drop a drawing's monospacing exactly when it is longest.
    /// </summary>
    /// <param name="insideFold">
    /// Inside an expandable quotation <c>&lt;pre&gt;</c> is not available — Telegram refuses the
    /// block entity there — so the block becomes one <c>&lt;code&gt;</c> per line. The monospacing an
    /// ASCII mockup depends on survives; what is lost is the single scroll container, which is a
    /// smaller loss than the 400 the honest tag would earn.
    /// </param>
    static int Append_FencedBlock(StringBuilder html, string[] lines, int fenceIndex, bool insideFold)
    {
        var body = new List<string>();
        var lineIndex = fenceIndex + 1;

        while (lineIndex < lines.Length && !Is_Fence(lines[lineIndex]))
        {
            body.Add(lines[lineIndex]);
            lineIndex++;
        }

        if (insideFold)
            Append_CodeLines(html, body);
        else
            html.Append("<pre>").Append(Escape_Text(string.Join('\n', body))).Append("</pre>");

        // Past the closing fence when there is one; already past the end when there is not.
        return lineIndex < lines.Length ? lineIndex + 1 : lineIndex;
    }

    /// <summary>
    /// A blank line inside the block gets NO <c>&lt;code&gt;</c> pair of its own: an empty entity is
    /// a thing Telegram has no reason to accept, and the newline alone already draws the gap.
    /// </summary>
    static void Append_CodeLines(StringBuilder html, IReadOnlyList<string> body)
    {
        for (var i = 0; i < body.Count; i++)
        {
            if (i > 0)
                html.Append('\n');

            if (body[i].Length == 0)
                continue;

            html.Append("<code>").Append(Escape_Text(body[i])).Append("</code>");
        }
    }

    /// <summary>
    /// Consecutive quote lines become ONE blockquote. Telegram refuses a nested blockquote, and a
    /// per-line one would render a quoted paragraph as a stack of separate quotes.
    ///
    /// <para>
    /// INSIDE THE FOLD there is no blockquote to open at all: the fold IS one, and a second would be
    /// the nesting Telegram refuses. The <c>&gt;</c> markers are still stripped and the lines still
    /// render their inline emphasis, so the words arrive — only the indent is gone.
    /// </para>
    /// </summary>
    static int Append_Quote(StringBuilder html, string[] lines, int quoteIndex, bool insideFold)
    {
        if (!insideFold)
            html.Append("<blockquote>");

        var lineIndex = quoteIndex;

        while (lineIndex < lines.Length && Is_Quote(lines[lineIndex]))
        {
            if (lineIndex > quoteIndex)
                html.Append('\n');

            Append_Line(html, Strip_QuoteMarker(lines[lineIndex]));
            lineIndex++;
        }

        if (!insideFold)
            html.Append("</blockquote>");

        return lineIndex;
    }

    static string Strip_QuoteMarker(string line)
    {
        var afterMarker = line.TrimStart()[1..];

        return afterMarker.StartsWith(' ') ? afterMarker[1..] : afterMarker;
    }

    /// <summary>
    /// The line-level constructs: a heading becomes a bold line, a list marker becomes a bullet or
    /// keeps its number. Both are TEXT — Telegram has no tag for either — so what survives is the
    /// shape the agent drew, not the markup they wrote.
    /// </summary>
    static void Append_Line(StringBuilder html, string line)
    {
        var indent = line.Length - line.TrimStart(' ').Length;
        var body = line[indent..];

        var headingHashes = 0;

        while (headingHashes < body.Length && headingHashes < 6 && body[headingHashes] == '#')
            headingHashes++;

        if (headingHashes > 0 && headingHashes < body.Length && body[headingHashes] == ' ')
        {
            html.Append(' ', indent).Append("<b>");
            Append_Inline(html, body[(headingHashes + 1)..].TrimStart());
            html.Append("</b>");
            return;
        }

        if (body.Length > 1 && (body[0] == '-' || body[0] == '*') && body[1] == ' ')
        {
            html.Append(' ', indent).Append(BULLET);
            Append_Inline(html, body[2..]);
            return;
        }

        var digits = 0;

        while (digits < body.Length && char.IsAsciiDigit(body[digits]))
            digits++;

        if (digits > 0 && digits + 1 < body.Length && body[digits] == '.' && body[digits + 1] == ' ')
        {
            // The NUMBER is kept, not replaced: an ordered list carries information a bullet loses.
            html.Append(' ', indent).Append(body[..(digits + 2)]);
            Append_Inline(html, body[(digits + 2)..]);
            return;
        }

        html.Append(' ', indent);
        Append_Inline(html, body);
    }

    /// <summary>
    /// The inline scanner. Hand-written on purpose: the whole point is that it emits ONLY tags it
    /// balanced itself, and a general Markdown library would emit the dozens of tags Telegram
    /// rejects. Anything it does not recognise falls through to <see cref="Escape_Text"/>.
    /// </summary>
    static void Append_Inline(StringBuilder html, string text)
    {
        var position = 0;

        while (position < text.Length)
        {
            var character = text[position];

            if (character == '`')
            {
                // CODE IS OPAQUE: its content is escaped and NOT re-scanned, so `**x**` inside
                // backticks arrives as the four characters the agent typed.
                var closing = text.IndexOf('`', position + 1);

                if (closing > position + 1)
                {
                    html.Append("<code>").Append(Escape_Text(text[(position + 1)..closing])).Append("</code>");
                    position = closing + 1;
                    continue;
                }

                html.Append('`');
                position++;
                continue;
            }

            if (character == '[' && Try_AppendLink(html, text, position, out var afterLink))
            {
                position = afterLink;
                continue;
            }

            if (Try_AppendRun(html, text, position, "***", "<b><i>", "</i></b>", out var afterBoldItalic))
            {
                position = afterBoldItalic;
                continue;
            }

            if (Try_AppendRun(html, text, position, "**", "<b>", "</b>", out var afterBold))
            {
                position = afterBold;
                continue;
            }

            if (Try_AppendRun(html, text, position, "~~", "<s>", "</s>", out var afterStrike))
            {
                position = afterStrike;
                continue;
            }

            if (Try_AppendRun(html, text, position, "__", "<b>", "</b>", out var afterUnderscoreBold))
            {
                position = afterUnderscoreBold;
                continue;
            }

            if (Try_AppendRun(html, text, position, "*", "<i>", "</i>", out var afterItalic))
            {
                position = afterItalic;
                continue;
            }

            if (Try_AppendRun(html, text, position, "_", "<i>", "</i>", out var afterUnderscoreItalic))
            {
                position = afterUnderscoreItalic;
                continue;
            }

            Append_EscapedChar(html, character);
            position++;
        }
    }

    /// <summary>
    /// One emphasis run, or nothing. Returns false — leaving the caller to emit the character as
    /// literal text — whenever the marker is not balanced on this line, which is the rule that
    /// keeps a lone asterisk in prose from opening a tag that never closes.
    ///
    /// <para>
    /// THE UNDERSCORE CARRIES AN EXTRA CONDITION, and it is the one that matters in this codebase:
    /// a marker touching a word character on its outer side is not a marker at all. Without it
    /// every <c>snake_case</c> identifier an agent writes — and they write them constantly —
    /// becomes half an italic run, and <c>PlanLedger_Parser and PlanLedger_Sections</c> renders as
    /// one italic blob.
    /// </para>
    /// </summary>
    static bool Try_AppendRun(StringBuilder html, string text, int position, string marker, string openTag, string closeTag, out int nextPosition)
    {
        nextPosition = position;

        if (!Starts_With(text, position, marker))
            return false;

        var isUnderscore = marker[0] == '_';

        if (isUnderscore && position > 0 && Is_WordCharacter(text[position - 1]))
            return false;

        var contentStart = position + marker.Length;
        var searchFrom = contentStart;

        while (true)
        {
            var closing = text.IndexOf(marker, searchFrom, StringComparison.Ordinal);

            if (closing < 0)
                return false;

            var content = text[contentStart..closing];

            // An empty run is not a run: `****` is four literal asterisks, not bold nothing.
            if (content.Length == 0)
                return false;

            var afterClosing = closing + marker.Length;

            // Whitespace against the marker means the agent was writing, not marking up:
            // "2 * 3 * 4" and "a _ b" must not become emphasis.
            var opensCleanly = !char.IsWhiteSpace(content[0]);
            var closesCleanly = !char.IsWhiteSpace(content[^1]);
            var outerBoundaryOk = !isUnderscore || afterClosing >= text.Length || !Is_WordCharacter(text[afterClosing]);

            if (opensCleanly && closesCleanly && outerBoundaryOk)
            {
                // TELEGRAM REFUSES <code> NESTED INSIDE b/i/s (and the reverse): "can contain and
                // can be part of any other entities, EXCEPT pre and code". `**`x`**` recursing
                // straight through would emit <b><code>x</code></b> and earn a 400, which is the
                // defect this guards — so a run whose content holds a code span opens no wrapping
                // tag at all; the content (code span included) still renders, just unwrapped.
                if (!Contains_CodeSpan(content))
                    html.Append(openTag);

                Append_Inline(html, content);

                if (!Contains_CodeSpan(content))
                    html.Append(closeTag);

                nextPosition = afterClosing;
                return true;
            }

            // This candidate closer was not one — keep looking further along the same line rather
            // than giving up, so "_a b_c_" still finds the run its author meant.
            searchFrom = closing + 1;
        }
    }

    /// <summary>
    /// Mirrors the code-span test <see cref="Append_Inline"/> itself uses (a backtick with a
    /// closing backtick more than one position later — an empty pair is not a span). Only an
    /// EXISTENCE check: it need not track state the way the scanner does, because either answer
    /// it gives on an edge case is safe — a false positive costs a run its wrapping tag, never an
    /// illegal nesting.
    /// </summary>
    static bool Contains_CodeSpan(string text)
    {
        var position = 0;

        while (position < text.Length)
        {
            if (text[position] != '`')
            {
                position++;
                continue;
            }

            if (text.IndexOf('`', position + 1) > position + 1)
                return true;

            position++;
        }

        return false;
    }

    /// <summary>
    /// <c>[text](url)</c> for http(s) ONLY. Every other scheme is left as literal text: a
    /// <c>javascript:</c> or <c>file:</c> target in an agent-written message is either a mistake or
    /// something the owner should see spelled out rather than hidden behind a tappable word.
    /// </summary>
    static bool Try_AppendLink(StringBuilder html, string text, int position, out int nextPosition)
    {
        nextPosition = position;

        var labelEnd = text.IndexOf(']', position + 1);

        if (labelEnd < 0 || labelEnd + 1 >= text.Length || text[labelEnd + 1] != '(')
            return false;

        var urlEnd = text.IndexOf(')', labelEnd + 2);

        if (urlEnd < 0)
            return false;

        var label = text[(position + 1)..labelEnd];
        var url = text[(labelEnd + 2)..urlEnd];

        if (label.Length == 0 || !Is_SafeHttpUrl(url))
            return false;

        html.Append("<a href=\"").Append(Escape_Attribute(url)).Append("\">");
        Append_Inline(html, label);
        html.Append("</a>");

        nextPosition = urlEnd + 1;
        return true;
    }

    static bool Is_SafeHttpUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;

        return parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps;
    }

    static bool Starts_With(string text, int position, string marker)
    {
        if (position + marker.Length > text.Length)
            return false;

        for (var i = 0; i < marker.Length; i++)
        {
            if (text[position + i] != marker[i])
                return false;
        }

        return true;
    }

    static bool Is_WordCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }

    /// <summary>
    /// The three characters Telegram's HTML parser reads as markup. Nothing else is escaped —
    /// quotes and apostrophes are ordinary text in a message body, and escaping them would put
    /// <c>&amp;#39;</c> on the owner's screen.
    /// </summary>
    static string Escape_Text(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    static void Append_EscapedChar(StringBuilder html, char character)
    {
        switch (character)
        {
            case '&':
                html.Append("&amp;");
                return;
            case '<':
                html.Append("&lt;");
                return;
            case '>':
                html.Append("&gt;");
                return;
            default:
                html.Append(character);
                return;
        }
    }

    /// <summary>An href value additionally escapes the quote that delimits it.</summary>
    static string Escape_Attribute(string url)
    {
        return Escape_Text(url).Replace("\"", "&quot;");
    }
}
