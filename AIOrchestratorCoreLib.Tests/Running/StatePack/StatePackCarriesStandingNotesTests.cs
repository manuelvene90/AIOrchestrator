using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.StatePack;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.StatePack;

/// <summary>
/// A FRESH SESSION IS TOLD WHAT THE APP TOLD ITS PREDECESSOR — but only what is STILL STANDING. The
/// riding notes reach a session that is already running and holds a cursor; the pack is what a session
/// reads when it has neither. Without this section, routing the bookkeeping out of the channels would
/// mean a supervisor on <c>resume = fresh</c> never learning that its ledger is behind, that its
/// question was refused for being incomplete, or that a member has gone deaf — three things whose ONLY
/// trace was the app entry plan 02 moved.
///
/// <para>
/// AND ONLY WHAT IS STILL STANDING. The log is a LOG: it holds the whole chronicle, including the
/// advisory that was answered three days ago and the twenty <c>turn_ended</c> records of a working
/// afternoon. A pack that carried the chronicle would put this series' own saving straight back into
/// the boot it was taken out of. The screen is
/// <see cref="StatePackInputs_Reader.Select_StandingNotes"/> and these cases are its contract.
/// </para>
/// </summary>
public class StatePackCarriesStandingNotesTests : IDisposable
{
    static readonly DateTime NOW = new(2026, 9, 15, 16, 0, 0, DateTimeKind.Local);

    readonly string _log = Path.Combine(Path.GetTempPath(), $"aiorch-standing-{Guid.NewGuid():N}.jsonl");

    public void Dispose()
    {
        try { File.Delete(_log); } catch { }
    }

    [Fact]
    public void TheStandingNotesAppearUnderTheirOwnHeading_NewestLast()
    {
        StatusLog_Store.Append(_log, "PLAN.md is behind your verdicts", "Update PLAN.md.", NOW.AddMinutes(-20));
        StatusLog_Store.Append(_log, "your question was NOT sent to the owner — it is incomplete", "Ask again with every line present.", NOW.AddMinutes(-4));

        var text = Pack();

        Assert.Contains(StatePack_Builder.STANDING_NOTES_HEADING, text, StringComparison.Ordinal);
        Assert.Contains("PLAN.md is behind your verdicts", text, StringComparison.Ordinal);
        Assert.Contains("your question was NOT sent", text, StringComparison.Ordinal);
        Assert.True(
            text.IndexOf("PLAN.md is behind your verdicts", StringComparison.Ordinal)
            < text.IndexOf("your question was NOT sent", StringComparison.Ordinal),
            "the notes are out of order — the newest must read last, as the channel's do");
    }

