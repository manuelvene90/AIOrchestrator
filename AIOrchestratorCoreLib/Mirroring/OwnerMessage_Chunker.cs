using AIOrchestratorCoreLib.Telegram;

namespace AIOrchestratorCoreLib.Mirroring;

/// <summary>
/// AN OWNER-FACING ENTRY IS SPLIT, NEVER DROPPED, and the pieces say which is which.
///
/// <para>
/// WHY (VPS, 2026-09-07): the owner asked the supervisor a question, the supervisor answered at
/// length, and the answer never reached the phone — the owner was left waiting on a reply that had
/// already been written. Two mechanisms could produce that and this closes both.
/// </para>
/// <para>
/// THE FIRST IS A LENGTH THE SENDER MEASURED ON THE WRONG TEXT. The mirror chunks the MARKDOWN at
/// Telegram's 4096, then renders each chunk to HTML — and rendering GROWS text: every <c>&lt;</c>
/// becomes <c>&amp;lt;</c>, every <c>&amp;</c> four characters longer, and a fenced block gains a
/// <c>&lt;pre&gt;</c> pair. A chunk of 4096 markdown characters can therefore be well over 4096
/// characters of HTML, which Telegram refuses with a 400. The plain-text fallback then re-sends the
/// same text, and if THAT is also over the limit the send throws, the append is left unconfirmed,
/// and the entry is retried into the identical refusal for ever — the channel wedges and the owner
/// sees nothing. So the budget here is measured on the RENDERED text, which is the thing Telegram
/// actually counts.
/// </para>
/// <para>
/// THE SECOND IS SILENCE ABOUT THE SPLIT. Several unlabelled messages arriving in a row read as
/// several separate statements, and on a phone the second one starting mid-sentence reads as a
/// glitch — which is how a delivered message still gets reported as lost. Every piece of a split
/// message is numbered, so the owner can see there are three and that they have all three.
/// </para>
/// <para>
/// A message that fits is returned untouched and UNNUMBERED: "(1/1)" on every ordinary reply would
/// be noise on the one thing the owner reads most.
/// </para>
/// </summary>
public static class OwnerMessage_Chunker
{
    /// <summary>Room kept for the "(10/10) " marker, so a numbered chunk cannot push itself over the cap.</summary>
    public const int MARKER_BUDGET = 12;

    /// <summary>
    /// The floor the budget search stops at. Reached only by text that is almost entirely
    /// escapable characters; below it the split would be so fine that a message would arrive as
    /// dozens of notifications, which is its own kind of loss.
    /// </summary>
    public const int MINIMUM_BUDGET = 512;

    public static string Build_Marker(int number, int total)
    {
        return $"({number}/{total}) ";
    }

    /// <summary>
    /// <paramref name="maxLength"/> is Telegram's cap on the text it actually receives — which for
    /// this mirror is HTML, because the choice of API method IS the parse mode.
    /// </summary>
    public static IReadOnlyList<string> Chunk_ForOwner(string text, int maxLength = TelegramMessage_Chunker.TELEGRAM_MAX_MESSAGE_LENGTH)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        if (Fits(text, maxLength))
            return [text];

        var pieces = Split_UntilEachFits(text, maxLength);

        if (pieces.Count == 1)
            return pieces;

        List<string> numbered = [];

        for (var i = 0; i < pieces.Count; i++)
            numbered.Add(Build_Marker(i + 1, pieces.Count) + pieces[i]);

        return numbered;
    }

    /// <summary>
    /// Line-boundary splitting, from <see cref="TelegramMessage_Chunker"/> — the same chunker the
    /// mirror has always used, asked for a smaller budget until what comes back also FITS RENDERED.
    /// Reusing it is the point: a second splitter would be a second place for the fence-straddling
    /// rule the renderer depends on to drift.
    /// </summary>
    static IReadOnlyList<string> Split_UntilEachFits(string text, int maxLength)
    {
        var budget = Math.Max(MINIMUM_BUDGET, maxLength - MARKER_BUDGET);

        while (true)
        {
            var pieces = TelegramMessage_Chunker.Chunk(text, budget);
            var over = false;

            foreach (var piece in pieces)
            {
                if (Fits(piece, maxLength - MARKER_BUDGET))
                    continue;

                over = true;
                break;
            }

            if (!over || budget <= MINIMUM_BUDGET)
                return pieces;

            // Halved rather than trimmed by the overshoot: the growth is per-character and uneven,
            // so subtracting the difference can loop many times on text that is mostly escapable.
            budget = Math.Max(MINIMUM_BUDGET, budget / 2);
        }
    }

    /// <summary>
    /// Measured on BOTH readings — the markdown as the plain-text fallback would send it, and the
    /// rendered HTML as the primary send does. Either one over the cap is a refusal, and the
    /// fallback path is exactly the one that was left with nothing to fall back to.
    ///
    /// <para>
    /// THE HTML IS MEASURED AS TELEGRAM MEASURES IT: after entities are parsed (brief F4). Counting
    /// the raw markup charged <c>&lt;b&gt;</c>, <c>&lt;/b&gt;</c>, <c>&lt;blockquote expandable&gt;</c>
    /// and every <c>&amp;amp;</c> against a budget none of them spends — always in the safe
    /// direction, and "safe" is not free: it cut entries that fit comfortably into three numbered
    /// pieces, worst on exactly the long formatted reports the owner most wants whole. The paragraph
    /// above still holds; what changed is which reading of "the HTML" is the true one.
    /// </para>
    /// </summary>
    static bool Fits(string text, int maxLength)
    {
        return text.Length <= maxLength
            && TelegramText_Ruler.Count_AfterEntityParsing(TelegramHtml_Renderer.Render(text)) <= maxLength;
    }
}
