using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using AIOrchestratorCoreLib.Status.SessionModelReading;

namespace AIOrchestratorCoreLib.Telegram.TopicStatusMember;

internal sealed class TopicStatusMemberModel(
    string memberId,
    IReadOnlyList<IChannelEntry> entries,
    bool isClosed,
    ISessionContextUsage? contextUsage,
    ISessionModelReading? model) : ITopicStatusMember
{
    public string MemberId { get; } = memberId;

    public IReadOnlyList<IChannelEntry> Entries { get; } = entries;

    public bool IsClosed { get; } = isClosed;

    public ISessionContextUsage? ContextUsage { get; } = contextUsage;

    public ISessionModelReading? Model { get; } = model;
}
