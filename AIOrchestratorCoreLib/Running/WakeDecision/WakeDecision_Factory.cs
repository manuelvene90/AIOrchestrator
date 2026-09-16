using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.WakeDecision;

public static class WakeDecision_Factory
{
    /// <summary>
    /// A decision is only ever built for a turn that IS starting, so a blank reason is a caller that
    /// lost its reason on the way — and the reason is the whole audit trail of a turn that happened
    /// (<see cref="PendingTraffic.WakeUp_Policy.Resolve_WakeReason_OrNull"/> returns a sentence rather
    /// than a bool for exactly that). "Not yet" is a null decision, never a decision with no words.
    /// </summary>
    public static IWakeDecision Create(string reason, IReadOnlyList<PendingEntry> pending, IReadOnlyList<ITurnSource> sources)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A wake decision needs the reason the turn is starting");

        return new WakeDecisionModel(reason, pending, sources);
    }
}
