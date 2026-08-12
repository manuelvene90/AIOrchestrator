using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.Planning;
using AIOrchestratorCoreLib.Planning.PlanProgress;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TopicStatusMember;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The per-topic status line: posted once, edited forever, never pinned.
///
/// The owner refused pinning for a reason that decides this file's contents: "working now" elsewhere
/// in the app is FILE MTIME, true for ~2 minutes after a turn ends, and pinned that wrong state
/// would sit in front of them permanently. So NO MTIME appears here — every word comes from parsed
/// channel state and the ledger, which is why these tests can build a line from entries alone with
/// no filesystem at all. If a future change makes that impossible, that is the signal.
/// </summary>
public class TopicStatusLineBuilderTests
{
    static readonly DateTime NOW = new(2026, 8, 12, 12, 30, 0);

    [Fact]
    public void TheTitleLineCarriesTheLedgerCountAndPercent()
    {
        var line = TopicStatusLine_Builder.Build("Telegram UX + limits", Progress(72, 113), [], null, NOW);

        Assert.Equal("Telegram UX + limits          72/113 · 63%", line);
    }

    /// <summary>
    /// NOTHING TO SAY MEANS SAY NOTHING. With no ledger, no live member and no history, the line
    /// would have been the topic's own title repeated back at the owner — a message whose entire
    /// content is what they are already looking at. Item 15.
    /// </summary>
    [Fact]
    public void AnOrchestrationWithNothingToReportEmitsNothing()
    {
        Assert.Equal("", TopicStatusLine_Builder.Build("CRM invoice crash", null, [], null, NOW));
    }

    /// <summary>But a title with a REAL ledger is substance, and it still stands alone.</summary>
    [Fact]
    public void ATitleWithALedgerIsWorthWriting()
    {
        Assert.Equal("CRM invoice crash          3/4 · 75%", TopicStatusLine_Builder.Build("CRM invoice crash", Progress(3, 4), [], null, NOW));
    }

    /// <summary>
    /// A closed member does not resurrect the line either — the contract says it falls off, and one
    /// message must not disagree with itself about whether a member exists.
    /// </summary>
    [Fact]
    public void AnOrchestrationWhoseOnlyMemberIsClosedEmitsNothing()
    {
        var line = TopicStatusLine_Builder.Build(
            "orch", null, [Member("imp-1", Brief("the old task", "2026-08-12 09:00"), isClosed: true)], null, NOW);

        Assert.Equal("", line);
    }

    /// <summary>
    /// N8: a PLAN.md of nothing but struck-out `- [-]` lines parses to a NON-NULL progress with
    /// Total 0. Weakening the check to `progress != null` left 610 green, because no case covered a
    /// ledger that exists and owes nothing.
    /// </summary>
    [Fact]
    public void ALedgerOfNothingButDroppedLinesHasNothingToSay()
    {
        Assert.Equal("", TopicStatusLine_Builder.Build("orch", Progress(0, 0), [], null, NOW));
    }

    [Fact]
    public void AMemberRowIsWhoWhatAndHowLong()
    {
        var line = TopicStatusLine_Builder.Build(
            "orch",
            null,
            [Member("imp-1", Brief("committing the marker fix", "2026-08-12 12:26"))],
            null,
            NOW);

        Assert.Contains("imp-1", line);
        Assert.Contains("committing the marker fix", line);
        // "4 min", not the mock's "4m": the ONE duration formatter this repo has renders it that way,
        // and item 12 forbids a second one just to shorten a column.
        Assert.Contains("4 min", line);
    }

    /// <summary>
    /// A DECLARED-idle member reads "idle" — that is the whole point of the marker existing, and the
    /// owner should not be shown a stale task for somebody who has said they have nothing running.
    /// </summary>
    [Fact]
    public void ADeclaredIdleMemberReadsIdle()
    {
        var entries = new[]
        {
            Entry(1, ChannelAuthors.Supervisor, "review the marker fix", "2026-08-12 11:00"),
            Entry(2, ChannelAuthors.Reviewer, "STANDING BY — review filed", "2026-08-12 12:00"),
        };

        var line = TopicStatusLine_Builder.Build("orch", null, [Member("rev-3", entries)], null, NOW);

        Assert.Contains("rev-3   idle", line);
        Assert.DoesNotContain("review the marker fix", line);
    }

    /// <summary>
    /// AWAITING A VERDICT IS NOT IDLE. That member is waiting on the SUPERVISOR, and showing it as
    /// idle would hide the one queue the owner can actually unblock.
    /// </summary>
    [Fact]
    public void AMemberAwaitingAVerdictIsNotShownAsIdle()
    {
        var entries = new[]
        {
            Entry(1, ChannelAuthors.Supervisor, "fix the ledger denominator", "2026-08-12 12:00"),
            Entry(2, ChannelAuthors.Implementer, "done, 565 tests pass", "2026-08-12 12:20"),
        };

        var line = TopicStatusLine_Builder.Build("orch", null, [Member("imp-1", entries)], null, NOW);

        Assert.DoesNotContain("idle", line);
        Assert.Contains("fix the ledger", line);
    }

