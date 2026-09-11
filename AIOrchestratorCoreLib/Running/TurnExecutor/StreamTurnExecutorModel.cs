using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.SessionSandbox;
using AIOrchestratorCoreLib.Running.RoleRunnerConfig;
using AIOrchestratorCoreLib.Running.StreamTurn;
using AIOrchestratorCoreLib.Running.TurnLog;
using AIOrchestratorCoreLib.Running.TurnResult;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.TurnExecutor;

/// <summary>
/// THE TUBE. One <c>claude</c> process per session, kept alive between turns, messages written on
/// its stdin — the shape the supervisor-mode study measured at p50 1.27 s against print's 5.77 s,
/// and the reason the supervisor is worth running differently from a member at all.
///
/// <para>
/// THE BOOT IS PART OF THE FIRST TURN, not a turn of its own. There is no positional prompt in this
/// mode, so a fresh session learns its role by being SENT its role command as its first message;
/// the pending entries then go as the second, and the dispatcher sees one turn with one entry, as
/// it does for every other transport. The boot's cost is added to the turn's, because it was spent
/// on it — a turn_ended that reported only the second half would understate the first turn of every
/// session. A resumed process needs no boot: the transcript already contains it.
/// </para>
/// <para>
/// A TRANSPORT FAILURE IS NOT A TURN FAILURE, and only the first walks the ladder. A process that
/// dies mid-conversation is counted per session; at <see cref="STRUCTURAL_FAILURES_BEFORE_FALLBACK"/>
/// the session moves to the next runner this stage implements and says so once — loudly, because a
/// silent degradation is a supervisor that got slower for a reason nobody can find. A turn the CLI
/// answered badly, a timeout, a mute process: those are the dispatcher's attempt counter, which
/// already exists and already alerts.
/// </para>
/// </summary>
internal sealed class StreamTurnExecutorModel : ITurnExecutor
{
    /// <summary>Consecutive process deaths for one session before it stops using this transport.</summary>
    public const int STRUCTURAL_FAILURES_BEFORE_FALLBACK = 3;

    readonly ISupervisionPaths _paths;
    readonly IClaudeInvocation _invocation;
    readonly ITurnExecutor? _fallback;
    readonly IOrchestrationLog _log;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly ISessionSandbox _sandbox;

    readonly Lock _lock = new();
    readonly Dictionary<string, StreamSessionProcess> _processes = [];
    readonly Dictionary<string, int> _structuralFailures = [];
    readonly HashSet<string> _fellBack = [];
    readonly HashSet<string> _fallbackNotices = [];
    readonly Dictionary<string, string> _lastRateLimitJson = [];

    public SessionRunners Kind => SessionRunners.Stream;

    public StreamTurnExecutorModel(ISupervisionPaths paths, IClaudeInvocation invocation, ITurnExecutor? fallback, IOrchestrationLog log, IOrchestratorConfigProvider configProvider, ISessionSandbox sandbox)
    {
        _paths = paths;
        _invocation = invocation;
        _fallback = fallback;
        _log = log;
        _configProvider = configProvider;
        _sandbox = sandbox;
    }

    /// <summary>Read per turn, like every other setting: an edit to config.json applies to the next one.</summary>
    TimeSpan SilenceLimit => _configProvider.Get_Current().Runners.SilenceLimit;

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
        var key = Build_Key(state.OrchId, state.MemberId);

        if (Has_FallenBack(key))
            return await Run_OnFallback_Async(state, roleConfig, sessionId, resumeTranscript, requestId, pending, sources, alreadyExecutedTurns, environment, timeout, cancellationToken);

        var logFile = TurnLog_Store.Get_File(_paths, state.Role, state.OrchId, state.MemberId);
        var (process, booted) = Ensure_Process(key, state, roleConfig, sessionId, resumeTranscript, requestId, environment, logFile);

        var deadline = timeout;
        var bootCost = 0.0;
        var bootElapsed = TimeSpan.Zero;
        ITurnResult? bootResult = null;

