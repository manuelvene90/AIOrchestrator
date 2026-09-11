using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// IS A QUESTION ACTUALLY OUTSTANDING ON THE OWNER CHANNEL — the predicate behind the ❓ glyph.
///
/// The glyph used to be driven by <see cref="Status.OwnerOwesReply_Decider"/>, which answers a
/// DIFFERENT question: "whose move is it". That decider was written for the stall alert (the owner's
/// 2026-08-15 ruling, "alert only if I owe you a reply") and reused verbatim for the topic name on
/// 2026-08-19 as though the two facts were one. They are not. "The session spoke last" is true after
/// every progress report, every recap, and even after the session's ANSWER to the owner's own
/// message — so ❓ was on essentially permanently, and went off only for as long as it took the
/// session to write anything at all.
///
/// The owner, 2026-08-21, reading ❓ on a topic whose session was correctly reporting no open
/// questions: *"If there are no questions, why did they put the question mark in the topic name?
/// That emoji is reserved for when there's a non-blocking question."*
///
/// DECLARED, NEVER INFERRED — and this is where the glyph parts company with the push.
///
/// It used to call BOTH <see cref="OwnerPush_Policy.Carries_Question"/> (the explicit `QUESTION:` /
/// `OPTION:` markers) and <see cref="OwnerPush_Policy.Asks_InProse"/> (any line ending in `?`), on
/// the principle that one reader cannot disagree with itself. The principle was right and the
/// conclusion was wrong, because the two decisions have OPPOSITE costs:
///
///   - THE PUSH is biased towards false POSITIVES on purpose. A real question that never reaches
///     the owner deadlocks the conversation, and an extra notification costs one glance. So
///     ask-shaped means push, and `Asks_InProse` earns its place there.
///   - THE GLYPH is biased the other way. It is a claim standing on the topic LIST, it persists
///     until something clears it, and the owner cannot clear it themselves. A false ❓ is a lie
///     they have to live with, and the app has already made them live with it repeatedly.
///
/// The owner, 2026-08-25: *"it's quite absurd that a question mark gets added to the topic name
/// every time a question mark appears. Even when I'm the one asking the question 🤣 ... It's not
/// that it should be interpreted indirectly based on the presence of a ? here and there that could
/// mean anything."*
///
/// Their own case is the sharpest one and it was unavoidable: `supervisor.md` tells a supervisor to
/// open its reply by QUOTING the owner (`Owner: <their text>`), so the moment the owner asked
/// anything, the supervisor's own next entry contained a line ending in `?` and lit the glyph. The
/// owner's question became the supervisor's, by punctuation.
///
/// So the glyph now reads only what a session DECLARED: `QUESTION:` / `OPTION:` lines, or
/// `BLOCKED ON OWNER`. Every role command already mandates those for any decision that needs the
/// owner, so nothing is lost — a session that wants the glyph asks for it in the vocabulary it was
/// already told to use.
///
/// SCANNING STOPS AT THE OWNER, not at the first session entry. A question stays outstanding across
/// any number of later progress reports — the owner has still not answered it — so the scan walks
/// back over non-question session entries and only a genuine OWNER entry clears it. That matches how
/// the app clears its open-question registry: any owner message answers whatever was pending.
///
/// APP ENTRIES ARE SKIPPED for the same reason the other decider skips them: they are the app
/// talking about the conversation, on their own schedule, and letting a status push clear a real
/// question would be the failure mode where a feature quietly disables itself.
/// </summary>
public static class OwnerQuestionPending_Decider
{
    /// <summary>
    /// What a session writes to say it has what it needed and the glyph should come off — the
    /// unequivocal counterpart to `QUESTION:`. Read by the shared marker matcher, so it counts in
    /// the SUBJECT anywhere or at the START of a body line, and never mid-sentence.
    /// </summary>
    // FROM THE GRAMMAR, and `static readonly` rather than `const` because of it: the grammar is a
    // FILE both this app and the bash tool read, so its values arrive at runtime. A `const` would
    // have to be a literal here, which is the ninth copy E3 removes.
    public static readonly string ANSWERED_MARKER = ChannelGrammar.ANSWERED;

    public static bool Decide(IReadOnlyList<IChannelEntry> ownerChannelEntries)
    {
        for (var i = ownerChannelEntries.Count - 1; i >= 0; i--)
        {
            var entry = ownerChannelEntries[i];

            if (entry.Author == ChannelAuthors.Owner)
                return false;

            // THE SESSION CAN SAY SO ITSELF, which is the other half of the owner's ask: *"when they
            // receive it, regardless of how, they modify the status as 'info received' one more time
            // in some unequivocal way."* An owner entry is not the only way an answer arrives — they
            // tap a button, they say it in the terminal, they answer in a different topic — and in
            // all of those the channel never sees a `FROM owner` entry, so the glyph used to stay
            // lit on a question that had been answered minutes ago.
            if (ChannelAuthor_Kinds.Speaks_ToOwner(entry.Author)
                && MemberState_Resolver.Contains_Marker(entry, ANSWERED_MARKER))
                return false;

            if (!ChannelAuthor_Kinds.Speaks_ToOwner(entry.Author))
                continue;

            // DECLARED ONLY. `Asks_InProse` is deliberately NOT consulted here — see the summary.
            if (OwnerPush_Policy.Carries_Question(entry.RawText)
                || entry.RawText.Contains(OwnerPush_Policy.BLOCKED_MARKER, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Nobody has asked anything the owner has not already answered.
        return false;
    }
}
