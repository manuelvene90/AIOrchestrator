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

        Assert.Equal("increase the throughput\n\nI meant the CSV extraction", ready["chan-a"]);
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
        Assert.Equal("for the crm", ready["chan-a"]);
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

        Assert.Equal("stop what you are doing", taken["chan-a"]);

        // The owner speaks again while the first delivery is out being translated.
        buffer.Add_Segment("chan-a", "actually carry on", T0.AddSeconds(3));

        // ...and the first delivery fails, so it goes back.
        buffer.Prepend_Segment("chan-a", "stop what you are doing");
        buffer.Release("chan-a");

        Assert.Equal(
            "stop what you are doing\n\nactually carry on",
            buffer.Take_ReadyDeliveries(T0.AddSeconds(3))["chan-a"]);
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
}
