using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.TurnCursor;

namespace AIOrchestratorCoreLib.Running.PrintSessionState;

public static class PrintSessionState_Factory
{
    public static IPrintSessionState Create(
        string sessionId,
        bool sessionStarted,
        SessionRoles role,
        string orchId,
        string memberId,
        string workingDirectory,
        string? model,
        string channelFilePath,
        IReadOnlyList<ITurnCursor> cursors,
        int nextTurnNumber,
        int failedAttempts,
        IReadOnlyList<IExecutedTurn> executedTurns,
        DateTime? retryNotBeforeUtc = null)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException($"Session id must be non-empty ('{orchId}/{memberId}')");
        if (string.IsNullOrWhiteSpace(orchId) || string.IsNullOrWhiteSpace(memberId))
            throw new ArgumentException($"Orchestration and member ids must be non-empty (got '{orchId}/{memberId}')");
        if (string.IsNullOrWhiteSpace(channelFilePath))
            throw new ArgumentException($"Channel file path must be non-empty ('{orchId}/{memberId}')");
        if (nextTurnNumber < 1)
            throw new ArgumentException($"Next turn number must be >= 1, got {nextTurnNumber} ('{orchId}/{memberId}')");
        if (failedAttempts < 0)
            throw new ArgumentException($"Failed attempts must be >= 0, got {failedAttempts} ('{orchId}/{memberId}')");
        if (cursors.Select(cursor => cursor.SourceKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != cursors.Count)
            throw new ArgumentException($"Two cursors share a source key ('{orchId}/{memberId}') — a source is delivered under exactly one cursor or it is delivered twice");

        // A NAKED DateTime IS THE BUG THIS REFUSES. The value is compared against the wall clock on
        // every tick and written to a file read by another process, so a stamp that does not say
        // which zone it is in would be silently wrong by the machine's offset — and CLAUDE.md
        // decision 12 is the standing evidence that a confident wrong instant here costs hours.
        if (retryNotBeforeUtc != null && retryNotBeforeUtc.Value.Kind != DateTimeKind.Utc)
            throw new ArgumentException($"The scheduled retry must be UTC, got {retryNotBeforeUtc.Value.Kind} ('{orchId}/{memberId}')");

        return new PrintSessionStateModel(sessionId, sessionStarted, role, orchId, memberId, workingDirectory, model, channelFilePath, cursors, nextTurnNumber, failedAttempts, executedTurns, retryNotBeforeUtc);
    }

    /// <summary>
    /// A session the bridge has just registered. <paramref name="cursors"/> is the baseline of every
    /// source the roster knows at this instant — see <see cref="TurnCursor_Factory.Create_Baseline"/> for
    /// why registration and not the first tick is the moment history stops and traffic starts.
    /// </summary>
    public static IPrintSessionState Create_New(string sessionId, SessionRoles role, string orchId, string memberId, string workingDirectory, string? model, string channelFilePath, IReadOnlyList<ITurnCursor> cursors)
    {
        return Create(sessionId, false, role, orchId, memberId, workingDirectory, model, channelFilePath, cursors, 1, 0, []);
    }

    /// <summary>A turn completed: recorded, the cursors advanced, attempts reset, any scheduled retry dropped, the transcript id possibly replaced (Fresh mode).</summary>
    public static IPrintSessionState CreateFrom_Existing_TurnExecuted(IPrintSessionState source, IExecutedTurn executed, string sessionId, IReadOnlyList<ITurnCursor> cursors)
    {
        return Create(
            sessionId,
            true,
            source.Role,
            source.OrchId,
            source.MemberId,
            source.WorkingDirectory,
            source.Model,
            source.ChannelFilePath,
            cursors,
            Math.Max(source.NextTurnNumber, executed.TurnNumber + 1),
            0,
            [.. source.ExecutedTurns, executed]);
    }

