using System.Text;

namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// HOW LONG TELEGRAM THINKS A MESSAGE IS, AND WHERE IT MAY BE CUT — brief F4.
///
/// <para>
/// TWO MEASUREMENTS, ONE FILE, because they are the same mistake made twice: both are places where
/// the app counted CHARACTERS OF ITS OWN STRING and Telegram counts something else.
/// </para>
/// <para>
/// THE LENGTH. Telegram's cap is "1-4096 characters AFTER ENTITIES PARSING" — the text the reader
/// sees, not the markup that produced it. <see cref="Mirroring.OwnerMessage_Chunker"/> measured the
/// rendered HTML instead, so <c>&lt;b&gt;</c>, <c>&lt;/b&gt;</c>, <c>&lt;blockquote expandable&gt;</c>
/// and every <c>&amp;amp;</c> counted against a budget they do not spend. That is only ever wrong in
/// the safe direction — parsed length can never exceed raw length — but "safe" here means an entry
/// that fits comfortably on the phone arrives cut into three numbered pieces, which is the shape the
/// owner reads as a glitch. The over-count is worst exactly where entries are longest and most
/// formatted, i.e. on the supervisor's reports.
/// </para>
/// <para>
/// THE CUT. Telegram counts in UTF-16 code units, and so does .NET — but an emoji is TWO of them,
/// and a cut between the two halves produces a pair of lone surrogates. Telegram answers that with a
/// 400, and the plain-text fallback re-sends the same broken text into the same 400, which is the
/// wedge <see cref="Mirroring.OwnerMessage_Chunker"/> exists to prevent, reached through the one
/// door it did not close. It needs a 4096-character message whose 4096th unit is half an emoji, so
/// it is rare — and it is unconditional when it happens, because the retry cannot heal it.
/// </para>
/// <para>
/// THE PARSER IS EXACT FOR THIS RENDERER'S OUTPUT, not for HTML in general, and that is what makes
/// a hand-written scan sound here. <see cref="TelegramHtml_Renderer"/> escapes <c>&amp;</c>,
/// <c>&lt;</c> and <c>&gt;</c> in every piece of text it emits, so a raw <c>&lt;</c> in its output is
/// ALWAYS a tag it opened itself and never a character the agent typed. Handed arbitrary HTML this
/// would be a guess; handed the only input it ever gets, it is a decoder.
/// </para>
/// </summary>
public static class TelegramText_Ruler
{
    /// <summary>
    /// The length Telegram will count for a piece of rendered HTML: tags removed, the three escaped
    /// characters counted as one each, everything else counted as the UTF-16 code units it is.
    /// </summary>
    public static int Count_AfterEntityParsing(string html)
    {
        if (string.IsNullOrEmpty(html))
            return 0;

        var length = 0;

        for (var position = 0; position < html.Length; position++)
        {
            var character = html[position];

            if (character == '<')
            {
                var close = html.IndexOf('>', position + 1);

                // AN UNCLOSED '<' CANNOT COME FROM THE RENDERER — it escapes every literal one — so
                // this is defence, not a case. Counting the remainder as text is the conservative
                // reading: it can only make the measurement longer, never shorter than the truth.
                if (close < 0)
                    return length + (html.Length - position);

                position = close;
                continue;
            }

            if (character == '&')
            {
                var entityLength = Read_EntityLength(html, position);

                if (entityLength > 0)
                {
                    position += entityLength - 1;
                    length++;
                    continue;
                }
            }

            length++;
        }

        return length;
    }

    /// <summary>
    /// The character count of the entity starting at <paramref name="position"/>, or 0 when what is
    /// there is a bare ampersand. Only the entities <see cref="TelegramHtml_Renderer"/> actually
    /// emits are recognised — a longer list would be inventing inputs this never receives.
    /// </summary>
    static int Read_EntityLength(string html, int position)
    {
        foreach (var entity in ENTITIES)
        {
            if (string.CompareOrdinal(html, position, entity, 0, entity.Length) == 0)
                return entity.Length;
        }

        return 0;
    }

    static readonly string[] ENTITIES = ["&amp;", "&quot;", "&lt;", "&gt;"];

    /// <summary>
    /// The nearest cut position at or before <paramref name="index"/> that does not fall between the
    /// two halves of an emoji.
    ///
    /// <para>
    /// Moves BACKWARDS by one at most, and never below zero — so it can shorten a piece by a single
    /// code unit and can never turn a terminating loop into a non-terminating one. Callers pass the
    /// index they were going to cut at and use what comes back.
    /// </para>
    /// </summary>
    public static int Move_OffSurrogatePair(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
            return index;

        // A cut at `index` puts text[index - 1] at the end of the left piece and text[index] at the
        // start of the right one. That is only a broken pair when the two are a pair.
        if (char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]))
            return index - 1;

        return index;
    }

    /// <summary>
    /// <paramref name="text"/> shortened to at most <paramref name="maximumLength"/> code units,
    /// never splitting an emoji. The truncating twin of <see cref="Move_OffSurrogatePair"/>, for the
    /// callers that shorten rather than split.
    /// </summary>
    public static string Truncate_WholeCharacters(string text, int maximumLength)
    {
        if (maximumLength <= 0)
            return string.Empty;

        if (text.Length <= maximumLength)
            return text;

        return text[..Move_OffSurrogatePair(text, maximumLength)];
    }

    /// <summary>
    /// Whether a string carries a surrogate that has lost its partner — the state Telegram answers
    /// with a 400. Exposed for the tests that have to assert the ABSENCE of it, since a lone
    /// surrogate survives a string comparison and shows up only as a replacement glyph.
    /// </summary>
    public static bool Has_LoneSurrogate(string text)
    {
        for (var position = 0; position < text.Length; position++)
        {
            if (char.IsHighSurrogate(text[position]))
            {
                if (position + 1 >= text.Length || !char.IsLowSurrogate(text[position + 1]))
                    return true;

                position++;
                continue;
            }

            if (char.IsLowSurrogate(text[position]))
                return true;
        }

        return false;
    }

    /// <summary>Kept so the file has one home for the UTF-16 facts; used by the tests and the chunkers.</summary>
    public static string Describe_Length(string text)
    {
        return new StringBuilder()
            .Append(text.Length).Append(" code units, ")
            .Append(Count_AfterEntityParsing(text)).Append(" after entity parsing")
            .ToString();
    }
}
