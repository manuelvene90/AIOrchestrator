using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// Which members have outgrown their work — the owner's own complaint, 2026-08-12: "if an impl is
/// done and the sup doesn't want to use it anymore and spawns another one, the old one stays open
/// forever monitoring the channel and wasting tokens."
///
/// IT ADVISES. IT NEVER RETIRES ANYTHING. Killing a live session on an inference is the failure the
/// whole protocol warns about, and a supervisor nearly did it to a reviewer at 03:00 off a finder
/// that had simply gone quiet. The decision stays a person's; this only makes the state visible, so
/// that closing a finished member stops depending on somebody remembering to look.
///
/// It is a READ of declared state rather than a guess about it. That distinction is only available
/// because the member vocabulary exists: <see cref="MemberStates.StandingBy"/> means the member
/// itself said it has nothing owed and nothing running. Before that marker, "finished" and "stalled"
/// were indistinguishable from outside — which is exactly why this could not have been built
/// yesterday, and why it must not fall back to guessing when the marker is absent.
/// </summary>
public static class Retirement_Advisor
{
    /// <summary>
    /// How long a member must have been declared idle before it is worth mentioning. Members declare
    /// between tasks routinely; the owner's complaint is about ones left open FOREVER, so the
    /// threshold separates a pause from an accumulation.
    /// </summary>
    public const int IDLE_MINUTES_BEFORE_FLAGGING = 30;

    /// <summary>
    /// The subject the app writes when it flags idle members. A CONSTANT because two places must
    /// agree on it exactly: the engine writes it, and the mirror suppresses it by subject so it
    /// never reaches Telegram. The owner cannot close a member — the supervisor can — so pushing
    /// this to their phone would be the app forwarding somebody else's job to their lock screen,
    /// which is precisely what rule 15 exists to stop.
    /// </summary>
    public const string FLAG_SUBJECT = "idle members you could close";

    /// <summary>
    /// True when this member has done work, has DECLARED it has nothing left, and has been that way
    /// long enough to be an accumulation rather than a pause.
    ///
    /// Every other state is deliberately excluded, and each exclusion is someone who would be harmed
    /// by being retired:
    ///   - AwaitingSupervisorReview — it is owed a verdict; closing it discards filed work.
    ///   - WritingWindowOpen — it is mid-batch.
    ///   - BlockedOnOwner — it is waiting on a human.
    ///   - NewNoTraffic and never-briefed — a freshly spawned imp-1/rev-1 waiting for work, which is
    ///     the correct state for every orchestration's first minutes.
    /// </summary>
    public static bool Should_SuggestClosing(IReadOnlyList<IChannelEntry> entries, bool hasBeenBriefed, DateTime now)
    {
        if (!hasBeenBriefed)
            return false;

        if (MemberState_Resolver.Resolve(entries) != MemberStates.StandingBy)
            return false;

        return Describe_IdleFor_OrNull(entries, now) != null;
    }

    /// <summary>
    /// How long the member has been declared idle, or null if it is not idle or the declaration is
    /// too recent — or if its stamp cannot be trusted.
    ///
    /// The clock comes from the DECLARATION's own stamp, never from file mtime: an entry's mtime
    /// moves when anyone appends, including the app, so a member nudged twice would look freshly
    /// active while having said nothing. An unparseable or future stamp yields null rather than a
    /// number, so an unreadable clock never argues for retiring a session.
    /// </summary>
    public static string? Describe_IdleFor_OrNull(IReadOnlyList<IChannelEntry> entries, DateTime now)
    {
        var declaration = Find_LastMemberEntry_OrNull(entries);

        if (declaration == null)
            return null;

        if (!DateTime.TryParse(declaration.DateText, out var declaredAt))
            return null;

        var idleFor = now - declaredAt;

        if (idleFor.TotalMinutes < IDLE_MINUTES_BEFORE_FLAGGING)
            return null;

        return Formatting.SessionDuration_Formatter.Describe(idleFor);
    }

    static IChannelEntry? Find_LastMemberEntry_OrNull(IReadOnlyList<IChannelEntry> entries)
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            if (ChannelAuthor_Kinds.Is_Member(entries[index].Author))
                return entries[index];
        }

        return null;
    }
}
