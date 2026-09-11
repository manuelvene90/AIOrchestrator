namespace AIOrchestratorCoreLib.Running.TurnResult;

public static class TurnResult_Factory
{
    public static ITurnResult Create(
        int exitCode,
        bool timedOut,
        bool isError,
        string? subtype,
        string? resultText,
        string? sessionId,
        double? totalCostUsd,
        long? durationMs,
        long? durationApiMs,
        int? numTurns,
        int? apiErrorStatus,
        string rawStdout,
        string rawStderr,
        TimeSpan elapsed,
        bool nothingToClose = false,
        IReadOnlyList<string>? supersededFinals = null)
    {
        return new TurnResultModel(exitCode, timedOut, isError, subtype, resultText, sessionId, totalCostUsd, durationMs, durationApiMs, numTurns, apiErrorStatus, rawStdout, rawStderr, elapsed, nothingToClose, supersededFinals ?? []);
    }

    /// <summary>
    /// The same result, marked as a kill with NOTHING BEHIND IT — so it keeps today's retry instead
    /// of earning a closing turn (<see cref="ClosingTurn.ClosingTurn_Rule"/>). Called by the stream
    /// executor, the only place that still holds the distinction, for both of its cases: a mute
    /// process the heartbeat killed, and a boot that ate the whole turn timeout before the brief was
    /// ever sent. ONE method for both, because the flag they set is one fact — <c>ITurnResult</c>
    /// says which producers there are and the call site says which one it is.
    /// </summary>
    public static ITurnResult CreateFrom_NothingToClose(ITurnResult source)
    {
        return Create(
            source.ExitCode, source.TimedOut, source.IsError, source.Subtype, source.ResultText, source.SessionId,
            source.TotalCostUsd, source.DurationMs, source.DurationApiMs, source.NumTurns, source.ApiErrorStatus,
            source.RawStdout, source.RawStderr, source.Elapsed, nothingToClose: true, source.SupersededFinals);
    }
}
