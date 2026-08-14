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

    /// <summary>
    /// The channel's WHOLE conversation in order — everything <see cref="Channel_Compactor"/> has moved
    /// out, then everything still live. This is what a caller wants whenever it asks a question about
    /// the channel's HISTORY rather than about its recent traffic.
    ///
    /// ARCHIVE FIRST, AND THE ORDER IS THE CONTRACT. The compactor only ever moves from the FRONT, so
    /// everything archived is older than everything live. Every consumer here scans backwards for "the
    /// last X", so concatenating the other way round would silently hand them the OLDEST match — which
    /// is the mute-switch failure reached from the far side, and is why the ordering has its own test
    /// rather than riding along inside a behaviour case.
    ///
    /// WHY THIS EXISTS AS WELL AS <see cref="Read_ArchivedEntries"/>. That one answers "what was moved
    /// out", which is what a live-first FALLBACK needs. This one answers "what is the whole story",
    /// which is what anything reasoning over a SEQUENCE needs — a window's open and its close can land
    /// on opposite sides of a compaction, and a scan that sees only one half draws the wrong conclusion
    /// from a complete pair.
    ///
    /// MEASURED, NOT FEARED. The state that makes this differ from a live-only read is a channel whose
    /// live file holds NO conversation entry at all, which needs 45 consecutive app entries and sounds
    /// unreachable. On 2026-08-14 three member channels on this machine were in it —
    /// `ai-orchestrator-3/imp-1` (81 live entries, 0 non-app), `da-vinci-fintech-suite-5/imp-6` (86, 0)
    /// and `imp-8` (54, 0) — and all three had a null `closedUtc`, so nothing screened them out. Their
    /// last real word was a supervisor's *"THE ENDEAVOUR IS CLOSED BY THE OWNER"* and
    /// <see cref="Status.MemberState_Resolver.Resolve"/>, reading the live file alone, called them
    /// `ImplementerWorking`.
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Read_AllEntries(string channelFilePath)
    {
        var archived = Read_ArchivedEntries(channelFilePath);
        var live = ChannelEntry_Parser.Parse_All(UsageTotals_Reader.Read_Text_Safe(channelFilePath));

        if (archived.Count == 0)
            return live;

        return [.. archived, .. live];
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
