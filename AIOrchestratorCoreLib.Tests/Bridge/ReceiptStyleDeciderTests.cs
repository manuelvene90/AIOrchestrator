using AIOrchestratorCoreLib.Bridge;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The pure half of <c>phone.receipts</c> (plan 03 Task 6). The engine half — that the answer is read
/// at the point of effect and that the ✓ fallback survives it — is <see cref="ReceiptStyleTests"/>.
/// </summary>
public class ReceiptStyleDeciderTests
{
    [Fact]
    public void UnderReactions_AMessageWithAnId_IsTriedAsAReaction()
    {
        Assert.True(ReceiptStyle_Decider.Should_TryReaction(ReceiptStyles.Reactions, 77));
    }

    /// <summary>Not "attempted and discarded": a call and a rate-limit slot per owner message is the cost being refused.</summary>
    [Fact]
    public void UnderTicks_NoReactionIsTried_EvenWithAnIdToReactTo()
    {
        Assert.False(ReceiptStyle_Decider.Should_TryReaction(ReceiptStyles.Ticks, 77));
    }

    /// <summary>An update with no message id has nothing to react to — the ✓ path takes it under either style.</summary>
    [Theory]
    [InlineData(ReceiptStyles.Reactions)]
    [InlineData(ReceiptStyles.Ticks)]
    public void AMessageWithNoId_IsNeverTriedAsAReaction(ReceiptStyles receipts)
    {
        Assert.False(ReceiptStyle_Decider.Should_TryReaction(receipts, null));
    }
}
