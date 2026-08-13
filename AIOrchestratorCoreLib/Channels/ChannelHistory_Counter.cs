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
    /// The channel's WHOLE history as entries, archive first — because the archive holds the OLDER
    /// ones and a caller reading them in order should meet the conversation as it happened.
    ///
    /// Added for the promotion gate, which asks whether the solo ever filed a handover entry. That
    /// is a question about history, not about the live file: `Channel_Compactor` moves all but the
    /// newest 45 entries out once a channel passes 90, and `owner-channel.md` is on the compactor's
    /// list. A solo that filed its handover, was declined once and kept working would have been told
    /// to "file your HANDOVER entry first" a day after it had — instructing it to do the thing it had
    /// already done, which is the option-lab-2 shape this class exists to prevent.
    ///
    /// It lives HERE rather than in the caller so the knowledge that history = live + archive stays
    /// in one place. A second span written next to a consumer is how the two come to disagree about
    /// what a channel contains.
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Read_Entries(string channelFilePath)
    {
        List<IChannelEntry> entries = [.. Parse_In(Channel_Compactor.Build_ArchiveFilePath(channelFilePath))];

        entries.AddRange(Parse_In(channelFilePath));

        return entries;
    }

    static int Count_In(string filePath, ChannelAuthors author)
    {
        var count = 0;

        foreach (var entry in Parse_In(filePath))
        {
            if (entry.Author == author)
                count++;
        }

        return count;
    }

    /// <summary>One read of one file, so the count and the entries can never disagree about it.</summary>
    static IReadOnlyList<IChannelEntry> Parse_In(string filePath)
    {
        return ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(filePath));
    }
}
