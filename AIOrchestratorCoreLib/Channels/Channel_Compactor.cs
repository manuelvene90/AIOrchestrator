using AIOrchestratorCoreLib.Channels.ChannelEntry;

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

    public static string Build_ArchiveFilePath(string channelFilePath)
    {
        var folder = Path.GetDirectoryName(channelFilePath) ?? "";
        var name = Path.GetFileNameWithoutExtension(channelFilePath);

        return Path.Combine(folder, $"{name}.archive.md");
    }

    /// <summary>
    /// Returns the compacted file's new length when compaction happened, else null (nothing to do
    /// or something went wrong — in which case the channel is left exactly as it was).
    /// </summary>
    public static long? Compact_IfNeeded(string channelFilePath)
    {
        try
        {
            if (!File.Exists(channelFilePath))
                return null;

            var text = Read_Text_Safe(channelFilePath);
            var entries = ChannelEntry_Parser.Parse_All(text);

            if (entries.Count <= COMPACT_ABOVE_ENTRIES)
                return null;

            var archivedCount = entries.Count - KEEP_RECENT_ENTRIES;

            var archivedEntries = entries.Take(archivedCount).ToList();
            var keptEntries = entries.Skip(archivedCount).ToList();

            var archiveFile = Build_ArchiveFilePath(channelFilePath);
            File.AppendAllText(archiveFile, $"{Build_Block(archivedEntries)}\n");

            var header =
                $"> Entries 1–{archivedEntries[^1].Index} are archived in '{Path.GetFileName(archiveFile)}' "
                + $"(read it only if you need older context). This file keeps the most recent {keptEntries.Count}.\n\n";

            File.WriteAllText(channelFilePath, $"{header}{Build_Block(keptEntries)}\n");

            return new FileInfo(channelFilePath).Length;
        }
        catch
        {
            // A channel being written right now simply gets compacted on a later pass.
            return null;
        }
    }

    static string Build_Block(IReadOnlyList<IChannelEntry> entries)
    {
        return string.Join("\n\n", entries.Select(entry => entry.RawText));
    }

    static string Read_Text_Safe(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
