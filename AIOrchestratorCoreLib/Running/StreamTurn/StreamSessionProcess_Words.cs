namespace AIOrchestratorCoreLib.Running.StreamTurn;

/// <summary>
/// The fields the bridge ADDS to a stream event before filing it. Public because the turn log's
/// formatter reads them and the process that writes them is internal.
/// </summary>
public static class StreamSessionProcess_Words
{
    /// <summary>
    /// What one turn cost, stamped onto its result event. The CLI's own <c>total_cost_usd</c> is the
    /// PROCESS's running total (measured), so a reader of the log has no baseline to difference it
    /// against — and /tail reporting the total while the channel reported the turn is exactly the
    /// disagreement CLAUDE.md decision 10 exists to prevent.
    /// </summary>
    public const string TURN_COST_KEY = "aiorch_turn_cost_usd";
}
