using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.TurnCursor;

namespace AIOrchestratorCoreLib.Running.PrintSessionState;

internal sealed class PrintSessionStateModel(
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
    DateTime? retryNotBeforeUtc) : IPrintSessionState
{
    public string SessionId { get; } = sessionId;
    public bool SessionStarted { get; } = sessionStarted;
    public SessionRoles Role { get; } = role;
    public string OrchId { get; } = orchId;
    public string MemberId { get; } = memberId;
    public string WorkingDirectory { get; } = workingDirectory;
    public string? Model { get; } = model;
    public string ChannelFilePath { get; } = channelFilePath;
    public IReadOnlyList<ITurnCursor> Cursors { get; } = cursors;
    public int NextTurnNumber { get; } = nextTurnNumber;
    public int FailedAttempts { get; } = failedAttempts;
    public IReadOnlyList<IExecutedTurn> ExecutedTurns { get; } = executedTurns;
    public DateTime? RetryNotBeforeUtc { get; } = retryNotBeforeUtc;
}
