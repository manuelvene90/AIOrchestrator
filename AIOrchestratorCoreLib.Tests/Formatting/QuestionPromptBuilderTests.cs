using AIOrchestratorCoreLib.Formatting;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Formatting;

/// <summary>
/// The owner's complaint this exists for: a long supervisor message with buttons hanging off the
/// bottom and no visible question — "I have a multi choice, and no idea what is the question".
/// The buttons now ride on their own short message, and this builds what that message says.
/// </summary>
public class QuestionPromptBuilderTests
{
    [Fact]
    public void Build_IsTheGlyphAndTheQuestion()
    {
        Assert.Equal("❓ Merge wf-perf into master now?", QuestionPrompt_Builder.Build("Merge wf-perf into master now?"));
        Assert.Equal("❓ Merge wf-perf into master now?", QuestionPrompt_Builder.Build("  Merge wf-perf into master now?  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Build_ThrowsOnAnEmptyQuestion_RatherThanSubstitutingOne(string empty)
    {
        // THE DERIVATION AND THE CANNED "Your call:" ARE RETIRED. Both were rescues of a malformed
        // question, and the rescue is why the shape was never fixed — the owner spent an afternoon
        // answering questions with questions. OwnerQuestion_Contract refuses an incomplete question
        // before this is ever reached, so an empty one here is a broken invariant, not an input.
        Assert.Throws<ArgumentException>(() => QuestionPrompt_Builder.Build(empty));
    }

    [Fact]
    public void Build_AnsweredText_KeepsTheQuestionAndRecordsTheChoice()
    {
        var answered = QuestionPrompt_Builder.Build_AnsweredText("❓ Merge now, or hold?", "Hold");

        Assert.Equal("❓ Merge now, or hold?\n\n✅ Hold", answered);
    }

    /// <summary>
    /// THE SAME RECORD FOR AN ANSWER THAT WAS TYPED. The owner answers by message whenever the reply
    /// needs more than a label, and that path used to stamp nothing at all — so scrolling back showed
    /// a question that still read as open. Owner, 2026-08-24: *"The unanswered question — or rather,
    /// the one I answered with a message instead of a tap — dated back further."*
    /// </summary>
    [Fact]
    public void Build_AnsweredByMessageText_RecordsTheirWords_NotAChoice()
    {
        var answered = QuestionPrompt_Builder.Build_AnsweredByMessageText("❓ Merge now, or hold?", "hold it, I want to read the diff first");

        Assert.Equal("❓ Merge now, or hold?\n\n✅ answered: hold it, I want to read the diff first", answered);
    }

    /// <summary>
    /// A typed answer can be a pasted conversation. The record must stay a record rather than become
    /// a second copy of the message, so only the first line survives and it is truncated.
    /// </summary>
    [Fact]
    public void Build_AnsweredByMessageText_TakesTheFirstLineAndTruncatesIt()
    {
        var pasted = $"{new string('x', QuestionPrompt_Builder.MAX_ANSWER_PREVIEW_LENGTH + 40)}\nand a second line";

        var answered = QuestionPrompt_Builder.Build_AnsweredByMessageText("❓ Which?", pasted);

        Assert.Equal(
            $"❓ Which?\n\n✅ answered: {new string('x', QuestionPrompt_Builder.MAX_ANSWER_PREVIEW_LENGTH)}…",
            answered);

        Assert.DoesNotContain("second line", answered, StringComparison.Ordinal);
    }

    /// <summary>
    /// A photo or a sticker leaves no usable preview. It still answered the question, so the record
    /// still has to say so — a tick with nothing after it would read as a rendering bug.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   \n  \n")]
    public void Build_AnsweredByMessageText_WithNothingToPreview_StillSaysItWasAnswered(string ownerText)
    {
        var answered = QuestionPrompt_Builder.Build_AnsweredByMessageText("❓ Which?", ownerText);

        Assert.Equal($"❓ Which?\n\n✅ answered: {QuestionPrompt_Builder.ANSWERED_IN_WRITING}", answered);
    }

    /// <summary>
    /// A superseded question keeps its words (the owner scrolls back to it) and says plainly why it
    /// closed — and never with a "✅", because no choice was recorded.
    /// </summary>
    [Fact]
    public void ASupersededQuestion_KeepsItsWords_SaysWhy_AndRecordsNoChoice()
    {
        var text = QuestionPrompt_Builder.Build_SupersededText("❓ Start the build now?");

        Assert.StartsWith("❓ Start the build now?", text, StringComparison.Ordinal);
        Assert.EndsWith(QuestionPrompt_Builder.SUPERSEDED_SUFFIX, text, StringComparison.Ordinal);
        Assert.Contains("superseded", text, StringComparison.Ordinal);
        Assert.DoesNotContain("✅", text, StringComparison.Ordinal);
    }
}
