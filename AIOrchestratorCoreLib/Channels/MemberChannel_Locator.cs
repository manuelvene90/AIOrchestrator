using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// WHERE A MEMBER'S TRAFFIC ACTUALLY IS. For an implementer or a reviewer that is its own spoke;
/// for a SOLO it is the owner channel, because a basic orchestration has no spoke — the solo IS the
/// conversation with the owner and writes there.
///
/// A solo's member folder does contain a `channel.md`: the store seeds one for every member at
/// creation and nothing ever writes to it again. So every reader that built the path from the member
/// id got a real file, present and parseable and permanently empty — the worst shape a wrong answer
/// can take, because nothing errors. The owner saw it as "solo-1: new — no traffic · last wrote
/// 5 h 31 min ago" while they were mid-conversation with that very session; the mtime being quoted
/// was the moment the seed file was created.
///
/// It is not only cosmetic. `/resume` and the respawn notice APPEND to this path, so on a basic
/// orchestration both were being written into a file the solo never reads: the owner's "wake
/// everything up" reached every session except the only one there was.
///
/// NOT used by <see cref="ChannelDiscovery"/>, deliberately: that enumerates spokes to TAIL, and it
/// already skips any folder that is not an implementer. Routing a solo to the owner channel there
/// would tail that file twice and mirror every entry to Telegram twice.
/// </summary>
public static class MemberChannel_Locator
{
    public static string Get_ChannelFile(ISupervisionPaths paths, string orchId, string memberId)
    {
        return MemberKind_Ids.Resolve_Kind(memberId) == MemberKinds.Solo
            ? paths.Get_OwnerChannelFile(orchId)
            : paths.Get_ImplementerChannelFile(orchId, memberId);
    }
}
