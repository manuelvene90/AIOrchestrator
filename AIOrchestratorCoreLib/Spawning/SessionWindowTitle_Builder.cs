using AIOrchestratorCoreLib.Sessions;

namespace AIOrchestratorCoreLib.Spawning;

/// <summary>
/// THE ONE PLACE A SESSION'S WINDOW TITLE IS SPELLED. Three components depend on it and none of
/// them can see the others' copy: <see cref="SpawnCommand_Builder"/> titles the window,
/// <c>SessionTerminator</c> closes that window by title when the app exits, and the app's
/// "Show session" button focuses it by title.
///
/// It drifted exactly as a formatter written three times does. A solo's window was spawned as
/// "SOLO · &lt;orch&gt;" while both readers built "SOLO-1 · &lt;orch&gt;" from its member id, so no
/// reader ever found it: the window outlived every app shutdown displaying "[process exited with
/// code 4294967295]" — the app's own tree-kill, read by the owner as a session failing to start —
/// and "Show session" reported no window at all. Implementers and reviewers were unaffected only
/// because their titles happen to BE their member ids, which is what hid the defect.
///
/// Solo keeps the short form deliberately: a basic orchestration has exactly one session, so
/// "SOLO-1" would number a thing there is only ever one of.
/// </summary>
public static class SessionWindowTitle_Builder
{
    public const string GENERAL_TITLE = "GENERAL";

    const string SEPARATOR = " · ";
    const string SUPERVISOR_LABEL = "SUP";
    const string COMMUNICATOR_LABEL = "COM";
    const string SOLO_LABEL = "SOLO";

    public static string Build_ForSupervisor(string orchId) => $"{SUPERVISOR_LABEL}{SEPARATOR}{orchId}";

    public static string Build_ForCommunicator(string orchId) => $"{COMMUNICATOR_LABEL}{SEPARATOR}{orchId}";

    /// <summary>
    /// Implementers, reviewers and the solo. The member id carries the kind (that is the documented
    /// contract of <see cref="MemberKind_Ids.Resolve_Kind"/>), so a pid-file path is enough to derive
    /// the title — which is what lets the terminator close a window it has only a path for.
    /// </summary>
    public static string Build_ForMember(string memberId, string orchId)
    {
        return MemberKind_Ids.Resolve_Kind(memberId) == MemberKinds.Solo
            ? $"{SOLO_LABEL}{SEPARATOR}{orchId}"
            : $"{memberId.ToUpperInvariant()}{SEPARATOR}{orchId}";
    }
}
