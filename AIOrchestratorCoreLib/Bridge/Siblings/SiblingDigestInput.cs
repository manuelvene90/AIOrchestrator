using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning.PlanProgress;

namespace AIOrchestratorCoreLib.Bridge.Siblings;

public sealed class SiblingDigestInput(string name, string orchId, string workingPath, string? branch, bool paused, IPlanProgress? progress, IReadOnlyList<IChannelEntry> ownerChannelTail, IReadOnlyList<string> outboxSubjects)
{
    public string Name { get; } = name;
    public string OrchId { get; } = orchId;
    public string WorkingPath { get; } = workingPath;
    public string? Branch { get; } = branch;
    public bool Paused { get; } = paused;
    public IPlanProgress? Progress { get; } = progress;
    public IReadOnlyList<IChannelEntry> OwnerChannelTail { get; } = ownerChannelTail;
    public IReadOnlyList<string> OutboxSubjects { get; } = outboxSubjects;
}
