using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnCursor;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

public class PrintTurnTriggerTests
{
    const string CHANNEL =
        "## [1] FROM supervisor — 2026-09-05 10:00 — brief\n\ndo X\n" +
        "## [2] FROM implementer — 2026-09-05 10:01 — imp-1 online\n\nhi\n" +
        "## [3] FROM app — 2026-09-05 10:02 — [agent] turn_ended imp-1 turn 1 — success\n\nrequest_id: o/imp-1/1\n" +
        "## [4] FROM supervisor — 2026-09-05 10:03 — GO AHEAD\n\ncontinue\n" +
        "## [5] FROM owner — 2026-09-05 10:04 — via Telegram\n\ntyped straight in\n";

    static ITurnCursor Cursor(params IChannelEntry[] delivered)
    {
        return TurnCursor_Factory.Create(
            "imp-1",
            "/c.md",
            delivered.Length == 0 ? 0 : delivered.Max(entry => entry.Index),
            delivered.Select(ChannelEntry_Digest.Compute).ToHashSet());
    }

    [Fact]
    public void AMember_IsTriggeredBySupervisorAndOwner_NeverByItselfOrTheApp()
    {
        var entries = ChannelEntry_Parser.Parse_All(CHANNEL);

        var pending = PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, entries, Cursor());

