using AIOrchestratorCoreLib.Bridge.Decisions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// The five fields a question to the owner must carry before the app will forward it.
/// </summary>
public class OwnerQuestionContractTests
{
    static OwnerQuestionDraft Complete() => new(
        QuestionLines: ["Merge wf-perf into master now, or hold for your review?"],
        Options: ["Merge it", "Hold"],
        Recommendations: ["Hold — you asked to read every merge to master first."],
        Risks: ["high"],
        Rows: ["FIN-D-277a"]);

    [Fact]
    public void ACompleteDraft_HasNoFaults_AndBuilds()
    {
        var draft = Complete();

        Assert.Empty(OwnerQuestion_Contract.Check(draft));

        var question = OwnerQuestion_Contract.Build(draft);

        Assert.Equal("Merge wf-perf into master now, or hold for your review?", question.Question);
        Assert.Equal(["Merge it", "Hold"], question.Options);
        Assert.Equal("Hold — you asked to read every merge to master first.", question.Recommendation);
        Assert.True(question.DeclaredHighRisk);
        Assert.Equal("FIN-D-277a", question.RowCode);
    }

    [Fact]
    public void NothingAttempted_IsNotAQuestion_AndOneMarkerIs()
    {
        Assert.False(OwnerQuestion_Contract.Is_Attempted(new([], [], [], [], [])));
        Assert.True(OwnerQuestion_Contract.Is_Attempted(new([], ["only an option"], [], [], [])));
        Assert.True(OwnerQuestion_Contract.Is_Attempted(new([], [], ["only a recommendation"], [], [])));
        Assert.True(OwnerQuestion_Contract.Is_Attempted(new(["only a question"], [], [], [], [])));
    }

    [Fact]
    public void EveryMissingField_IsItsOwnFault_AndAllAreReportedAtOnce()
    {
        // The old grammar accepted exactly this — an OPTION line and nothing else.
        var faults = OwnerQuestion_Contract.Check(new([], ["Merge it"], [], [], []));

        Assert.Equal(
            [
                QuestionFaults.NoQuestion,
                QuestionFaults.FewerThanTwoOptions,
                QuestionFaults.NoRecommendation,
                QuestionFaults.NoRisk,
                QuestionFaults.NoRow,
            ],
            faults);
    }

    [Fact]
    public void OneOptionIsNotAChoice()
    {
        Assert.Contains(QuestionFaults.FewerThanTwoOptions, OwnerQuestion_Contract.Check(Complete() with { Options = ["Merge it"] }));
        Assert.Contains(QuestionFaults.FewerThanTwoOptions, OwnerQuestion_Contract.Check(Complete() with { Options = ["Merge it", "   "] }));
    }

    [Theory]
    [InlineData("high", true)]
    [InlineData("HIGH", true)]
    [InlineData(" High ", true)]
    [InlineData("low", false)]
    public void Risk_IsHighOrLow_CaseInsensitively(string risk, bool expectedHigh)
    {
        var draft = Complete() with { Risks = [risk] };

        Assert.Empty(OwnerQuestion_Contract.Check(draft));
        Assert.Equal(expectedHigh, OwnerQuestion_Contract.Build(draft).DeclaredHighRisk);
    }

    [Theory]
    [InlineData("medium")]
    [InlineData("moderate")]
    [InlineData("yes")]
    public void AnUnreadableRisk_IsAFault_AndIsNeverReadAsLow(string risk)
    {
        // Reading it as `low` would be the app deciding a lock is unnecessary on the agent's behalf.
        Assert.Equal([QuestionFaults.RiskNotHighOrLow], OwnerQuestion_Contract.Check(Complete() with { Risks = [risk] }));
    }

    [Theory]
    [InlineData("none", null)]
    [InlineData("NONE", null)]
    [InlineData("FIN-D-277", "FIN-D-277")]
    [InlineData("FIN-D-277a", "FIN-D-277a")]
    [InlineData("AI-ORCH-D-012b", "AI-ORCH-D-012b")]
    public void Row_IsACodeOrTheWordNone(string row, string? expectedCode)
    {
        var draft = Complete() with { Rows = [row] };

        Assert.Empty(OwnerQuestion_Contract.Check(draft));
        Assert.Equal(expectedCode, OwnerQuestion_Contract.Build(draft).RowCode);
    }

    [Theory]
    [InlineData("the pricing thing")]
    [InlineData("277")]
    [InlineData("FIN-277")]
    public void AnUnreadableRow_IsAFault(string row)
    {
        Assert.Equal([QuestionFaults.RowNotACodeOrNone], OwnerQuestion_Contract.Check(Complete() with { Rows = [row] }));
    }

    [Fact]
    public void TheFirstReadableMarkerWins_WhenOneIsRepeated()
    {
        // The rule QuestionDirectives_Parser already keeps: a marker repeated at the bottom of an
        // entry must not silently override the one a human reads at the top.
        var question = OwnerQuestion_Contract.Build(Complete() with
        {
            Risks = ["low", "high"],
            Recommendations = ["Hold", "Merge"],
            Rows = ["FIN-D-277a", "none"],
        });

        Assert.False(question.DeclaredHighRisk);
        Assert.Equal("Hold", question.Recommendation);
        Assert.Equal("FIN-D-277a", question.RowCode);
    }

    [Fact]
    public void SeveralQuestionLines_AreJoinedIntoOneQuestion()
    {
        var question = OwnerQuestion_Contract.Build(Complete() with
        {
            QuestionLines = ["Merge wf-perf into master now,", "or hold for your review?"],
        });

        Assert.Equal("Merge wf-perf into master now, or hold for your review?", question.Question);
    }

    [Fact]
    public void Build_RefusesAnIncompleteDraft_RatherThanInventingTheMissingHalf()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => OwnerQuestion_Contract.Build(Complete() with { Recommendations = [] }));
        Assert.Contains(nameof(QuestionFaults.NoRecommendation), thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFault_SaysWhatToWrite()
    {
        foreach (var fault in Enum.GetValues<QuestionFaults>())
        {
            var text = OwnerQuestion_Contract.Describe(fault);

            Assert.False(string.IsNullOrWhiteSpace(text));

            // It names the marker the agent has to write, which is the whole point of describing it.
            Assert.Contains(":", text, StringComparison.Ordinal);
        }
    }
}
