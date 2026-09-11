using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.TurnResult;

namespace AIOrchestratorCoreLib.Running.TurnExecutor;

/// <summary>
/// HOW ONE TURN REACHES THE MODEL — the seam between the dispatcher's scheduling and the transport.
///
/// <para>
/// The dispatcher owns everything that is the same for every transport, and that list is the whole
/// argument for this interface existing: the queue (which IS the channel), the coalesce window, the
/// per-session serialisation, the slot limits, idempotency by request id, the attempt counter and
/// its stall alert, the channel entry, the <c>turn_ended</c> record. An executor owns only the part
/// that differs — a process per turn with the prompt on stdin, or one living process with the
/// prompt on a pipe. Duplicating the first list per transport is how two schedulers drift into two
/// different answers about whether a turn already ran.
/// </para>
/// <para>
/// It takes the PENDING ENTRIES rather than a finished prompt because the two transports disagree
/// about the first turn: print boots by putting the role command on the command line and sends no
/// prompt at all, while a stream has no command line to put it on and boots by sending it as its
/// first message. That decision belongs to whoever knows the transport.
/// </para>
/// <para>
/// The entries arrive PAIRED WITH THEIR CHANNEL and the whole source set comes with them, because a
/// session woken by several channels cannot answer without being told which is which — and the prompt
/// is the only place that can tell it.
/// </para>
/// </summary>
public interface ITurnExecutor
{
    SessionRunners Kind { get; }

    Task<ITurnResult> Execute_Async(
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
        CancellationToken cancellationToken);

    /// <summary>
    /// ONE SHORT TURN ON A TRANSCRIPT THE DEADLINE KILLED — "write where you are and stop". It is
    /// always PRINT-SHAPED (<c>claude -p --resume &lt;killed id&gt; --max-budget-usd …</c>) whatever the
    /// session's transport is, because a stream session's living process was killed along with its
    /// turn and <c>--max-budget-usd</c> works only with <c>--print</c> — so the stream executor runs
    /// it on the rung below rather than growing a second process seam of its own.
    ///
    /// <para>
    /// NULL means this executor cannot run one — the only honest answer for a transport with no
    /// print rung wired beneath it. The dispatcher then says so in one line and falls back to
    /// today's behaviour (the attempt counts, the pending set is retried): decision 21, a component
    /// that cannot evaluate its predicate says which one and does not invent a result.
    /// </para>
    /// </summary>
    Task<ITurnResult?> Execute_ClosingTurn_Async(
        IPrintSessionState state,
        IRoleRunnerConfig roleConfig,
        string resumeSessionId,
        string closingRequestId,
        IReadOnlyList<TurnSource.ITurnSource> sources,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases whatever the executor is holding for this session — a resident process, in the only
    /// implementation that holds anything. Called when a turn's transport failed and the next
    /// attempt must start clean, and when the session is closed.
    /// </summary>
    void Release(string orchId, string memberId);

    /// <summary>
    /// Whether this session has just CHANGED TRANSPORT — walked down the fallback ladder — and
    /// clears the notice. The attempts spent proving the old transport broken are not evidence
    /// against the new one, so the dispatcher starts its counter again: without this the two
    /// counters coincide (three deaths, three attempts) and the session stalls on the rung it just
    /// stepped off, having never once tried the one below.
    /// </summary>
    bool Consume_RunnerChange(string orchId, string memberId);

    /// <summary>Ends every resident process. The app is exiting; a stream left running would outlive it.</summary>
    Task Stop_Async();
}
