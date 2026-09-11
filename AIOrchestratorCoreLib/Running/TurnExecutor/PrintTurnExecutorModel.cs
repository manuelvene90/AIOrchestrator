using AIOrchestratorCoreLib.Running.ClosingTurn;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.PrintTurnRunner;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.StatePack;
using AIOrchestratorCoreLib.Running.TurnLog;
using AIOrchestratorCoreLib.Running.TurnResult;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.TurnExecutor;

/// <summary>
/// The transient transport, unchanged: one <c>claude -p</c> per turn, the role command as the
/// positional prompt on the first turn and the pending entries on stdin after. Everything here was
/// the dispatcher's own body before the seam existed — moved, not rewritten.
///
/// The one addition is the turn log: the result JSON is appended as one line per turn, so /log and
/// /tail can answer for a print session too instead of only for a stream.
/// </summary>
internal sealed class PrintTurnExecutorModel(ISupervisionPaths paths, IPrintTurnRunner turnRunner) : ITurnExecutor
{
    readonly ISupervisionPaths _paths = paths;
    readonly IPrintTurnRunner _turnRunner = turnRunner;

    public SessionRunners Kind => SessionRunners.Print;

    public async Task<ITurnResult> Execute_Async(
        IPrintSessionState state,
        IRoleRunnerConfig roleConfig,
        string sessionId,
        bool resumeTranscript,
        string requestId,
        IReadOnlyList<PendingEntry> pending,
        IReadOnlyList<TurnSource.ITurnSource> sources,
        IReadOnlyList<int> alreadyExecutedTurns,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var arguments = PrintTurnCommand_Builder.Build_Arguments(state, roleConfig, sessionId, resumeTranscript, null);
        // A resumed turn takes its entries on stdin, as it always has. A FRESH turn takes NOTHING on
        // stdin: measured 2026-09-09 (claude 2.1.263), stdin is appended to the positional prompt in
        // the same message, so a slash command's $ARGUMENTS swallows it — `solo` and `communicator`
        // interpolate $ARGUMENTS into paths verbatim. Its memory travels in a FILE instead: the pack,
        // written beside the files the role reads at boot (StatePack_Locator), holding the pending
        // entries, the brief, the last own report, the ledger lines and the git state. The skill's boot
        // sequence says "if pack.md exists, read it first". A boot turn (nothing pending) writes none:
        // the role command's own boot sequence is the right thing there.
        var fresh = roleConfig.Resume == ResumeModes.Fresh;
        string? prompt = null;

        if (resumeTranscript)
            prompt = PrintTurnPrompt_Builder.Build_FollowUp(requestId, pending, alreadyExecutedTurns, sources);
        else if (fresh && pending.Count > 0)
            Write_StatePack(state, requestId, pending, sources);

        var result = await _turnRunner.Run_Async(arguments, prompt, state.WorkingDirectory, environment, timeout, cancellationToken);

        TurnLog_Store.Append_TurnResult(TurnLog_Store.Get_File(_paths, state.Role, state.OrchId, state.MemberId), requestId, result);

        return result;
    }

    /// <summary>
    /// THE CLOSING TURN, which this transport can always run: one more process, resuming the
    /// transcript the deadline killed, with a spend cap and a prompt that forbids continuing the
    /// task. Its result is logged as its own kind, so /tail shows a closing turn as a closing turn
    /// rather than as a second attempt at the work.
    /// </summary>
    public async Task<ITurnResult?> Execute_ClosingTurn_Async(
        IPrintSessionState state,
        IRoleRunnerConfig roleConfig,
        string resumeSessionId,
        string closingRequestId,
        IReadOnlyList<TurnSource.ITurnSource> sources,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var arguments = PrintTurnCommand_Builder.Build_ClosingTurnArguments(state, roleConfig, resumeSessionId, null);
        var prompt = ClosingTurnPrompt_Builder.Build(closingRequestId, sources);

        var result = await _turnRunner.Run_Async(arguments, prompt, state.WorkingDirectory, environment, timeout, cancellationToken);

        TurnLog_Store.Append_ClosingTurnResult(TurnLog_Store.Get_File(_paths, state.Role, state.OrchId, state.MemberId), closingRequestId, result);

        return result;
    }

    /// <summary>
    /// Reads what the bridge holds and writes the pack. Guarded as a whole on top of the reader's own
    /// per-section guards: a pack that cannot be written must not stop the turn — the session then
    /// finds no pack and falls back to its boot sequence, which is today's behaviour, not a failure.
    /// </summary>
    void Write_StatePack(IPrintSessionState state, string requestId, IReadOnlyList<PendingEntry> pending, IReadOnlyList<TurnSource.ITurnSource> sources)
    {
        try
        {
            var inputs = StatePackInputs_Reader.Read(_paths, state, requestId, pending, sources);
            StatePack_Writer.Write(StatePack_Locator.Get_File(_paths, state.Role, state.OrchId, state.MemberId), StatePack_Builder.Build(inputs));
        }
        catch
        {
            // Swallowed by design — see the summary. The session's boot sequence covers the gap.
        }
    }

    public void Release(string orchId, string memberId)
    {
        // Nothing is held between turns: that is what "print" means.
    }

    public bool Consume_RunnerChange(string orchId, string memberId)
    {
        // The bottom rung: there is nothing below to change to.
        return false;
    }

    public Task Stop_Async()
    {
        return Task.CompletedTask;
    }
}
