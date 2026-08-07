namespace AIOrchestratorCoreLib.Planning.PlanProgress;

public static class PlanProgress_Factory
{
    public static IPlanProgress Create(int done, int inProgress, int blocked, int total, string? currentTaskText)
    {
        return new PlanProgressModel(done, inProgress, blocked, total, currentTaskText);
    }
}
