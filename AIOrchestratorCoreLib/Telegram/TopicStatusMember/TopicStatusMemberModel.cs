using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Status.SessionContextUsage;

namespace AIOrchestratorCoreLib.Telegram.TopicStatusMember;

sealed class TopicStatusMemberModel : ITopicStatusMember
{
    readonly string _memberId;
    readonly IReadOnlyList<IChannelEntry> _entries;
    readonly bool _isClosed;
    readonly ISessionContextUsage? _contextUsage;

    internal TopicStatusMemberModel(string memberId, IReadOnlyList<IChannelEntry> entries, bool isClosed, ISessionContextUsage? contextUsage = null)
    {
        _memberId = memberId;
        _entries = entries;
        _isClosed = isClosed;
        _contextUsage = contextUsage;
    }

    public string MemberId => _memberId;

    public IReadOnlyList<IChannelEntry> Entries => _entries;

    public bool IsClosed => _isClosed;

    public ISessionContextUsage? ContextUsage => _contextUsage;
}
