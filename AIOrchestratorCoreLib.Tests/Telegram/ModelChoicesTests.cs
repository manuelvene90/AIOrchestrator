using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// The owner, 2026-09-09: "/model fabel 5.1" — a typo in the very message that asked for the
/// command. The catalogue exists so that a typed name resolves when it is recognisable and is
/// REFUSED when it is not, never guessed: an unrecognised name gets the buttons instead.
/// </summary>
public class ModelChoicesTests
{
    [Fact]
    public void TheCatalogue_OffersTheFourFamilies_InThatOrder()
    {
        Assert.Equal(["fable", "opus", "sonnet", "haiku"], ModelChoices.ALL.Select(choice => choice.Alias).ToList());
        Assert.Equal(["Fable 5.1", "Opus 5", "Sonnet 5", "Haiku 4.5"], ModelChoices.ALL.Select(choice => choice.Label).ToList());
    }

    [Theory]
    [InlineData("fable", "fable")]
    [InlineData("Fable", "fable")]
    [InlineData("  fable  ", "fable")]
    [InlineData("fable 5.1", "fable")]
    [InlineData("Fable 5.1", "fable")]
    [InlineData("opus 5", "opus")]
    [InlineData("sonnet", "sonnet")]
    [InlineData("haiku 4.5", "haiku")]
    public void AFamilyName_ResolvesToItsAlias(string typed, string expected)
    {
        Assert.Equal(expected, ModelChoices.Resolve_OrNull(typed));
    }

    /// <summary>
    /// `opus[1m]` is a real alias Claude Code accepts (the owner's own settings use it), and a full
    /// id is what `--model` documents. Both pass through as typed, lowercased — the catalogue never
    /// narrows a choice the CLI would have taken.
    /// </summary>
    [Theory]
    [InlineData("opus[1m]", "opus[1m]")]
    [InlineData("Sonnet[1m]", "sonnet[1m]")]
    [InlineData("claude-fable-5-1", "claude-fable-5-1")]
    [InlineData("Claude-Opus-5", "claude-opus-5")]
    public void AnExplicitVariantOrFullId_PassesThrough(string typed, string expected)
    {
        Assert.Equal(expected, ModelChoices.Resolve_OrNull(typed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("fabel 5.1")]
    [InlineData("gpt")]
    [InlineData("5.1")]
    public void AnythingUnrecognised_IsNull_NeverAGuess(string? typed)
    {
        Assert.Null(ModelChoices.Resolve_OrNull(typed));
    }

    [Theory]
    [InlineData("fable", "Fable 5.1")]
    [InlineData("haiku", "Haiku 4.5")]
    [InlineData("opus[1m]", "opus[1m]")]
    [InlineData("claude-fable-5-1", "claude-fable-5-1")]
    public void Describe_UsesTheLabelForAnAlias_AndTheArgumentItselfOtherwise(string resolved, string expected)
    {
        Assert.Equal(expected, ModelChoices.Describe(resolved));
    }
}
