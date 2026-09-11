namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// <c>&lt;orch&gt;/&lt;member&gt;/&lt;turn&gt;</c> — the idempotency key of a print turn. Spelled once here;
/// the state file, the turn_ended entry and the resume prompt all carry the same string.
/// </summary>
public static class PrintTurn_RequestId
{
    public static string Build(string orchId, string memberId, int turnNumber)
    {
        return $"{orchId}/{memberId}/{turnNumber}";
    }
}
