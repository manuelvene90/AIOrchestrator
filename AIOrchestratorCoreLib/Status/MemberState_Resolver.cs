using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// Derives a spoke's development state from its parsed channel entries. The window and blocked
/// markers are protocol vocabulary — the role commands instruct agents to use these exact phrases.
/// </summary>
public static class MemberState_Resolver
{
    public const string WRITING_WINDOW_OPEN_MARKER = "WRITING WINDOW OPEN";
    public const string WRITING_WINDOW_CLOSED_MARKER = "WRITING WINDOW CLOSED";
    public const string MUTATION_WINDOW_OPEN_MARKER = "MUTATION WINDOW OPEN";
    public const string MUTATION_WINDOW_CLOSED_MARKER = "MUTATION WINDOW CLOSED";
    public const string BLOCKED_ON_OWNER_MARKER = "BLOCKED ON OWNER";

    public static MemberStates Resolve(IReadOnlyList<IChannelEntry> entries)
    {
        if (entries.Count == 0)
            return MemberStates.NewNoTraffic;

        if (Has_OpenWindow(entries, WRITING_WINDOW_OPEN_MARKER, WRITING_WINDOW_CLOSED_MARKER))
            return MemberStates.WritingWindowOpen;

        if (Has_OpenWindow(entries, MUTATION_WINDOW_OPEN_MARKER, MUTATION_WINDOW_CLOSED_MARKER))
            return MemberStates.WritingWindowOpen;

        var lastEntry = entries[entries.Count - 1];

        if (lastEntry.Author == ChannelAuthors.Implementer)
        {
            if (Contains_Marker(lastEntry, BLOCKED_ON_OWNER_MARKER))
                return MemberStates.BlockedOnOwner;

            return MemberStates.AwaitingSupervisorReview;
        }

        return MemberStates.ImplementerWorking;
    }

    static bool Has_OpenWindow(IReadOnlyList<IChannelEntry> entries, string openMarker, string closedMarker)
    {
        var lastOpen = Find_LastEntryIndex_WithMarker(entries, openMarker);

        if (lastOpen < 0)
            return false;

        var lastClosed = Find_LastEntryIndex_WithMarker(entries, closedMarker);

        return lastClosed < lastOpen;
    }

    static int Find_LastEntryIndex_WithMarker(IReadOnlyList<IChannelEntry> entries, string marker)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (Contains_Marker(entries[i], marker))
                return i;
        }

        return -1;
    }

    static bool Contains_Marker(IChannelEntry entry, string marker)
    {
        return entry.RawText.Contains(marker, StringComparison.OrdinalIgnoreCase);
    }
}
