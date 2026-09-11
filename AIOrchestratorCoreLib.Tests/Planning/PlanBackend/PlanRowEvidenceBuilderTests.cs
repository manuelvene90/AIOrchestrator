using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning.PlanBackend;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Planning.PlanBackend;

/// <summary>
/// The evidence line is read by software outside this repository, so its FORMAT is a contract — and it
/// was written inside engine code no test could reach, while PlanRowEvidence quoted the same string in
/// its docstring. One format, two copies, neither pinned.
/// </summary>
public class PlanRowEvidenceBuilderTests
{
    static readonly DateTime OBSERVED = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject)
    {
        return ChannelEntry_Factory.Create(index, author, "2026-09-06 12:00", subject, "body", "raw");
    }

    [Fact]
    public void ItNamesTheLastEntryAndCarriesItsSubject()
    {
        var evidence = PlanRowEvidence_Builder.Build(
            [Entry(11, ChannelAuthors.Owner, "is it done?"), Entry(12, ChannelAuthors.Supervisor, "export button shipped")],
            OBSERVED);

        Assert.Equal("owner-channel #12 FROM supervisor", evidence.ChannelEntryRef);
        Assert.Equal("export button shipped", evidence.Excerpt);
        Assert.Equal(OBSERVED, evidence.ObservedUtc);
    }

    /// <summary>
    /// An empty channel is ordinary — a fresh orchestration whose plan came entirely from upstream. It
    /// answers null rather than inventing a reference nobody can look up.
    /// </summary>
    [Fact]
    public void AnEmptyChannelYieldsAnObservationWithNoReference()
    {
        var evidence = PlanRowEvidence_Builder.Build([], OBSERVED);

        Assert.Null(evidence.ChannelEntryRef);
        Assert.Null(evidence.Excerpt);
        Assert.Equal(OBSERVED, evidence.ObservedUtc);
    }
}
