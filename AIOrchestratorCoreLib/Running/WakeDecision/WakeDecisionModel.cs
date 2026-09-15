using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.WakeDecision;

internal sealed class WakeDecisionModel(string reason, IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources) : IWakeDecision
{
    public string Reason { get; } = reason;
    public IReadOnlyList<PendingEntry> Pending { get; } = pending;
    public IReadOnlyList<ITurnSource> Sources { get; } = sources;
}
