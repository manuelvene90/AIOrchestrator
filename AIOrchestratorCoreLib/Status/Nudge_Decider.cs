using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// Whether a session is dormant in a way that needs waking. The member's nudge and the supervisor's
/// nudge live HERE, together, because they were written apart and were exhaustive without either
/// author noticing: a channel ends either in a member's entry or in someone else's, so one of the
/// two always fired. Every idle member in every open orchestration was woken every 8 minutes for as
/// long as it stayed correctly idle — including a nudge about a nudge — and "append one line and go
/// quiet" only chose which of the two it got.
///
/// The state that had no name is the fix: a member that has DECLARED itself idle
/// (<see cref="MemberStates.StandingBy"/>) owes nobody a reply and is owed none, so neither nudge
/// fires. Any inbound entry makes the declaration no longer the last entry, and both go live again.
///
/// What is deliberately NOT weakened: a member that stops with work still announced and says
/// nothing is woken exactly as before. That nudge is load-bearing — a session cannot give itself the
/// next turn — and it is the false positives around it that were not.
/// </summary>
public static class Nudge_Decider
{
    /// <summary>
    /// The member stopped with work in flight and nobody about to speak to it. Its monitor fires
    /// only when someone ELSE writes, so this state cannot resolve itself.
    /// </summary>
    public static bool Is_DormantMidWork(IReadOnlyList<IChannelEntry> entries, bool hasBeenBriefed)
    {
        if (entries.Count == 0)
            return false;

        if (!ChannelAuthor_Kinds.Is_Member(entries[entries.Count - 1].Author))
            return false;

        // Never briefed is not dormant — it is a freshly spawned member waiting for work, which is
        // the correct state for the imp-1 and rev-1 that every orchestration starts with. Nudging
        // them for saying "online" respawned them on a loop and cost them their context.
        if (!hasBeenBriefed)
            return false;

        return !Is_LegitimatelyQuiet(entries);
    }

    /// <summary>
    /// Has a supervisor EVER written here — across the live file AND its archive.
    ///
    /// CLAUDE.md item 13: <see cref="Channel_Compactor"/> moves older entries into a sibling archive,
    /// so a live-file scan is not monotonic and "no supervisor entry" stops meaning "never briefed"
    /// the moment a long-running channel compacts. The failure is silent and one-directional: a
    /// briefed member reverts to looking freshly spawned, and the stalled-mid-task nudge — the
    /// load-bearing one — switches off for exactly the members that have been running longest.
    ///
    /// Counted through the one reader that spans both, never by re-scanning here.
    /// </summary>
    public static bool Has_BeenBriefed(string channelFilePath)
    {
        return ChannelHistory_Counter.Count_Entries_ByAuthor(channelFilePath, ChannelAuthors.Supervisor) > 0;
    }

    /// <summary>
    /// WHICH unanswered thing a member is being nudged about — so it can be nudged ONCE for it.
    ///
    /// The nudge used to repeat every 8 minutes for as long as a member behaved correctly, and the
    /// app's own entry was the engine: it made <see cref="Has_UnansweredInboundTraffic"/> true, it
    /// woke the member (whose watcher fires on any write), the waking proved the member alive, that
    /// proof cleared the nudged map, and the quiet clock — restarted by that same write — elapsed.
    /// Nothing outside the app was needed for any turn of it. Measured on two channels; one member
    /// took three in a row while saying nothing, which is what the protocol tells it to do.
    ///
    /// This is the identity the app remembers, and comparing it is the whole gate: the app's own
    /// writes never change the last CONVERSATION entry, so nothing the app says can ever qualify a
    /// member for another nudge. One nudge per unanswered thing.
    ///
    /// IT IS THE RAW TEXT AND IT MUST NEVER BE THE INDEX OR THE TIMESTAMP. Both are agent-written and
    /// neither is unique: `option-lab-2` carried two `[80]`s and two `[81]`s on 2026-08-10, and one
    /// evening's traffic produced two duplicate indices in a single channel. A genuinely NEW entry
    /// that repeats an index — or that lands in the same MINUTE as the one before it, which is the
    /// resolution of a header stamp — would compare equal to what is remembered here and lose the
    /// nudge it earned. That failure is silent, and it is the exact defect this gate replaces wearing
    /// the other mask. The next person to touch this will reach for the index because it is smaller;
    /// this paragraph is why they should not.
    ///
    /// Null when the channel holds nothing but app entries: there is no conversation to be nudged
    /// about, and the caller treats that as "no memory", which nudges rather than suppresses.
    /// </summary>
    public static string? Identify_LastConversationEntry_OrNull(IReadOnlyList<IChannelEntry> entries)
    {
        return MemberState_Resolver.Find_LastConversationEntry_OrNull(entries)?.RawText;
    }

    /// <summary>
    /// Somebody else wrote last and the member has not replied. Note this counts the APP's own
    /// entries: that is deliberate, because the escalation to orphan-recovery is what proves a
    /// member's monitor is dead, and it can only run on a member that has already been nudged.
    ///
    /// UNCHANGED ON PURPOSE. Two fixes that reinterpreted this predicate were tried and withdrawn:
    /// skipping app entries here makes it and <see cref="Is_DormantMidWork"/> false at the same
    /// instant, the settled-reset fires, and a genuinely dead session can never escalate. The
    /// repetition was never in this predicate — it was in what the app remembered about the nudge it
    /// had already sent, which was nothing.
    /// </summary>
    public static bool Has_UnansweredInboundTraffic(IReadOnlyList<IChannelEntry> entries)
    {
        if (entries.Count == 0)
            return false;

        return !ChannelAuthor_Kinds.Is_Member(entries[entries.Count - 1].Author);
    }

    /// <summary>
    /// The member filed something the supervisor has not answered. A declaration is not a filing:
    /// <see cref="MemberStates.StandingBy"/> asks for no verdict, and treating it as one moved the
    /// loop rather than ending it — the member went quiet and the supervisor was woken every 8
    /// minutes instead, told it had failed to answer an entry that had asked it for nothing.
    /// </summary>
    public static bool Owes_MemberAVerdict(IReadOnlyList<IChannelEntry> entries)
    {
        if (entries.Count == 0)
            return false;

        if (!ChannelAuthor_Kinds.Is_Member(entries[entries.Count - 1].Author))
            return false;

        // ASKS WHETHER WORK WAS FILED, not whether the member looks idle. Those are different
        // questions and this line collapsed them: it returned false for any member resolving to
        // StandingBy, so a filed report that ENDED with the declaration — the shape the role commands
        // tell members to write — left the supervisor with no reminder that it owed a verdict.
        //
        // The docstring three lines above named both states while the code recognised one. A spurious
        // nudge costs a wake; a missing one costs filed work sitting unread with nothing anywhere
        // saying so.
        return MemberState_Resolver.Is_AwaitingVerdict(entries);
    }

    /// <summary>
    /// Quiet for a reason, by either of the two shapes it takes: somebody owes the member a reply
    /// (a filed report, a question with the owner), or the member owes nothing and has SAID SO.
    /// </summary>
    static bool Is_LegitimatelyQuiet(IReadOnlyList<IChannelEntry> entries)
    {
        var state = MemberState_Resolver.Resolve(entries);

        return state == MemberStates.AwaitingSupervisorReview
            || state == MemberStates.BlockedOnOwner
            || state == MemberStates.StandingBy;
    }

}
