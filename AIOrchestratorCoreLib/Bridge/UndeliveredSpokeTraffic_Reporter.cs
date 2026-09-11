using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionLaunch;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHAT A CLOSING MEMBER'S SPOKE STILL HAD PENDING, SAID OUT LOUD BEFORE IT STOPS BEING A SOURCE.
///
/// <para>
/// Closing a member is <c>Close_Member</c> plus a kill: no drain, no read of the spoke. From that
/// moment <c>TurnSources_Resolver</c> skips the channel, so an entry the supervisor was never handed
/// is never handed over — and because the cursor is deliberately kept, it is not re-delivered either.
/// Nothing was lost from the FILE, and nothing is re-run; what is lost is that anybody knows.
/// </para>
/// <para>
/// THE EXPOSURE WAS ONE TICK AND IS NOW A DIGEST WINDOW. Before the member digest (2026-09-09) an
/// ordinary report was handed over on the next tick, so the gap between "filed" and "delivered" was
/// seconds; a held report now waits up to <c>printRunner.memberDigestMinutes</c>. The re-review of
/// that change measured the widening and called it unacceptable as shipped, for the right reason: the
/// entry most likely to be in flight when a supervisor closes a member is the member's LAST one —
/// "done, pushed, here is the diff and the suite output" — and the supervisor may then close a ledger
/// line whose evidence it never read.
/// </para>
/// <para>
/// A LINE, NOT A DRAIN. Draining the spoke would mean running one more supervisor turn inside a close,
/// which is the close deciding to spend a turn nobody asked for; and the honest half of the problem is
/// not the delivery, it is the silence. So this says what is being dropped, to the log the app tails —
/// never to Telegram, which decision 15 reserves for what the owner can act on, and this is the
/// supervisor's own decision taking effect. When it cannot answer, it says which read failed rather
/// than inventing a verdict (decision 21).
/// </para>
/// <para>
/// It lives here rather than in <c>BridgeEngineModel</c> because that file takes no new lines by
/// inertia (`.claude/rules/code-conventions.md`): the engine keeps the one call.
/// </para>
/// </summary>
public static class UndeliveredSpokeTraffic_Reporter
{
    public static void Log_BeforeClosing(ISupervisionPaths paths, IOrchestrationLog log, string orchId, string memberId)
    {
        try
        {
            var describedPending = Describe_Pending_OrNull(paths, orchId, memberId);

            if (describedPending == null)
                return;

            log.Log_Warning(orchId, describedPending);
        }
        catch (Exception ex)
        {
            // The predicate could not be evaluated, so it says so and says nothing else: a close is
            // not stopped by this, and a silence here would be the very thing it exists to remove.
            log.Log_Error(orchId, $"'{memberId}' is being closed and what its spoke still had pending could not be read, so this cannot say whether anything is being dropped", ex);
        }
    }

    /// <summary>
    /// The line, or null when there is nothing to say — no bridge-driven supervisor for this
    /// orchestration (nobody was going to be handed anything), no cursor for this spoke, or an empty
    /// pending set. Split out so a test can read the sentence without a log sink.
    /// </summary>
    public static string? Describe_Pending_OrNull(ISupervisionPaths paths, string orchId, string memberId)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(paths, SessionRoles.Supervisor, orchId, SessionLaunch_Factory.SUPERVISOR_MEMBER_ID);
        var state = PrintSessionState_Store.Read_OrNull(stateFile);

        var cursor = state?.Cursors.FirstOrDefault(known => string.Equals(known.SourceKey, memberId, StringComparison.OrdinalIgnoreCase));

        if (cursor == null)
            return null;

        var channelFile = paths.Get_ImplementerChannelFile(orchId, memberId);

        if (!File.Exists(channelFile))
            return null;

        var pending = PrintTurn_Trigger.Select_Pending(SessionRoles.Supervisor, ChannelHistory_Cache.Read_Entries(channelFile), cursor);

        if (pending.Count == 0)
            return null;

        return $"'{memberId}' is being closed with {pending.Count} entr{(pending.Count == 1 ? "y" : "ies")} its supervisor was never handed — the spoke stops being a source, so these stay in its channel file and reach nobody: "
            + string.Join("; ", pending.Select(entry => $"[{entry.Index}] {entry.Subject}"));
    }
}
