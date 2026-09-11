using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// ONE READ AND ONE PARSE PER CHANGE of a channel file, instead of one per asker.
///
/// <para>
/// WHAT IT IS FOR. A mirror tick asks the same channel the same questions from a dozen unrelated
/// places — the topic status line, the pending-owner-reply resolver, the idle-member sweep, the
/// progress artefacts, the state pack a fresh turn is handed — and each of them read the file and
/// parsed it again. Measured on this branch, <c>Build_TopicStatusMembers</c> alone accounted for the
/// larger part of 3.65 MB of channel text read per 2-second tick, for text that had not changed
/// since the tick before. The answers were identical every time; the reading was not free.
/// </para>
/// <para>
/// INVALIDATED BY THE FILE, NEVER BY A CLOCK. The key is (length, last-write-time): a cached parse is
/// used only while the file on disk still has exactly the size and stamp it had when it was read.
/// A time-based cache would have to choose a staleness window, and every window is a period in which
/// the app answers questions about a channel from text the channel no longer contains — which is
/// the class of defect CLAUDE.md decision 13 is about. There is no such window here.
/// </para>
/// <para>
/// WHY THE STAMP IS ENOUGH, said plainly. Channels are append-only: every append grows the file, so
/// the length alone already separates every ordinary write. Compaction rewrites the file shorter,
/// which the length also catches. The stamp is the second half of the belt for the one case a length
/// cannot see — a rewrite to the same size — and the pair has to match, not either of them.
/// </para>
/// <para>
/// IT IS NOT A STORE OF TRUTH. Nothing here is authoritative and nothing depends on a hit: a miss
/// reads the file, and every caller gets entries parsed from the file's current bytes either way.
/// The compactor deliberately does NOT come through here — it reads under the channel's write gate
/// because it is about to rewrite what it read, and a cached parse is exactly the wrong input for
/// that.
/// </para>
/// </summary>
public static class ChannelHistory_Cache
{
    /// <summary>
    /// Distinct files kept before the whole map is dropped. Generous against the real shape — an
    /// orchestration has a handful of channels and each has at most one archive — and a hard bound
    /// on a static that lives for the app's lifetime. Dropping everything is the right eviction
    /// here rather than the cheapest: a miss costs one read, so there is nothing to be clever about.
    /// </summary>
    const int MAX_CACHED_FILES = 512;

    static readonly Lock _lock = new();

    static readonly Dictionary<string, (long Length, DateTime LastWriteUtc, IReadOnlyList<IChannelEntry> Entries)> _byPath =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The file's entries, parsed from its current bytes — from the cache when the file is byte-for-byte
    /// the one already parsed, and from disk otherwise. Empty for a file that does not exist, which
    /// matches <see cref="UsageTotals_Reader.Read_Text_Safe"/> and is an answer, not an error.
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Read_Entries(string channelFilePath)
    {
        var file = new FileInfo(channelFilePath);

        // NOT CACHED AS EMPTY. A file that does not exist yet has no stamp to invalidate against, so
        // remembering the answer would be remembering it for ever. One stat is already the cheapest
        // question here.
        if (!file.Exists)
            return [];

        var length = file.Length;
        var lastWriteUtc = file.LastWriteTimeUtc;

        lock (_lock)
        {
            if (_byPath.TryGetValue(channelFilePath, out var cached)
                && cached.Length == length
                && cached.LastWriteUtc == lastWriteUtc)
                return cached.Entries;
        }

        // OUTSIDE THE LOCK. The read is the slow part and two threads racing on the same channel
        // costing two reads is cheaper than every reader of every channel queueing behind one of
        // them. The last writer wins and both wrote the same thing.
        // WRAPPED, because this exact instance is handed to every caller that asks about this file
        // while it is unchanged. Before the cache each of them got a private list and could have done
        // anything with it; now a single cast to List<T> anywhere would edit what every other sweep
        // in the tick reads. The wrapper is what makes "they all get the same object" safe rather
        // than merely true today.
        var entries = new System.Collections.ObjectModel.ReadOnlyCollection<IChannelEntry>(
            [.. ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFilePath))]);

        lock (_lock)
        {
            if (_byPath.Count >= MAX_CACHED_FILES)
                _byPath.Clear();

            _byPath[channelFilePath] = (length, lastWriteUtc, entries);
        }

        return entries;
    }

}
