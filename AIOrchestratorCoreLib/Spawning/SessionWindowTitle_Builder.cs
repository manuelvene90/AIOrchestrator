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

    /// <summary>
    /// THE FULL WINDOW TITLE: the match fragment, plus the orchestration's display name when it has
    /// one. Everything above builds the FRAGMENT — what the focuser, the organizer and the terminator
    /// search for — and this is the only place the display-name suffix is spelled.
    ///
    /// <para>
    /// It exists because that suffix was about to be written twice: the live window rename composed
    /// it inline while spawn was about to compose it again — the duplicate-formatter mistake this
    /// repo has paid for before (CLAUDE.md decision 12: two copies of a duration formatter, one
    /// missing its guard). The rename is gone since 2026-08-21 (see TerminalWindow_Focuser — it
    /// could never change what Windows Terminal draws), so spawn is now the ONLY caller, and this
    /// stays the one place the suffix is spelled.
    /// </para>
    /// <para>
    /// THE SUFFIX IS APPENDED AFTER THE SEPARATOR AND THAT IS LOAD-BEARING, not decoration.
    /// <see cref="AIOrchestratorCoreLib.WindowFocus.SessionWindowTitle_Matcher"/> accepts a fragment
    /// that is followed by end-of-title or by exactly this separator, so a titled window is still
    /// found by the bare fragment every consumer builds from ids alone — including the terminator,
    /// which has only a pid-file path and cannot know the display name. Glue the name on with
    /// anything else (a dash, brackets, a bare space) and every one of them stops finding the window:
    /// Show reports nothing, Organize skips it, and app exit leaves the terminal open.
    /// </para>
    /// </summary>
    public static string Build_Title(string titleFragment, string? displayName)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? titleFragment
            : $"{titleFragment}{SEPARATOR}{displayName.Trim()}";
    }
}