        if (booted)
        {
            var bootPrompt = SessionRole_Names.Build_RoleCommand(state.Role, state.OrchId, state.MemberId);
            var boot = await process.Send_AndAwaitResult_Async(bootPrompt, deadline, SilenceLimit, cancellationToken);

            Record_RateLimit_IfNew(state, boot);

            // NOTHING TO CLOSE: the boot is not the turn. Whatever killed it, the brief this turn is
            // about was never sent, so the transcript holds only a role command — resuming it for a
            // closing turn would buy a "where I got to" about nothing and, worse, would mark the
            // unseen brief delivered (adversarial review, 2026-09-09).
            if (Is_TransportFailure(boot))
                return TurnResult_Factory.CreateFrom_NothingToClose(Report_TransportFailure(key, state, boot, requestId, "while booting the session with its role command"));

            bootCost = boot.Result.TotalCostUsd ?? 0;
            bootElapsed = boot.Result.Elapsed;
            bootResult = boot.Result;
            deadline -= boot.Result.Elapsed;

            _log.Log_Info(state.OrchId, $"Stream session '{state.MemberId}' booted with '{bootPrompt}' in {boot.Result.Elapsed.TotalSeconds:F1} s");

            // Same fact by the other route: the boot answered, but it answered so slowly that there is
            // no turn left to send the brief in. Nothing was asked, so there is nothing to close.
            if (deadline <= TimeSpan.Zero)
                return TurnResult_Factory.CreateFrom_NothingToClose(TurnResult_Parser.Parse(-1, timedOut: true, string.Empty, "the boot turn used the whole timeout", bootElapsed));
        }

        // THE BOOT ALONE IS THE WHOLE TURN when nothing is pending — the dispatcher's boot turn, run so
        // the session that owns the owner channel greets and its Telegram topic comes into existence
        // before anybody has said anything to it. The role command has just told it to read its channels
        // and file that greeting, and its answer to THAT is the turn's answer. Sending a second message
        // as well would hand the session a prompt describing no traffic and buy a second entry saying
        // nothing — the cost of one more model turn for the privilege of being confusing.
        if (bootResult != null && pending.Count == 0)
        {
            Clear_StructuralFailures(key);
            return bootResult;
        }

        var prompt = PrintTurnPrompt_Builder.Build_FollowUp(requestId, pending, alreadyExecutedTurns, sources);
        var outcome = await process.Send_AndAwaitResult_Async(prompt, deadline, SilenceLimit, cancellationToken);

        Record_RateLimit_IfNew(state, outcome);

        if (Is_TransportFailure(outcome))
            return Report_TransportFailure(key, state, outcome, requestId, "mid-turn");

        Clear_StructuralFailures(key);

