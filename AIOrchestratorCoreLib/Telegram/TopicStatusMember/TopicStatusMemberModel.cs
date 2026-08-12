using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Telegram.TopicStatusMember;

sealed class TopicStatusMemberModel : ITopicStatusMember
{
    readonly string _memberId;
    readonly IReadOnlyList<IChannelEntry> _entries;
    readonly bool _isClosed;

    internal TopicStatusMemberModel(string memberId, IReadOnlyList<IChannelEntry> entries, bool isClosed)
    {
        _memberId = memberId;
        _entries = entries;
        _isClosed = isClosed;
    }

    public string MemberId => _memberId;

    public IReadOnlyList<IChannelEntry> Entries => _entries;

    public bool IsClosed => _isClosed;
}
