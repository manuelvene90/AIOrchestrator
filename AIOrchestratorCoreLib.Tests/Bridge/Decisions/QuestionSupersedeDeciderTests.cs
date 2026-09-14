using AIOrchestratorCoreLib.Bridge.Decisions;
using AIOrchestratorCoreLib.Bridge.EngineState;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge.Decisions;

/// <summary>
/// The three rules that stop a question being asked twice — each one a link in the fincanva-6 chain
/// of 2026-09-11 (see <see cref="QuestionSupersede_Decider"/>).
/// </summary>
public class QuestionSupersedeDeciderTests
{
    static readonly DateTime T0 = new(2026, 9, 11, 8, 35, 0, DateTimeKind.Utc);

    static OpenQuestionRecord Question(long messageId, DateTime askedUtc, string? prompt = null)
    {
        return new OpenQuestionRecord { MessageId = messageId, OrchId = "fincanva-6", Text = $"{prompt ?? "q"}\n\n1) a\n2) b", Prompt = prompt, AskedUtc = askedUtc };
    }

    [Theory]
    [InlineData("devo fare solo smoke?")]
    [InlineData("Potuto lavoro finito, devo fare solo smoke?")]
    public void AnOwnerMessageThatAsks_IsNotAReply(string text)
    {
        Assert.False(QuestionSupersede_Decider.Counts_AsAReply(text));
    }

    [Theory]
    [InlineData("start the build and hold the merge")]
    [InlineData("B as I already said")]
    public void AnOwnerMessageThatDoesNotAsk_IsAReply(string text)
    {
        Assert.True(QuestionSupersede_Decider.Counts_AsAReply(text));
    }

    [Fact]
    public void WithNoReply_NothingIsSuperseded()
    {
        Assert.Empty(QuestionSupersede_Decider.Select_ToSupersede([Question(1, T0)], ownerRepliedUtc: null, awaitingReadBack: []));
    }

    [Fact]
    public void AQuestionAskedBeforeTheReply_IsSuperseded_AndOneAskedAfterIt_IsNot()
    {
        var before = Question(1, T0);
        var after = Question(2, T0.AddMinutes(5));

        var superseded = QuestionSupersede_Decider.Select_ToSupersede([before, after], T0.AddMinutes(2), awaitingReadBack: []);

        Assert.Equal([1L], superseded.Select(question => question.MessageId));
    }

    [Fact]
    public void AQuestionTappedAndAwaitingItsCode_IsNeverSuperseded()
    {
        var tapped = Question(1, T0);
        var untapped = Question(2, T0.AddMinutes(1));

        var superseded = QuestionSupersede_Decider.Select_ToSupersede([tapped, untapped], T0.AddMinutes(2), awaitingReadBack: [1L]);

        Assert.Equal([2L], superseded.Select(question => question.MessageId));
    }

    [Fact]
    public void ARepeat_IsTheSameQuestionLine_WhateverItsCaseAndSpacing()
    {
        var open = Question(7, T0, "Unisco FIN-D-302a a dev, così lo trovi su staging per lo smoke?");

        var repeat = QuestionSupersede_Decider.Find_Repeat_OrNull([open], "unisco FIN-D-302a  a dev,\ncosì lo trovi su staging per lo smoke?");

        Assert.Same(open, repeat);
    }

    [Fact]
    public void ADifferentQuestion_IsNotARepeat()
    {
        var open = Question(7, T0, "Unisco FIN-D-302a a dev?");

        Assert.Null(QuestionSupersede_Decider.Find_Repeat_OrNull([open], "Unisco FIN-D-302b a dev?"));
    }

    [Fact]
    public void AQuestionSavedWithoutItsLine_IsNeverARepeat()
    {
        // Written by a build before the line was remembered: nothing to compare is not a match.
        var open = Question(7, T0, prompt: null);

        Assert.Null(QuestionSupersede_Decider.Find_Repeat_OrNull([open], "q"));
    }

    static ClosedQuestionRecord Closed(DateTime closedUtc, string prompt, string orchId = "fincanva-6", string? answer = null)
    {
        return new ClosedQuestionRecord
        {
            OrchId = orchId,
            Prompt = prompt,
            ClosedUtc = closedUtc,
            Closure = answer == null ? QuestionClosure_Wording.TALK_REQUEST : QuestionClosure_Wording.TAPPED_OPTION,
            AnswerLabel = answer,
        };
    }

    [Fact]
    public void AQuestionClosedByATalkRequest_IsStillARepeat()
    {
        var closed = Closed(T0, "Faccio aprire il topic subito?");

        var repeat = QuestionSupersede_Decider.Find_ClosedRepeat_OrNull([closed], "fincanva-6", "faccio  aprire il topic subito?", T0.AddMinutes(3));

        Assert.Same(closed, repeat);
    }

    [Fact]
    public void PastTheWindow_TheSameSentenceIsANewQuestion()
    {
        var closed = Closed(T0, "Merge stage 13 now?");

        Assert.Null(QuestionSupersede_Decider.Find_ClosedRepeat_OrNull(
            [closed], "fincanva-6", "Merge stage 13 now?", T0 + QuestionSupersede_Decider.CLOSED_REPEAT_WINDOW + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void AnotherOrchestrationsDecisionIsNotThisOnes()
    {
        var closed = Closed(T0, "Merge stage 13 now?", orchId: "ai-orch-6");

        Assert.Null(QuestionSupersede_Decider.Find_ClosedRepeat_OrNull([closed], "fincanva-6", "Merge stage 13 now?", T0.AddMinutes(1)));
    }

    /// <summary>
    /// THE NEWEST MATCH WINS, because it is the one whose answer the session is about to be told
    /// about — telling it what the owner decided an hour ago, when they decided something else five
    /// minutes ago, would be worse than saying nothing.
    /// </summary>
    [Fact]
    public void TheNewestDecisionIsTheOneReported()
    {
        var older = Closed(T0, "Merge stage 13 now?", answer: "Hold");
        var newer = Closed(T0.AddMinutes(30), "Merge stage 13 now?", answer: "Merge it");

        var repeat = QuestionSupersede_Decider.Find_ClosedRepeat_OrNull([older, newer], "fincanva-6", "Merge stage 13 now?", T0.AddMinutes(31));

        Assert.Same(newer, repeat);
    }

    /// <summary>
    /// THE FIRST COPY IS WITHHELD AND THE SECOND IS NOT, which is the whole rule: it is a warning,
    /// not a wall. A decision that can never be put to the owner is worse than one duplicate.
    /// </summary>
    [Fact]
    public void AReaskIsWithheldOnce_AndGoesOutIfTheSessionInsists()
    {
        var decided = Closed(T0, "Merge stage 13 now?", answer: "Hold");

        Assert.True(QuestionSupersede_Decider.Should_Withhold_Reask(decided, withheldOnceAlready: false));
        Assert.False(QuestionSupersede_Decider.Should_Withhold_Reask(decided, withheldOnceAlready: true));
    }

    [Fact]
    public void AQuestionThatRepeatsNothing_IsNeverWithheld()
    {
        Assert.False(QuestionSupersede_Decider.Should_Withhold_Reask(null, withheldOnceAlready: false));
        Assert.False(QuestionSupersede_Decider.Should_Withhold_Reask(null, withheldOnceAlready: true));
    }
}
