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
        IReadOnlyList<PlanLedgerLine>? lines = null,

        // TRAILING AND OPTIONAL so the three test builders that call this positionally keep working.
        // A subset of `blocked`, never a sibling — see IPlanProgress.BlockedOnOwner.
        int blockedOnOwner = 0)
    {
        return new PlanProgressModel(done, inProgress, blocked, blockedOnOwner, notDoing, total, currentTaskText, inProgressTasks, blockedTasks, openTasks, doneTasks ?? [], lines ?? []);
    }
}
