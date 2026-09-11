using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Mirroring;

/// <summary>
/// A LONG ENTRY ARRIVES AS ONE HEADLINE PLUS A TAP, not as three walls of text.
///
/// <para>
/// WHY (owner, on a phone): <see cref="OwnerMessage_Chunker"/> made a long supervisor entry ARRIVE —
/// which was the defect it was written for, and is not in question here — but it arrives as several
/// full-length messages in a row, and three of those is a screenful of scrolling before the owner
/// can tell whether any of it needs them. Telegram has had a collapsed-by-default quotation since
/// Bot API 7.4 (<c>&lt;blockquote expandable&gt;</c>, May 2024): the phone shows the first lines and
/// a "show more". So the shape delivered here is the entry's OPENING in the clear — the subject
/// line, the thing the owner reads to decide — with everything after it behind one tap.
/// </para>
/// <para>
/// FOLDING IS A PRESENTATION CHOICE AND CHUNKING IS A HARD LIMIT, in that order. Telegram's 4096 is
/// counted after entity parsing and it REFUSES rather than truncates, so a folded message that is
/// still over the cap must be split exactly as before — and every piece after the first is then
/// entirely a fold, its head line being nothing but its own <c>(2/3)</c>. Nothing is ever dropped to
/// make a message shorter; the fold hides text behind a tap, it never removes it.
/// </para>
/// <para>
/// AND IT DEGRADES TO THE PROVEN PATH RATHER THAN GUESSING. Every route out of the fold —
/// the feature turned off, an entry that is short, an entry with no second paragraph to hide, an
/// opening too long to leave room, or a body that will not fit even folded — returns exactly what
/// <see cref="OwnerMessage_Chunker"/> returns. That is the shape that has been measured against
/// Telegram; the fold is allowed to improve on it and is never allowed to be a new way to lose a
/// message.
/// </para>
/// </summary>
public static class OwnerMessage_Folder
{
    /// <summary>
    /// Rendered characters above which an entry is folded. 900 is roughly the point at which a
    /// message stops fitting on one phone screen, so below it the fold would hide something the owner
    /// could already see — which reads as the bridge withholding text.
    /// </summary>
    public const int DEFAULT_FOLD_THRESHOLD = 900;

    /// <summary>
    /// How many lines of the opening stay in the clear. Two, because agent entries lead with a
    /// subject line and often a one-line verdict under it, and that pair is the part the owner reads
    /// to decide whether to expand at all. The first paragraph wins when it is shorter.
    /// </summary>
    public const int HEAD_LINE_LIMIT = 2;

    /// <summary>
    /// The floor the body-budget search stops at, matching <see cref="OwnerMessage_Chunker"/>'s own:
    /// below it a message would arrive as dozens of notifications, which is its own kind of loss.
    /// Reaching it without a fit is not a smaller fold, it is the unfolded path.
    /// </summary>
    public const int MINIMUM_BODY_BUDGET = 512;

    /// <summary>
    /// The pieces to send, in order, each as the HTML the primary send uses and the Markdown the
    /// plain-text fallback re-sends when Telegram refuses that HTML.
    ///
    /// <para>
    /// BOTH READINGS ARE CARRIED because the fallback is what a rendering bug costs instead of the
    /// message, and a fallback that does not fit is a fallback that throws — the wedge
    /// <see cref="OwnerMessage_Chunker"/> exists to close. Every piece below fits under
    /// <paramref name="maxLength"/> measured BOTH ways, or the whole entry goes out unfolded.
    /// </para>
    /// </summary>
    /// <param name="foldThreshold">Rendered length above which to fold; 0 or less disables folding entirely.</param>
    public static IReadOnlyList<(string Markdown, string Html)> Fold_ForOwner(
        string text,
        int foldThreshold = DEFAULT_FOLD_THRESHOLD,
        int maxLength = TelegramMessage_Chunker.TELEGRAM_MAX_MESSAGE_LENGTH)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        if (foldThreshold <= 0 || TelegramHtml_Renderer.Render(text).Length <= foldThreshold)
            return Build_Unfolded(text, maxLength);

        var (head, rest) = Split_Head(text);

        // NOTHING TO HIDE IS NOT A FOLD. An entry that is one long paragraph has no second part, and
        // wrapping the whole of it in a quotation would show the owner a "show more" and no message.
        if (rest.Length == 0)
            return Build_Unfolded(text, maxLength);

        if (!Leaves_RoomForABody(head, maxLength))
            return Build_Unfolded(text, maxLength);

        var bodies = Split_BodyUntilEachFits_OrNull(head, rest, maxLength);

        if (bodies == null)
            return Build_Unfolded(text, maxLength);

        List<(string Markdown, string Html)> pieces = [];

        for (var i = 0; i < bodies.Count; i++)
        {
            var headLine = Build_HeadLine(head, i + 1, bodies.Count);

            pieces.Add((Build_Markdown(headLine, bodies[i]), Build_Html(headLine, bodies[i])));
        }

