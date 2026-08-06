namespace AIOrchestratorCoreLib.Status;

/// <summary>Development state of one implementer spoke, derived from its channel entries.</summary>
public enum MemberStates
{
    NewNoTraffic,
    ImplementerWorking,
    AwaitingSupervisorReview,
    WritingWindowOpen,
    BlockedOnOwner,
}
