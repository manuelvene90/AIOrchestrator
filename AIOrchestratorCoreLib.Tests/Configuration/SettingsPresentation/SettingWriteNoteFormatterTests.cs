using AIOrchestratorCoreLib.Configuration.SettingsPresentation;
using AIOrchestratorCoreLib.Configuration.SettingsWriting;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.SettingsPresentation;

/// <summary>
/// WHAT THE WPF WINDOW SAYS AFTER AN EDIT (plan 04 Task 9) — decided here because the window cannot be tested.
/// A refusal is the writer's own message (decision 21), and <c>WriteFailed</c> (P33) is a refusal, never "Saved.".
/// </summary>
public class SettingWriteNoteFormatterTests
{
    [Fact]
    public void AnAppliedEdit_SaysSaved_AndIsNotARefusal()
    {
        Assert.Equal((SettingWriteNote_Formatter.SAVED, false), SettingWriteNote_Formatter.Describe(SettingsWriteOutcomes.Applied, null));
    }

    [Fact]
    public void AReset_SaysReset_OrTheWritersNothingToResetSentence()
    {
        Assert.Equal((SettingWriteNote_Formatter.RESET, false), SettingWriteNote_Formatter.Describe(SettingsWriteOutcomes.Reset, null));
        Assert.Equal(("There was nothing to reset.", false), SettingWriteNote_Formatter.Describe(SettingsWriteOutcomes.Reset, "There was nothing to reset."));
    }

    /// <summary>Every refusal, the file's included, is drawn as a refusal carrying the writer's words verbatim.</summary>
    [Theory]
    [InlineData(SettingsWriteOutcomes.RefusedInvalid)]
    [InlineData(SettingsWriteOutcomes.RefusedReadOnly)]
    [InlineData(SettingsWriteOutcomes.RefusedUnknownPath)]
    [InlineData(SettingsWriteOutcomes.WriteFailed)]
    public void ARefusal_IsTheWritersOwnMessage_AndIsARefusal(SettingsWriteOutcomes outcome)
    {
        Assert.Equal(("'x' must be between 1 and 1440.", true), SettingWriteNote_Formatter.Describe(outcome, "'x' must be between 1 and 1440."));
        Assert.Equal(($"Not applied ({outcome}).", true), SettingWriteNote_Formatter.Describe(outcome, null));
    }

    [Fact]
    public void AWriteThatThrew_SaysNothingWasSaved_AndCarriesTheReason()
    {
        var text = SettingWriteNote_Formatter.Describe_WriteThrew("'secrets.json' does not parse as a JSON object.");

        Assert.StartsWith("Nothing was saved", text);
        Assert.EndsWith("'secrets.json' does not parse as a JSON object.", text);
    }

    /// <summary>Walked over every outcome, so a seventh one is drawn the day it is added — or throws, never guesses.</summary>
    [Fact]
    public void EveryOutcome_IsARefusalExactlyWhenItTookNoEffect()
    {
        foreach (var outcome in Enum.GetValues<SettingsWriteOutcomes>())
            Assert.Equal(!Settings_Writer.Took_Effect(outcome), SettingWriteNote_Formatter.Describe(outcome, "m").IsRefusal);
    }
}
