namespace AIOrchestratorCoreLib.Status.SessionContextUsage;

internal sealed class SessionContextUsageModel(
    double usedPercent,
    DateTime probeTimeUtc) : ISessionContextUsage
{
    public double UsedPercent { get; } = usedPercent;

    public DateTime ProbeTimeUtc { get; } = probeTimeUtc;
}
