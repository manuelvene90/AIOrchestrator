namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// THE ONE LINE A SESSION DECLARES ABOUT ITSELF at the end of a turn — `STATE: …` — read back out
/// of the entry it was written in.
///
/// <para>
/// WHY THE APP STOPPED GUESSING. PULSE's second field used to be inferred: "the session spoke last,
/// so it is waiting on you" produced *"supervisor: waiting on you"* thirty minutes after the
/// supervisor wrote "Nothing more needed from you", and *"idle — waiting"* for two hours while it
/// was actually paused for a usage limit. No reading of the channel can recover what a session is
/// DOING; only the session knows, so the owner's decision (2026-09-09) is that it says so — one line
/// per turn, its own words — and the app reports it verbatim with the time it was declared.
/// </para>
/// <para>
/// WHAT THE APP STILL ADDS is only what it knows for certain: a usage-limit pause and its resume
/// time. Everything else on that row is the session's own sentence.
/// </para>
/// <para>
/// THE LAST ONE IN THE ENTRY WINS. An entry that carries two `STATE:` lines is a session that
/// changed its mind while writing, and the later line is the one it meant — the same rule the
/// question directives use for a repeated marker, and the opposite of "first wins" only because
/// here the later line is the correction rather than the header a human reads first.
/// </para>
/// </summary>
public static class DeclaredState_Parser
{
    // FROM THE GRAMMAR, and `static readonly` rather than `const` because of it: the grammar is a
    // FILE both this app and the bash tool read, so its values arrive at runtime. A `const` would
    // have to be a literal here, which is the ninth copy E3 removes.
    public static readonly string MARKER = ChannelGrammar.STATE;

    /// <summary>
    /// Beyond this the declaration is a paragraph, and PULSE has one row for it. Cut rather than
    /// dropped: a session that wrote too much still said something true.
    /// </summary>
    public const int MAX_LENGTH = 120;

    /// <summary>
    /// The declared state, or null when the entry carries none — which is a BLANK ROW on the status
    /// line, never an invented state. A turn that says nothing to the owner declares nothing.
    /// </summary>
    public static string? Find_OrNull(string? entryBody)
    {
        if (string.IsNullOrWhiteSpace(entryBody))
            return null;

        string? found = null;

        foreach (var rawLine in entryBody.Split('\n'))
        {
            var line = rawLine.Trim();

            // AT THE START OF A LINE, like every other marker in this vocabulary: a session quoting
            // the word "STATE:" mid-sentence is discussing the protocol, not declaring anything.
            if (!line.StartsWith(MARKER, StringComparison.Ordinal))
                continue;

            var declared = line[MARKER.Length..].Trim();

            if (declared.Length > 0)
                found = declared;
        }

        if (found == null)
            return null;

        return found.Length <= MAX_LENGTH ? found : found[..MAX_LENGTH].TrimEnd() + "…";
    }
}
