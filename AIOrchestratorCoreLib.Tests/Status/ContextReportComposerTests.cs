using AIOrchestratorCoreLib.Status;
using AIOrchestratorCoreLib.Status.SessionContextUsage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// /context - the one command that answers "how full is everything" directly, so unlike the status
/// line and the digest it filters nothing by percentage. These tests live here rather than against
/// the bridge engine because the engine is internal sealed with no InternalsVisibleTo: anything
/// decided inside it can be deleted without reddening a test, and three gates once were.
/// </summary>
public class ContextReportComposerTests
{
    static readonly DateTime NOW = new(2026, 8, 21, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ItListsEverySessionThatHasReported()
    {
        var text = ContextReport_Composer.Build_ForOrchestration(
            "AI-Orch · context awareness",
            [Row("supervisor", 41), Row("imp-1", 93), Row("rev-1", 7)],
            NOW);

        Assert.Equal(
            "CONTEXT - AI-Orch · context awareness" + LF
            + "- supervisor: ctx 41%" + LF
            + "- imp-1: ctx 93%" + LF
            + "- rev-1: ctx 7%",
            text);
    }

    /// <summary>
    /// THE ASK IS THE ASK. A member at 7% is nowhere near either surface threshold and is listed
    /// anyway - answering a direct question with a partial roster would be worse than not answering.
    /// If this ever starts filtering, the owner asking /context gets a list that silently omits
    /// sessions and no way to tell which.
    /// </summary>
    [Fact]
    public void NothingIsFilteredByPercentage()
    {
        var text = ContextReport_Composer.Build_ForOrchestration("orch", [Row("imp-1", 1)], NOW);

        Assert.Contains("- imp-1: ctx 1%", text);
    }

    /// <summary>
    /// A CLOSED SESSION HAS NO WINDOW. Its probe file survives as audit trail and the lifetime cost
    /// totals still count it on purpose, but a percentage is a claim about right now.
    /// </summary>
    [Fact]
    public void AClosedSessionIsNotListed()
    {
        var text = ContextReport_Composer.Build_ForOrchestration(
            "orch", [Row("imp-1", 93), Closed("imp-2", 88)], NOW);

        Assert.Contains("imp-1", text);
        Assert.DoesNotContain("imp-2", text);
    }

    /// <summary>Unknown is not zero: a session that never reported simply is not described.</summary>
    [Fact]
    public void ASessionWithNoReadingIsNotDescribed()
    {
        var text = ContextReport_Composer.Build_ForOrchestration(
            "orch", [Row("solo-1", 52), Unknown("communicator")], NOW);

        Assert.Contains("solo-1", text);
        Assert.DoesNotContain("communicator", text);
    }

    [Fact]
    public void AnOrchestrationWhereNobodyHasReportedSaysSoPlainly()
    {
        Assert.Equal(
            "orch: no session has reported its context yet",
            ContextReport_Composer.Build_ForOrchestration("orch", [Unknown("supervisor"), Closed("imp-1", 90)], NOW));
    }

    /// <summary>
    /// A working session rewrites its probe on every render, so its figure is seconds old and dating
    /// every row would be noise on all of them.
    /// </summary>
    [Fact]
    public void AFreshReadingIsNotDated()
    {
        var text = ContextReport_Composer.Build_ForOrchestration(
            "orch", [Row("imp-1", 93, NOW.AddSeconds(-30))], NOW);

        Assert.Equal("CONTEXT - orch" + LF + "- imp-1: ctx 93%", text);
    }

    /// <summary>
    /// But a session that has not rendered in a while is quoting history, and the owner needs told
    /// rather than left to assume the number is live.
    /// </summary>
    [Fact]
    public void AStaleReadingIsDated()
    {
        var text = ContextReport_Composer.Build_ForOrchestration(
            "orch", [Row("imp-1", 93, NOW.AddMinutes(-17))], NOW);

        Assert.Contains("- imp-1: ctx 93% · 17 min old", text);
    }

    /// <summary>
    /// A file mtime can land in the FUTURE on a skewed clock, and a negative age must never be
    /// described - the same ruling SessionDuration_Formatter carries for agent-written stamps.
    /// </summary>
    [Fact]
    public void AReadingStampedInTheFutureIsTreatedAsFresh()
    {
        Assert.Equal("", ContextReport_Composer.Describe_Age_Suffix(Reading(93, NOW.AddHours(2)), NOW));
    }

    [Fact]
    public void TheFullestSessionIsPickedAndNamed()
    {
        var fullest = ContextReport_Composer.Pick_Fullest_OrNull([Row("supervisor", 41), Row("imp-1", 93), Row("rev-1", 7)]);

        Assert.NotNull(fullest);
        Assert.Equal("imp-1", fullest.Value.Label);
        Assert.Equal(93, fullest.Value.Percent);
    }

    /// <summary>The fullest of what is LIVE - a closed session must not win the comparison.</summary>
    [Fact]
    public void AClosedSessionCannotBeTheFullest()
    {
        var fullest = ContextReport_Composer.Pick_Fullest_OrNull([Row("imp-1", 40), Closed("imp-2", 99)]);

        Assert.NotNull(fullest);
        Assert.Equal("imp-1", fullest.Value.Label);
    }

    [Fact]
    public void NothingLiveMeansNothingToPick()
    {
        Assert.Null(ContextReport_Composer.Pick_Fullest_OrNull([Unknown("supervisor"), Closed("imp-1", 99)]));
    }

    /// <summary>
    /// The FIRST row must be able to win. Seeding the comparison with a label already set would let
    /// a later, emptier session hold the title; seeding with 0 and no label is what makes the very
    /// first live reading the one to beat.
    /// </summary>
    [Fact]
    public void TheOnlyLiveSessionWinsEvenAtZero()
    {
        var fullest = ContextReport_Composer.Pick_Fullest_OrNull([Row("solo-1", 0)]);

        Assert.NotNull(fullest);
        Assert.Equal("solo-1", fullest.Value.Label);
    }

    /// <summary>
    /// THE GENERAL SUPERVISOR IS NOT AN ORCHESTRATION and belongs to no roster, so a report built
    /// from session.json files alone leaves out the one session the owner talks to from every topic.
    /// It is listed first because if ITS window fills, the concierge that answers "work on X" stops
    /// and they would find that out by being ignored.
    /// </summary>
    [Fact]
    public void TheGeneralSupervisorIsListedFirstAndIsNotAnOrchestration()
    {
        var text = ContextReport_Composer.Build_ForEveryOrchestration(
            Reading(31, NOW),
            [Orchestration("AI-Orch · context awareness", Row("solo-1", 32))]);

        Assert.Equal(
            "CONTEXT - fullest session per orchestration" + LF
            + "general supervisor: ctx 31%" + LF
            + "AI-Orch · context awareness: solo-1 ctx 32%",
            text);
    }

    [Fact]
    public void EachOrchestrationContributesItsFullestSessionByName()
    {
        var text = ContextReport_Composer.Build_ForEveryOrchestration(
            null,
            [
                Orchestration("crew", Row("supervisor", 41), Row("imp-1", 93)),
                Orchestration("basic", Row("solo-1", 12)),
            ]);

        Assert.Equal(
            "CONTEXT - fullest session per orchestration" + LF
            + "crew: imp-1 ctx 93%" + LF
            + "basic: solo-1 ctx 12%",
            text);
    }

    /// <summary>An orchestration whose sessions have all gone quiet contributes no line at all.</summary>
    [Fact]
    public void AnOrchestrationWithNothingToReportIsSkippedRatherThanShownEmpty()
    {
        var text = ContextReport_Composer.Build_ForEveryOrchestration(
            Reading(31, NOW),
            [Orchestration("silent", Unknown("supervisor")), Orchestration("live", Row("solo-1", 12))]);

        Assert.DoesNotContain("silent", text);
        Assert.Contains("live: solo-1 ctx 12%", text);
    }

    /// <summary>
    /// The general supervisor ALONE is still an answer. It is the session the owner is most likely
    /// asking about, and "no open orchestration has reported" would be false while it is sitting
    /// there with a reading.
    /// </summary>
    [Fact]
    public void TheGeneralSupervisorAloneIsStillAnAnswer()
    {
        Assert.Equal(
            "CONTEXT - fullest session per orchestration" + LF + "general supervisor: ctx 31%",
            ContextReport_Composer.Build_ForEveryOrchestration(Reading(31, NOW), []));
    }

    [Fact]
    public void NothingAnywhereSaysSoPlainly()
    {
        Assert.Equal(
            "no open orchestration has reported its context yet",
            ContextReport_Composer.Build_ForEveryOrchestration(null, [Orchestration("silent", Unknown("supervisor"))]));
    }

    static ContextReport_Composer.OrchestrationRows Orchestration(string title, params ContextReport_Composer.ContextRow[] sessions)
    {
        return new ContextReport_Composer.OrchestrationRows(title, sessions);
    }

    

    const string LF = "\n";

    static ContextReport_Composer.ContextRow Row(string label, double percent)
    {
        return new ContextReport_Composer.ContextRow(label, IsClosed: false, Reading(percent, NOW.AddSeconds(-5)));
    }

    static ContextReport_Composer.ContextRow Row(string label, double percent, DateTime probedUtc)
    {
        return new ContextReport_Composer.ContextRow(label, IsClosed: false, Reading(percent, probedUtc));
    }

    static ContextReport_Composer.ContextRow Closed(string label, double percent)
    {
        return new ContextReport_Composer.ContextRow(label, IsClosed: true, Reading(percent, NOW.AddSeconds(-5)));
    }

    static ContextReport_Composer.ContextRow Unknown(string label)
    {
        return new ContextReport_Composer.ContextRow(label, IsClosed: false, null);
    }

    static ISessionContextUsage Reading(double percent, DateTime probedUtc)
    {
        return SessionContextUsage_Factory.Create(percent, probedUtc);
    }
}
