using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Telegram.TopicStatusMember;

public static class TopicStatusMember_Factory
{
    public static ITopicStatusMember Create(string memberId, IReadOnlyList<IChannelEntry> entries, bool isClosed)
    {
        return new TopicStatusMemberModel(memberId, entries, isClosed);
    }
}
