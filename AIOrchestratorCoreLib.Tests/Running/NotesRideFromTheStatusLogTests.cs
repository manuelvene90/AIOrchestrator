using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Channels.StatusLog;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.RunnerConfigs;
using AIOrchestratorCoreLib.Running.SessionCursors;
using AIOrchestratorCoreLib.Running.StatusNotes;
using AIOrchestratorCoreLib.Running.TurnSource;
using AIOrchestratorCoreLib.Running.WakeDecision;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// THE RIDING NOTES SURVIVE THE MOVE. Before 2026-09-15 an app note was an entry in the session's own
/// channel and rode the next turn from there; plan 02 routes the bookkeeping to <c>status.jsonl</c>,
/// and the mechanism that carries it must follow — or every note plan 02 moves becomes a note nothing
/// delivers, which is a far worse failure than a noisy channel.
///
/// <para>
/// ONE RULE, TWO SOURCES. The window, the cap and the delivered-test stay in
/// <c>PrintTurn_Trigger.Select_AgentNotes</c>; what changes is that it is asked over both files'
/// entries at once, so the cap is applied to the merged set and not twice (CLAUDE.md decision 12).
/// </para>
/// <para>
/// HALF THESE CASES GO THROUGH <c>Resolve_OrNull</c> AND NOT THROUGH THE SELECTOR, deliberately. A
/// case that merges the two lists ITSELF and hands them to <c>Select_AgentNotes</c> is green whether
/// or not anything in the app ever reads the log — it pins the selector's arithmetic and nothing
/// about delivery. The wiring is the whole of this task, so the wiring is what the fixture cases
/// measure: a note written to a real log file, read off a real state file, arriving in a real
/// decision's pending set.
/// </para>
/// </summary>
public class NotesRideFromTheStatusLogTests
{
    static readonly DateTime NOW = new(2026, 9, 15, 16, 0, 0, DateTimeKind.Local);

    const string CHANNEL =
        "## [1] FROM owner — 2026-09-15 15:38 — via Telegram\n\nfai tutto\n" +
        "## [2] FROM app — 2026-09-15 15:40 — [agent] an old-style note still in the channel\n\nnot migrated, still delivered\n";

    static bool NeverDelivered(IChannelEntry entry) => false;

    // ----- the selector: one rule over the merged pair -----

    [Fact]
    public void ANoteInTheLogRidesExactlyAsOneInTheChannelDid()
    {
        using var log = new TempLog();

        log.Append("PLAN.md is behind your verdicts", "update it", NOW.AddMinutes(-19));
        log.Append($"{PrintTurn_Words.TURN_ENDED_SUBJECT} sup turn 3 — success", "r/sup/3", NOW.AddMinutes(-18));

        var merged = ChannelEntry_Parser.Parse_All(CHANNEL).Concat(log.Entries()).ToList();

        var notes = PrintTurn_Trigger.Select_AgentNotes(merged, NeverDelivered, NOW);

        // The channel's un-migrated note AND the log's advisory ride. turn_ended never does — it says
        // what the session already knows, and a turn started by the record of the previous turn would
        // be a loop with one member in it.
        Assert.Equal(
            ["[agent] an old-style note still in the channel", "[agent] PLAN.md is behind your verdicts"],
            notes.Select(note => note.Subject));
    }

    /// <summary>
    /// THE CAP IS APPLIED ONCE, OVER BOTH FILES. Asking each source for its newest five and
    /// concatenating gives ten, and ten notes at the head of a prompt is the growing boot this whole
    /// series exists to shrink.
    /// </summary>
    [Fact]
    public void AtMostFiveNotesRide_AcrossBothFilesTogether()
    {
        using var log = new TempLog();

        for (var index = 1; index <= 6; index++)
            log.Append($"log note {index}", "body", NOW.AddMinutes(-30 + index));

        var channelText = string.Concat(Enumerable.Range(1, 6).Select(index =>
            $"## [{index}] FROM app — 2026-09-15 15:{index:00} — [agent] channel note {index}\n\nbody\n"));

        var merged = ChannelEntry_Parser.Parse_All(channelText).Concat(log.Entries()).ToList();

        Assert.Equal(PrintTurn_Trigger.MAXIMUM_AGENT_NOTES, PrintTurn_Trigger.Select_AgentNotes(merged, NeverDelivered, NOW).Count);
    }