    /// <summary>
    /// A source was seen for the first time (or one disappeared with its member): the cursor set is
    /// replaced and NOTHING else moves. It is its own transition because it happens outside a turn — a
    /// baseline taken on a tick that starts no turn still has to survive a restart, or the same history
    /// is absorbed again and announced again on every tick.
    /// </summary>
    public static IPrintSessionState CreateFrom_Existing_Cursors(IPrintSessionState source, IReadOnlyList<ITurnCursor> cursors)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, cursors, source.NextTurnNumber, source.FailedAttempts, source.ExecutedTurns, source.RetryNotBeforeUtc);
    }

    /// <summary>
    /// The id this session is about to hand the CLI as <c>--session-id</c>, marked as claimed
    /// BEFORE the process starts. Every later attempt then resumes it instead of re-claiming it,
    /// which the CLI refuses.
    /// </summary>
    public static IPrintSessionState CreateFrom_Existing_SessionClaimed(IPrintSessionState source, string sessionId)
    {
        return Create(sessionId, true, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber, source.FailedAttempts, source.ExecutedTurns, source.RetryNotBeforeUtc);
    }

    /// <summary>
    /// THE CLAIM IS GIVEN BACK. The CLI answered that the id names no conversation, so it was claimed and
    /// never created: the session takes a fresh id and is marked unstarted, which makes the next attempt
    /// pass <c>--session-id</c> again instead of resuming a transcript that does not exist. The opposite
    /// of <see cref="CreateFrom_Existing_SessionClaimed"/>, and the only thing that can undo it.
    /// </summary>
    public static IPrintSessionState CreateFrom_Existing_SessionUnclaimed(IPrintSessionState source, string sessionId)
    {
        return Create(sessionId, false, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber, source.FailedAttempts, source.ExecutedTurns, source.RetryNotBeforeUtc);
    }

    /// <summary>
    /// The session was launched again with a different launch configuration: the model and the working
    /// directory follow config.json, everything the session REMEMBERS — its transcript, its cursors, its
    /// executed turns — does not.
    /// </summary>
    public static IPrintSessionState CreateFrom_Existing_Relaunched(IPrintSessionState source, string workingDirectory, string? model)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, workingDirectory, model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber, source.FailedAttempts, source.ExecutedTurns, source.RetryNotBeforeUtc);
    }

    /// <summary>
    /// A turn attempt failed (timeout or error): counted, nothing else moves — the same request id
    /// retries after the backoff. Any scheduled retry is DROPPED: this failure is the newer fact
    /// about the same turn, and a stale appointment would hold the backoff off for hours.
    /// </summary>
    public static IPrintSessionState CreateFrom_Existing_AttemptFailed(IPrintSessionState source)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber, source.FailedAttempts + 1, source.ExecutedTurns);
    }

    /// <summary>
    /// THE TURN HAS AN APPOINTMENT, NOT A FAILURE. The CLI refused for a usage limit and named when
    /// the window reopens, so the same request id waits for that instant and the attempt counter
    /// does NOT move — measured on the VPS 2026-09-08/09, three attempts a minute apart against a
    /// weekly quota spent the session's whole allowance and left the orchestration stalled for the
    /// night. The one transition that writes <see cref="IPrintSessionState.RetryNotBeforeUtc"/>.
    /// </summary>
    public static IPrintSessionState CreateFrom_Existing_LimitDeferred(IPrintSessionState source, DateTime retryNotBeforeUtc)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber, source.FailedAttempts, source.ExecutedTurns, retryNotBeforeUtc);
    }

    /// <summary>
    /// THE APPOINTMENT IS BROKEN, NOT KEPT — <c>/resume</c>'s override (see
    /// <see cref="PrintTurnDispatcher.IPrintTurnDispatcher.Clear_LimitDeferrals"/>). The owner is not
    /// the mechanism that scheduled the wait, so they get to end it early: the same request id tries
    /// again on the very next tick. The exact opposite of <see cref="CreateFrom_Existing_LimitDeferred"/>,
    /// and like it, <see cref="IPrintSessionState.FailedAttempts"/> does not move — breaking the wait is
    /// not a new failure of the turn any more than scheduling it was one.
    /// </summary>
    public static IPrintSessionState CreateFrom_Existing_LimitDeferralCleared(IPrintSessionState source)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber, source.FailedAttempts, source.ExecutedTurns, null);
    }

    /// <summary>
    /// New traffic arrived after a stall: the attempt counter starts over for the next turn, and any
    /// scheduled retry goes with it — the appointment belonged to the turn that stalled.
    /// </summary>
    public static IPrintSessionState CreateFrom_Existing_AttemptsReset(IPrintSessionState source)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber, 0, source.ExecutedTurns);
    }

    /// <summary>A request id found already executed: the turn number is skipped without running anything, and nothing is left waiting on it.</summary>
    public static IPrintSessionState CreateFrom_Existing_TurnSkipped(IPrintSessionState source)
    {
        return Create(source.SessionId, source.SessionStarted, source.Role, source.OrchId, source.MemberId, source.WorkingDirectory, source.Model, source.ChannelFilePath, source.Cursors, source.NextTurnNumber + 1, 0, source.ExecutedTurns);
    }
}
