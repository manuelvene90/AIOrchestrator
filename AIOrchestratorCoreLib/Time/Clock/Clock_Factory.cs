namespace AIOrchestratorCoreLib.Time.Clock;

public static class Clock_Factory
{
    /// <summary>The wall clock. Every production caller uses this one.</summary>
    public static IClock Create_System()
    {
        return new SystemClockModel();
    }
}