    /// <summary>
    /// AND "THE NEWEST FIVE" MEANS BY STAMP, NOT BY WHICH FILE THE NOTE LANDED IN. This is the case
    /// the cap-count above cannot see: with a merged list the log's records sit behind every channel
    /// entry, so a cap that took the tail of the list would hand over five two-hour-old log notes and
    /// drop a channel note written a minute ago — a rule that silently prefers a file. Both halves are
    /// asserted, because a set that merely contained the newest one would pass with the drop broken.
    /// </summary>
    [Fact]
    public void TheNewestFiveWin_EvenWhenTheNewestIsInTheOtherFile()
    {
        using var log = new TempLog();

        // Five in the log, all older than every channel note below.
        for (var index = 1; index <= 5; index++)
            log.Append($"stale log note {index}", "body", NOW.AddMinutes(-90 + index));

        var channelText = string.Concat(Enumerable.Range(1, 2).Select(index =>
            $"## [{index}] FROM app — 2026-09-15 15:5{index} — [agent] fresh channel note {index}\n\nbody\n"));

        var merged = ChannelEntry_Parser.Parse_All(channelText).Concat(log.Entries()).ToList();

        var subjects = PrintTurn_Trigger.Select_AgentNotes(merged, NeverDelivered, NOW).Select(note => note.Subject).ToList();

        Assert.Equal(
            [
                "[agent] stale log note 3",
                "[agent] stale log note 4",
                "[agent] stale log note 5",
                "[agent] fresh channel note 1",
                "[agent] fresh channel note 2",
            ],
            subjects);
    }

    /// <summary>
    /// DELIVERED ONCE, NEVER AGAIN — and the record of that is the same identity cursor the channel
    /// uses, because the log's records ARE channel entries and <c>ChannelEntry_Digest</c> identifies
    /// them the same way. Nothing new was invented to remember a note.
    /// </summary>
    [Fact]
    public void ADeliveredLogNoteDoesNotRideTwice()
    {
        using var log = new TempLog();

        log.Append("your question was NOT sent to the owner", "it is incomplete", NOW.AddMinutes(-5));

        var entries = log.Entries();
        var delivered = entries.Select(ChannelEntry_Digest.Compute).ToHashSet();

        Assert.Single(PrintTurn_Trigger.Select_AgentNotes(entries, NeverDelivered, NOW));
        Assert.Empty(PrintTurn_Trigger.Select_AgentNotes(entries, entry => delivered.Contains(ChannelEntry_Digest.Compute(entry)), NOW));
    }

    // ----- the wiring: a real log, a real state file, a real decision -----

    /// <summary>
    /// THE ONE CASE THIS TASK EXISTS FOR. A ledger advisory routed to <c>status.jsonl</c> reaches the
    /// session's next turn, in front of the traffic that started it — which is what nothing in the
    /// tree did between task 7 and this one.
    /// </summary>
    [Fact]
    public void ALogNoteRidesTheTurnTheOwnerStarts()
    {
        using var fixture = new SupervisorFixture();

        fixture.Append_LogNote("PLAN.md is behind your verdicts", NOW.AddMinutes(-5));
        fixture.Append_OwnerEntry("status?");

        var decision = fixture.Resolve(NOW);

        Assert.NotNull(decision);

        // IN FRONT, AND IT DECIDED NOTHING — the two halves of the rule. A set that merely CONTAINED
        // the note would pass with either of them broken.
        Assert.Equal(ChannelAuthors.App, decision!.Pending[0].Entry.Author);
        Assert.Contains("PLAN.md is behind your verdicts", decision.Pending[0].Entry.Subject, StringComparison.Ordinal);
        Assert.Contains("the owner wrote", decision.Reason);
    }

    /// <summary>
    /// AND IT CANNOT START ONE. <c>Is_Inbound</c> is false for <c>ChannelAuthors.App</c> for every
    /// role and the log is not a source at all, so a log with notes in it and nothing else pending
    /// leaves the session asleep. The opposite road, run on the same fixture as the case above: the
    /// only difference between them is the owner's entry.
    /// </summary>
    [Fact]
    public void ALogNoteAloneStartsNoTurn()
    {
        using var fixture = new SupervisorFixture();

        fixture.Append_LogNote("PLAN.md is behind your verdicts", NOW.AddMinutes(-5));

        Assert.Null(fixture.Resolve(NOW));
    }

