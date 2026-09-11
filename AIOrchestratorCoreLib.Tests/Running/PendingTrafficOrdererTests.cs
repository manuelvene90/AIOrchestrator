using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running.PendingTraffic;
using AIOrchestratorCoreLib.Running.TurnSource;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// What order one turn reads its channels in. Order is PRESENTATION here — the stamp it sorts on is
/// agent-written and may be wrong (CLAUDE.md decision 12) — so every case below is about a paragraph
/// being in the right place, never about an entry existing or not.
/// </summary>
public class PendingTrafficOrdererTests
{
    static readonly ITurnSource OWNER = TurnSource_Factory.Create_Owner("/o/owner-channel.md");
    static readonly ITurnSource IMP = TurnSource_Factory.Create_Spoke("imp-1", "/o/imp-1/channel.md");

    static IReadOnlyList<IChannelEntry> Entries(params string[] raw)
    {
        return ChannelEntry_Parser.Parse_All(string.Concat(raw));
    }

    static string Owner_Entry(string time, string subject)
    {
        return $"## [1] FROM owner — 2026-09-05 {time} — {subject}\n\nbody\n";
    }

    static string Member_Entry(int index, string time, string subject)
    {
        return $"## [{index}] FROM implementer — 2026-09-05 {time} — {subject}\n\nbody\n";
    }

    [Fact]
    public void TheOldestGoesFirst_AcrossChannels()
    {
        var ordered = PendingTraffic_Orderer.Order(
        [
            (OWNER, Entries(Owner_Entry("10:30", "late question"))),
            (IMP, Entries(Member_Entry(7, "10:05", "early report"))),
        ]);

        Assert.Equal(["early report", "late question"], ordered.Select(item => item.Entry.Subject));
    }

    /// <summary>
    /// The priority rule, and the only place it applies: a tie. Both channels are read in the same turn
    /// either way — the owner's line is the reason this transport exists, so it is not read after a
    /// busy crew's traffic when they arrived together.
    /// </summary>
    [Fact]
    public void OnATie_TheOwnerGoesFirst()
    {
        var ordered = PendingTraffic_Orderer.Order(
        [
            (IMP, Entries(Member_Entry(7, "10:05", "report"))),
            (OWNER, Entries(Owner_Entry("10:05", "question"))),
        ]);

        Assert.Equal(["question", "report"], ordered.Select(item => item.Entry.Subject));
    }

    [Fact]
    public void WithinOneChannel_FileOrderIsKept_EvenWhenTheIndicesAreNot()
    {
        var ordered = PendingTraffic_Orderer.Order(
        [
            (IMP, Entries(Member_Entry(9, "10:05", "first"), Member_Entry(4, "10:05", "second"))),
        ]);

        Assert.Equal(["first", "second"], ordered.Select(item => item.Entry.Subject));
    }

    /// <summary>
    /// An unreadable stamp inherits the last good one FROM ITS OWN CHANNEL rather than falling to the
    /// beginning of time. Sorted as a minimum it would be dragged to the top of the prompt, ahead of the
    /// owner's message and of the entry it demonstrably came after — wrong twice, from the one thing that
    /// is actually known about it.
    /// </summary>
    [Fact]
    public void AnUnreadableStamp_InheritsItsNeighbours_RatherThanSortingToTheTop()
    {
        var ordered = PendingTraffic_Orderer.Order(
        [
            (OWNER, Entries(Owner_Entry("10:00", "question"))),
            (IMP, Entries(Member_Entry(7, "10:05", "report"), "## [8] FROM implementer — later — follow-up\n\nbody\n")),
        ]);

        Assert.Equal(["question", "report", "follow-up"], ordered.Select(item => item.Entry.Subject));
    }

    [Fact]
    public void EveryEntryKeepsTheChannelItCameFrom()
    {
        var ordered = PendingTraffic_Orderer.Order(
        [
            (OWNER, Entries(Owner_Entry("10:00", "question"))),
            (IMP, Entries(Member_Entry(7, "10:05", "report"))),
        ]);

        Assert.Equal([TurnSource_Factory.OWNER_KEY, "imp-1"], ordered.Select(item => item.Source.Key));
    }
}
