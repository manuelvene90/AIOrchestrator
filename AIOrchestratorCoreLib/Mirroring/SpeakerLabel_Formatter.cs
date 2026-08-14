namespace AIOrchestratorCoreLib.Mirroring;

/// <summary>
/// WHO IS SPEAKING TO THE OWNER, in one place — the coloured label that opens every line the app
/// writes about a session ("🔴 Sup: busy…", "🟠 Solo: turn ended…").
///
/// IT EXISTS BECAUSE THE LABEL WAS A LITERAL AT SIX SITES AND ALL SIX SAID "Sup". A BASIC
/// orchestration has no supervisor — one session talks to the owner and does the work — so every
/// narration line about it named a role that is not there. The owner, 2026-08-14: *"the app didn't
/// realize this is a 'solo' session and writes things like ✓✓ · 🔴 Sup: turn ended without a reply…
/// It should be done in a way that it knows it's solo, otherwise I get confused."*
///
/// Six copies is also exactly the shape decision 12 warns about: a seventh site would have been
/// written with the same literal, and correcting the six by hand leaves the seventh saying "Sup"
/// for ever. The label is a function of the orchestration now, so a new site cannot get it wrong
/// without asking the wrong question out loud.
///
/// The GLYPHS match the voices <see cref="MirrorText_Formatter"/> already gives each speaker — 🔴
/// orchestration supervisor, 🟡 general supervisor, 🟠 the solo of a basic orchestration — so the
/// app's narration about a session and the session's own mirrored words read as the same voice.
/// </summary>
public static class SpeakerLabel_Formatter
{
    /// <summary>The concierge in the General topic.</summary>
    public const string GENERAL = "🟡 Gen-Sup";

    /// <summary>An orchestration's supervisor — the crew case.</summary>
    public const string SUPERVISOR = "🔴 Sup";

    /// <summary>
    /// The single session of a BASIC orchestration. "Solo" rather than "Sup" is the whole point:
    /// the owner is talking to the thing doing the work, and there is no supervisor to wait for.
    /// </summary>
    public const string SOLO = "🟠 Solo";

    /// <summary>
    /// The label alone, with no trailing punctuation — callers add ": " themselves, because some of
    /// them are building a sentence and some a prefix.
    /// </summary>
    /// <param name="isGeneral">The General topic, which is neither an orchestration nor a solo.</param>
    /// <param name="isBasic">
    /// Whether this orchestration is BASIC. It is the caller's answer rather than a member-id scan
    /// here: <see cref="Sessions.OrchestrationShape.Is_BasicOrchestration"/> owns that question and
    /// documents why reading it off the roster is wrong for a promoted orchestration.
    /// </param>
    public static string Describe(bool isGeneral, bool isBasic)
    {
        if (isGeneral)
            return GENERAL;

        return isBasic ? SOLO : SUPERVISOR;
    }
}