        Assert.Equal([1, 4, 5], pending.Select(entry => entry.Index));
    }

    [Fact]
    public void OnlyEntriesNotYetDelivered_ArePending()
    {
        var entries = ChannelEntry_Parser.Parse_All(CHANNEL);

        Assert.Equal([4, 5], PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, entries, Cursor(entries[0])).Select(entry => entry.Index));
        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, entries, Cursor(entries[0], entries[3], entries[4])));
    }

    /// <summary>
    /// THE DEFECT THE IDENTITY CURSOR EXISTS FOR. CLAUDE.md decision 12 records two <c>[80]</c> and two
    /// <c>[81]</c> in one live channel: the <c>[n]</c> is written by the agent and is a guess unless it
    /// re-read the file. A supervisor woken by its members' spokes reads exactly those agent-written
    /// headers — so under a cursor that was a NUMBER, the second <c>[4]</c> here is at or below the
    /// high-water mark and is never handed over: an implementer's filed report, lost, with nothing
    /// anywhere saying so. It is pending, because the cursor holds what it has delivered and not how far
    /// it has counted.
    /// </summary>
    [Fact]
    public void ASecondEntryReusingAnIndex_IsStillPending_BecauseTheCursorIsNotANumber()
    {
        var entries = ChannelEntry_Parser.Parse_All(
            "## [4] FROM supervisor — 2026-09-05 10:03 — GO AHEAD\n\nfirst\n" +
            "## [4] FROM supervisor — 2026-09-05 10:09 — SECOND THOUGHTS\n\nstop instead\n");

        var pending = PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, entries, Cursor(entries[0]));

        Assert.Equal("SECOND THOUGHTS", Assert.Single(pending).Subject);
    }

    /// <summary>
    /// Two entries that are byte-identical, header and all, ARE one entry — to the parser, to a human
    /// reading the file, and here. Delivering the pair once is the right answer and the alternative
    /// (delivering the same words twice because they appear twice) is not.
    /// </summary>
    [Fact]
    public void ByteIdenticalEntries_AreOneEntry()
    {
        const string ONE = "## [7] FROM supervisor — 2026-09-05 10:03 — GO AHEAD\n\ncontinue\n";
        var entries = ChannelEntry_Parser.Parse_All(ONE + ONE);

        Assert.Equal(2, entries.Count);
        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, entries, Cursor(entries[0])));
    }

    [Theory]
    [InlineData(SessionRoles.Solo, ChannelAuthors.Owner, true)]
    [InlineData(SessionRoles.Solo, ChannelAuthors.Solo, false)]
    [InlineData(SessionRoles.Solo, ChannelAuthors.App, false)]
    [InlineData(SessionRoles.General, ChannelAuthors.Owner, true)]
    [InlineData(SessionRoles.General, ChannelAuthors.Supervisor, false)]
    [InlineData(SessionRoles.Reviewer, ChannelAuthors.Supervisor, true)]
    [InlineData(SessionRoles.Reviewer, ChannelAuthors.Reviewer, false)]
    [InlineData(SessionRoles.Supervisor, ChannelAuthors.Implementer, true)]
    [InlineData(SessionRoles.Supervisor, ChannelAuthors.Reviewer, true)]
    [InlineData(SessionRoles.Supervisor, ChannelAuthors.Supervisor, false)]
    [InlineData(SessionRoles.Supervisor, ChannelAuthors.App, false)]
    [InlineData(SessionRoles.Implementer, ChannelAuthors.Unknown, false)]
    public void InboundRule_PerRole(SessionRoles role, ChannelAuthors author, bool inbound)
    {
        Assert.Equal(inbound, PrintTurn_Trigger.Is_Inbound(role, author));
    }
    // ----- app notes ride a turn, and never start one (ai-orch-1, 2026-09-11) -----

    static readonly DateTime NOW = new(2026, 9, 11, 16, 0, 0, DateTimeKind.Local);

    const string NOTES =
        "## [1] FROM owner — 2026-09-11 15:38 — via Telegram\n\nfai tutto\n" +
        "## [2] FROM app — 2026-09-11 15:40 — [agent] PLAN.md is behind your verdicts\n\nUpdate PLAN.md.\n" +
        "## [3] FROM app — 2026-09-11 15:41 — [agent] the owner is still waiting for your reply\n\nReply now.\n" +
        "## [4] FROM app — 2026-09-11 15:48 — [agent] turn_ended solo-1 turn 5 — success\n\nrequest_id: o/solo-1/5\n" +
        "## [5] FROM app — 2026-09-11 15:49 — orchestration 'ai-orch-2' started\n\nthe owner saw this one\n" +
        "## [6] FROM app — 2026-09-11 12:00 — [agent] an old note\n\nfrom this morning\n";

    [Fact]
    public void AnAgentNote_IsTheAppsTaggedEntry_NotItsTurnRecord_NorWhatTheOwnerSaw()
    {
        var entries = ChannelEntry_Parser.Parse_All(NOTES);

        Assert.Equal([2, 3, 6], entries.Where(PrintTurn_Trigger.Is_AgentNote).Select(entry => entry.Index));
    }

    [Fact]
    public void TheNotesThatRide_AreUndelivered_Recent_AndNeverInbound()
    {
        var entries = ChannelEntry_Parser.Parse_All(NOTES);

        // [6] is past the window; [2] and [3] ride — and none of them would ever START a turn.
        Assert.Equal([2, 3], PrintTurn_Trigger.Select_AgentNotes(entries, Cursor(), NOW).Select(entry => entry.Index));
        Assert.Equal([1], PrintTurn_Trigger.Select_Pending(SessionRoles.Solo, entries, Cursor()).Select(entry => entry.Index));

        // Delivered once, never again.
        Assert.Equal([3], PrintTurn_Trigger.Select_AgentNotes(entries, Cursor(entries[1]), NOW).Select(entry => entry.Index));
    }

    [Fact]
    public void AtMostTheNewestFiveNotesRide()
    {
        var text = string.Concat(Enumerable.Range(1, 8).Select(index => $"## [{index}] FROM app — 2026-09-11 15:{index:00} — [agent] note {index}\n\nbody\n"));

        Assert.Equal([4, 5, 6, 7, 8], PrintTurn_Trigger.Select_AgentNotes(ChannelEntry_Parser.Parse_All(text), Cursor(), NOW).Select(entry => entry.Index));
    }

    [Fact]
    public void ADeliveredNote_SurvivesTheCursorsPrune_SoItDoesNotRideAgain()
    {
        var entries = ChannelEntry_Parser.Parse_All(NOTES);
        var cursor = TurnCursor_Factory.CreateFrom_Delivered(Cursor(), SessionRoles.Solo, entries, [entries[0], entries[2]]);

        // The NEXT turn's advance is where the prune runs over what was delivered before it — a note
        // it did not recognise as deliverable would be dropped there and ride the turn after.
        cursor = TurnCursor_Factory.CreateFrom_Delivered(cursor, SessionRoles.Solo, entries, []);

        Assert.Equal([2], PrintTurn_Trigger.Select_AgentNotes(entries, cursor, NOW).Select(entry => entry.Index));

        // And a note does not move the high-water mark, which is about traffic.
        Assert.Equal(1, cursor.HighWaterIndex);
    }
}
