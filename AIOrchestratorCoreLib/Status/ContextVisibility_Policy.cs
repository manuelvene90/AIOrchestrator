using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Status.SessionContextUsage;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// WHOSE context percentage is worth showing, on WHICH surface. The rule is the owner's, given with
/// the request on 2026-08-21: the session they TALK TO is always shown, and the ones they do not
/// talk to appear only once they are in trouble.
///
/// The two thresholds differ ON PURPOSE and the difference is the surface's cadence. The Telegram
/// status line is re-read at a glance all day, so a member earns a place on it only at 90%, near
/// the end. The half-hourly digest is read deliberately and has room, so it starts at 80% — early
/// enough that the owner can still do something about it before a compaction lands mid-task.
///
/// IT LIVES HERE RATHER THAN AT THE THREE CALL SITES because three inline comparisons is how the
/// surfaces start disagreeing: one gets changed, the others do not, and the owner sees a member on
/// the digest that the status line says nothing about.
/// </summary>
public static class ContextVisibility_Policy
{
    /// <summary>
    /// Implementers and reviewers reach the Telegram status line at this much context.
    ///
    /// COMPARED INCLUSIVELY, so 90 itself shows. The owner said "above 90%", and read strictly that
    /// would hide the exact number they named — the probe reports whole percentages, so 90 is a
    /// value sessions really sit at, and every other threshold in this app (the 90/95/97/98/99/100
    /// limit alerts) fires AT its number rather than one past it.
    /// </summary>
    public const double STATUS_LINE_MEMBER_PERCENT = 90;

    /// <summary>
    /// The same for the half-hourly digest, and lower for the reason given on the class: it is the
    /// surface with room to warn early. Inclusive for the same reason as above.
    /// </summary>
    public const double PERIODIC_DIGEST_MEMBER_PERCENT = 80;

    /// <summary>
    /// The supervisor of a crew, and the general supervisor: ALWAYS shown when there is a reading.
    /// That session is the owner's phone line — if its window fills, everything they can see stops
    /// with it, so there is no threshold at which they would rather not know.
    /// </summary>
    public static bool Show_Supervisor(ISessionContextUsage? usage)
    {
        return usage != null;
    }

    /// <summary>A member's row on the Telegram status line.</summary>
    public static bool Show_Member_OnStatusLine(string memberId, ISessionContextUsage? usage)
    {
        return Show_Member(memberId, usage, STATUS_LINE_MEMBER_PERCENT);
    }

    /// <summary>A member's line in the half-hourly digest.</summary>
    public static bool Show_Member_InPeriodicDigest(string memberId, ISessionContextUsage? usage)
    {
        return Show_Member(memberId, usage, PERIODIC_DIGEST_MEMBER_PERCENT);
    }

    /// <summary>
    /// A SOLO IS NOT A MEMBER LIKE THE OTHERS — it is the session the owner is talking to, the
    /// basic orchestration's whole crew, so it takes the supervisor's rule and not the threshold.
    /// Deciding that from the member id rather than from a flag keeps it true for a solo the caller
    /// forgot to special-case, which is how the first draft of this shipped: two id literals spelled
    /// out at one call site and nothing at the other two.
    /// </summary>
    static bool Show_Member(string memberId, ISessionContextUsage? usage, double thresholdPercent)
    {
        if (usage == null)
            return false;

        if (MemberKind_Ids.Resolve_Kind(memberId) == MemberKinds.Solo)
            return true;

        return usage.UsedPercent >= thresholdPercent;
    }
}
