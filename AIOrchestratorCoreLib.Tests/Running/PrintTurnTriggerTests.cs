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
}
