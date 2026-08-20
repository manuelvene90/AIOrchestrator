namespace AIOrchestratorCoreLib.Telegram;

/// <summary>
/// Puts the message the owner REPLIED TO in front of what they said, so the session knows what they
/// were pointing at.
///
/// Their ask, 2026-08-20: *"support for message replies in Telegram so that I can add context to a
/// message simply by replying to it"*. Before this the quote was dropped entirely and a reply
/// arrived as a bare message — worse than it sounds, because from THEIR side it looked like context
/// had been attached.
///
/// QUOTED, NOT MERGED. The quote is marked as theirs-from-earlier rather than folded into the new
/// message: a session that cannot tell the two apart would answer the old message again, which is
/// the failure a reply exists to prevent.
/// </summary>
public static class OwnerReplyContext_Formatter
{
    /// <summary>
    /// Long enough for any message worth pointing at, short enough that replying to a wall of text
    /// does not paste that wall into the channel a second time.
    /// </summary>
    public const int MAX_QUOTED_CHARACTERS = 300;

    public const string PREFIX = "↩ replying to:";

    /// <summary>Returns the text unchanged when there is nothing being replied to.</summary>
    public static string Prepend_OrSame(string text, string? replyToText)
    {
        if (string.IsNullOrWhiteSpace(replyToText))
            return text;

        var quoted = replyToText.Trim().ReplaceLineEndings(" ");

        if (quoted.Length > MAX_QUOTED_CHARACTERS)
            quoted = quoted[..MAX_QUOTED_CHARACTERS].TrimEnd() + "…";

        return $"{PREFIX} \"{quoted}\"\n\n{text}";
    }
}
