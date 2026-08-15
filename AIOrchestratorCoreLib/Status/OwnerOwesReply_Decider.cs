using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// WHOSE MOVE IS IT ON THE OWNER CHANNEL — asked of the last thing anybody actually said.
///
/// The stall alert used to fire on quiet alone, and told the owner "the session may have ended its
/// turn without re-arming its watcher. Text it to wake it up." On 2026-08-15 they got that on two
/// solo topics and answered "makes no sense": the channel was quiet because THEY had stopped
/// texting, and the session was idle exactly as it should be. Their ruling — alert "only if I owe
/// you a reply".
///
/// So quiet splits into two states that deserve opposite treatment:
///   - the SESSION spoke last → it has said its piece and is waiting on the owner. That is the one
///     worth a nudge, and the owner can act on it.
///   - the OWNER spoke last → the session owes THEM, and a session that has gone silent on an
///     unanswered message is already covered by the reply nudge, which wakes the session rather
///     than telling the owner to.
///
/// APP ENTRIES ARE SKIPPED, not treated as a turn. They are the app talking about the conversation —
/// status pushes, nudges, ledger complaints — and they arrive on their own schedule. Counting one as
/// the last word would let the app's own status push silence the alert, which is the failure mode
/// where a feature quietly disables itself.
/// </summary>
public static class OwnerOwesReply_Decider
{
    public static bool Decide(IReadOnlyList<IChannelEntry> ownerChannelEntries)
    {
        for (var i = ownerChannelEntries.Count - 1; i >= 0; i--)
        {
            var author = ownerChannelEntries[i].Author;

            if (author == ChannelAuthors.Owner)
                return false;

            if (ChannelAuthor_Kinds.Speaks_ToOwner(author))
                return true;
        }

        // Nobody has spoken yet — a brand-new orchestration, or a channel of nothing but app
        // entries. There is no reply owed because there is no message to reply to.
        return false;
    }
}
