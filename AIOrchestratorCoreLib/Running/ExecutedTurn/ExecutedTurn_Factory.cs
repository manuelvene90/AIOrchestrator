namespace AIOrchestratorCoreLib.Running.ExecutedTurn;

public static class ExecutedTurn_Factory
{
    public static IExecutedTurn Create(int turnNumber, string requestId, int firstEntryIndex, int lastEntryIndex, DateTime endedUtc, string outcome, double? costUsd, string? sessionId = null)
    {
        if (turnNumber < 1)
            throw new ArgumentException($"Turn number must be >= 1, got {turnNumber} (request '{requestId}')");
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException($"Request id must be non-empty (turn {turnNumber})");
        if (string.IsNullOrWhiteSpace(outcome))
            throw new ArgumentException($"Outcome must be non-empty (request '{requestId}')");

        return new ExecutedTurnModel(turnNumber, requestId, firstEntryIndex, lastEntryIndex, endedUtc, outcome, costUsd, sessionId);
    }
}
