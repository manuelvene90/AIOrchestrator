using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Running.TurnSource;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The cursor's two jobs: never hand the same entry over twice, and never miss one. Everything here is
/// stated against COMPACTION, because that is the event that breaks a cursor built on counting or on a
/// position (CLAUDE.md decision 13) and it is the one this repo has already been bitten by.
/// </summary>
public class TurnCursorTests
{
    static readonly ITurnSource SOURCE = TurnSource_Factory.Create_Spoke("imp-1", "/o/imp-1/channel.md");

    static string Channel(int firstIndex, int count)
    {
        return string.Concat(Enumerable.Range(firstIndex, count).Select(index =>
            $"## [{index}] FROM supervisor — 2026-09-05 10:{index % 60:00} — brief {index}\n\ndo {index}\n"));
    }

    static IReadOnlyList<IChannelEntry> Parse(string text)
    {
        return ChannelEntry_Parser.Parse_All(text);
    }

    [Fact]
    public void ABaseline_AbsorbsEveryInboundEntry_AndNothingElse()
    {
        var entries = Parse(Channel(1, 3) + "## [4] FROM app — 2026-09-05 10:04 — [agent] turn_ended\n\nnoise\n");

        var cursor = TurnCursor_Factory.Create_Baseline(SOURCE, SessionRoles.Implementer, entries);

        // Three inbound entries recorded; the app's is not inbound, so it is not worth remembering.
        Assert.Equal(3, cursor.Delivered.Count);
        Assert.Equal(3, cursor.HighWaterIndex);
        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, entries, cursor));
    }

    [Fact]
    public void AnEmptyCursor_LeavesEverythingPending()
    {
        var entries = Parse(Channel(1, 3));

        Assert.Equal(3, PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, entries, TurnCursor_Factory.Create_Empty(SOURCE)).Count);
    }

    /// <summary>
    /// THE COMPACTION CASE. The live file loses its oldest entries to the archive and keeps the newest
    /// with their ORIGINAL indices; the cursor must still deliver the one entry that arrived and nothing
    /// else. A cursor that counted entries, or that remembered a position, would be wrong in both
    /// directions here — the count went down while the channel grew.
    /// </summary>
    [Fact]
    public void AfterCompaction_TheDeliveredSetShrinksToTheLiveFile_AndOnlyTheNewEntryIsPending()
    {
        var before = Parse(Channel(1, 50));
        var delivered = TurnCursor_Factory.Create_Baseline(SOURCE, SessionRoles.Implementer, before);

        Assert.Equal(50, delivered.Delivered.Count);

        // Compaction moved [1]–[5] out; [51] arrived after it.
        var after = Parse(Channel(6, 45) + "## [51] FROM supervisor — 2026-09-05 11:00 — NEW\n\nlatest\n");

        var pending = PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, after, delivered);

        Assert.Equal("NEW", Assert.Single(pending).Subject);

        var advanced = TurnCursor_Factory.CreateFrom_Delivered(delivered, SessionRoles.Implementer, after, pending);

        // Bounded by the live file: the five archived identities are gone, the 45 live ones and the new
        // one remain. Nothing puts an archived entry back, so dropping it cannot cause a re-delivery.
        Assert.Equal(46, advanced.Delivered.Count);
        Assert.Equal(51, advanced.HighWaterIndex);
        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, after, advanced));
    }

    /// <summary>
    /// An entry delivered by the turn that has ALSO just been compacted out is still recorded. It was
    /// handed over; pruning it because it left the live file in the same second would hand it over again.
    /// </summary>
    [Fact]
    public void AnEntryDeliveredAndArchivedInTheSamePass_StaysRecorded()
    {
        var live = Parse(Channel(1, 2));
        var justDelivered = live;

        var advanced = TurnCursor_Factory.CreateFrom_Delivered(TurnCursor_Factory.Create_Empty(SOURCE), SessionRoles.Implementer, [], justDelivered);

        Assert.Equal(2, advanced.Delivered.Count);
    }

    /// <summary>
    /// THE ONE DESTRUCTIVE STEP, GUARDED. The re-read that drives the prune is best-effort: a read landing
    /// inside the compactor's rename-over comes back as the empty string, which is indistinguishable from
    /// "the channel is empty". Pruning against that wipes every delivered identity, and the next tick hands
    /// the session its whole live channel again as new traffic — a supervisor re-answering every member
    /// report, silently, because the high-water index is untouched and the archive-gap warning cannot fire.
    /// </summary>
    [Fact]
    public void AReadThatComesBackEmpty_DoesNotPruneAwayEverythingDelivered()
    {
        var live = Parse(Channel(1, 30));
        var delivered = TurnCursor_Factory.Create_Baseline(SOURCE, SessionRoles.Implementer, live);

        Assert.Equal(30, delivered.Delivered.Count);

        var afterAFailedRead = TurnCursor_Factory.CreateFrom_Delivered(delivered, SessionRoles.Implementer, [], []);

        Assert.Equal(30, afterAFailedRead.Delivered.Count);
        Assert.Empty(PrintTurn_Trigger.Select_Pending(SessionRoles.Implementer, live, afterAFailedRead));
    }

    /// <summary>A channel that really is empty and a cursor that has delivered nothing stay empty — the guard is about LOSS, not about refusing to prune.</summary>
    [Fact]
    public void AnEmptyCursorAndAnEmptyRead_StayEmpty()
    {
        Assert.Empty(TurnCursor_Factory.CreateFrom_Delivered(TurnCursor_Factory.Create_Empty(SOURCE), SessionRoles.Implementer, [], []).Delivered);
    }

    /// <summary>
    /// THE PRUNE CAN EMPTY A DELIVERED SET THAT HAD SOMETHING IN IT, and this is where the app is
    /// allowed to see that — decision 13's shape, found by an adversarial review on 2026-09-10.
    ///
    /// <para>
    /// On a turn where a compacted source had NOTHING pending, every identity the cursor held has left
    /// the live file and nothing is added, so <c>Delivered</c> comes back EMPTY on a channel with
    /// hours of history. Anything asking "has this source ever handed something over" from that count
    /// is therefore answering "no" about a long-lived member — which is exactly what the wake-up
    /// digest's first-contact exemption did, and why it now reads
    /// <see cref="ITurnCursor.HighWaterIndex"/> as well (it is only ever raised, so it cannot come
    /// back).
    /// </para>
    /// <para>
    /// NOTHING IS RE-DELIVERED BY IT — that half of the design holds. The emptied set leaves the one
    /// live entry pending, which it already was. The damage is only to the readers that treated an
    /// empty set as "first contact".
    /// </para>
    /// </summary>
    [Fact]
    public void CreateFrom_Delivered_WhenCompactionTookEveryDeliveredEntry_EmptiesTheSet()
    {
        var before = Parse(Channel(1, 3));
        var delivered = TurnCursor_Factory.CreateFrom_Delivered(TurnCursor_Factory.Create_Empty(SOURCE), SessionRoles.Implementer, before, before);

        Assert.Equal(3, delivered.Delivered.Count);
        Assert.Equal(3, delivered.HighWaterIndex);

        // Compaction archived [1]-[3] and a fourth entry landed; this turn delivered nothing from here.
        var after = TurnCursor_Factory.CreateFrom_Delivered(delivered, SessionRoles.Implementer, Parse(Channel(4, 1)), []);

        Assert.Empty(after.Delivered);
        Assert.Equal(3, after.HighWaterIndex);
    }

    [Fact]
    public void TheIdentityIsTheTEXT_SoTwoEntriesSharingAnIndexAreTwoIdentities()
    {
        var first = Parse("## [4] FROM supervisor — 2026-09-05 10:03 — GO\n\none\n")[0];
        var second = Parse("## [4] FROM supervisor — 2026-09-05 10:09 — STOP\n\ntwo\n")[0];

        Assert.NotEqual(ChannelEntry_Digest.Compute(first), ChannelEntry_Digest.Compute(second));
    }

    /// <summary>Line endings are not content: a file read back on another platform must not re-deliver.</summary>
    [Fact]
    public void TheIdentityIgnoresLineEndingsAndSurroundingBlankLines()
    {
        Assert.Equal(
            ChannelEntry_Digest.Compute("## [1] FROM owner — 2026-09-05 10:00 — via Telegram\n\nhello"),
            ChannelEntry_Digest.Compute("\r\n## [1] FROM owner — 2026-09-05 10:00 — via Telegram\r\n\r\nhello\r\n"));
    }

    [Fact]
    public void TwoCursorsForOneSource_AreRefused_BecauseOneSourceIsDeliveredOnce()
    {
        Assert.Throws<ArgumentException>(() => AIOrchestratorCoreLib.Running.PrintSessionState.PrintSessionState_Factory.Create_New(
            "sid", SessionRoles.Supervisor, "o", "sup", "/r", null, "/r/owner-channel.md",
            [TurnCursor_Factory.Create_Empty(SOURCE), TurnCursor_Factory.Create_Empty(SOURCE)]));
    }
}
