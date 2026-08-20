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
        return OwnerDeliveryBuffer_Factory.Create(aggregationSeconds: 4);
    }

    [Fact]
    public void WithoutAHold_TheShortWindowDelivers()
    {
        var buffer = Buffer();
        buffer.Add_Segment(KEY, "one", T0);

        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(3)));
        Assert.Equal("one", Assert.Single(buffer.Take_ReadyDeliveries(T0.AddSeconds(4))).Value.Text);
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

    /// <summary>
    /// The owner's actual habit: send a message, realise mid-countdown you have more to say, type
    /// WAIT. The message already sitting in the window must be caught by it — otherwise the thing
    /// you were trying to add to reaches the session without the addition.
    /// </summary>
    [Fact]
    public void Wait_AlsoHolds_AMessageAlreadyMidCountdown()
    {
        var buffer = Buffer();
        buffer.Add_Segment(KEY, "first thought", T0);

        // WAIT arrives 2 s in, before the 4 s window would have delivered it.
        buffer.Hold(KEY, T0.AddSeconds(2));

        Assert.Equal(1, buffer.Count_Pending(KEY));
        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(6)));

        buffer.Add_Segment(KEY, "the rest of it", T0.AddSeconds(30));
        buffer.Release(KEY);

        var delivered = Assert.Single(buffer.Take_ReadyDeliveries(T0.AddSeconds(31)));
        Assert.Equal("first thought\n\nthe rest of it", delivered.Value.Text);
    }

    [Fact]
    public void Count_Pending_TracksWhatTheHoldReceiptShows()
    {
        var buffer = Buffer();
        Assert.Equal(0, buffer.Count_Pending(KEY));

        buffer.Add_Segment(KEY, "a", T0);
        buffer.Hold(KEY, T0.AddSeconds(1));
        Assert.Equal(1, buffer.Count_Pending(KEY));

        buffer.Add_Segment(KEY, "b", T0.AddSeconds(2));
        Assert.Equal(2, buffer.Count_Pending(KEY));
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
        Assert.Equal("first\n\nsecond", delivered.Value.Text);
        Assert.False(buffer.Is_Holding(KEY));
    }

    /// <summary>
    /// A HOLD NEVER RELEASES ITSELF — reversed on the owner's ruling, 2026-08-20.
    ///
    /// This test used to assert the opposite, on the reasoning it still carried: "a forgotten WAIT
    /// must not swallow the owner's messages indefinitely". That was a real concern and the cap was a
    /// real answer to it — but the cap expired in SILENCE, so the receipt reverted to delivered and
    /// every following message went through as though nothing had been pressed. The owner watched
    /// that happen and ruled: hold until GO, never lapse.
    ///
    /// It is safe without a timer because the hold is VISIBLE. The receipt reads ⏸ holding for as
    /// long as it lasts, so a forgotten hold is something they can SEE and end — not a silence they
    /// have to infer. The old rule guessed when they had stopped caring; the new one just shows them.
    /// </summary>
    [Fact]
    public void ForgottenHold_NeverReleasesItself()
    {
        var buffer = Buffer();
        buffer.Hold(KEY, T0);
        buffer.Add_Segment(KEY, "stranded", T0.AddSeconds(5));

        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(60)));
        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddSeconds(66)));
        Assert.Empty(buffer.Take_ReadyDeliveries(T0.AddHours(6)));

        Assert.True(buffer.Is_Holding(KEY));

        buffer.Release(KEY);

        Assert.Equal("stranded", Assert.Single(buffer.Take_ReadyDeliveries(T0.AddHours(6))).Value.Text);
    }

    /// <summary>
    /// Typing during a hold changes nothing — it all waits for GO.
    ///
    /// This was named TheCapIsIdleTime_NotTimeSinceTheWait, and it proved that the old cap measured
    /// SILENCE rather than elapsed time. The cap is gone (owner, 2026-08-20), so the name asserted a
    /// concept the code no longer has — a stale name outlives the reader who knows it is stale. What
    /// it actually demonstrates is still worth keeping: a hold does not care how much you type.
    /// </summary>
    [Fact]
    public void TypingDuringAHoldStillDeliversNothing()
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

        Assert.Equal("free", Assert.Single(ready).Value.Text);
        Assert.True(buffer.Is_Holding(KEY));
    }

    [Fact]
    public void Factory_RejectsAHoldCapShorterThanTheWindow()
    {
    }
}
