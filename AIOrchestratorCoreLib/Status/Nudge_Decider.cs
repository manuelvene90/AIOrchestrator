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
    /// Somebody else wrote last and the member has not replied. Note this counts the APP's own
    /// entries: that is deliberate, because the escalation to orphan-recovery is what proves a
    /// member's monitor is dead, and it can only run on a member that has already been nudged.
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

        return MemberState_Resolver.Resolve(entries) != MemberStates.StandingBy;
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