        return pieces;
    }

    /// <summary>
    /// What every route out of the fold returns: the stage-1i chunking, each chunk paired with its
    /// own rendering. Pre-rendering here rather than inside the sender changes nothing —
    /// <see cref="TelegramHtml_Renderer.Render"/> is pure — and it lets one send path serve both
    /// shapes.
    /// </summary>
    static IReadOnlyList<(string Markdown, string Html)> Build_Unfolded(string text, int maxLength)
    {
        return [.. OwnerMessage_Chunker.Chunk_ForOwner(text, maxLength)
            .Select(chunk => (chunk, TelegramHtml_Renderer.Render(chunk)))];
    }

    /// <summary>
    /// The opening that stays in the clear, and everything after it.
    ///
    /// <para>
    /// The first PARAGRAPH or <see cref="HEAD_LINE_LIMIT"/> lines, whichever is shorter. Blank lines
    /// between the two halves are dropped: the quotation draws that break itself, and a fold that
    /// opened on an empty line would waste the two lines the phone previews.
    /// </para>
    /// </summary>
    public static (string Head, string Body) Split_Head(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var paragraphLines = 0;

        while (paragraphLines < lines.Length && lines[paragraphLines].Trim().Length > 0)
            paragraphLines++;

        // At least one line even when the entry opens on a blank one: a head of nothing is not a head.
        var headLines = Math.Clamp(paragraphLines, 1, HEAD_LINE_LIMIT);
        var restStart = headLines;

        while (restStart < lines.Length && lines[restStart].Trim().Length == 0)
            restStart++;

        return (string.Join('\n', lines[..headLines]), string.Join('\n', lines[restStart..]));
    }

    /// <summary>
    /// False when the opening is so long that no useful body could ride with it — a single
    /// 4000-character first line, say. Folding there would produce pieces the fold cannot shrink, and
    /// the unfolded path already handles a wall of text correctly.
    /// </summary>
    static bool Leaves_RoomForABody(string head, int maxLength)
    {
        return Reserve(head) + MINIMUM_BODY_BUDGET <= maxLength;
    }

    /// <summary>
    /// What a piece spends before a single character of body: the numbering marker, the rendered
    /// opening, the newline under it and the quotation's own tags.
    /// </summary>
    static int Reserve(string head)
    {
        return OwnerMessage_Chunker.MARKER_BUDGET
            + TelegramHtml_Renderer.Render(head).Length
            + 1
            + TelegramHtml_Renderer.ExpandableQuoteOverhead;
    }

    /// <summary>
    /// Line-boundary splitting of the body, from <see cref="TelegramMessage_Chunker"/> — the same
    /// splitter the mirror has always used — asked for a smaller budget until every piece fits WHEN
    /// WRAPPED. Null when even the floor does not fit, which is the caller's signal to send the entry
    /// unfolded rather than to send something Telegram will refuse.
    /// </summary>
    static IReadOnlyList<string>? Split_BodyUntilEachFits_OrNull(string head, string rest, int maxLength)
    {
        var budget = Math.Max(MINIMUM_BODY_BUDGET, maxLength - Reserve(head));

        while (true)
        {
            var bodies = TelegramMessage_Chunker.Chunk(rest, budget);

            if (All_Fit(head, bodies, maxLength))
                return bodies;

            if (budget <= MINIMUM_BODY_BUDGET)
                return null;

            // Halved rather than trimmed by the overshoot, for the reason OwnerMessage_Chunker gives:
            // the growth is per-character and uneven, so subtracting the difference can loop many
            // times on text that is mostly escapable.
            budget = Math.Max(MINIMUM_BODY_BUDGET, budget / 2);
        }
    }

    static bool All_Fit(string head, IReadOnlyList<string> bodies, int maxLength)
    {
        for (var i = 0; i < bodies.Count; i++)
        {
            var headLine = Build_HeadLine(head, i + 1, bodies.Count);

            if (Build_Markdown(headLine, bodies[i]).Length > maxLength)
                return false;

            if (Build_Html(headLine, bodies[i]).Length > maxLength)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The line above the fold. The opening rides on the FIRST piece only; every later piece's head
    /// line is its own number and nothing else, because repeating the subject on each would read as
    /// three separate messages about the same thing rather than as one message in three parts.
    /// A single piece carries no number at all — "(1/1)" on an ordinary reply is noise.
    /// </summary>
    static string Build_HeadLine(string head, int number, int total)
    {
        if (total == 1)
            return head;

        return number == 1
            ? OwnerMessage_Chunker.Build_Marker(number, total) + head
            : OwnerMessage_Chunker.Build_Marker(number, total).TrimEnd();
    }

    /// <summary>What the plain-text fallback sends: the piece UNFOLDED, exactly as stage 1i sent it.</summary>
    static string Build_Markdown(string headLine, string body)
    {
        return headLine + "\n" + body;
    }

    static string Build_Html(string headLine, string body)
    {
        return TelegramHtml_Renderer.Render(headLine)
            + "\n"
            + TelegramHtml_Renderer.Wrap_InExpandableQuote(TelegramHtml_Renderer.Render_ForFoldBody(body));
    }
}