    /// <summary>
    /// THE SHARP CASE, and the only one of these that cannot pass for the wrong reason: a turn record
    /// and an advisory are written in the same minute, and exactly one of them is carried. A screen
    /// that filtered nothing fails on the first assertion; a screen that filtered everything fails on
    /// the second.
    /// </summary>
    [Fact]
    public void ATurnRecordIsNeverStanding_AndTheAdvisoryBesideItStillIs()
    {
        StatusLog_Store.Append(_log, $"{PrintTurn_Words.TURN_ENDED_SUBJECT} imp-1 turn 4 — success", "request_id: r/imp-1/4", NOW.AddMinutes(-3));
        StatusLog_Store.Append(_log, "PLAN.md is behind your verdicts", "Update PLAN.md.", NOW.AddMinutes(-2));

        var text = Pack();

        Assert.DoesNotContain(PrintTurn_Words.TURN_ENDED_SUBJECT, text, StringComparison.Ordinal);
        Assert.Contains("PLAN.md is behind your verdicts", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// turn_ended NEVER APPEARS. It is the largest family in the log by far — one per turn of every
    /// session — and it says what the session already knows. Twenty of them leave no section at all,
    /// not a section of twenty lines.
    /// </summary>
    [Fact]
    public void TwentyTurnRecordsLeaveNoSectionAtAll()
    {
        for (var turn = 1; turn <= 20; turn++)
            StatusLog_Store.Append(_log, $"{PrintTurn_Words.TURN_ENDED_SUBJECT} imp-1 turn {turn} — success", $"request_id: r/imp-1/{turn}", NOW.AddMinutes(-turn));

        var text = Pack();

        Assert.DoesNotContain(PrintTurn_Words.TURN_ENDED_SUBJECT, text, StringComparison.Ordinal);
        Assert.DoesNotContain(StatePack_Builder.STANDING_NOTES_HEADING, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// STILL STANDING MEANS STILL RECENT, and this is the case the section exists to get right. The
    /// ledger advisory is written ONCE per spell (<c>_ledgerBehindReportedOrchIds</c> in the engine):
    /// once the supervisor updates PLAN.md nothing writes a retraction, so the resolved advisory of
    /// three days ago stays the newest of its family in the log for ever. Carried, it would tell every
    /// fresh session for the rest of the orchestration's life to fix a ledger that is already fixed.
    /// The window <see cref="PrintTurn_Trigger.AGENT_NOTE_WINDOW"/> is what makes it chronicle instead.
    /// </summary>
    [Fact]
    public void ANoteOlderThanTheWindowIsChronicle_NotAStandingNote()
    {
        StatusLog_Store.Append(_log, "PLAN.md is behind your verdicts", "Update PLAN.md.", NOW - TimeSpan.FromDays(3));
        StatusLog_Store.Append(_log, "the entry you just sent breaks the message contract", "Subject, blank line, body.", NOW - PrintTurn_Trigger.AGENT_NOTE_WINDOW - TimeSpan.FromMinutes(1));
        StatusLog_Store.Append(_log, "a member has gone deaf", "imp-2 has not moved since the nudge.", NOW - PrintTurn_Trigger.AGENT_NOTE_WINDOW + TimeSpan.FromMinutes(1));

        var text = Pack();

        Assert.DoesNotContain("PLAN.md is behind your verdicts", text, StringComparison.Ordinal);
        Assert.DoesNotContain("breaks the message contract", text, StringComparison.Ordinal);
        Assert.Contains("a member has gone deaf", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE COUNT HAS A CEILING — the same five the riding notes use. A quarter of an hour of a noisy
    /// orchestration can put a dozen notes in one log, and a pack whose section grows with the traffic
    /// is the growing boot this series exists to shrink.
    /// </summary>
    [Fact]
    public void AtMostTheNewestFiveAreCarried()
    {
        for (var note = 1; note <= 9; note++)
            StatusLog_Store.Append(_log, $"advisory number {note}", $"body {note}", NOW.AddMinutes(-10 + note));

        var text = Pack();

        Assert.Equal(PrintTurn_Trigger.MAXIMUM_AGENT_NOTES, Standing().Count);
        Assert.DoesNotContain("advisory number 4", text, StringComparison.Ordinal);
        Assert.Contains("advisory number 5", text, StringComparison.Ordinal);
        Assert.Contains("advisory number 9", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// THE SECTION HAS A CEILING IN CHARACTERS TOO, and it SAYS when it has cut — the pack's rule for
    /// every other section. It keeps the TAIL, so a cut costs the oldest of the five and never the
    /// newest, which is the one a session most needs.
    /// </summary>
    [Fact]
    public void AnOversizedSectionSaysItTruncated_AndKeepsTheNewest()
    {
        for (var note = 1; note <= 5; note++)
            StatusLog_Store.Append(_log, $"advisory number {note}", new string('x', 1_500), NOW.AddMinutes(-10 + note));

        var text = Pack();

        Assert.Contains("truncated by the bridge", text, StringComparison.Ordinal);
        Assert.Contains(StatusLog_Store.FILE_NAME, text, StringComparison.Ordinal);
        Assert.DoesNotContain("advisory number 1", text, StringComparison.Ordinal);
        Assert.Contains("advisory number 5", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASessionWithNoNotes_GetsNoSection()
    {
        Assert.Empty(Standing());
        Assert.DoesNotContain(StatePack_Builder.STANDING_NOTES_HEADING, Pack(), StringComparison.Ordinal);
    }

    /// <summary>
    /// AN ABSENT LOG IS A SESSION THAT HAS BEEN TOLD NOTHING, not a section the bridge could not fill.
    /// Naming it in <c>Unavailable</c> would put "the app's notes could not be read" in front of every
    /// session on every machine that has never set the <c>bookkeeping</c> key.
    /// </summary>
    [Fact]
    public void AnAbsentLogReadsAsNothingToldAndIsNeverNamedUnavailable()
    {
        Assert.Empty(StatePackInputs_Reader.Select_StandingNotes(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.jsonl"), NOW));
    }

    /// <summary>
    /// THE WIRING, end to end: the reader looks in the SESSION'S OWN log — the one
    /// <see cref="StatusLog_Store.Get_File"/> names, beside its state file — and applies the screen
    /// there. Without this the three cases above would pin a method nothing calls.
    /// </summary>
    [Fact]
    public void Read_CarriesTheStandingNotesOfTheSessionsOwnLog_AndLeavesTheChronicleBehind()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiorch-standing-root-{Guid.NewGuid():N}");

        try
        {
            var paths = SupervisionPaths_Factory.Create(root);
            var channel = paths.Get_ImplementerChannelFile("repo-1", "imp-1");
            var log = StatusLog_Store.Get_File(paths, SessionRoles.Implementer, "repo-1", "imp-1");

            StatusLog_Store.Append(log, "PLAN.md is behind your verdicts", "Resolved days ago.", DateTime.Now - TimeSpan.FromDays(3));
            StatusLog_Store.Append(log, $"{PrintTurn_Words.TURN_ENDED_SUBJECT} imp-1 turn 4 — success", "request_id: r/imp-1/4", DateTime.Now.AddMinutes(-2));
            StatusLog_Store.Append(log, "your question was NOT sent to the owner — it is incomplete", "Ask again with every line present.", DateTime.Now.AddMinutes(-1));

            var state = PrintSessionState_Factory.Create_New("sid", SessionRoles.Implementer, "repo-1", "imp-1", Path.Combine(root, "not-a-repo"), null, channel, []);
            var inputs = StatePackInputs_Reader.Read(paths, state, "repo-1/imp-1/7", [], []);

            Assert.Contains("your question was NOT sent", Assert.Single(inputs.StandingNotes).Subject, StringComparison.Ordinal);
            Assert.DoesNotContain(inputs.Unavailable, line => line.Contains(StatusLog_Store.FILE_NAME, StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    IReadOnlyList<IChannelEntry> Standing() => StatePackInputs_Reader.Select_StandingNotes(_log, NOW);

    string Pack() => StatePack_Builder.Build(Inputs(Standing()));

    static StatePackInputs Inputs(IReadOnlyList<IChannelEntry> standingNotes)
    {
        return new StatePackInputs(
            orchId: "repo-1", memberId: "imp-1", role: SessionRoles.Implementer, requestId: "repo-1/imp-1/7",
            pending: [], sources: [], brief: null, lastOwnEntry: null, ledgerLines: [], planText: null,
            gitLines: [], ownerTail: [], unavailable: [], progressNote: null, conclusions: null,
            standingNotes: standingNotes);
    }
}
