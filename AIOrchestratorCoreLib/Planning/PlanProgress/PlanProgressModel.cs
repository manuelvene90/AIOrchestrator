namespace AIOrchestratorCoreLib.Planning.PlanProgress;

internal sealed class PlanProgressModel(
    int done,
    int inProgress,
    int blocked,
    int total,
    string? currentTaskText) : IPlanProgress
{
    public int Done { get; } = done;
    public int InProgress { get; } = inProgress;
    public int Blocked { get; } = blocked;
    public int Total { get; } = total;
    public string? CurrentTaskText { get; } = currentTaskText;
}