    /// <summary>
    /// THE LOG IS NOT LISTED AS A CHANNEL THE SESSION MAY ANSWER. A source is addressable — the prompt
    /// names them and a reply is checked against them — so making the log one would put a channel in
    /// front of every session that nobody can write to and the app would have to refuse replies to.
    /// It gets a cursor and nothing else, and this is the assertion that says the cursor did not come
    /// with a source attached.
    /// </summary>
    [Fact]
    public void TheLogIsNeverOneOfTheSessionsSources()
    {
        using var fixture = new SupervisorFixture();

        fixture.Append_LogNote("PLAN.md is behind your verdicts", NOW.AddMinutes(-5));
        fixture.Append_OwnerEntry("status?");

        var decision = fixture.Resolve(NOW);

        Assert.NotNull(decision);
        Assert.DoesNotContain(decision!.Sources, source => source.Key == StatusLog_Store.CURSOR_KEY);
        Assert.DoesNotContain(decision.Sources, source => source.ChannelFilePath.EndsWith(StatusLog_Store.FILE_NAME, StringComparison.Ordinal));

        // The note rode under the session's OWN channel, because that is the source the prompt groups
        // it under — it is not evidence of a source for the log.
        Assert.Equal(fixture.State.ChannelFilePath, decision.Pending[0].Source.ChannelFilePath);
    }

    /// <summary>
    /// THE ADVANCE REMEMBERS IT, ONCE. The cursor lands under <c>.status</c> in the state file's
    /// existing cursors array, the note does not ride the next turn, and a read that PERSISTS the
    /// cursor set neither drops it nor files a second copy of it — the two ways a key that is not a
    /// source can be lost or duplicated.
    /// </summary>
    [Fact]
    public void TheNoteIsRecordedUnderTheStatusCursor_AndDoesNotRideAgain()
    {
        using var fixture = new SupervisorFixture();

        fixture.Append_LogNote("PLAN.md is behind your verdicts", NOW.AddMinutes(-5));
        fixture.Append_OwnerEntry("status?");

        var first = fixture.Resolve(NOW);
        Assert.NotNull(first);

        fixture.Advance_Cursors(first!);

        Assert.Single(fixture.State.Cursors, cursor => cursor.SourceKey == StatusLog_Store.CURSOR_KEY);

        fixture.Append_OwnerEntry("and now?");
        var second = fixture.Resolve(NOW.AddMinutes(1));

        Assert.NotNull(second);
        Assert.DoesNotContain(second!.Pending, item => item.Entry.Author == ChannelAuthors.App);
    }

    /// <summary>
    /// AND THE READ THAT PERSISTS DOES NOT LOSE IT, NOR FILE A SECOND ONE. <c>Read_AndPersist</c>
    /// rewrites the whole cursors array, and it does so only when the read MADE a cursor — so the
    /// moment that matters is the tick a new member appears, which is the one moment nobody would
    /// think to check the status log's row. Two ways to get it wrong and this case sees both: the
    /// "keep a cursor whose key matched no source" loop files a SECOND <c>.status</c> unless it is
    /// skipped, and skipping it without putting the row back DROPS the log's whole delivery record,
    /// after which every note already shown rides again.
    /// </summary>
    [Fact]
    public void TheStatusCursorSurvivesAReadThatPersists_ExactlyOnce()
    {
        using var fixture = new SupervisorFixture();

        fixture.Append_LogNote("PLAN.md is behind your verdicts", NOW.AddMinutes(-5));
        fixture.Append_OwnerEntry("status?");

        var decision = fixture.Resolve(NOW);
        Assert.NotNull(decision);
        fixture.Advance_Cursors(decision!);

        var noted = Assert.Single(fixture.State.Cursors, cursor => cursor.SourceKey == StatusLog_Store.CURSOR_KEY).Delivered;
        Assert.Single(noted);

        // A NEW SOURCE IS WHAT MAKES THE READ WRITE. Without one it finds every cursor it needs, says
        // nothing changed and persists nothing — so a case that merely called Read_AndPersist would be
        // green with the whole re-add deleted, which is how this case read until it was checked.
        fixture.Add_Member();
        fixture.Read_AndPersist();

        Assert.Equal(noted, Assert.Single(fixture.State.Cursors, cursor => cursor.SourceKey == StatusLog_Store.CURSOR_KEY).Delivered);
    }

    /// <summary>
    /// AND A SESSION THAT HAS NEVER BEEN HANDED A LOG NOTE CARRIES NO <c>.status</c> ROW. The default
    /// sink is the channel and nothing on either of the owner's machines has routed its bookkeeping,
    /// so the ordinary session must look exactly as it did before this task: the cursor is a record of
    /// a delivery, and a record of nothing is a row every cursor-counting reader has to learn to
    /// ignore. This is the opposite road of the case above — same fixture, same turn, no log note.
    /// </summary>
    [Fact]
    public void ASessionThatHasRiddenNoLogNote_GrowsNoStatusCursor()
    {
        using var fixture = new SupervisorFixture();

        fixture.Append_OwnerEntry("status?");

        var decision = fixture.Resolve(NOW);
        Assert.NotNull(decision);

        fixture.Advance_Cursors(decision!);

        Assert.DoesNotContain(fixture.State.Cursors, cursor => cursor.SourceKey == StatusLog_Store.CURSOR_KEY);
    }

