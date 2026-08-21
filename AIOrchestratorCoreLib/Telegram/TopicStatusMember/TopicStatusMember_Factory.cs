using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status.SessionContextUsage;

namespace AIOrchestratorCoreLib.Telegram.TopicStatusMember;

public static class TopicStatusMember_Factory
{
    public static ITopicStatusMember Create(string memberId, IReadOnlyList<IChannelEntry> entries, bool isClosed, ISessionContextUsage? contextUsage = null)
    {
        return new TopicStatusMemberModel(memberId, entries, isClosed, contextUsage);
    }
}
