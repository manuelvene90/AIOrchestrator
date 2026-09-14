using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// AN ANSWER BELONGS TO ITS QUESTION.
///
/// <para>
/// Every case below is taken from one two-hour topic the owner reported. The engine's rule at the
/// time was its own comment — <i>any owner message answers whatever was pending</i> — and these are
/// the shapes that rule got wrong. They are pinned here rather than only end to end because the
/// rule is the thing under test, and a rule with a name can be read at the call site.
/// </para>
/// </summary>
public class AnswerBindingDeciderTests
{
    const string A_PLAIN_ANSWER = "the plan being bought";

    /// <summary>Verbatim from the topic, and the message that produced `✅ answered:` twice.</summary>
    const string THE_OWNERS_OWN_QUESTION = "A che punto siamo?";

    [Fact]
    public void OneOpenQuestion_AndAPlainReply_Binds()
    {
        var binding = AnswerBinding_Decider.Decide(1, A_PLAIN_ANSWER);

        Assert.Equal(AnswerBindings.TheOnlyOpenQuestion, binding);
        Assert.True(AnswerBinding_Decider.Binds(binding));
    }

    [Fact]
    public void NothingOpen_BindsNothing()
    {
        var binding = AnswerBinding_Decider.Decide(0, A_PLAIN_ANSWER);

        Assert.Equal(AnswerBindings.NothingWasOpen, binding);
        Assert.False(AnswerBinding_Decider.Binds(binding));
    }

    /// <summary>
    /// THE REPORTED DEFECT. "Where are we at?" is a question, and it was filed as the answer to the
    /// merge question — twice, once per open question the sweep removed.
    /// </summary>
    [Fact]
    public void TheOwnersOwnQuestion_IsNeverAnAnswer()
    {
        var binding = AnswerBinding_Decider.Decide(1, THE_OWNERS_OWN_QUESTION);

        Assert.Equal(AnswerBindings.NothingItIsAQuestion, binding);
        Assert.False(AnswerBinding_Decider.Binds(binding));
    }

    /// <summary>The count must not rescue it: ask-shaped is ask-shaped whatever is pending.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void AnAskShapedMessage_BindsNothing_AtAnyCount(int openQuestions)
    {
        Assert.False(AnswerBinding_Decider.Binds(
            AnswerBinding_Decider.Decide(openQuestions, THE_OWNERS_OWN_QUESTION)));
    }

    /// <summary>
    /// FOUR QUESTIONS, ONE WORD, ALL FOUR CLOSED. With a merge question, 004, 267 and 277 open, any
    /// single reply closed every one of them and wrote the same text under each.
    /// </summary>
    [Fact]
    public void SeveralOpenQuestions_AndAReplyThatNamesNone_BindsNothing()
    {
        var binding = AnswerBinding_Decider.Decide(4, A_PLAIN_ANSWER);

        Assert.Equal(AnswerBindings.NothingItIsAmbiguous, binding);
        Assert.False(AnswerBinding_Decider.Binds(binding));
    }

    [Fact]
    public void TwoOpenQuestions_AreAlreadyAmbiguous()
    {
        Assert.False(AnswerBinding_Decider.Binds(AnswerBinding_Decider.Decide(2, A_PLAIN_ANSWER)));
    }

    /// <summary>
    /// A tapped option arrives as a synthetic owner message carrying the option label. Labels are
    /// short and declarative, so the tap echo must not read as a question and must not be blocked
    /// from binding by its own shape.
    /// </summary>
    [Theory]
    [InlineData("Merge both")]
    [InlineData("The plan being bought")]
    [InlineData("No card — that's the intent")]
    public void ATappedOptionLabel_ReadsAsAnAnswer(string optionLabel)
    {
        Assert.Equal(AnswerBindings.TheOnlyOpenQuestion, AnswerBinding_Decider.Decide(1, optionLabel));
    }

    /// <summary>
    /// A COMPOUND REPLY IS AN ANSWER PLUS A REQUEST, and the topic has one: "the four, plus give me
    /// the questions for 274". The trailing line is not ask-shaped, so it binds — which is right,
    /// because the first half really did answer. Pinned so the ask-shaped rule is not later widened
    /// into "contains a request" and made to swallow ordinary answers.
    /// </summary>
    [Fact]
    public void ACompoundReplyWithNoQuestionMark_StillBinds()
    {
        Assert.True(AnswerBinding_Decider.Binds(
            AnswerBinding_Decider.Decide(1, "le 4 piu fammi le domande per 274")));
    }

    /// <summary>
    /// A TELEGRAM REPLY NAMES ITS QUESTION (owner, 2026-09-11: several open questions are fine, as
    /// long as a reply links to one). It binds however many are open.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void AReplyOnAnOpenQuestion_BindsToIt_HoweverManyAreOpen(int openQuestions)
    {
        Assert.Equal(AnswerBindings.TheQuestionItRepliesTo, AnswerBinding_Decider.Decide(openQuestions, "tienilo fermo per ora", OwnerReplyTargets.AnOpenQuestion));
        Assert.True(AnswerBinding_Decider.Binds(AnswerBindings.TheQuestionItRepliesTo));
    }

    /// <summary>fincanva-6, 2026-09-11 12:58 — an instruction pointing elsewhere was stamped as the only open question's answer.</summary>
    [Fact]
    public void AReplyToAnotherMessage_BindsNothing_EvenWithOneQuestionOpen()
    {
        var binding = AnswerBinding_Decider.Decide(1, "usa key o MCP per cancellare mio abbonamento", OwnerReplyTargets.AnotherMessage);

        Assert.Equal(AnswerBindings.NothingItRepliesToSomethingElse, binding);
        Assert.False(AnswerBinding_Decider.Binds(binding));
    }

    /// <summary>A reply that asks is a question about the question, not an answer to it.</summary>
    [Fact]
    public void AReplyThatAsks_BindsNothing()
    {
        Assert.Equal(AnswerBindings.NothingItIsAQuestion, AnswerBinding_Decider.Decide(2, "e se lo facessimo domani?", OwnerReplyTargets.AnOpenQuestion));
    }

    /// <summary>Empty and whitespace must not crash the path every inbound message runs through.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n")]
    public void AnEmptyMessage_DoesNotThrow_AndBindsLikeAPlainReply(string text)
    {
        Assert.Equal(AnswerBindings.TheOnlyOpenQuestion, AnswerBinding_Decider.Decide(1, text));
    }

    /// <summary>Every outcome says something, so a question left open is never left unexplained.</summary>
    [Theory]
    [InlineData(AnswerBindings.NothingWasOpen)]
    [InlineData(AnswerBindings.TheOnlyOpenQuestion)]
    [InlineData(AnswerBindings.NothingItIsAQuestion)]
    [InlineData(AnswerBindings.NothingItIsAmbiguous)]
    [InlineData(AnswerBindings.TheQuestionItRepliesTo)]
    [InlineData(AnswerBindings.NothingItRepliesToSomethingElse)]
    public void EveryOutcome_Describes(AnswerBindings binding)
    {
        Assert.False(string.IsNullOrWhiteSpace(AnswerBinding_Decider.Describe(binding, 2)));
    }
}
