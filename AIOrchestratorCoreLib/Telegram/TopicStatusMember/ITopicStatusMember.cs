using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Telegram.TopicStatusMember;

/// <summary>
/// One member's contribution to a topic's status line: who it is, everything it has said, and
/// whether it is closed. The ENTRIES are passed rather than a pre-computed state so the line and the
/// app's cards cannot disagree — both go through <see cref="Status.MemberState_Resolver"/>.
/// </summary>
public interface ITopicStatusMember
{
    string MemberId { get; }

    IReadOnlyList<IChannelEntry> Entries { get; }

    /// <summary>Closed members drop OFF the line rather than lingering as a stale row.</summary>
    bool IsClosed { get; }
}
