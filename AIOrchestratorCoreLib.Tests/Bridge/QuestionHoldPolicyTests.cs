using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The decision behind "one question at a time". It is small on purpose — the value of extracting it
/// is that the RULE is readable and pinned, rather than living inside a loop in an 11,000-line file
/// where the previous version of this rule was a comment saying the app deliberately does NOT do it.
/// </summary>
public class QuestionHoldPolicyTests
{
    /// <summary>
    /// The owner's case, 2026-09-09: a question is already on their phone, unanswered, and the
    /// session writes another one. That second entry waits.
    /// </summary>
    [Fact]
    public void AnOwnerChannel_WithAQuestionAlreadyOutstanding_IsHeld()
    {
        Assert.True(QuestionHold_Policy.Should_Hold(isOwnerChannel: true, questionOutstanding: true));
    }

    [Fact]
    public void AnOwnerChannel_WithNothingOutstanding_GoesStraightThrough()
    {
        Assert.False(QuestionHold_Policy.Should_Hold(isOwnerChannel: true, questionOutstanding: false));
    }

    /// <summary>
    /// MEMBER CHANNELS ARE NEVER HELD, and this is the assertion that keeps the fix from turning into
    /// a different bug. Member traffic is agent-to-agent and never reaches the phone; holding it
    /// would stop an orchestration WORKING because its supervisor asked the owner something — which
    /// is precisely the objection the engine raised against gating on a pending question, and a fair
    /// one. What this gates is what gets TEXTED.
    /// </summary>
    [Fact]
    public void AMemberChannel_IsNeverHeld_EvenWithAQuestionOutstanding()
    {
        Assert.False(QuestionHold_Policy.Should_Hold(isOwnerChannel: false, questionOutstanding: true));
    }

    [Fact]
    public void AMemberChannel_WithNothingOutstanding_IsNotHeldEither()
    {
        Assert.False(QuestionHold_Policy.Should_Hold(isOwnerChannel: false, questionOutstanding: false));
    }
}