    /// <summary>A member that has never been briefed has nothing to show, and says so.</summary>
    [Fact]
    public void ANeverBriefedMemberReadsIdle()
    {
        var line = TopicStatusLine_Builder.Build(
            "orch",
            null,
            [Member("rev-1", [Entry(1, ChannelAuthors.Reviewer, "rev-1 online", "2026-08-12 12:00")])],
            null,
            NOW);

        Assert.Contains("rev-1   idle", line);
    }

    /// <summary>Contract item 4: a closed member drops off rather than lingering as a stale row.</summary>
    [Fact]
    public void AClosedMemberDropsOffTheLine()
    {
        var brief = Brief("the old task", "2026-08-12 09:00");

        var line = TopicStatusLine_Builder.Build(
            "orch",
            null,
            [Member("imp-1", brief, isClosed: true), Member("imp-2", Brief("the live task", "2026-08-12 12:25"))],
            null,
            NOW);

        Assert.DoesNotContain("imp-1", line);
        Assert.Contains("imp-2", line);
    }

    /// <summary>
    /// A FUTURE stamp yields no duration rather than a confident wrong number — the one formatter
    /// this repo has returns null for it, and this line must not invent one. A supervisor really did
    /// stamp an entry 10 hours ahead.
    /// </summary>
    [Fact]
    public void AFutureStampShowsTheTaskWithoutADuration()
    {
        var line = TopicStatusLine_Builder.Build(
            "orch",
            null,
            [Member("imp-1", Brief("the task", "2026-08-13 23:00"))],
            null,
            NOW);

        // Asserted as the WHOLE member ROW, not as "contains the task": a duration appended after it
        // is exactly what must not happen, and a Contains check cannot see a trailing anything.
        Assert.Equal("imp-1   the task", line.Split('\n')[1]);
    }

    [Fact]
    public void TheLastLineIsAddedOnlyWhenThereIsSomethingToSay()
    {
        Assert.Contains("last", TopicStatusLine_Builder.Build("orch", null, [], "gate cleared on 34e5515", NOW));
        Assert.DoesNotContain("last", TopicStatusLine_Builder.Build("orch", null, [], null, NOW));
        Assert.DoesNotContain("last", TopicStatusLine_Builder.Build("orch", null, [], "   ", NOW));
    }

    /// <summary>
    /// THE WHOLE SHAPE, as the owner approved it. Pinned here because the individual assertions above
    /// can all pass while the line reads as something nobody would want on their phone.
    /// </summary>
    [Fact]
    public void TheApprovedShape()
    {
        var line = TopicStatusLine_Builder.Build(
            "Telegram UX + limits",
            Progress(72, 113),
            [
                Member("imp-1", Brief("committing the marker fix", "2026-08-12 12:26")),
                Member("rev-2", Brief("reviewing the hooks branch", "2026-08-12 12:18")),
                Member("rev-3", [Entry(1, ChannelAuthors.Reviewer, "rev-3 online", "2026-08-12 12:00")]),
            ],
            "gate cleared on 34e5515",
            NOW);

        var lines = line.Split('\n');

        Assert.Equal(6, lines.Length);
        Assert.Equal("Telegram UX + limits          72/113 · 63%", lines[0]);
        Assert.StartsWith("imp-1", lines[1]);
        Assert.EndsWith("4 min", lines[1]);
        Assert.StartsWith("rev-2", lines[2]);
        Assert.EndsWith("12 min", lines[2]);
        Assert.Equal("rev-3   idle", lines[3]);
        Assert.StartsWith("last", lines[5]);
    }

    /// <summary>
    /// ONE reading of an agent stamp, and the two used to disagree inside a single message: the
    /// builder printed no duration for a future-stamped entry while the engine's own comparison
    /// promoted that same entry to `last` and held it there until real time caught up.
    /// </summary>
    [Fact]
    public void AFutureStampIsRefusedByTheSharedReaderTheEngineAlsoUses()
    {
        Assert.False(SessionDuration_Formatter.Try_ReadTrustedStamp("2026-08-13 23:00", NOW, out _));
        Assert.False(SessionDuration_Formatter.Try_ReadTrustedStamp("not a date", NOW, out _));
        Assert.True(SessionDuration_Formatter.Try_ReadTrustedStamp("2026-08-12 12:26", NOW, out _));

        // The skew tolerance survives: a stamp a minute ahead is a minute-rounded clock, not a lie.
        Assert.True(SessionDuration_Formatter.Try_ReadTrustedStamp("2026-08-12 12:31", NOW, out _));
    }

    static IPlanProgress Progress(int done, int total)
    {
        return PlanProgress_Factory.Create(done, 0, 0, 0, total, null, [], [], []);
    }

    static IReadOnlyList<IChannelEntry> Brief(string subject, string stamp)
    {
        return [Entry(1, ChannelAuthors.Supervisor, subject, stamp)];
    }

    static ITopicStatusMember Member(string memberId, IReadOnlyList<IChannelEntry> entries, bool isClosed = false)
    {
        return TopicStatusMember_Factory.Create(memberId, entries, isClosed);
    }

    static IChannelEntry Entry(int index, ChannelAuthors author, string subject, string stamp)
    {
        return ChannelEntry_Factory.Create(
            index, author, stamp, subject, "body",
            $"## [{index}] FROM {author} — {stamp} — {subject}\nbody");
    }
}
