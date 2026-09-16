namespace AIOrchestratorCoreLib.Reviewing;

internal sealed class RerouteContractModel(
    string id,
    string orchId,
    string implementerId,
    string reviewerId,
    string baseCommit,
    string brief,
    DateTime declaredUtc,
    RerouteStates state,
    string? reportIdentity,
    string? headCommit,
    DateTime? routedUtc) : IRerouteContract
{
    public string Id { get; } = id;
    public string OrchId { get; } = orchId;
    public string ImplementerId { get; } = implementerId;
    public string ReviewerId { get; } = reviewerId;
    public string BaseCommit { get; } = baseCommit;
    public string Brief { get; } = brief;
    public DateTime DeclaredUtc { get; } = declaredUtc;
    public RerouteStates State { get; } = state;
    public string? ReportIdentity { get; } = reportIdentity;
    public string? HeadCommit { get; } = headCommit;
    public DateTime? RoutedUtc { get; } = routedUtc;
}
