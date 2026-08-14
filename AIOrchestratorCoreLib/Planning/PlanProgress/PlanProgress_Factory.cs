namespace AIOrchestratorCoreLib.Planning.PlanProgress;

public static class PlanProgress_Factory
{
    public static IPlanProgress Create(
        int done,
        int inProgress,
        int blocked,
        int notDoing,
        int total,
        string? currentTaskText,
        IReadOnlyList<string> inProgressTasks,
        IReadOnlyList<string> blockedTasks,
        IReadOnlyList<string> openTasks,
        IReadOnlyList<string>? doneTasks = null,
        IReadOnlyList<PlanLedgerLine>? lines = null)
    {
        return new PlanProgressModel(done, inProgress, blocked, notDoing, total, currentTaskText, inProgressTasks, blockedTasks, openTasks, doneTasks ?? [], lines ?? []);
    }
}
