namespace AIOrchestratorCoreLib.Planning.PlanProgress;

/// <summary>
/// Parsed state of an orchestration's PLAN.md task ledger — the structured answer to "how far
/// along is this long task?" that freeform channel prose cannot give.
/// </summary>
public interface IPlanProgress
{
    int Done { get; }
    int InProgress { get; }
    int Blocked { get; }
    int Total { get; }

    /// <summary>The first in-progress task's text, else the first open one — "what's happening now".</summary>
    string? CurrentTaskText { get; }
}
