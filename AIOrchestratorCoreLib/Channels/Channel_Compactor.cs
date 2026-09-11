using System.Text;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Storage;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Keeps long-running channels cheap to resume. Every respawned session re-reads its channel, so
/// a days-long orchestration would pay for its whole history on every boot. Entries beyond the
/// recent window move (never disappear) into a sibling '.archive.md', and the live file keeps a
/// pointer to it. Entry NUMBERING is untouched — the kept entries carry their original indices,
/// so the append-only protocol continues seamlessly.
/// </summary>
public static class Channel_Compactor
{
    /// <summary>Compaction runs only past this size, so short-lived orchestrations are never touched.</summary>
    public const int COMPACT_ABOVE_ENTRIES = 90;

    /// <summary>Recent entries the live file always keeps — a resuming session's working memory.</summary>
    public const int KEEP_RECENT_ENTRIES = 45;

    /// <summary>
    /// The FEWEST bytes one entry can occupy, so a file's LENGTH alone can rule compaction out.
    ///
    /// <para>
    /// Derived from the header pattern in <see cref="ChannelEntry_Parser"/>, at its most permissive
    /// reading: <c>##[1]FROM a</c> — eleven single-byte characters, every optional space omitted.
    /// Nothing shorter can open an entry, so a file of fewer than
    /// <c>(COMPACT_ABOVE_ENTRIES + 1) * MIN_ENTRY_BYTES</c> bytes cannot hold enough entries to be
    /// eligible, and does not need to be opened to find that out.
    /// </para>
    /// <para>
    /// THE FLOOR IS PINNED BY A TEST, not by this comment: <c>ChannelCompactionGateTests</c> builds
    /// exactly <c>COMPACT_ABOVE_ENTRIES + 1</c> minimal entries and requires compaction to happen. A
    /// floor set too high would silently stop compacting a real channel — the file would grow instead
    /// of being archived, which loses nothing but is invisible — so the derivation may not live only
    /// in prose.
    /// </para>
    /// </summary>
    public const int MIN_ENTRY_BYTES = 11;

    public static string Build_ArchiveFilePath(string channelFilePath)
    {
        var folder = Path.GetDirectoryName(channelFilePath) ?? "";
        var name = Path.GetFileNameWithoutExtension(channelFilePath);

        return Path.Combine(folder, $"{name}.archive.md");
    }

    /// <summary>
    /// Returns the compacted file's new length when compaction happened, else null (nothing to do
    /// or something went wrong — in which case the live channel file is byte-for-byte unchanged and
    /// a later pass retries).
    /// </summary>
    /// <summary>
    /// Shorter than a write's budget on purpose: compaction is the one channel write nobody is
    /// waiting for, so it should yield to an append rather than make it queue.
    /// </summary>
    static readonly TimeSpan COMPACTION_LOCK_BUDGET = TimeSpan.FromSeconds(1);

    public static long? Compact_IfNeeded(string channelFilePath)
    {
        return Compact_IfNeeded(channelFilePath, () => true);
    }

    /// <summary>
    /// <paramref name="mayRewrite"/> is asked INSIDE the gate, immediately before the read, and a
    /// false answer leaves the file untouched (null). That placement is the whole point: a guard
    /// evaluated before the gate answers for a file that can still change while this call WAITS for
    /// the gate. On 2026-09-10 a session's append held the gate, the compactor queued behind it with
    /// a guard that had already said "nothing unread", then read the file WITH the new entry, kept it,
    /// and re-anchored the tailer past it: the entry survived in the file and never reached the
    /// phone. Nothing slow belongs in the callback; a cursor comparison is what it is for.
    /// </summary>
    public static long? Compact_IfNeeded(string channelFilePath, Func<bool> mayRewrite)
    {
        try
        {
            var file = new FileInfo(channelFilePath);

            if (!file.Exists)
                return null;

            // THE CHEAPEST NO IN THE SYSTEM, and the one asked most often: every channel of every
            // orchestration is offered here on every 2-second tick, and almost all of them are far
            // short of the threshold. Answering from the length the stat above already returned
            // means a short channel is neither opened, nor read, nor parsed, and does not take the
            // write gate — which is what it used to do before returning "nothing to do".
            // See MIN_ENTRY_BYTES for why a length can answer an entry-count question.
            if (file.Length < (COMPACT_ABOVE_ENTRIES + 1) * (long)MIN_ENTRY_BYTES)
                return null;

            // Read-then-rewrite is only safe if nothing appends in between: an entry landing after
            // the read is written to content the rename below discards, and nothing anywhere
            // records that it existed. The gate makes the pair indivisible against every writer
            // that takes it, in this process and in the sessions.
            long? newLength = null;

            var acquired = ChannelWrite_Lock.Try_Run_Serialised(
                channelFilePath,
                COMPACTION_LOCK_BUDGET,
                () =>
                {
                    if (mayRewrite())
                        newLength = Compact_Gated(channelFilePath);
                },
                out _);

            // Not acquiring is a non-event: compaction is housekeeping with no deadline, and null
            // already means "nothing happened, the offset is unchanged". It retries next tick.
            return acquired ? newLength : null;
        }
        catch
        {
            // Broad by design: a channel being written right now, a locked file, a full disk — all
            // mean the same thing here, "not this pass". Safe to swallow only because of the order
            // inside: the archive is verified before the live file is touched, and the live rewrite
            // is a rename, so it either happened completely or not at all. The live file therefore
            // still holds every entry, and returning null tells the caller its offset is unchanged.
            return null;
        }
    }

