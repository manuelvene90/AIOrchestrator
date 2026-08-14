using System.Collections;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// A channel's whole conversation — the archived entries followed by the live ones — CARRYING THE
/// BOUNDARY BETWEEN THEM.
///
/// It IS an <see cref="IReadOnlyList{T}"/>, so every consumer that just wants "the entries" is
/// unchanged and unaware. The extra thing it knows is <see cref="LiveStartIndex"/>: the position where
/// the live file begins.
///
/// WHY THE BOUNDARY TRAVELS WITH THE DATA RATHER THAN BESIDE IT. Most questions asked of a channel are
/// about its HISTORY and want every entry — what was the last conversation entry, has the member
/// declared, is a verdict owed. One is not: whether a WINDOW IS OPEN is a statement about the PRESENT,
/// and an opener that has been compacted out sits behind at least
/// <see cref="Channel_Compactor.KEEP_RECENT_ENTRIES"/> later entries. A member that has written 45
/// entries since is not mid-write; it forgot the close.
///
/// Passing that boundary as a separate argument was the obvious design and it does not survive the
/// journey. `ITopicStatusMember.Entries` hands a bare list to the status-line builder, which calls
/// <see cref="Status.MemberState_Resolver.Resolve"/> — an index parameter dies at that hop unless the
/// interface, its model, its factory and the engine's construction all learn about it. A value that can
/// be silently dropped in transit is exactly what produced the defect below, so it is attached instead.
///
/// THE DEFECT THIS EXISTS TO CLOSE, because it was shipped and accepted before it was caught.
/// `Has_OpenWindow` short-circuits ahead of every other rule in `Resolve`, and with no matching close
/// anywhere it returns true for ANY opener. While the resolver read the live file alone an archived
/// opener was invisible and the state HEALED as soon as one real entry landed. Reading the whole
/// history made the opener permanently visible — and nothing prunes an archive, so no later traffic can
/// ever clear it. Two live channels were pinned that way the day the change landed:
/// `da-vinci-fintech-suite-5/imp-6`, whose real close was written `WINDOW CLOSED` without the `WRITING`
/// prefix so the marker never matched, and `imp-8`, whose only later close was a `MUTATION` one.
///
/// A plain list has no boundary and scans from zero, which is the correct reading for in-memory
/// callers and for every test that builds entries by hand.
/// </summary>
public sealed class ChannelHistory : IReadOnlyList<IChannelEntry>
{
    readonly IReadOnlyList<IChannelEntry> _entries;

    public ChannelHistory(IReadOnlyList<IChannelEntry> entries, int liveStartIndex)
    {
        _entries = entries;
        LiveStartIndex = liveStartIndex;
    }

    /// <summary>
    /// Where the live file begins. Equal to <see cref="Count"/> when everything has been archived and
    /// the live file is empty, and 0 when nothing has been archived yet — both are ordinary.
    /// </summary>
    public int LiveStartIndex { get; }

    public IChannelEntry this[int index] => _entries[index];

    public int Count => _entries.Count;

    public IEnumerator<IChannelEntry> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
