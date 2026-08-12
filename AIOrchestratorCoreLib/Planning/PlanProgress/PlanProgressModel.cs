namespace AIOrchestratorCoreLib.Planning.PlanProgress;

internal sealed class PlanProgressModel(
    int done,
    int inProgress,
    int blocked,
    int notDoing,
    int total,
    string? currentTaskText,
    IReadOnlyList<string> inProgressTasks,
    IReadOnlyList<string> blockedTasks,
    IReadOnlyList<string> openTasks,
    IReadOnlyList<string> doneTasks) : IPlanProgress
{
    public int Done { get; } = done;
    public int InProgress { get; } = inProgress;
    public int Blocked { get; } = blocked;
    public int NotDoing { get; } = notDoing;
    public int Total { get; } = total;
    public string? CurrentTaskText { get; } = currentTaskText;
    public IReadOnlyList<string> InProgressTasks { get; } = inProgressTasks;
    public IReadOnlyList<string> BlockedTasks { get; } = blockedTasks;
    public IReadOnlyList<string> OpenTasks { get; } = openTasks;

    public IReadOnlyList<string> DoneTasks { get; } = doneTasks;
}
