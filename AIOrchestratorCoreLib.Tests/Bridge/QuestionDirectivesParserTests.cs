using AIOrchestratorCoreLib.Bridge.Decisions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

public class QuestionDirectivesParserTests
{
    [Theory]
    [InlineData("2h", 120)]
    [InlineData("90m", 90)]
    [InlineData("45", 45)]
    [InlineData("2H", 120)]
    [InlineData("90M", 90)]
    [InlineData("  2h  ", 120)]
    [InlineData(" 45 ", 45)]
    public void Parse_Duration_OrNull_HandlesHoursMinutesAndBareMinutes_CaseInsensitivelyAndTrimmed(string value, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), QuestionDirectives_Parser.Parse_Duration_OrNull(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-5")]
    public void Parse_Duration_OrNull_ReturnsNull_ForEmptyUnreadableZeroOrNegativeValues(string? value)
    {
        Assert.Null(QuestionDirectives_Parser.Parse_Duration_OrNull(value));
    }

    /// <summary>
    /// A value further out than MAXIMUM_DEADLINE_HOURS is not a huge deadline, it is treated as no
    /// deadline at all — an agent writing "9999h" has declared a number, not an intent, and carrying
    /// it forward would put a question in /pending with an expiry nobody will ever live to see.
    /// </summary>
    [Fact]
    public void Parse_Duration_OrNull_ReturnsNull_BeyondTheMaximumDeadlineHours()
    {
        var justOverTheLimit = (QuestionDirectives_Parser.MAXIMUM_DEADLINE_HOURS + 1) + "h";

        Assert.Null(QuestionDirectives_Parser.Parse_Duration_OrNull(justOverTheLimit));

        var atTheLimit = QuestionDirectives_Parser.MAXIMUM_DEADLINE_HOURS + "h";

        Assert.NotNull(QuestionDirectives_Parser.Parse_Duration_OrNull(atTheLimit));
    }

    [Theory]
    [InlineData("1", 5, 0)]
    [InlineData("5", 5, 4)]
    [InlineData("3", 5, 2)]
    public void Parse_OptionIndex_OrNull_ConvertsTheOwnersOneBasedNumberToAZeroBasedIndex(string value, int optionCount, int expectedIndex)
    {
        Assert.Equal(expectedIndex, QuestionDirectives_Parser.Parse_OptionIndex_OrNull(value, optionCount));
    }

    /// <summary>
    /// An off-by-one here would not throw or visibly fail — it would silently apply the option NEXT
    /// to the one the owner intended, which for a high-stakes default is a wrong decision made to
    /// look like the right one. Zero, past-the-end, and non-numeric input must all read as "no
    /// default" rather than clamp to something adjacent.
    /// </summary>
    [Theory]
    [InlineData("0", 5)]
    [InlineData("6", 5)]
    [InlineData("abc", 5)]
    public void Parse_OptionIndex_OrNull_ReturnsNull_ForZeroPastTheEndOrNonNumeric_RatherThanSilentlyDefaultingToTheNeighbour(string value, int optionCount)
    {
        Assert.Null(QuestionDirectives_Parser.Parse_OptionIndex_OrNull(value, optionCount));
    }

    [Fact]
    public void Parse_OptionIndex_OrNull_ReturnsNull_WhenOptionCountIsZero()
    {
        Assert.Null(QuestionDirectives_Parser.Parse_OptionIndex_OrNull("1", 0));
    }

    /// <summary>
    /// When a marker is repeated, the FIRST readable value wins — this matters because a human
    /// reading the entry sees the first occurrence at the top and would be confused if a later,
    /// possibly stray, repeat silently overrode it.
    /// </summary>
    [Fact]
    public void Parse_TakesTheFirstReadableValue_WhenAMarkerIsRepeated()
    {
        var (deadline, _) = QuestionDirectives_Parser.Parse(
            deadlineValues: ["abc", "2h", "4h"],
            defaultValues: [],
            optionCount: 3);

        Assert.Equal(TimeSpan.FromHours(2), deadline);
    }

    [Fact]
    public void Parse_TakesTheFirstReadableDefault_WhenAMarkerIsRepeated()
    {
        var (_, defaultOptionIndex) = QuestionDirectives_Parser.Parse(
            deadlineValues: ["1h"],
            defaultValues: ["0", "2", "3"],
            optionCount: 3);

        Assert.Equal(1, defaultOptionIndex);
    }

    /// <summary>
    /// The deadline survives an unreadable default — dropping the deadline just because the default
    /// next to it was a typo would be a second, unrelated failure riding on the first.
    /// </summary>
    [Fact]
    public void Parse_KeepsTheDeadline_WhenTheDefaultIsUnreadable()
    {
        var (deadline, defaultOptionIndex) = QuestionDirectives_Parser.Parse(
            deadlineValues: ["2h"],
            defaultValues: ["not-a-number"],
            optionCount: 3);

        Assert.Equal(TimeSpan.FromHours(2), deadline);
        Assert.Null(defaultOptionIndex);
    }

    /// <summary>
    /// The asymmetric half: a default with NO deadline is dropped entirely rather than kept as a
    /// dangling default nobody would ever apply. This is deliberate asymmetry, not an inconsistency —
    /// a default that is never reached is not a default, whereas losing a real deadline would
    /// silently restore "waits for ever" for the one question somebody bothered to bound. The two
    /// directions cost differently, so they are handled differently on purpose.
    /// </summary>
    [Fact]
    public void Parse_DropsTheDefault_WhenThereIsNoDeadline_BecauseADefaultNobodyWouldEverApplyIsNotADefault()
    {
        var (deadline, defaultOptionIndex) = QuestionDirectives_Parser.Parse(
            deadlineValues: [],
            defaultValues: ["2"],
            optionCount: 3);

        Assert.Null(deadline);
        Assert.Null(defaultOptionIndex);
    }
}