        return bootCost > 0 || bootElapsed > TimeSpan.Zero ? With_BootAddedIn(outcome.Result, bootCost, bootElapsed) : outcome.Result;
    }

    /// <summary>
    /// RUN ON THE RUNG BELOW, always. By the time a closing turn is wanted this session's living
    /// process is gone — <see cref="Report_TransportFailure"/> released it — and the flag that caps
    /// its spend works only with <c>--print</c>, so there is nothing here to run it WITH. Delegated
    /// rather than reimplemented: a second process seam in this class would be a second answer to
    /// "how does a turn reach the model", which is the thing <see cref="ITurnExecutor"/> exists to
    /// prevent. With no fallback wired there is no honest answer but null.
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
        if (_fallback == null)
            return null;

        return await _fallback.Execute_ClosingTurn_Async(state, roleConfig, resumeSessionId, closingRequestId, sources, environment, timeout, cancellationToken);
    }

    public void Release(string orchId, string memberId)
    {
        StreamSessionProcess? process;
        var key = Build_Key(orchId, memberId);

        lock (_lock)
        {
            if (!_processes.Remove(key, out process))
                return;
        }

        process.Dispose();
    }

    public bool Consume_RunnerChange(string orchId, string memberId)
    {
        lock (_lock)
            return _fallbackNotices.Remove(Build_Key(orchId, memberId));
    }

    public async Task Stop_Async()
    {
        StreamSessionProcess[] processes;

        lock (_lock)
        {
            processes = [.. _processes.Values];
            _processes.Clear();
        }

        foreach (var process in processes)
            process.Dispose();

        if (_fallback != null)
            await _fallback.Stop_Async();
    }

    // ----- the process -----

    /// <summary>
    /// The session's living process, started if there is none. <c>booted</c> is true when the
    /// process was started FRESH and still has to be told its role — a resumed one carries it in
    /// the transcript, and telling it twice would make it answer the brief as if it were new.
    /// </summary>
    (StreamSessionProcess Process, bool Booted) Ensure_Process(
        string key,
        IPrintSessionState state,
        IRoleRunnerConfig roleConfig,
        string sessionId,
        bool resumeTranscript,
        string requestId,
        IReadOnlyDictionary<string, string> environment,
        string logFile)
    {
        lock (_lock)
        {
            if (_processes.TryGetValue(key, out var existing))
            {
                if (existing.IsAlive && existing.SessionId == sessionId)
                    return (existing, false);

                // A dead one, or one holding a different transcript (Fresh mode mints a new id every
                // turn): it is replaced rather than reused, and disposing it closes the pipe.
                existing.Dispose();
                _processes.Remove(key);
            }

            var arguments = StreamTurnCommand_Builder.Build_Arguments(state, roleConfig, sessionId, resumeTranscript, null);

            // Resolved HERE, per start, so a ceiling raised in config.json applies to the next
            // process this session gets rather than to the next restart of the host.
            var invocation = _sandbox.Wrap(_invocation);

            _log.Log_Info(state.OrchId, $"Stream session '{state.MemberId}' starting: {Describe_Command(invocation, arguments)}");

            var process = StreamSessionProcess.Start(
                invocation,
                arguments,
                sessionId,
                state.WorkingDirectory,
                environment,
                line => TurnLog_Store.Append_StreamEvent(logFile, requestId, line));

            _processes[key] = process;

            return (process, !resumeTranscript);
        }
    }

    /// <summary>
    /// THE LINE THAT LAUNCHES IT, in the log. The stage-2 live round lost an evening to a session
    /// that was started with a root it could not see, and the bridge never wrote down what it had
    /// run — so the first question ("what was the command?") could only be answered by catching the
    /// process with <c>ps</c> while it lived.
    /// </summary>
    static string Describe_Command(IClaudeInvocation invocation, IReadOnlyList<string> arguments)
    {
        return string.Join(' ', new[] { invocation.Executable }.Concat(invocation.LeadingArguments).Concat(arguments));
    }

    // ----- failures and the ladder -----

    static bool Is_TransportFailure(StreamTurnOutcome outcome)
    {
        return outcome.ProcessDied || outcome.WentSilent || outcome.Result.TimedOut;
    }

    /// <summary>
    /// Reports the failure, frees the process so the next attempt starts a clean one (resuming the
    /// same transcript, which keeps whatever the killed attempt had already done), and counts it
    /// against the ladder — but only a DEATH counts: a timeout is the dispatcher's business.
    /// </summary>
    ITurnResult Report_TransportFailure(string key, IPrintSessionState state, StreamTurnOutcome outcome, string requestId, string where)
    {
        Release(state.OrchId, state.MemberId);

        var what = outcome.ProcessDied
            ? $"the process died {where} (exit {outcome.Result.ExitCode}){Describe_Stderr(outcome.Stderr)}"
            : outcome.WentSilent
                // NAMES THE LIMIT AND THE KEY THAT SETS IT. "said nothing for 121 s and was killed"
                // leaves an operator with no way to know what the threshold was, let alone how to
                // raise it — and raising it is the only action this line can lead to.
                ? $"the process said nothing for {outcome.Silence.TotalSeconds:F0} s {where} and was killed"
                    + $" (the limit is {SilenceLimit.TotalSeconds:F0} s, set by '{RunnerConfigs.RunnerConfigs_Json.LIMITS_KEY}.{RunnerConfigs.RunnerConfigs_Json.STREAM_SILENCE_SECONDS_KEY}' in config.json)"
                : $"the turn outlived its timeout {where} and was killed";

        _log.Log_Warning(state.OrchId, $"Stream turn {requestId}: {what} — the next attempt resumes the same transcript");

        // MARKED HERE, because this is the only place that still knows which kill it was. Downstream
        // both look the same (timed out, exit -1, no result document) and they mean opposite things:
        // a mute process has produced nothing to report, so it keeps today's retry, while a turn
        // killed at the deadline was working and gets its closing turn (ClosingTurn_Rule).
        if (!outcome.ProcessDied)
            return outcome.WentSilent ? TurnResult_Factory.CreateFrom_NothingToClose(outcome.Result) : outcome.Result;

        var failures = Count_StructuralFailure(key);

        if (failures >= STRUCTURAL_FAILURES_BEFORE_FALLBACK)
            Fall_Back(key, state, $"the process died {failures} times in a row");

        return outcome.Result;
    }

    void Fall_Back(string key, IPrintSessionState state, string reason)
    {
        var next = RunnerFallback_Ladder.Next_Implemented_OrNull(SessionRunners.Stream);

        lock (_lock)
        {
            _fellBack.Add(key);
            _fallbackNotices.Add(key);
        }

        if (next == null || _fallback == null)
        {
            _log.Log_Error(state.OrchId, $"'{state.MemberId}' cannot use runner 'stream' ({reason}) and there is no implemented runner below it — its turns keep failing until the owner changes config.json", null);
            return;
        }

        _log.Log_Error(state.OrchId, RunnerFallback_Ladder.Describe_Fallback(state.MemberId, SessionRunners.Stream, next.Value, reason), null);
    }

    async Task<ITurnResult> Run_OnFallback_Async(
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
        if (_fallback == null)
            throw new Exception($"'{state.MemberId}' fell back from the stream runner but no fallback executor was wired");

        // The fallback's own env word, so the session is told what it actually is: the kit's
        // conditional paragraph keys on this, and a session that believes it is a stream would arm
        // the wrong half of its role command.
        Dictionary<string, string> fallbackEnvironment = new(environment)
        {
            [PrintTurn_Words.RUNNER_ENV_VAR] = SessionRunner_Names.Get_Word(_fallback.Kind),
        };

        return await _fallback.Execute_Async(state, roleConfig, sessionId, resumeTranscript, requestId, pending, sources, alreadyExecutedTurns, fallbackEnvironment, timeout, cancellationToken);
    }

    bool Has_FallenBack(string key)
    {
        lock (_lock)
            return _fellBack.Contains(key);
    }

    int Count_StructuralFailure(string key)
    {
        lock (_lock)
        {
            var count = _structuralFailures.TryGetValue(key, out var known) ? known + 1 : 1;
            _structuralFailures[key] = count;
            return count;
        }
    }

    void Clear_StructuralFailures(string key)
    {
        lock (_lock)
            _structuralFailures.Remove(key);
    }

    // ----- limits -----

    /// <summary>
    /// The ONLY place a headless session ever states its limits. The event is emitted at the CHANGE,
    /// not per turn, so an unchanged reading is remembered rather than re-written — and it is
    /// written where the app's existing alert and dispatch-pause path already looks (a
    /// <c>*usage.json</c> under the supervision root), so the whole limits pipeline gains a source
    /// without gaining a branch.
    /// </summary>
    void Record_RateLimit_IfNew(IPrintSessionState state, StreamTurnOutcome outcome)
    {
        if (outcome.RateLimitInfo == null)
            return;

        var json = outcome.RateLimitInfo.ToJsonString();
        var key = Build_Key(state.OrchId, state.MemberId);

        lock (_lock)
        {
            if (_lastRateLimitJson.TryGetValue(key, out var previous) && previous == json)
                return;

            _lastRateLimitJson[key] = json;
        }

        if (RateLimitEvent_Translator.Write_UsageFile(_paths, state.Role, state.OrchId, state.MemberId, outcome.RateLimitInfo, state.Model))
            _log.Log_Info(state.OrchId, $"Stream session '{state.MemberId}' reported new rate limits: {RateLimitEvent_Translator.Describe(outcome.RateLimitInfo)}");
    }

    static ITurnResult With_BootAddedIn(ITurnResult result, double bootCost, TimeSpan bootElapsed)
    {
        return TurnResult_Factory.Create(
            result.ExitCode, result.TimedOut, result.IsError, result.Subtype, result.ResultText, result.SessionId,
            result.TotalCostUsd == null && bootCost == 0 ? null : (result.TotalCostUsd ?? 0) + bootCost,
            result.DurationMs, result.DurationApiMs, result.NumTurns, result.ApiErrorStatus,
            result.RawStdout, result.RawStderr, result.Elapsed + bootElapsed, supersededFinals: result.SupersededFinals);
    }

    static string Describe_Stderr(string stderr)
    {
        var trimmed = (stderr ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return string.Empty;

        return $": {(trimmed.Length <= 400 ? trimmed : "…" + trimmed[^400..])}";
    }

    static string Build_Key(string orchId, string memberId)
    {
        return $"{orchId}/{memberId}";
    }
}
