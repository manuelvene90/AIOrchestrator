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
/// THE SUPERVISOR'S OWN READING RODE ON THE LEAD LINE UNTIL 2026-09-09, for want of a row of its
/// own. Brief C gave field 2 ("sup · …") to the supervisor, so its context figure moved there with
/// everything else the supervisor's row now carries — a usage-limit pause, a declared state — rather
/// than staying a decoration on the lead word. The lead line opened with the topic's name until
/// 2026-08-24 and opens with the literal word `PULSE` now, which is why the expectations here still
/// read `PULSE` on their own first line — the owner did not want the topic name repeated back at
/// them, and that has not changed.
/// </summary>
public class ContextOnTheStatusLineTests
{
    static readonly DateTime NOW = new(2026, 8, 21, 20, 30, 0);
    static readonly DateTime PROBED = new(2026, 8, 21, 20, 29, 0, DateTimeKind.Utc);

    [Fact]
    public void ASolosContextIsOnItsRowWhateverItIs()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("solo-1", Briefed(), Reading(52))], null, NOW,
            aMessageIsAlreadyPosted: false);

        // The row also carries its STATE WORD now (owner, 2026-09-09) between the task and the
        // duration — this test is about WHERE the context figure lands, not that field, so it is
        // included in the expected substring rather than routed around with a second Contains.
        Assert.Contains("• solo-1 · wiring the context field · working · 30 min · ctx 52%", line);
    }

    /// <summary>
    /// A quiet member keeps its figure too: standing by with a nearly-full window is exactly the
    /// state the owner needs to see, and it is the one a "only show it with a task" rule would hide.
    /// </summary>
    [Fact]
    public void AMemberStandingByStillCarriesItWhenItIsNearlyFull()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("imp-1", [], Reading(95))], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Contains("• imp-1 · standing by · ctx 95%", line);
    }

    [Fact]
    public void AnImplementerWithRoomToSpareSaysNothingAboutIt()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("imp-1", Briefed(), Reading(40))], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.DoesNotContain("ctx", line);
    }

    /// <summary>
    /// REPLACES TheSupervisorsOwnWindowRidesOnTheTitle. Field 2 exists now, so the supervisor's
    /// context reading rides ON IT — its own line, "sup · ctx 41%" — rather than tacked onto the lead
    /// word. The class docstring records why.
    /// </summary>
    [Fact]
    public void TheSupervisorsOwnWindowRidesOnItsOwnSupRow()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(72, 113), [], null, NOW, aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: null, supervisorContext: Reading(41));

        Assert.Equal("PULSE\nsup · ctx 41%\n72/113 merged · 63 %\nupdated 20:30", line);
    }

    /// <summary>
    /// A BASIC ORCHESTRATION HAS NO SUPERVISOR, so field 2 does not appear at all — the figure the
    /// owner wants is on the solo's own row instead, and a "sup" row on a topic with no supervisor
    /// would name a session that does not exist.
    /// </summary>
    [Fact]
    public void NoSupervisorMeansNoSupRowAtAll()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(3, 4), [], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Equal("PULSE\n3/4 merged · 75 %\nupdated 20:30", line);
        Assert.DoesNotContain("sup ·", line);
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
                null, [], null, NOW, aMessageIsAlreadyPosted: false,
                figuresUnchangedFor: null, supervisorContext: Reading(41)));
    }

    /// <summary>
    /// REPLACES ItComesAfterTheFiguresHaveNotMovedClause. Field 2 now sits BEFORE the merged field
    /// rather than trailing it on one shared lead line — the owner's own field order (2026-09-09):
    /// what is waiting, what the supervisor says, who is live, the last event, the merge count, the
    /// heartbeat. The "unchanged" clause still rides beside the figures it is about, on the merged
    /// field's own line.
    /// </summary>
    [Fact]
    public void TheSupRowComesBeforeTheMergedFieldWhichStillCarriesTheUnchangedClause()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(3, 4), [], null, NOW, aMessageIsAlreadyPosted: false,
            figuresUnchangedFor: TimeSpan.FromMinutes(25), supervisorContext: Reading(41));

        Assert.Equal("PULSE\nsup · ctx 41%\n3/4 merged · 75 % · unchanged 25 min\nupdated 20:30", line);
    }

    [Fact]
    public void ASessionThatNeverReportedItsContextIsSimplyNotDescribed()
    {
        var line = TopicStatusLine_Builder.Build(
            Progress(1, 5), [Member("solo-1", Briefed(), null)], null, NOW, aMessageIsAlreadyPosted: false);

        Assert.Contains("• solo-1 · wiring the context field · working · 30 min", line);
        Assert.DoesNotContain("ctx", line);
    }

    /// <summary>
    /// A `FROM supervisor` brief — which is the real shape for an implementer or a reviewer, and a
    /// FIXTURE CONVENIENCE for the solo cases below: a basic orchestration has no supervisor, and a
    /// solo's member channel is the owner channel, so production never puts a supervisor entry in
    /// one. Nothing in this file turns on that — these tests ask WHOSE context figure is shown and
    /// where, and since 2026-08-24 a solo with a `FROM solo` entry of the same subject and stamp
    /// renders the identical row through TopicStatusLine_Builder's solo fallback. Left as it is
    /// rather than rewritten under cover of a compile fix; flagged so the next reader knows.
    /// </summary>
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
