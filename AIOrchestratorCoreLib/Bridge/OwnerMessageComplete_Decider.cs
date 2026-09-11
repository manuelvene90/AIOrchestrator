namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHETHER ONE OWNER MESSAGE READS AS FINISHED — the rule that decides whether they wait at all.
///
/// <para>
/// Measured on the VPS on 2026-09-09: an owner's Telegram message took 11–12 s median to reach the
/// supervisor's channel, and six of those seconds were the aggregation window
/// (<c>OWNER_AGGREGATION_SECONDS</c>). Every message served that quiet period so that the occasional
/// burst would arrive as ONE turn. The owner's ruling that day was to shrink the window to three
/// seconds and to shorten it sharply for a message that is plainly over: one ending in <c>.</c>,
/// <c>?</c> or <c>!</c>, or one that is a slash command.
/// </para>
/// <para>
/// A CLASS OF ITS OWN, not an <c>if</c> inside the buffer, because this is the half of the change an
/// owner can argue with. "Finished" is a judgement about how they type — the window's own doc comment
/// has been re-argued three times (eight seconds, then six with the ⏸ button, now three) — and it
/// must be readable and changeable without touching a lock-guarded class around it.
/// </para>
/// <para>
/// IT DOES NOT DECIDE "SINGLE", NOR "NOW". This answers a question about ONE text;
/// <see cref="OwnerDeliveryBuffer.IOwnerDeliveryBuffer"/> is what knows whether that text is the only
/// one buffered for its target, and what turns this answer into a wait. Saying yes here used to mean
/// delivering on the very next flush pass, which was measured on 2026-09-09 to defeat the aggregation
/// it was written beside: two finished messages two seconds apart bought TWO supervisor turns because
/// the first was taken before the second arrived. It now means a SHORT quiet period instead of the full
/// window — <c>OwnerDeliveryBufferModel.FINISHED_MESSAGE_QUIET_SECONDS</c> — so a burst still rides one
/// turn and a message that really is alone still goes fast.
/// </para>
/// <para>
/// AND IT DOES NOT OVERRIDE ⏸. A hold is checked BEFORE this rule is ever asked (see
/// <c>OwnerDeliveryBufferModel.Is_Ready</c>): a finished sentence escaping a hold would be the silent
/// lapse of 2026-08-20 arriving by a new route.
/// </para>
/// </summary>
public static class OwnerMessageComplete_Decider
{
    /// <summary>
    /// THE DOTS THAT ARE NOT ENDINGS — an abbreviation's full stop, not a sentence's. Lower-case and
    /// including the dot, so the lookup is the token as it is read.
    ///
    /// <para>
    /// A LIST, NOT A LANGUAGE MODEL, and a deliberately short one. Most abbreviations that matter carry
    /// an INTERIOR dot (<c>e.g.</c>, <c>i.e.</c>, <c>p.m.</c>, <c>U.S.</c>) and are caught by shape
    /// rather than by name; these are the single-dot ones a person actually types mid-thought, in the
    /// two languages this bridge sees. Anything missing costs three seconds, once — which is the whole
    /// reason the rule may be a list at all.
    /// </para>
    /// <para>
    /// WHAT IS DELIBERATELY ABSENT: <c>no.</c> (number), <c>min.</c>, <c>sec.</c>. Each is a real word
    /// an owner ends a message with — "the answer is no." — and holding those three seconds buys nothing
    /// against a case that is genuinely ambiguous. The list is for tokens that are not words.
    /// </para>
    /// </summary>
    static readonly HashSet<string> ABBREVIATIONS_THAT_END_IN_A_DOT = new(StringComparer.OrdinalIgnoreCase)
    {
        "etc.", "cf.", "vs.", "viz.", "approx.", "resp.", "fig.", "figs.", "al.", "ca.", "incl.", "excl.",
        "vol.", "pp.", "ff.", "mr.", "mrs.", "ms.", "dr.", "prof.", "st.", "jr.", "sr.", "inc.", "ltd.", "co.",
        "ecc.", "es.", "pag.", "sig.", "dott.", "ing.", "nr.", "cap.",
    };

    /// <summary>
    /// True when this text may be delivered with a SHORT quiet period rather than the full aggregation
    /// window — see <c>OwnerDeliveryBufferModel.FINISHED_MESSAGE_QUIET_SECONDS</c>.
    ///
    /// <para>
    /// Trailing whitespace is typing, not meaning, so it is trimmed before the last character is read
    /// — Telegram preserves the newline a phone keyboard leaves behind. An empty text is NOT complete,
    /// and the reason is not tidiness: it has no last character, so answering true here would be
    /// answering from an index that does not exist.
    /// </para>
    /// <para>
    /// IT LEANS TOWARDS "NOT FINISHED", because the two mistakes do not cost the same. Reading a
    /// finished message as unfinished costs the owner the difference between the two windows. Reading an
    /// UNFINISHED one as finished takes it out of the buffer before the rest of the thought arrives,
    /// which costs the ⏸ button its window and buys a second supervisor turn at roughly a million input
    /// tokens. So every rule below that says "not finished" is there because the cheap mistake is the
    /// one worth making.
    /// </para>
    /// </summary>
    public static bool Is_Complete(string text)
    {
        var trimmed = text.AsSpan().Trim();

        if (trimmed.Length == 0)
            return false;

        // A SLASH COMMAND IS FINISHED BY CONSTRUCTION — a word the app parses, never the opening of a
        // thought. STARTS with one, deliberately: "2/3 of the tests pass" and "check /status when you
        // can" are unfinished sentences that happen to contain a slash.
        if (trimmed[0] == '/')
            return true;

        // A question mark and an exclamation mark have no abbreviated form to be confused with, so they
        // are finished wherever they appear — "is it done...?" included.
        if (trimmed[^1] is '?' or '!')
            return true;

        if (trimmed[^1] != '.')
            return false;

        // AN ELLIPSIS IS A PAUSE, IN EITHER SPELLING, and the two spellings disagreeing is what sent
        // this rule back for repair on 2026-09-09: "hold on…" waited and "hold on..." did not, for no
        // reason but which key the phone offered. The single character is already excluded by the test
        // above (it is not a '.'); this is the typed-out one.
        if (trimmed.Length >= 2 && (trimmed[^2] == '.' || trimmed[^2] == '…'))
            return false;

        return !Ends_WithAnAbbreviation(trimmed);
    }

    /// <summary>
    /// Whether the final word's dot belongs to an abbreviation rather than to the sentence. Three shapes,
    /// cheapest first: an INTERIOR dot (<c>e.g.</c>, <c>p.m.</c>, <c>U.S.</c>, and version numbers and
    /// URLs with them), a single letter (<c>J.</c>, which is an initial and never an ending), and the
    /// short list of single-dot abbreviations above.
    /// </summary>
    static bool Ends_WithAnAbbreviation(ReadOnlySpan<char> trimmed)
    {
        var lastToken = trimmed;

        for (var i = trimmed.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(trimmed[i]))
                continue;

            lastToken = trimmed[(i + 1)..];
            break;
        }

        // The final dot is the sentence's candidate; a SECOND one inside the same word is what says the
        // word is an abbreviation and the first dot was never the sentence's at all.
        if (lastToken[..^1].Contains('.'))
            return true;

        if (lastToken.Length == 2 && char.IsLetter(lastToken[0]))
            return true;

        return ABBREVIATIONS_THAT_END_IN_A_DOT.Contains(lastToken.ToString());
    }
}
