namespace AIOrchestratorCoreLib.Running.TurnResult;

internal sealed class TurnResultModel(
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
    bool nothingToClose,
    IReadOnlyList<string> supersededFinals,
    string? brakeKill) : ITurnResult
{
    public int ExitCode { get; } = exitCode;
    public bool TimedOut { get; } = timedOut;
    public bool IsError { get; } = isError;
    public string? Subtype { get; } = subtype;
    public string? ResultText { get; } = resultText;
    public string? SessionId { get; } = sessionId;
    public double? TotalCostUsd { get; } = totalCostUsd;
    public long? DurationMs { get; } = durationMs;
    public long? DurationApiMs { get; } = durationApiMs;
    public int? NumTurns { get; } = numTurns;
    public int? ApiErrorStatus { get; } = apiErrorStatus;
    public string RawStdout { get; } = rawStdout;
    public string RawStderr { get; } = rawStderr;
    public TimeSpan Elapsed { get; } = elapsed;
    public bool NothingToClose { get; } = nothingToClose;
    public IReadOnlyList<string> SupersededFinals { get; } = supersededFinals;
    public string? BrakeKill { get; } = brakeKill;
}
