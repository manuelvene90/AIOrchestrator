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
}
