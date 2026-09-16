using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.SessionFiles;

/// <summary>
/// WHERE A SESSION'S PRIVATE FILES LIVE — one answer, reached from the one place that already had to
/// know: <see cref="PrintSessionState_Store.Get_StateFile"/>.
///
/// <para>
/// The rule is not "the member's folder". A member and a solo get a folder of their own; the
/// SUPERVISOR and the COMMUNICATOR share the orchestration folder and are told apart by a
/// <c>.supervisor.</c> / <c>.communicator.</c> prefix on the file name; the general supervisor sits
/// in its own folder. <c>TurnLog_Store.Get_File</c> already re-derived that prefix by string-editing
/// the state file's name, and <c>status.jsonl</c> would have been the second copy of the same
/// derivation — so it is here once and both call it (CLAUDE.md decision 12).
/// </para>
/// </summary>
public static class SessionFile_Locator
{
    /// <summary>
    /// The session's own file named <paramref name="fileName"/>, beside its state file and carrying
    /// the same role prefix, so two roles sharing a folder never share a file.
    /// </summary>
    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId, string fileName)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(paths, role, orchId, memberId);
        var folder = Path.GetDirectoryName(stateFile) ?? paths.Root;
        var prefix = Path.GetFileName(stateFile).Replace(PrintSessionState_Store.STATE_FILE_NAME, string.Empty);

        return Path.Combine(folder, prefix + fileName);
    }
}
