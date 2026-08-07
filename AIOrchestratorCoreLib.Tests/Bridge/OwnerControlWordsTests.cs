using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Bridge.OwnerDeliveryBuffer;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class OwnerControlWordsTests
{
    [Theory]
    [InlineData("wait")]
    [InlineData("WAIT")]
    [InlineData("Wait")]
    [InlineData("  wait  ")]
    [InlineData("wait.")]
    [InlineData("WAIT!")]
    public void Wait_IsRecognisedInAnyCasing_AndWithTrailingPunctuation(string text)
    {
        Assert.True(OwnerControlWords.Is_Wait(text));
    }

    [Theory]
    [InlineData("go")]
    [InlineData("GO")]
    [InlineData("Go!")]
    public void Go_LikewiseRecognised(string text)
    {
        Assert.True(OwnerControlWords.Is_Go(text));
    }

    /// <summary>
    /// The failure this system must never have: swallowing content. A control word is only a
    /// control word when it is the WHOLE message — anything else is an instruction for the
    /// supervisor and has to arrive verbatim.
    /// </summary>
    [Theory]
    [InlineData("wait for imp-2 to finish before merging")]
    [InlineData("go ahead and merge it")]
    [InlineData("don't wait")]
    [InlineData("waiting")]
    [InlineData("gone")]
    [InlineData("wait go")]
    public void RealInstructions_AreNeverTreatedAsControlWords(string text)
    {
        Assert.False(OwnerControlWords.Is_Wait(text));
        Assert.False(OwnerControlWords.Is_Go(text));
    }
}

/// <summary>
/// WAIT … GO exists so the aggregation window can be SHORT (4 s) for the common single-message
/// case without cutting a long dictated thought into several turns.
/// </summary>
public class OwnerDeliveryBufferHoldTests
{
    const string KEY = "orch-1/owner-channel.md";
    static readonly DateTime T0 = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    static IOwnerDeliveryBuffer Buffer()
    {
        return OwnerDeliveryBuffer_Factory.Create(aggregationSeconds: 4, holdCapSeconds: 60);
    }

    [Fact]
    public void WithoutAHold_TheShortWindowDelivers()
    {
        var buffer = Buffer();
        buffer.Add_Segment(KEY, "one", T0);

        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(3)));
        Assert.Equal("one", Assert.Single(buffer.Take_ReadyDeliveries(T0.AddSeconds(4))).Value);
    }

    [Fact]
    public void Held_NothingIsDelivered_HoweverLongTheOwnerKeepsTyping()
    {
        var buffer = Buffer();
        buffer.Hold(KEY, T0);

        buffer.Add_Segment(KEY, "first", T0.AddSeconds(2));
        buffer.Add_Segment(KEY, "second", T0.AddSeconds(20));
        buffer.Add_Segment(KEY, "third", T0.AddSeconds(40));

        Assert.True(buffer.Is_Holding(KEY));
        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(45)));
    }

    [Fact]
    public void Go_DeliversEverythingAtOnce_WithoutWaitingOutTheWindow()
    {
        var buffer = Buffer();
        buffer.Hold(KEY, T0);
        buffer.Add_Segment(KEY, "first", T0.AddSeconds(2));
        buffer.Add_Segment(KEY, "second", T0.AddSeconds(3));

        buffer.Release(KEY);

        // Immediately — no window wait; the owner already said they are done.
        var delivered = Assert.Single(buffer.Take_ReadyDeliveries(T0.AddSeconds(3)));
        Assert.Equal("first\n\nsecond", delivered.Value);
        Assert.False(buffer.Is_Holding(KEY));
    }

    /// <summary>
    /// A forgotten WAIT must not swallow the owner's messages indefinitely — the session would sit
    /// idle waiting for traffic that can never arrive.
    /// </summary>
    [Fact]
    public void ForgottenHold_ReleasesItselfAfterTheIdleCap()
    {
        var buffer = Buffer();
        buffer.Hold(KEY, T0);
        buffer.Add_Segment(KEY, "stranded", T0.AddSeconds(5));

        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(60)));
        Assert.Equal("stranded", Assert.Single(buffer.Take_ReadyDeliveries(T0.AddSeconds(66))).Value);
    }

    /// <summary>The cap is on SILENCE: someone still typing has not forgotten anything.</summary>
    [Fact]
    public void TheCapIsIdleTime_NotTimeSinceTheWait()
    {
        var buffer = Buffer();
        buffer.Hold(KEY, T0);

        buffer.Add_Segment(KEY, "a", T0.AddSeconds(50));
        buffer.Add_Segment(KEY, "b", T0.AddSeconds(100));

        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(140)));
    }

    [Fact]
    public void WaitThenGo_WithNothingInBetween_DeliversNothing()
    {
        var buffer = Buffer();
        buffer.Hold(KEY, T0);
        buffer.Release(KEY);

        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(10)));
        Assert.False(buffer.Has_PendingDeliveries());
    }

    [Fact]
    public void AHold_IsPerTarget_AndDoesNotStallOtherOrchestrations()
    {
        var buffer = Buffer();
        buffer.Hold(KEY, T0);
        buffer.Add_Segment(KEY, "held", T0.AddSeconds(1));
        buffer.Add_Segment("orch-2/owner-channel.md", "free", T0.AddSeconds(1));

        var ready = buffer.Take_ReadyDeliveries(T0.AddSeconds(6));

        Assert.Equal("free", Assert.Single(ready).Value);
        Assert.True(buffer.Is_Holding(KEY));
    }

    [Fact]
    public void Factory_RejectsAHoldCapShorterThanTheWindow()
    {
        Assert.Throws<ArgumentException>(() => OwnerDeliveryBuffer_Factory.Create(aggregationSeconds: 10, holdCapSeconds: 5));
    }
}
