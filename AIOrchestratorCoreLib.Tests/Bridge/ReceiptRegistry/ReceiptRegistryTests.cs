using AIOrchestratorCoreLib.Bridge.ReceiptRegistry;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.ReceiptRegistry;

/// <summary>
/// The receipt each topic is waiting to evolve — the ✓ to edit into ✓✓, or the owner message wearing 👀
/// to turn 👌 — moved out of <c>BridgeEngineModel</c> by plan 03 Task 6b.
/// </summary>
public class ReceiptRegistryTests
{
    const long TOPIC_ID = 4242;

    readonly IReceiptRegistry _registry = ReceiptRegistry_Factory.Create();

    /// <summary>Consumed on read: the next batch gets its own receipt rather than rewriting one already answered.</summary>
    [Fact]
    public void ATick_IsTakenOnce()
    {
        _registry.Remember_Tick(TOPIC_ID, 9001);

        Assert.Equal(9001, _registry.Take_Tick_OrNull(TOPIC_ID));
        Assert.Null(_registry.Take_Tick_OrNull(TOPIC_ID));
    }

    [Fact]
    public void AReaction_IsTakenOnce_AndIsNotATick()
    {
        _registry.Remember_Reaction(TOPIC_ID, 77);

        Assert.Null(_registry.Take_Tick_OrNull(TOPIC_ID));
        Assert.Equal(77, _registry.Take_Reaction_OrNull(TOPIC_ID));
        Assert.Null(_registry.Take_Reaction_OrNull(TOPIC_ID));
    }

    /// <summary>The newest ✓ is the one the delivery edits — the earlier ones of a batch stay ✓.</summary>
    [Fact]
    public void ALaterTick_ReplacesTheEarlierOne()
    {
        _registry.Remember_Tick(TOPIC_ID, 9001);
        _registry.Remember_Tick(TOPIC_ID, 9002);

        Assert.Equal(9002, _registry.Take_Tick_OrNull(TOPIC_ID));
    }

    [Fact]
    public void TheGeneralTopic_IsItsOwnKey()
    {
        _registry.Remember_Tick(null, 9003);

        Assert.Null(_registry.Take_Tick_OrNull(TOPIC_ID));
        Assert.Equal(9003, _registry.Take_Tick_OrNull(null));
    }

    /// <summary>
    /// A PEEK, NOT A TAKE: asking whether the delivery will edit a message must leave the ✓ where the
    /// delivery will find it.
    /// </summary>
    [Fact]
    public void TheRegisteredTick_IsTheOneTheDeliveryWillEdit_AndAskingDoesNotConsumeIt()
    {
        _registry.Remember_Tick(TOPIC_ID, 9001);

        Assert.True(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9001));
        Assert.False(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9000));
        Assert.False(_registry.Is_TheTickTheDeliveryWillEdit(null, 9001));
        Assert.Equal(9001, _registry.Take_Tick_OrNull(TOPIC_ID));
        Assert.False(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9001));
    }

    /// <summary>
    /// A 👀 WAITING IN THE SAME TOPIC WINS AT DELIVERY — the delivery turns it 👌 and returns before any ✓
    /// is edited — so a ✓ registered beside it is not the one the delivery will edit.
    /// </summary>
    [Fact]
    public void ATickBesideAWaitingReaction_IsNotTheOneTheDeliveryWillEdit()
    {
        _registry.Remember_Tick(TOPIC_ID, 9001);
        _registry.Remember_Reaction(TOPIC_ID, 77);

        Assert.False(_registry.Is_TheTickTheDeliveryWillEdit(TOPIC_ID, 9001));
    }
}
