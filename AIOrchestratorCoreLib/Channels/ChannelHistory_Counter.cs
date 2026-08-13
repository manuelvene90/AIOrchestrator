using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Channels;

/// <summary>
/// Counts a channel's entries across its WHOLE history — the live file plus the sibling archive
/// that <see cref="Channel_Compactor"/> moves older entries into.
///
/// Anything that compares "how many entries were there before" against "how many are there now"
/// must count this way, because a live-file count is NOT monotonic: compaction removes entries
/// from it. On 2026-08-10 `option-lab-2` compacted at 15:20:58, two minutes after an owner message
/// was delivered, moving 18 supervisor entries out of the live file. The pending-reply resolver
/// compared the shrunken count against the pre-compaction one, concluded the supervisor had never
/// answered, and kept telling the owner their message was still waiting — for a message that had
/// been answered — while nudging the supervisor for a failure that never happened.
/// </summary>
public static class ChannelHistory_Counter
{
    public static int Count_Entries_ByAuthor(string channelFilePath, ChannelAuthors author)
    {
        return Count_In(channelFilePath, author)
            + Count_In(Channel_Compactor.Build_ArchiveFilePath(channelFilePath), author);
    }

    /// <summary>
    /// The entries compaction has already moved out of the live file — OLDER than everything still
    /// live, by construction, because the compactor only ever moves from the front. Empty when nothing
    /// has been archived yet, which is the ordinary case.
    ///
    /// Here rather than at the call site because this class is the one place that knows a channel is
    /// two files. Counting was simply the first question anyone asked of that fact; "what was the last
    /// conversation entry" turned out to be the second, and it had been answered from the live file
    /// alone — which restarted a nudge loop on every channel long enough to compact.
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Read_ArchivedEntries(string channelFilePath)
    {
        return ChannelEntry_Parser.Parse_All(
            UsageTotals_Reader.Read_Text_Safe(Channel_Compactor.Build_ArchiveFilePath(channelFilePath)));
    }

    static int Count_In(string filePath, ChannelAuthors author)
    {
        var count = 0;

        foreach (var entry in ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(filePath)))
        {
            if (entry.Author == author)
                count++;
        }

        return count;
    }
}
