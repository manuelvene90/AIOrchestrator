using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Telegram.TopicStatusMember;

/// <summary>
/// One member's contribution to a topic's status line: who it is, everything it has said, and
/// whether it is closed. The ENTRIES are passed rather than a pre-computed state, so this line reads
/// the member's DECLARED state through <see cref="Status.MemberState_Resolver"/> and nothing else.
///
/// AND IT DOES DISAGREE WITH THE APP'S CARD, ON PURPOSE. An earlier version of this comment claimed
/// the two cannot diverge because both go through the resolver. The card does not: it computes
/// Is_MidTurn from FILE MTIME and lets that outrank the resolved state, which is why it can say
/// "working now" for about two minutes after a turn ends. That divergence is the entire point of this
/// feature — the owner refused a pinned line precisely because a mtime-derived "working now" would
/// sit in front of them while being false — so the comment asserted the opposite of the thing being
/// built.
/// </summary>
public interface ITopicStatusMember
{
    string MemberId { get; }

    IReadOnlyList<IChannelEntry> Entries { get; }

    /// <summary>Closed members drop OFF the line rather than lingering as a stale row.</summary>
    bool IsClosed { get; }
}
