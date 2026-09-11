namespace AIOrchestratorCoreLib.Time.Clock;

internal sealed class SystemClockModel : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
