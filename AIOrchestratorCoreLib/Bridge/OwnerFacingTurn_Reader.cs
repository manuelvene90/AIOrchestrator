using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// HAS THE TURN THE OWNER IS WAITING ON FAILED — read from the dispatcher's own record of the session
/// that talks to them (the general supervisor, the solo of a basic orchestration, or the supervisor of
/// a crew; <see cref="OwnerFacingSession_Locator"/> decides which, so this cannot disagree with the
/// usage path about who that is).
///
/// <para>
/// THE STATE FILE, NOT THE CHANNEL. <c>failed_attempts</c> counts the consecutive failed attempts of
/// the CURRENT turn and is reset only when a turn completes, so a non-zero value means exactly "the
/// turn that carries whatever is pending — the owner's message included — has failed and not yet
/// run". It survives a restart, which the dispatcher's in-memory tracker does not, and it is written
/// by the app rather than by an agent (decision 12).
/// </para>
/// </summary>
public static class OwnerFacingTurn_Reader
{
    /// <summary>
    /// The failed attempts of the owner-facing session's current turn, and why they could not be read
    /// when they could not.
    ///
    /// <para>
    /// NO STATE FILE IS ZERO, NOT A FAILURE: a session run in a terminal is not driven by the
    /// dispatcher and has no attempts to count. AN UNREADABLE ONE IS ZERO WITH A REASON — decision 21:
    /// a predicate that cannot be evaluated says so and allows, so the caller keeps its old behaviour
    /// and logs the reason, rather than inventing a failure the file does not show.
    /// </para>
    /// </summary>
    public static (int FailedAttempts, string? ReadFailure) Read_CurrentTurnFailures(ISupervisionPaths paths, string orchId, IOrchestrationSession? session)
    {
        var (role, memberId) = OwnerFacingSession_Locator.Resolve_Session(orchId, session);

        try
        {
            var state = PrintSessionState_Store.Read_OrNull(PrintSessionState_Store.Get_StateFile(paths, role, orchId, memberId));

            return (state?.FailedAttempts ?? 0, null);
        }
        catch (Exception ex)
        {
            return (0, $"'{memberId}' state file: {ex.Message}");
        }
    }
}
