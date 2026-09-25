namespace AIOrchestratorCoreLib.Bridge.PeriodicStatus;

/// <summary>Builds the sweep with empty memories — which is every orchestration's first sight.</summary>
public static class PeriodicStatusSweep_Factory
{
    public static IPeriodicStatusSweep Create()
    {
        return new PeriodicStatusSweepModel();
    }
}
