using AIOrchestratorCoreLib.Bridge.Decisions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class ConfirmationCodeTests
{
    /// <summary>
    /// Every generated code must be exactly ConfirmationCode.DIGITS ASCII digits, leading zeros
    /// included — a code that renders as "42" instead of "0042" on some draws would be a visibly
    /// inconsistent shape on the owner's screen, undermining the "read it back exactly" contract.
    /// Drawing ~500 codes and asserting a spread of distinct values also confirms it is not
    /// degenerately returning the same value every time.
    /// </summary>
    [Fact]
    public void Generate_AlwaysProducesFourAsciiDigits_LeadingZerosIncluded()
    {
        var distinctValues = new HashSet<string>();

        for (var i = 0; i < 500; i++)
        {
            var code = ConfirmationCode.Generate();

            Assert.Equal(ConfirmationCode.DIGITS, code.Length);
            Assert.True(code.All(char.IsAsciiDigit), $"'{code}' contains a non-ASCII-digit character");

            distinctValues.Add(code);
        }

        Assert.True(distinctValues.Count > 1, "500 draws produced only one distinct value — the generator looks broken or frozen");
    }

    [Fact]
    public void Matches_AcceptsTheExactCode()
    {
        Assert.True(ConfirmationCode.Matches("1234", "1234"));
    }

    [Fact]
    public void Matches_AcceptsTheCodeWithSurroundingWhitespace_BecauseThatIsThePhonesAutocorrectNotAnAnswer()
    {
        Assert.True(ConfirmationCode.Matches("  1234  \n", "1234"));
    }

    /// <summary>
    /// A message that merely CONTAINS the four digits — "push 1234 commits?" — must be rejected, not
    /// accepted as a match. If containment were enough, an entirely innocent sentence that happens to
    /// mention a number matching the code shown on screen would approve a production push the owner
    /// never actually confirmed.
    /// </summary>
    [Fact]
    public void Matches_RejectsAMessageThatMerelyContainsTheCode_BecauseThatWouldLetAnInnocentSentenceApproveADeploy()
    {
        Assert.False(ConfirmationCode.Matches("push 1234 commits?", "1234"));
    }

    [Fact]
    public void Matches_RejectsNull()
    {
        Assert.False(ConfirmationCode.Matches(null, "1234"));
    }

    [Fact]
    public void Matches_RejectsEmpty()
    {
        Assert.False(ConfirmationCode.Matches("", "1234"));
    }

    [Fact]
    public void Matches_RejectsADifferentCode()
    {
        Assert.False(ConfirmationCode.Matches("4321", "1234"));
    }

    [Fact]
    public void Matches_RejectsALongerString()
    {
        Assert.False(ConfirmationCode.Matches("12345", "1234"));
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("0000")]
    [InlineData("9999")]
    public void Looks_LikeACode_IsTrueForExactlyFourAsciiDigits(string text)
    {
        Assert.True(ConfirmationCode.Looks_LikeACode(text));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("")]
    [InlineData("١٢٣٤")]
    public void Looks_LikeACode_IsFalse_ForWrongLengthNonDigitOrNonAsciiDigits(string text)
    {
        Assert.False(ConfirmationCode.Looks_LikeACode(text));
    }

    [Fact]
    public void Looks_LikeACode_IsFalseForNull()
    {
        Assert.False(ConfirmationCode.Looks_LikeACode(null));
    }
}
