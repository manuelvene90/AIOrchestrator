using AIOrchestratorCoreLib.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// "Max ~5 short lines" had been in the supervisor's role command from the start and the owner
/// still reported it as too verbose. Wording it harder is what already failed; this measures.
/// </summary>
public class BrevityPolicyTests
{
    [Fact]
    public void AShortMessage_IsFine()
    {
        Assert.False(Brevity_Policy.Is_TooLong("done — branch wf-perf ready to merge"));
        Assert.False(Brevity_Policy.Is_TooLong("imp-1 blocked, needs your call\nimp-2 still on the broker fix"));
    }

    [Fact]
    public void SixLines_IsOverTheCeiling()
    {
        var text = string.Join('\n', Enumerable.Range(1, 6).Select(i => $"line {i}"));

        Assert.Equal(6, Brevity_Policy.Count_Lines(text));
        Assert.True(Brevity_Policy.Is_TooLong(text));
    }

    [Fact]
    public void ExactlyTheCeiling_IsAllowed()
    {
        var text = string.Join('\n', Enumerable.Range(1, Brevity_Policy.MAX_LINES).Select(i => $"line {i}"));

        Assert.False(Brevity_Policy.Is_TooLong(text));
    }

    /// <summary>Five lines of 400 characters is not brevity — the character cap catches the wall.</summary>
    [Fact]
    public void AFewVeryLongLines_AreAlsoTooLong()
    {
        var text = string.Join('\n', Enumerable.Repeat(new string('x', 300), 3));

        Assert.Equal(3, Brevity_Policy.Count_Lines(text));
        Assert.True(Brevity_Policy.Is_TooLong(text));
    }

    /// <summary>Blank lines are formatting, not content — counting them would flag tidy messages.</summary>
    [Fact]
    public void BlankLines_DoNotCount()
    {
        Assert.Equal(2, Brevity_Policy.Count_Lines("first\n\n\nsecond\n\n"));
        Assert.Equal(2, Brevity_Policy.Count_Lines("first\r\n\r\nsecond"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyText_IsNeverFlagged(string text)
    {
        Assert.False(Brevity_Policy.Is_TooLong(text));
        Assert.Equal(0, Brevity_Policy.Count_Lines(text));
    }

    /// <summary>
    /// The feedback has to carry the ACTUAL numbers. Without them it is just the rule restated,
    /// which is the thing that already failed to change anything.
    /// </summary>
    [Fact]
    public void TheNudge_QuotesTheRealCountAndTheCap()
    {
        var text = string.Join('\n', Enumerable.Range(1, 9).Select(i => $"line {i}"));
        var body = Brevity_Policy.Build_NudgeBody(text);

        Assert.Contains("9 lines", body);
        Assert.Contains($"{text.Length} characters", body);
        Assert.Contains($"{Brevity_Policy.MAX_LINES} lines", body);
        Assert.Contains("PHONE", body);
    }
}
