namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// THE WRITE SIDE OF <see cref="ChannelFence_Screen"/>: a body about to be appended may not contain
/// a line that OPENS AN ENTRY, unless that line is quoted code the reader already suppresses.
///
/// <para>
/// WHY IT EXISTS. <c>ChannelAppender</c> wrote <c>body.Trim()</c> straight into the file, so a body
/// carrying a line of the form <c>## [n] FROM &lt;author&gt; — &lt;date&gt; — &lt;subject&gt;</c>
/// minted a PHANTOM ENTRY. Measured on this branch 2026-09-17, by appending one owner message of
/// three lines through <c>Append_OwnerEntry</c>: the channel came back holding TWO entries, the
/// second signed <c>supervisor</c> under an index the owner's text had chosen, and the owner's own
/// closing line was inside it. That is the 2026-09-15 incident shape — one message split in two and
/// the tail signed by whoever was quoted — arriving through the WRITE path instead of the read one.
/// It reaches the file from Telegram (any owner message), from an app entry, and, since the relay,
/// from a member's report copied verbatim into another member's channel.
/// </para>
/// <para>
/// A CLOSED FENCE IS LEFT EXACTLY AS WRITTEN, and that is the whole reason this asks
/// <see cref="ChannelFence_Screen"/> rather than scanning for itself. A member quoting a channel
/// entry as evidence writes it in a fenced block; the parser, the tailer and
/// <c>ChannelShape_Validator</c> already agree that such a line is evidence and not an entry, so
/// touching it would be the app editing a member's words to defend against a fault that cannot
/// happen. Only a line the READER would have believed is defused here — one rule, both sides, and
/// the two cannot drift because the write side asks the read side.
/// </para>
/// <para>
/// NEUTRALISED, NEVER REJECTED OR RE-WRITTEN. The line keeps every character it had and gains a
/// markdown quote marker in front, so it stays readable and says plainly that it was quoted. The
/// alternatives are worse in both directions: dropping the entry loses an owner message that has
/// already left its buffer, and re-flowing the text puts the app's words in an author's mouth.
/// </para>
/// <para>
/// IT SCREENS THE SHAPE, NOT THE USABLE INDEX — <c>Is_HeaderLine</c> and not <c>Opens_AnEntry</c>.
/// A header-shaped line carrying an index this system cannot use (<c>## [0]</c>, or one too large
/// for <c>int</c>) opens no entry, but <c>ChannelEntry_Parser.Parse_All</c> still breaks the message
/// at it and then DROPS everything after it, and <c>ChannelShape_Validator</c> reports it to the
/// owner as a malformed header. Screening the shape defuses both; screening the usable index would
/// leave the worse of the two.
/// </para>
/// </summary>
public static class PhantomHeader_Screen
{
    /// <summary>
    /// What a header-shaped body line is turned into. A markdown quote marker: the line stays
    /// readable and stays in the entry, but it no longer BEGINS with the header pattern, which is
    /// the only thing the parser looks at.
    /// </summary>
    public const string NEUTRALISED_HEADER_PREFIX = "> ";

    /// <summary>
    /// The body with every header-shaped line that is not inside a closed fence prefixed, and the
    /// SAME INSTANCE back when there is nothing to defuse — which is every ordinary append.
    ///
    /// <para>
    /// IT SPLITS AND MAPS UNCONDITIONALLY, unlike <c>ChannelEntry_Parser.Count_Entries</c>, which
    /// goes to some length to stay allocation-free because it is asked of every channel on every
    /// two-second tick. This is asked once per APPEND — a thing a person or a session did — so the
    /// cheap-path complication would buy nothing and cost a second reading of the fence rule.
    /// </para>
    /// </summary>
    public static string Neutralise(string body)
    {
        if (body.Length == 0)
            return body;

        var lines = body.Split('\n');
        var quoted = ChannelFence_Screen.Map_QuotedLines(lines);
        var neutralised = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (quoted[i] || !ChannelEntry_Parser.Is_HeaderLine(lines[i]))
                continue;

            lines[i] = NEUTRALISED_HEADER_PREFIX + lines[i];
            neutralised = true;
        }

        return neutralised ? string.Join('\n', lines) : body;
    }
}