    // ----- fixtures -----

    sealed class TempLog : IDisposable
    {
        public string File { get; } = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".jsonl");

        public void Append(string subject, string body, DateTime stampedLocal)
        {
            Assert.True(StatusLog_Store.Append(File, subject, body, stampedLocal), $"could not append '{subject}' to the log");
        }

        public IReadOnlyList<IChannelEntry> Entries() => StatusLog_Store.Read_Entries(File);

        public void Dispose()
        {
            if (System.IO.File.Exists(File))
                System.IO.File.Delete(File);
        }
    }

    /// <summary>
    /// A supervisor over a real supervision root with real channel files, a real state file and a real
    /// status log — the resolver reads all three off the disk, so a mock of any of them would be a test
    /// of the mock. The boot turn is spent, because it answers before every other rule and would
    /// otherwise be the reason each case below is green.
    /// </summary>
    sealed class SupervisorFixture : IDisposable
    {
        readonly PrintRunnerTestHarness _harness = new("supervisor:stream");
        readonly string _orchId = "repo-1";
        readonly string _supervisorId;

        public SupervisorFixture()
        {
            _supervisorId = _harness.Register_Supervisor(_orchId);
            Spend_TheBootTurn();
        }

        public IPrintSessionState State => _harness.Read_State(SessionRoles.Supervisor, _orchId, _supervisorId);

        string StateFile => PrintSessionState_Store.Get_StateFile(_harness.Paths, SessionRoles.Supervisor, _orchId, _supervisorId);

        IReadOnlyList<ITurnSource> Sources => TurnSources_Resolver.Resolve(_harness.Paths, _harness.Store, SessionRoles.Supervisor, _orchId, _supervisorId);

        public void Append_LogNote(string subject, DateTime stampedLocal)
        {
            var logFile = StatusLog_Store.Get_File(_harness.Paths, SessionRoles.Supervisor, _orchId, _supervisorId);

            Assert.True(StatusLog_Store.Append(logFile, subject, "Bring it up to date.", stampedLocal), $"could not append '{subject}' to '{logFile}'");
        }

        public void Append_OwnerEntry(string text)
        {
            Assert.True(ChannelAppender.Append_OwnerEntry(_harness.Paths.Get_OwnerChannelFile(_orchId), text, NOW), "could not append the owner entry");
        }

        public IWakeDecision? Resolve(DateTime nowLocal)
        {
            return WakeDecision_Resolver.Resolve_OrNull(
                _harness.Paths, _harness.Store, State, RunnerConfigs_Factory.Create_Default(), digestHeldSince: null, nowLocal);
        }

        /// <summary>Records the decision's pending set as handed over, exactly as a completed turn does.</summary>
        public void Advance_Cursors(IWakeDecision decision)
        {
            var state = State;

            Write(PrintSessionState_Factory.CreateFrom_Existing_Cursors(
                state, SessionCursors_Bookkeeper.Advance(_harness.Paths, state, Sources, decision.Pending)));
        }

        /// <summary>A member registered mid-life: a source this session has no cursor for yet.</summary>
        public void Add_Member()
        {
            var session = _harness.Store.Add_Member(_orchId, MemberKinds.Implementer);
            var memberId = session.Members[^1].MemberId;

            Assert.True(
                ChannelAppender.Append_SessionEntry(_harness.Paths.Get_ImplementerChannelFile(_orchId, memberId), ChannelAuthors.Implementer, $"{memberId} online", "reporting for duty", NOW),
                $"could not append '{memberId}' greeting to its spoke");
        }

        public void Read_AndPersist()
        {
            var state = State;

            SessionCursors_Bookkeeper.Read_AndPersist(StateFile, ref state, SessionRoles.Supervisor, Sources);
        }

        void Spend_TheBootTurn()
        {
            var state = State;

            Write(PrintSessionState_Factory.CreateFrom_Existing_TurnExecuted(
                state, ExecutedTurn_Factory.Create(1, "req-boot", 0, 0, DateTime.UtcNow, "ok", null), state.SessionId, state.Cursors));
        }

        void Write(IPrintSessionState state) => PrintSessionState_Store.Write(StateFile, state);

        public void Dispose() => _harness.Dispose();
    }
}
