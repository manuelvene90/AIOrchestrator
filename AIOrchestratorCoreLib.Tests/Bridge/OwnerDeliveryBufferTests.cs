using AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class OwnerDeliveryBufferTests
{
    static readonly DateTime T0 = new(2026, 8, 6, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Take_ReadyDeliveries_BeforeQuietWindow_ReturnsNothing()
    {
        var buffer = OwnerDeliveryBuffer_Factory.Create(15, holdCapSeconds: 60);
        buffer.Add_Segment("chan-a", "first", T0);

        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(10)));
        Assert.True(buffer.Has_PendingDeliveries());
    }

    [Fact]
    public void Take_ReadyDeliveries_RapidBurst_AggregatesIntoOneText()
    {
        var buffer = OwnerDeliveryBuffer_Factory.Create(15, holdCapSeconds: 60);
        buffer.Add_Segment("chan-a", "increase the throughput", T0);
        buffer.Add_Segment("chan-a", "I meant the CSV extraction", T0.AddSeconds(8));

        var ready = buffer.Take_ReadyDeliveries(T0.AddSeconds(24));

        Assert.Equal("increase the throughput\n\nI meant the CSV extraction", ready["chan-a"].Text);
        Assert.False(buffer.Has_PendingDeliveries());
    }

    [Fact]
    public void Take_ReadyDeliveries_NewMessageResetsTheQuietWindow()
    {
        var buffer = OwnerDeliveryBuffer_Factory.Create(15, holdCapSeconds: 60);
        buffer.Add_Segment("chan-a", "first", T0);
        buffer.Add_Segment("chan-a", "second", T0.AddSeconds(14));

        // 16 s after the FIRST message but only 2 s after the second — still waiting.
        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(16)));
        Assert.Single(buffer.Take_ReadyDeliveries(T0.AddSeconds(29)));
    }

    [Fact]
    public void Take_ReadyDeliveries_IndependentTargets_FlushIndependently()
    {
        var buffer = OwnerDeliveryBuffer_Factory.Create(15, holdCapSeconds: 60);
        buffer.Add_Segment("chan-a", "for the crm", T0);
        buffer.Add_Segment("chan-b", "for the general", T0.AddSeconds(10));

        var ready = buffer.Take_ReadyDeliveries(T0.AddSeconds(16));

        Assert.Single(ready);
        Assert.Equal("for the crm", ready["chan-a"].Text);
        Assert.True(buffer.Has_PendingDeliveries());
    }

    /// <summary>
    /// A PUT-BACK RESTORES POSITION, NOT JUST CONTENT — rev-9's F2.
    /// <para>
    /// Take_ReadyDeliveries removes the key and the delivery is then awaited for SECONDS (a translator
    /// subprocess, Telegram calls). The inbound loop runs concurrently and can buffer a NEW segment B
    /// for the same key in that window. Appending the failed original A gives [B, A], and the
    /// supervisor reads the owner's LATER message above their EARLIER one, joined into one entry:
    /// "actually carry on" above "stop what you're doing", acted on last line first.
    /// </para>
    /// <para>The control is Add_Segment in place of Prepend_Segment: the order inverts and this reddens.</para>
    /// </summary>
    [Fact]
    public void APutBackLandsAHEADOfAMessageThatArrivedWhileItWasOut()
    {
        var buffer = OwnerDeliveryBuffer_Factory.Create(15, holdCapSeconds: 60);

        buffer.Add_Segment("chan-a", "stop what you are doing", T0);
        buffer.Release("chan-a");

        var taken = buffer.Take_ReadyDeliveries(T0);

        Assert.Equal("stop what you are doing", taken["chan-a"].Text);

        // The owner speaks again while the first delivery is out being translated.
        buffer.Add_Segment("chan-a", "actually carry on", T0.AddSeconds(3));

        // ...and the first delivery fails, so it goes back WITH ITS ORDINAL.
        buffer.Restore_Segment("chan-a", taken["chan-a"].Text, taken["chan-a"].FirstOrdinal);
        buffer.Release("chan-a");

        Assert.Equal(
            "stop what you are doing\n\nactually carry on",
            buffer.Take_ReadyDeliveries(T0.AddSeconds(3))["chan-a"].Text);
    }

    /// <summary>
    /// THE MIRROR-IMAGE INTERLEAVING — two put-backs for one key, landing NEWER FIRST.
    /// <para>
    /// This is the case no position-based method can survive and the reason the ordinal exists.
    /// Prepending inverts when the OLDER put-back lands first; appending inverts when the NEWER does.
    /// Two flush entry points make both reachable — the mirror tick and the GO branch on the inbound
    /// loop — so whichever position rule you pick, one of these two orders comes out wrong.
    /// </para>
    /// <para>
    /// Ordinals make the landing order irrelevant, which is what "removes the contention rather than
    /// guarding it" means in practice. Both orders are asserted below; without the ordinal ONE of
    /// them reddens whichever way the buffer is written.
    /// </para>
    /// <para>
    /// BOTH ORDERS BELONG IN ONE METHOD, deliberately. Each position rule fails a DIFFERENT half of
    /// it — appending fails newer-first, prepending fails older-first — so at method granularity both
    /// mutants redden this one case, and neither can pass it. Splitting it into two methods would
    /// hide that: each would look individually satisfiable, when the point is that no single rule
    /// satisfies both at once. (rev-10 measured the red sets rather than taking my description of
    /// them, and corrected me: they are disjoint by sub-case, not by method.)
    /// </para>
    /// </summary>
    [Fact]
    public void TwoPutBacksComeOutChronological_WHICHEVEROfThemLandsFirst()
    {
        foreach (var newerFirst in new[] { false, true })
        {
            var buffer = OwnerDeliveryBuffer_Factory.Create(15, holdCapSeconds: 60);

            buffer.Add_Segment("chan-a", "first", T0);
            buffer.Release("chan-a");
            var older = buffer.Take_ReadyDeliveries(T0)["chan-a"];

            buffer.Add_Segment("chan-a", "second", T0.AddSeconds(1));
            buffer.Release("chan-a");
            var newer = buffer.Take_ReadyDeliveries(T0.AddSeconds(1))["chan-a"];

            // Both deliveries are now out and both are about to fail. The ONLY difference between
            // the two runs is which failure returns first.
            if (newerFirst)
            {
                buffer.Restore_Segment("chan-a", newer.Text, newer.FirstOrdinal);
                buffer.Restore_Segment("chan-a", older.Text, older.FirstOrdinal);
            }
            else
            {
                buffer.Restore_Segment("chan-a", older.Text, older.FirstOrdinal);
                buffer.Restore_Segment("chan-a", newer.Text, newer.FirstOrdinal);
            }

            buffer.Release("chan-a");

            Assert.Equal(
                "first\n\nsecond",
                buffer.Take_ReadyDeliveries(T0.AddSeconds(2))["chan-a"].Text);
        }
    }

    /// <summary>
    /// PINS THE HALF OF THE INVARIANT THAT ACTUALLY HOLDS: taking a delivery removes its key
    /// atomically, so no second caller can take that key while the delivery is out.
    /// <para>
    /// THE NAME USED TO CLAIM THE CONSEQUENCE — "so no second put-back can exist" — AND THAT DOES NOT
    /// FOLLOW. <c>Add_Segment</c> recreates the key and <c>Release</c> makes it instantly takeable,
    /// which is exactly what the GO path does on the inbound loop. Nothing here pins the
    /// two-in-flight case and nothing anywhere does; see <c>Prepend_Segment</c> for why that window is
    /// accepted rather than closed. A test named after a property it does not test is a green light
    /// for the next reader to stop looking.
    /// </para>
    /// </summary>
    [Fact]
    public void TakingADeliveryREMOVESTheKeyAtomically()
    {
        var buffer = OwnerDeliveryBuffer_Factory.Create(15, holdCapSeconds: 60);

        buffer.Add_Segment("chan-a", "the only message", T0);
        buffer.Release("chan-a");

        Assert.Single(buffer.Take_ReadyDeliveries(T0));

        // Nothing is left to take, so nothing else can be in flight for this key.
        Assert.Empty(buffer.Take_ReadyDeliveries(T0));
        Assert.False(buffer.Has_PendingDeliveries());
        Assert.Equal(0, buffer.Count_Pending("chan-a"));
    }

    /// <summary>
    /// WAIT RACES THE AGGREGATION WINDOW AND WAS LOSING. The window is four seconds in the app, so by
    /// the time the owner types "wait" the message has usually already been TAKEN — and a take is
    /// irreversible. Measured on da-vinci-fintech-suite-6, 2026-08-15: buffered 08:36:47, WAIT
    /// accepted 08:36:52, delivered 08:37:04, and the owner watched ✓✓ and "thinking…" appear seconds
    /// after their own wait.
    ///
    /// The engine now re-asks Is_Holding immediately before the append and puts the segment back.
    /// What this pins is the buffer half of that: a segment restored into a HELD delivery must stay
    /// put until GO, rather than coming straight back out on the next tick.
    /// </summary>
    [Fact]
    public void ASegmentPutBackIntoAHeldDelivery_StaysUntilGo()
    {
        var buffer = OwnerDeliveryBuffer_Factory.Create(4, holdCapSeconds: 60);
        buffer.Add_Segment("chan-a", "launch it yourself", T0);

        var taken = buffer.Take_ReadyDeliveries(T0.AddSeconds(5));
        Assert.Single(taken);

        // The owner's WAIT, one second after the take.
        buffer.Hold("chan-a", T0.AddSeconds(6));
        buffer.Restore_Segment("chan-a", taken["chan-a"].Text, taken["chan-a"].FirstOrdinal);

        Assert.True(buffer.Is_Holding("chan-a"));
        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(30)));

        buffer.Release("chan-a");

        var released = buffer.Take_ReadyDeliveries(T0.AddSeconds(31));
        Assert.Equal("launch it yourself", released["chan-a"].Text);
    }

    /// <summary>
    /// And the cap still applies to it, or a forgotten WAIT would swallow a message that had already
    /// been on its way out — the failure the cap exists to prevent, reached through the put-back.
    /// </summary>
    [Fact]
    public void ARestoredHeldSegment_StillEscapesOnTheIdleCap()
    {
        var buffer = OwnerDeliveryBuffer_Factory.Create(4, holdCapSeconds: 60);
        buffer.Add_Segment("chan-a", "launch it yourself", T0);

        var taken = buffer.Take_ReadyDeliveries(T0.AddSeconds(5));

        buffer.Hold("chan-a", T0.AddSeconds(6));
        buffer.Restore_Segment("chan-a", taken["chan-a"].Text, taken["chan-a"].FirstOrdinal);

        var afterCap = buffer.Take_ReadyDeliveries(T0.AddSeconds(6 + 61));

        Assert.Equal("launch it yourself", afterCap["chan-a"].Text);
    }
}
