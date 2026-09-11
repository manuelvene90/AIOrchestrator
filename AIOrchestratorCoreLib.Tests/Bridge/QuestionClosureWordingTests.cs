using AIOrchestratorCoreLib.Bridge.Decisions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The line a lapsed high-risk read-back leaves behind — and the sentence it is not allowed to say
/// any more.
///
/// <para>
/// The old line claimed "the question is still open" without reading the registry, and on
/// 2026-09-09 at ~16:03Z said exactly that about a question that had already been stamped closed.
/// A log that asserts what it has not looked up is worse than a log that says nothing: it sends the
/// reader looking for a question that is not there.
/// </para>
/// </summary>
public class QuestionClosureWordingTests
{
    [Fact]
    public void AQuestionThatIsStillOpen_IsStillDescribedAsOpen()
    {
        var line = QuestionClosure_Wording.Describe_LapsedReadBack(questionStillOpen: true, closureReason: null);

        Assert.Contains("nothing was taken", line, StringComparison.Ordinal);
        Assert.Contains("the question is still open", line, StringComparison.Ordinal);
        Assert.DoesNotContain("already closed", line, StringComparison.Ordinal);
    }

    /// <summary>A reason it was handed is repeated verbatim: the reader wants the cause, not a hint.</summary>
    [Fact]
    public void AClosedQuestion_NamesWhatClosedIt()
    {
        var line = QuestionClosure_Wording.Describe_LapsedReadBack(
            questionStillOpen: false, closureReason: QuestionClosure_Wording.TYPED_ANSWER);

        Assert.Contains("already closed", line, StringComparison.Ordinal);
        Assert.Contains(QuestionClosure_Wording.TYPED_ANSWER, line, StringComparison.Ordinal);
        Assert.DoesNotContain("is still open", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A closure from before a restart is named as unrecorded rather than guessed at — and, above
    /// all, never reported as "still open" again, which is the defect this replaces.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AClosureWithNoRecordedReason_SaysThat_AndNeverClaimsItIsOpen(string? reason)
    {
        var line = QuestionClosure_Wording.Describe_LapsedReadBack(questionStillOpen: false, closureReason: reason);

        Assert.Contains("already closed", line, StringComparison.Ordinal);
        Assert.Contains(QuestionClosure_Wording.UNRECORDED, line, StringComparison.Ordinal);
        Assert.DoesNotContain("is still open", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// The vocabulary is fixed so two log lines a week apart can be compared, and every reason has
    /// to be distinguishable from every other — an alias pair would defeat the whole point.
    /// </summary>
    [Fact]
    public void EveryReason_IsDistinctAndReadable()
    {
        string[] reasons =
        [
            QuestionClosure_Wording.TAPPED_OPTION,
            QuestionClosure_Wording.TALK_REQUEST,
            QuestionClosure_Wording.TYPED_ANSWER,
            QuestionClosure_Wording.CONFIRMED_HIGH_RISK,
            QuestionClosure_Wording.DEADLINE,
            QuestionClosure_Wording.AWAY_PARKED,
            QuestionClosure_Wording.SUPERSEDED,
            QuestionClosure_Wording.UNRECORDED,
        ];

        Assert.Equal(reasons.Length, reasons.Distinct(StringComparer.Ordinal).Count());
        Assert.All(reasons, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
    }
}
