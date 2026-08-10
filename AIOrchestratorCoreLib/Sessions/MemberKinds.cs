namespace AIOrchestratorCoreLib.Sessions;

/// <summary>
/// What a session member is FOR. The kind decides how it is spawned and what it is allowed to do —
/// a reviewer is read-only BY CONSTRUCTION (denied the editing tools and guarded by a hook), not by
/// a "do not edit anything" sentence at the top of its brief that it merely remembers to honour.
/// </summary>
public enum MemberKinds
{
    /// <summary>Writes code in its own worktree, commits, reports at boundaries.</summary>
    Implementer,

    /// <summary>Reviews — adversarially by default. Cannot edit, cannot commit, has no worktree.</summary>
    Reviewer,

    /// <summary>
    /// A BASIC orchestration: one session, talking straight to the owner, with no supervisor, no
    /// reviewer and none of the gates between them. For small endeavours where the whole
    /// coordination apparatus costs more than the work it coordinates.
    /// </summary>
    Solo,
}

public static class MemberKind_Ids
{
    public const string IMPLEMENTER_PREFIX = "imp-";
    public const string REVIEWER_PREFIX = "rev-";
    public const string SOLO_PREFIX = "solo-";

    public static string Build_Prefix(MemberKinds kind)
    {
        return kind switch
        {
            MemberKinds.Implementer => IMPLEMENTER_PREFIX,
            MemberKinds.Reviewer => REVIEWER_PREFIX,
            MemberKinds.Solo => SOLO_PREFIX,
            _ => throw new Exception($"Unhandled MemberKinds: {kind}"),
        };
    }

    /// <summary>The id carries the kind, so a folder, a pid file or a window title is self-describing.</summary>
    public static MemberKinds Resolve_Kind(string memberId)
    {
        if (memberId.StartsWith(REVIEWER_PREFIX, StringComparison.OrdinalIgnoreCase))
            return MemberKinds.Reviewer;

        if (memberId.StartsWith(SOLO_PREFIX, StringComparison.OrdinalIgnoreCase))
            return MemberKinds.Solo;

        return MemberKinds.Implementer;
    }

    /// <summary>
    /// A BASIC orchestration is one with a solo member: no supervisor was ever spawned, so nothing
    /// may go looking for one (the watchdog would respawn a supervisor forever). Derived from the
    /// members rather than stored, exactly like the member kinds — session.json needs no migration
    /// and an orchestration cannot end up disagreeing with itself about what it is.
    /// </summary>
    public static bool Is_BasicOrchestration(IEnumerable<string> memberIds)
    {
        foreach (var memberId in memberIds)
        {
            if (Resolve_Kind(memberId) == MemberKinds.Solo)
                return true;
        }

        return false;
    }
}
