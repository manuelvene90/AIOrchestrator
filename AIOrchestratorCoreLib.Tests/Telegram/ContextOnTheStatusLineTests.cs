using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// How full each session's window is, on the one message per topic. The owner asked for it on
/// 2026-08-21: the session they talk to always, implementers and reviewers only once they are
/// nearly full — so the line stays glanceable and a member appearing on it MEANS something.
///
/// The supervisor rides on the TITLE rather than a row of its own, because this line lists members
/// and a crew's supervisor is not one.
/// </summary>
public class ContextOnTheStatusLineTests
{
    static readonly DateTime NOW = new(2026, 8, 21, 20, 30, 0);
    static readonly DateTime PROBED = new(2026, 8, 21, 20, 29, 0, DateTimeKind.Utc);

    [Fact]
    public void ASolosContextIsOnItsRowWhateverItIs()
    {
        var line = TopicStatusLine_Builder.Build(
            "AI-Orch · context awareness", Progress(1, 5), [Member("solo-1", Briefed(), Reading(52))], null, NOW,
            aMessageIsAlreadyPosted: false);

        Assert.Contains("• solo-1 · wiring the context field · 30 min · ctx 52%", line);
    }

    /// <summary>
    /// A quiet member keeps its figure too: standing by with a nearly-full window is exactly the
    /// state the owner needs to see, and it is the one a "only show it with a task" rule would hide.
    /// </summary>
    [Fact]
    public void AMemberStandingByStillCarriesItWhenItIsNearlyFull()
    {
        var line = TopicStatusLine_Builder.Build(
            "orch", Progress(1, 5), [Member("imp-1", [], Reading(95))], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Contains("• imp-1 · standing by · ctx 95%", line);
    }

    [Fact]
    public void AnImplementerWithRoomToSpareSaysNothingAboutIt()
    {
        var line = TopicStatusLine_Builder.Build(
            "orch", Progress(1, 5), [Member("imp-1", Briefed(), Reading(40))], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.DoesNotContain("ctx", line);
    }

    [Fact]
    public void TheSupervisorsOwnWindowRidesOnTheTitle()
    {
        var line = TopicStatusLine_Builder.Build(
            "Telegram UX + limits", Progress(72, 113), [], null, NOW, aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: null, supervisorContext: Reading(41));

        Assert.Equal("Telegram UX + limits · 72/113 · 63% · sup ctx 41%", line);
    }

    /// <summary>
    /// A BASIC ORCHESTRATION HAS NO SUPERVISOR, so nothing is added to its title — the figure the
    /// owner wants is on the solo's own row instead, and a "sup ctx" on a topic with no supervisor
    /// would name a session that does not exist.
    /// </summary>
    [Fact]
    public void NoSupervisorMeansNothingOnTheTitle()
    {
        var line = TopicStatusLine_Builder.Build(
            "CRM invoice crash", Progress(3, 4), [], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Equal("CRM invoice crash · 3/4 · 75%", line);
    }

    /// <summary>
    /// NOTHING TO SAY STILL MEANS SAY NOTHING. A supervisor reading is not substance on its own —
    /// a topic with no ledger, no live member and no history must stay silent rather than send a
    /// message whose entire content is the title the owner is already looking at, plus a number.
    /// </summary>
    [Fact]
    public void ASupervisorReadingAloneDoesNotBreakTheSilenceRule()
    {
        Assert.Equal(
            "",
            TopicStatusLine_Builder.Build(
                "CRM invoice crash", null, [], null, NOW, aMessageIsAlreadyPosted: false,
                figuresUnchangedFor: null, supervisorContext: Reading(41)));
    }

    /// <summary>It sits AFTER the unchanged-for clause, so the ledger reading stays together.</summary>
    [Fact]
    public void ItComesAfterTheFiguresHaveNotMovedClause()
    {
        var line = TopicStatusLine_Builder.Build(
            "orch", Progress(3, 4), [], null, NOW, aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(25), supervisorContext: Reading(41));

        Assert.Equal("orch · 3/4 · 75% · unchanged 25 min · sup ctx 41%", line);
    }

    [Fact]
    public void ASessionThatNeverReportedItsContextIsSimplyNotDescribed()
    {
        var line = TopicStatusLine_Builder.Build(
            "orch", Progress(1, 5), [Member("solo-1", Briefed(), null)], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Contains("• solo-1 · wiring the context field · 30 min", line);
        Assert.DoesNotContain("ctx", line);
    }

    static IReadOnlyList<IChannelEntry> Briefed()
    {
        return ChannelEntry_Parser.Parse_All(
            "## [1] FROM supervisor — 2026-08-21 20:00 — wiring the context field\n\ngo\n");
    }

    static IPlanProgress Progress(int done, int total)
    {
        return PlanProgress_Factory.Create(done, 0, 0, 0, total, null, [], [], []);
    }

    static ITopicStatusMember Member(string memberId, IReadOnlyList<IChannelEntry> entries, ISessionContextUsage? context)
    {
        return TopicStatusMember_Factory.Create(memberId, entries, isClosed: false, context);
    }

    static ISessionContextUsage Reading(double percent)
    {
        return SessionContextUsage_Factory.Create(percent, PROBED);
    }
}
