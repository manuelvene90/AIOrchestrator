using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.TurnLog;

/// <summary>
/// WHICH SESSION THE OWNER MEANT, from a word typed on a phone. "/tail 2", "/tail imp-2", "/tail
/// rev-1", "/tail sup" — and, when they typed nothing, the answer is a list of what is open rather
/// than a guess: picking a member for them is how the owner reads a healthy session's log and
/// concludes the broken one is fine.
/// </summary>
public static class TurnLog_Locator
{
    public static (SessionRoles Role, string MemberId)? Resolve_OrNull(IOrchestrationSession session, string word)
    {
        var text = word.Trim().ToLowerInvariant();

        if (text.Length == 0)
            return null;

        if (text is "sup" or "supervisor")
            return (SessionRoles.Supervisor, SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);

        var openMembers = session.Members.Where(member => member.ClosedUtc == null).Select(member => member.MemberId).ToList();

        // A member id typed in full wins over any inference — "rev-1" is never implementer 1.
        if (openMembers.Contains(text))
            return (SessionRole_Names.From_MemberKind(MemberKind_Ids.Resolve_Kind(text)), text);

        // A bare number means an implementer, which is what the existing /imp already taught the owner.
        var digits = new string([.. text.Where(char.IsAsciiDigit)]);

        if (digits.Length == 0 || text.Any(character => char.IsLetter(character) && !text.StartsWith("imp", StringComparison.Ordinal)))
            return null;

        var memberId = $"imp-{digits}";

        return openMembers.Contains(memberId) ? (SessionRoles.Implementer, memberId) : null;
    }

    public static string Describe_Choices(IOrchestrationSession session, string command)
    {
        var open = session.Members.Where(member => member.ClosedUtc == null).Select(member => member.MemberId).ToList();

        open.Insert(0, SessionLaunch.SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);

        return $"which session? e.g. {command} 1 (open: {string.Join(", ", open)})";
    }

    public static string Get_LogFile(ISupervisionPaths paths, string orchId, (SessionRoles Role, string MemberId) target)
    {
        return TurnLog_Store.Get_File(paths, target.Role, orchId, target.MemberId);
    }
}
