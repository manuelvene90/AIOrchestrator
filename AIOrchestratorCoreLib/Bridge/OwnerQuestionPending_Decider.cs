using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

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
/// ONE READER FOR "IS THIS A QUESTION". <see cref="OwnerPush_Policy"/> already owns that test and
/// uses it to decide what reaches the phone, so the glyph is deliberately built on the same call
/// rather than on a second opinion: if an entry was worth pushing to them AS A QUESTION, the topic
/// list says so, and if it was not, the topic list stays quiet. A second predicate here is how the
/// app would end up disagreeing with itself again, which is the whole shape of this bug.
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
    public static bool Decide(IReadOnlyList<IChannelEntry> ownerChannelEntries)
    {
        for (var i = ownerChannelEntries.Count - 1; i >= 0; i--)
        {
            var entry = ownerChannelEntries[i];

            if (entry.Author == ChannelAuthors.Owner)
                return false;

            if (!ChannelAuthor_Kinds.Speaks_ToOwner(entry.Author))
                continue;

            if (OwnerPush_Policy.Carries_Question(entry.RawText) || OwnerPush_Policy.Asks_InProse(entry.RawText))
                return true;
        }

        // Nobody has asked anything the owner has not already answered.
        return false;
    }
}
