using AIOrchestratorCoreLib.Bridge.HeldAppendMemo;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE RESUME MUST SKIP THE ENTRIES IT SENT, NOT A NUMBER OF THEM.
///
/// <para>
/// The memo used to be a <c>Dictionary&lt;string, int&gt;</c> in the engine holding "how many
/// mirrorable entries of this held append already went out", and the resume skipped that many off
/// the FRONT of the re-emitted list. Correct only while the list re-composes identically — and
/// <c>Select_MirrorableEntries</c> keeps just the NEWEST <c>STATUS</c> entry of an append, so an
/// ordinary periodic status arriving during a hold reshuffles it.
/// </para>
/// </summary>
public class HeldAppendMemoTests
{
    const string CHANNEL = "/tmp/owner-channel.md";

    static IChannelEntry Entry(int index, string author, string subject, string body)
    {
        var text = $"## [{index}] FROM {author} — 2026-09-15 10:00 — {subject}\n{body}";

        return Assert.Single(ChannelEntry_Parser.Parse_All(text));
    }

    /// <summary>
    /// THE SEQUENCE THAT LOST AN ENTRY, and every step of it is routine.
    ///
    /// <para>
    /// Poll 1 emits [A(STATUS), B, C]. A and B go out; B's question raises the one-question hold, so
    /// C is parked. Before poll 2 the supervisor writes D(STATUS) — the periodic status, which is
    /// protocol — and A is now superseded, so the mirrorable list becomes [B, C, D]. The positional
    /// memo said "skip 2", which skipped B and C: C had never been sent, the append was then
    /// confirmed, the cursor moved, and C was gone. No duplicate, no error, no log line.
    /// </para>
    /// </summary>
    [Fact]
    public void AfterAStatusEntryIsSuperseded_TheResumeStillSkipsOnlyWhatWasSent()
    {
        var a = Entry(1, "app", "STATUS", "first status");
        var b = Entry(2, "supervisor", "a question", "QUESTION: which?");
        var c = Entry(3, "supervisor", "the one that was lost", "the body nobody received");
        var d = Entry(4, "app", "STATUS", "newer status");

        var memo = HeldAppendMemo_Factory.Create();

        // Poll 1: A and B went out, then the hold parked C.
        memo.Remember_Delivered(CHANNEL, [a, b]);

        // Poll 2: A is dropped as superseded by D, so the list is [B, C, D].
        Assert.True(memo.Was_Delivered(CHANNEL, b));

        Assert.False(memo.Was_Delivered(CHANNEL, c), "C was parked, never sent — the resume must send it.");
        Assert.False(memo.Was_Delivered(CHANNEL, d), "D arrived after the hold and has never been seen.");
    }

    /// <summary>
    /// TWO ENTRIES THAT SHARE AN INDEX ARE STILL TWO ENTRIES. Duplicate indexes are a fault this
    /// codebase has watched happen (CLAUDE.md decision 12), so a memo keyed on the index alone would
    /// suppress a real entry that merely shares a number with a delivered one — the same loss
    /// through a different door.
    /// </summary>
    [Fact]
    public void TwoEntriesSharingAnIndex_AreTrackedSeparately()
    {
        var first = Entry(80, "supervisor", "the first eighty", "one body");
        var second = Entry(80, "implementer", "the second eighty", "a different body");

        var memo = HeldAppendMemo_Factory.Create();
        memo.Remember_Delivered(CHANNEL, [first]);

        Assert.True(memo.Was_Delivered(CHANNEL, first));
        Assert.False(memo.Was_Delivered(CHANNEL, second));
    }

    [Fact]
    public void Forget_DropsTheNote_SoALaterAppendIsNotSuppressed()
    {
        var entry = Entry(1, "supervisor", "a report", "body");

        var memo = HeldAppendMemo_Factory.Create();
        memo.Remember_Delivered(CHANNEL, [entry]);
        memo.Forget(CHANNEL);

        Assert.False(memo.Was_Delivered(CHANNEL, entry));
    }

    [Fact]
    public void ANoteIsPerChannel_AndNeverLeaksToAnother()
    {
        var entry = Entry(1, "supervisor", "a report", "body");

        var memo = HeldAppendMemo_Factory.Create();
        memo.Remember_Delivered(CHANNEL, [entry]);

        Assert.False(memo.Was_Delivered("/tmp/another-channel.md", entry));
    }

    /// <summary>Remembering nothing is forgetting — otherwise an empty batch would leave a stale note.</summary>
    [Fact]
    public void RememberingAnEmptyBatch_ClearsTheNote()
    {
        var entry = Entry(1, "supervisor", "a report", "body");

        var memo = HeldAppendMemo_Factory.Create();
        memo.Remember_Delivered(CHANNEL, [entry]);
        memo.Remember_Delivered(CHANNEL, []);

        Assert.False(memo.Was_Delivered(CHANNEL, entry));
    }
}
