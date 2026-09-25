using AIOrchestratorCoreLib.Sessions.OrchestrationSession;

namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// WHO IS IN AN ENDEAVOUR, answered from the session list and nowhere else. Spec §3.2 (approved
/// 2026-09-23): "Membership is derived, never stored as a list." The siblings of X are the sessions
/// whose <see cref="IOrchestrationSession.EndeavourId"/> equals X's — there is no <c>siblings[]</c>
/// array to fall out of step, so a close, a crash between two saves or a hand edit cannot leave two
/// sessions disagreeing about who their siblings are.
///
/// <para>
/// IT TAKES THE LIST, NOT THE STORE, on purpose. The engine already holds this tick's
/// <c>Load_All()</c> (<c>Sessions_ThisTick()</c>), and a resolver that asked the store again would
/// add a second enumeration of the supervision root to every caller on the 2-second tick — the call
/// count <c>LoadAllCounting_Store_Fake</c> exists to keep down. The caller decides which snapshot is
/// current; this class only filters it, preserving its order.
/// </para>
/// </summary>
public static class EndeavourMembers_Resolver
{
    /// <summary>
    /// Every member of the endeavour, OPEN AND CLOSED, in <paramref name="sessions"/> order. Closed
    /// members are kept because the shared progress bar sums them (spec §3.5): a finished job that
    /// closed and dropped out of the sum would make the bar go backwards at the moment it succeeded.
    /// </summary>
    public static IReadOnlyList<IOrchestrationSession> Resolve_All(IReadOnlyList<IOrchestrationSession> sessions, string endeavourId)
    {
        return sessions
            .Where(session => Is_MemberOf(session, endeavourId))
            .ToList();
    }

    /// <summary>
    /// The OPEN siblings of <paramref name="self"/> — same endeavour, not closed, never self itself.
    /// Empty when self is not linked: a null endeavour is "no endeavour", and must never match the
    /// other unlinked sessions, which would make every ordinary orchestration a sibling of every other.
    /// </summary>
    public static IReadOnlyList<IOrchestrationSession> Resolve_OpenSiblings(IReadOnlyList<IOrchestrationSession> sessions, IOrchestrationSession self)
    {
        if (self.EndeavourId == null)
            return [];

        return sessions
            .Where(session => Is_MemberOf(session, self.EndeavourId)
                              && session.ClosedUtc == null
                              && !string.Equals(session.OrchId, self.OrchId, StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>
    /// How many members of the endeavour are OPEN, the requester included — the number owner decision
    /// O2 caps (N open members, not N siblings besides the one asking).
    /// </summary>
    public static int Count_Open(IReadOnlyList<IOrchestrationSession> sessions, string endeavourId)
    {
        return sessions.Count(session => Is_MemberOf(session, endeavourId) && session.ClosedUtc == null);
    }

    static bool Is_MemberOf(IOrchestrationSession session, string endeavourId)
    {
        return session.EndeavourId != null && string.Equals(session.EndeavourId, endeavourId, StringComparison.Ordinal);
    }
}