    /// <summary>Runs with the channel's write gate held; the caller owns the try/catch.</summary>
    static long? Compact_Gated(string channelFilePath)
    {
        var text = Read_Text_Safe(channelFilePath);

        // PRE-FILTER, cheap, on the same splitting and the same header pattern the parse uses. A
        // file long enough to reach here is usually still under the threshold — 90 entries is a
        // days-long orchestration — so the common answer is this "no", and it now costs a scan
        // rather than a full entry construction per header.
        if (ChannelEntry_Parser.Count_Entries(text) <= COMPACT_ABOVE_ENTRIES)
            return null;

        // THE DECISION, on the entries themselves. What leaves the live file has to be the entries,
        // so the count that authorises moving them is the parse's own — never the pre-filter's.
        var entries = ChannelEntry_Parser.Parse_All(text);

        if (entries.Count <= COMPACT_ABOVE_ENTRIES)
            return null;

        var archivedCount = entries.Count - KEEP_RECENT_ENTRIES;

        var archivedEntries = entries.Take(archivedCount).ToList();
        var keptEntries = entries.Skip(archivedCount).ToList();

        var archiveFile = Build_ArchiveFilePath(channelFilePath);

        // Order is the whole point: the entries about to leave the live file must be PROVEN to
        // exist elsewhere before the live file is rewritten. A half-written archive plus a
        // rewritten live file is permanent data loss; channel files are the audit trail and the
        // agents' memory, and nothing regenerates them.
        if (!Try_Append_ToArchive_Verified(archiveFile, $"{Build_Block(archivedEntries)}\n"))
            return null;

        var header =
            $"> Entries 1–{archivedEntries[^1].Index} are archived in '{Path.GetFileName(archiveFile)}' "
            + $"(read it only if you need older context). This file keeps the most recent {keptEntries.Count}.\n\n";

        // Rename-over, never truncate-then-write: the live file is either the old one or the
        // new one. If this throws, the archive already holds a copy of the entries that are
        // still in the live file, and the next pass appends that same block again — a duplicate
        // block in the append-only history a human reads is survivable; a lost entry is not.
        Atomic_FileWriter.Write_AllText(channelFilePath, $"{header}{Build_Block(keptEntries)}\n");

        return new FileInfo(channelFilePath).Length;
    }

    /// <summary>
    /// Appends <paramref name="block"/> to the archive and returns whether the file actually grew by
    /// at least the bytes appended. "The call did not throw" is not evidence the bytes landed — a
    /// disk that fills mid-append can leave a short write — and this is the only check standing
    /// between a truncating rewrite and losing history.
    /// </summary>
    static bool Try_Append_ToArchive_Verified(string archiveFilePath, string block)
    {
        var lengthBefore = File.Exists(archiveFilePath) ? new FileInfo(archiveFilePath).Length : 0L;
        var appendedByteCount = Encoding.UTF8.GetByteCount(block);

        File.AppendAllText(archiveFilePath, block);

        if (!File.Exists(archiveFilePath))
            return false;

        // "At least", not "exactly": creating the file may add a preamble the append itself did not.
        return new FileInfo(archiveFilePath).Length - lengthBefore >= appendedByteCount;
    }

    static string Build_Block(IReadOnlyList<IChannelEntry> entries)
    {
        return string.Join("\n\n", entries.Select(entry => entry.RawText));
    }

    static string Read_Text_Safe(string filePath)
    {
        Diagnostics.TickIo_Counters.Count_TextFileRead();

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
