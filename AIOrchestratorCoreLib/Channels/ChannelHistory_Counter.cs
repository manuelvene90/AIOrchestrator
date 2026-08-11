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
