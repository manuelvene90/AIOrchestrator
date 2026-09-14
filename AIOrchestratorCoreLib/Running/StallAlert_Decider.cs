using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// ONE STALLED TURN, ONE MESSAGE ON THE PHONE. Decides who a <c>turn stalled</c> alert is addressed
/// to when the same turn has already stalled in front of the owner.
///
/// <para>
/// OBSERVED 2026-09-11, fincanva-6: the supervisor could not log in (every attempt answered
/// "Not logged in · Please run /login"), and the owner's phone received
/// "⚙ App: turn stalled sup turn 35 — error × 3" THREE times — channel entries #158 (08:19:05 UTC),
/// #165 (08:22:09) and #172 (08:27:10), each a new, ringing message. A stall is "not retried until
/// new traffic arrives", so the owner resending their message was new traffic, the same turn failed
/// three more times, and the same alert was written again; the third came from the daemon restarting
/// at 08:24:52, which retries a stalled turn by itself. Nothing new was said by the second or the
/// third — decision 14: an owner-facing repeat is a waterfall, never information.
/// </para>
/// <para>
/// THE CHANNEL KEEPS EVERY LINE, THE PHONE GETS THE FIRST. A repeat is still written — it is the
/// session's audit trail, and it is the only record of how often the turn was retried — but under the
/// agent audience, which the mirror never sends. So nothing about the mirror had to learn a new rule,
/// and the decision is made once, by the component that knows it is repeating itself.
/// </para>
/// <para>
/// THE MEMORY IS THE CHANNEL FILE, NOT THE DISPATCHER'S TRACKER — and the incident is why. The
/// tracker dies with the process, and the third alert of 2026-09-11 was written by a dispatcher four
/// seconds old. What the channel already says survives any restart and costs nothing to bound: it is
/// read once, when a turn stalls. Compaction may move the first alert into the archive, in which case
/// the next repeat reaches the phone once more — the safe direction for an alert, and bounded by the
/// same compaction that caused it.
/// </para>
/// <para>
/// KEYED ON MEMBER AND TURN NUMBER, the subject's stable part. The tail after the dash is the outcome
/// and the attempt count, which vary between repeats of the same stall ("error × 3", "failed outside
/// the process × 3") and must not make a repeat look new. A DIFFERENT turn number is a different
/// stall and always reaches the owner.
/// </para>
/// </summary>
public static class StallAlert_Decider
{
    /// <summary>
    /// The one spelling of the alert's subject. The dispatcher writes through here and
    /// <see cref="Has_AlreadyReachedOwner"/> matches through <see cref="Build_SubjectStem"/>, so the
    /// writer and the reader cannot drift apart.
    /// </summary>
    public static string Build_Subject(string memberId, int turnNumber, string outcomeTail)
    {
        return $"{Build_SubjectStem(memberId, turnNumber)} — {outcomeTail}";
    }

    /// <summary>The stable part of the subject: which session, which turn.</summary>
    public static string Build_SubjectStem(string memberId, int turnNumber)
    {
        return $"{PrintTurn_Words.TURN_STALLED_SUBJECT} {memberId} turn {turnNumber}";
    }

    /// <summary>
    /// The audience for this stall's alert. <paramref name="roleAudience"/> is who a first alert goes
    /// to for this role (the dispatcher's <c>Stall_Audience</c>); an agent-audience alert is returned
    /// unchanged, because it never reached the phone in the first place.
    /// </summary>
    public static AppEntryAudiences Resolve_Audience(AppEntryAudiences roleAudience, IReadOnlyList<IChannelEntry> channelEntries, string memberId, int turnNumber)
    {
        if (roleAudience != AppEntryAudiences.Owner)
            return roleAudience;

        return Has_AlreadyReachedOwner(channelEntries, memberId, turnNumber)
            ? AppEntryAudiences.Agent
            : AppEntryAudiences.Owner;
    }

    /// <summary>
    /// Whether an owner-facing stall alert for THIS member's THIS turn is already in the channel.
    ///
    /// <para>
    /// WALKED BACK FROM THE END, AND IT STOPS AT ANOTHER TURN. Every record between two stalls of one
    /// turn is about that turn (its <c>turn_ended</c> records, its repeats), so the first record of
    /// this session naming a DIFFERENT turn number means the numbering has moved on — or started
    /// over, which is what a deleted <c>print-session.json</c> does. An alert older than that belongs
    /// to another turn that happened to carry the same number, and must not silence this one.
    /// </para>
    /// </summary>
    public static bool Has_AlreadyReachedOwner(IReadOnlyList<IChannelEntry> channelEntries, string memberId, int turnNumber)
    {
        var stalledStem = Build_SubjectStem(memberId, turnNumber);
        var endedStem = $"{PrintTurn_Words.TURN_ENDED_SUBJECT} {memberId} turn {turnNumber}";
        var stalledOfMember = $"{PrintTurn_Words.TURN_STALLED_SUBJECT} {memberId} turn ";
        var endedOfMember = $"{PrintTurn_Words.TURN_ENDED_SUBJECT} {memberId} turn ";

        for (var index = channelEntries.Count - 1; index >= 0; index--)
        {
            var entry = channelEntries[index];

            if (entry.Author != ChannelAuthors.App)
                continue;

            var isAgentTagged = AppEntryAudience_Tag.Is_AgentTagged(entry.Subject);
            var subject = Strip_AgentTag(entry.Subject);

            if (Names_Turn(subject, stalledStem))
            {
                // A repeat already filed for the record says nothing about the phone; keep looking
                // for the one that was sent.
                if (!isAgentTagged)
                    return true;

                continue;
            }

            if (Names_Turn(subject, endedStem))
                continue;

            if (subject.StartsWith(stalledOfMember, StringComparison.Ordinal) || subject.StartsWith(endedOfMember, StringComparison.Ordinal))
                return false;
        }

        return false;
    }

    /// <summary>The log line for a repeat kept off the phone — one per repeated stall, never silent.</summary>
    public static string Describe_Repeat(string memberId, int turnNumber)
    {
        return $"'{memberId}' turn {turnNumber} stalled again — its first stall alert already reached the owner, so this one is recorded in the channel for the session only (one stalled turn, one message on the phone)";
    }

    /// <summary>
    /// "turn stalled sup turn 35" names turn 35 when it is the whole subject or is followed by a space
    /// — never when it is followed by a digit, which is turn 350.
    /// </summary>
    static bool Names_Turn(string subject, string stem)
    {
        return subject.Length == stem.Length
            ? subject == stem
            : subject.StartsWith(stem + " ", StringComparison.Ordinal);
    }

    static string Strip_AgentTag(string subject)
    {
        var trimmed = subject.TrimStart();

        return AppEntryAudience_Tag.Is_AgentTagged(trimmed)
            ? trimmed[AppEntryAudience_Tag.AGENT_TAG.Length..].TrimStart()
            : trimmed;
    }
}
