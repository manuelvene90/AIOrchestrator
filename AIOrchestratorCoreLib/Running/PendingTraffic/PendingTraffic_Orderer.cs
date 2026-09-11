using System.Globalization;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.TurnSource;

namespace AIOrchestratorCoreLib.Running.PendingTraffic;

/// <summary>
/// THE ORDER THE ENTRIES OF ONE TURN ARE READ IN when they came from several channels: by the time
/// stamped on them, the owner ahead of a spoke when those are equal, then by source, then by the order
/// they sit in their own file.
///
/// <para>
/// ORDER IS PRESENTATION, NEVER DELIVERY. The stamp is agent-written for a terminal member's entries and
/// is a guess unless that session re-read the file — CLAUDE.md decision 12, with a supervisor stamping
/// <c>01:34</c> on an entry written at <c>15:20</c> the previous day. So a wrong stamp can only put a
/// paragraph in the wrong place in one prompt; it cannot lose an entry, cannot deliver one twice and
/// cannot advance a cursor, because none of those read this. Building the ordering on the same field the
/// cursor once trusted, and saying plainly that here it is allowed to be wrong, is the point of the
/// split.
/// </para>
/// <para>
/// AN UNREADABLE STAMP INHERITS THE LAST GOOD ONE FROM ITS OWN SOURCE rather than falling to the
/// beginning of time. A malformed header in the middle of a spoke would otherwise drag that entry to the
/// top of the prompt, ahead of the owner's message and of everything that actually preceded it — the
/// wrong answer twice over, since the one thing known about it IS that it came after its neighbour.
/// </para>
/// <para>
/// THE OWNER GOES FIRST ON A TIE, which is the only place the priority rule bites: both channels are
/// read in one turn either way, so it decides what the session reads first, not what it reads at all.
/// A turn that answers a member and never notices the owner's message would be a phone line that a
/// busy crew can drown out, and the owner's channel is the reason this transport exists.
/// </para>
/// </summary>
public static class PendingTraffic_Orderer
{
    public static IReadOnlyList<PendingEntry> Order(IReadOnlyList<(ITurnSource Source, IReadOnlyList<IChannelEntry> Entries)> perSource)
    {
        List<(PendingEntry Pending, DateTime Stamp, int OwnerFirst, string SourceKey, int PositionInSource)> keyed = [];

        foreach (var (source, entries) in perSource)
        {
            var lastKnown = DateTime.MinValue;

            for (var position = 0; position < entries.Count; position++)
            {
                var entry = entries[position];

                if (Try_ReadStamp(entry, out var stamp))
                    lastKnown = stamp;

                keyed.Add((new PendingEntry(source, entry), lastKnown, source.IsOwnerChannel ? 0 : 1, source.Key, position));
            }
        }

        return
        [
            .. keyed
                .OrderBy(item => item.Stamp)
                .ThenBy(item => item.OwnerFirst)
                .ThenBy(item => item.SourceKey, StringComparer.Ordinal)
                .ThenBy(item => item.PositionInSource)
                .Select(item => item.Pending)
        ];
    }

    /// <summary>
    /// The header's date field, in the format <see cref="Channels.ChannelAppender"/> writes
    /// (<c>yyyy-MM-dd HH:mm</c>) and in whatever else parses as a local date-time. False for anything
    /// else — including the empty string a header without an em-dash produces.
    /// </summary>
    static bool Try_ReadStamp(IChannelEntry entry, out DateTime stamp)
    {
        return DateTime.TryParse(entry.DateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp);
    }
}
