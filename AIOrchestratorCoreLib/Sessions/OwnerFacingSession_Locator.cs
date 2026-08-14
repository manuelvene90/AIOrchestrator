using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// WHICH SESSION IS THE OWNER ACTUALLY WAITING ON — and therefore whose usage file answers "are they
/// mid-turn right now?".
///
/// Three places asked that question and each built the path itself, always the SUPERVISOR's:
/// <c>&lt;orch&gt;/.usage.json</c>. In a basic orchestration nothing ever writes that file — the solo
/// writes its own, inside its member folder — so a solo read as permanently idle, and every
/// consequence landed on the owner:
///
///   - "the owner is still waiting for your reply", sent while the solo was mid-turn, 16 times on one
///     channel in one evening;
///   - never the "busy — working on X" narration a crew gets, so a working solo looked absent;
///   - and each of those notices is an append, which wakes the session again — a full context reload
///     bought with nothing to act on.
///
/// One locator, so the readers cannot disagree about who talks to the owner.
/// </summary>
public static class OwnerFacingSession_Locator
{
    /// <summary>
    /// <paramref name="session"/> may be null — the general supervisor has no session record at all,
    /// and a store lookup can miss. Both fall back to the folder-level slot, which is what all three
    /// call sites used before, so an unknown orchestration behaves exactly as it always did rather
    /// than newly throwing inside the mirror tick.
    /// </summary>
    public static string Get_UsageFile(ISupervisionPaths paths, string orchId, IOrchestrationSession? session)
    {
        if (orchId == ChannelDiscovery.GENERAL_ORCH_ID)
            return Path.Combine(paths.GeneralFolder, UsageTotals_Reader.SESSION_USAGE_FILE);

        var soloMemberId = Find_LiveSoloMemberId_OrNull(session);

        return soloMemberId == null
            ? Path.Combine(paths.Get_OrchestrationFolder(orchId), UsageTotals_Reader.SESSION_USAGE_FILE)
            : Path.Combine(paths.Get_ImplementerFolder(orchId, soloMemberId), UsageTotals_Reader.SESSION_USAGE_FILE);
    }

    /// <summary>
    /// A LIVE solo, never merely a solo in the roster. Member folders are audit trail and closed ones
    /// never leave, so a promoted orchestration keeps its retired solo for ever — judging the new
    /// supervisor's turns by the file its predecessor stopped writing would report a turn that ended
    /// hours ago as still running. And a basic orchestration whose solo has been CLOSED has nobody
    /// talking to the owner at all: the slot nobody writes is then the honest answer, because
    /// "mid-turn" reads false there, which is exactly true.
    /// </summary>
    static string? Find_LiveSoloMemberId_OrNull(IOrchestrationSession? session)
    {
        if (session == null || !OrchestrationShape.Is_BasicOrchestration(session.SupervisorSpawnedUtc))
            return null;

        foreach (var member in session.Members)
        {
            if (member.ClosedUtc == null && MemberKind_Ids.Resolve_Kind(member.MemberId) == MemberKinds.Solo)
                return member.MemberId;
        }

        return null;
    }
}
