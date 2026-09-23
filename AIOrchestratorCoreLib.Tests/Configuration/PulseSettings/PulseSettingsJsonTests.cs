using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Formatting;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration.PulseSettings;

/// <summary>
/// THE <c>pulse</c> BLOCK, THROUGH THE LOADER — same fixture and same reason as
/// <c>PhoneSettingsJsonTests</c>. The expected lists are SPELLED OUT rather than read back from the
/// preset files, for the reason <c>PresetProbeTests</c> gives: deriving them would assert that a
/// file equals itself.
/// </summary>
public class PulseSettingsJsonTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public PulseSettingsJsonTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-pulse-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A MACHINE THAT SAYS NOTHING GETS classic, and classic is master's pulse: the compact task count
    /// first (owner, 2026-09-23 — it replaced `merged`), then the supervisor, the model/effort reading
    /// riding the rows, and the hold toggle is on the receipt rather than the bar. General gets no
    /// buttons at all.
    /// </summary>
    [Fact]
    public void WithNoConfigFileAtAll_ThePulseBlockIsClassics()
    {
        var pulse = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Pulse;

        Assert.Equal(
            [PulseField_Names.PROGRESS, PulseField_Names.SUPERVISOR, PulseField_Names.MEMBERS, PulseField_Names.MODEL_EFFORT, PulseField_Names.UPDATED],
            pulse.Fields);
        Assert.False(pulse.HoldToggle);
        Assert.Equal(UnchangedFor_Formatter.STEP_MINUTES, pulse.StepMinutes);
        Assert.Equal(["screen", "show", "merge", "test", "pc", "close", "pause", "progress"], pulse.Buttons);
        Assert.Empty(pulse.GeneralButtons);
    }

    /// <summary>
    /// Quiet states nothing in this block, so every row is the catalogue's own — the fork's seven
    /// fields, the toggle on the bar, and both of the fork's button bars.
    /// </summary>
    [Fact]
    public void UnderTheQuietPreset_ThePulseBlockIsTheCataloguesDefaults()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"preset":"quiet"}""");

        var pulse = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Pulse;

        Assert.Equal(
            [
                PulseField_Names.WAITING_ON_YOU, PulseField_Names.SUPERVISOR, PulseField_Names.MEMBERS, PulseField_Names.CLOSED_COUNT,
                PulseField_Names.LAST_EVENT, PulseField_Names.MERGED, PulseField_Names.UPDATED,
            ],
            pulse.Fields);
        Assert.True(pulse.HoldToggle);
        Assert.Equal(UnchangedFor_Formatter.STEP_MINUTES, pulse.StepMinutes);
        Assert.Equal(["pending", "left", "tail sup", "limits", "merge", "close"], pulse.Buttons);
        Assert.Equal(TopicCommandButtons.GeneralCommands, pulse.GeneralButtons);
    }

    /// <summary>config.json beats the preset — the third rung, for a list and for a bool.</summary>
    [Fact]
    public void AValueInConfigJson_BeatsTheNamedPreset()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"pulse":{"holdToggle":true,"fields":["updated"]},"general":{"buttons":["summary"]}}""");

        var pulse = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Pulse;

        Assert.True(pulse.HoldToggle);
        Assert.Equal(["updated"], pulse.Fields);
        Assert.Equal(["summary"], pulse.GeneralButtons);
    }

    /// <summary>
    /// AN OUT-OF-RANGE STEP COSTS THE STEP ITS DEFAULT, NEVER THE LOAD. 0 is not "off" here — the
    /// step is a divisor on the pulse's "unchanged for" reading — and 61 is past the catalogue's
    /// ceiling; both fall through to the shipped five.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void AnOutOfRangeStepMinutes_FallsToTheDefault_AndDoesNotThrow(int stepMinutes)
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"pulse":{"stepMinutes":""" + stepMinutes + "}}");

        Assert.Equal(UnchangedFor_Formatter.STEP_MINUTES, OrchestratorConfig_Loader.Load_OrEmpty(_paths).Pulse.StepMinutes);
    }

    /// <summary>
    /// THE VALIDATORS ARE WHAT MAKE A TYPO COST ONE KEY. Before they existed a misspelled field or verb
    /// was accepted as written and reached the builders; now the definition refuses the list and the
    /// resolver falls to the layer below — which on an untouched machine is classic's list.
    /// </summary>
    [Fact]
    public void AMisspelledFieldOrVerb_CostsThatListItsPresetValue_NotTheLoad()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"pulse":{"fields":["supervisor","suprvisor"],"buttons":["tale sup"]}}""");

        var pulse = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Pulse;

        Assert.Equal(
            [PulseField_Names.PROGRESS, PulseField_Names.SUPERVISOR, PulseField_Names.MEMBERS, PulseField_Names.MODEL_EFFORT, PulseField_Names.UPDATED],
            pulse.Fields);
        Assert.Equal(["screen", "show", "merge", "test", "pc", "close", "pause", "progress"], pulse.Buttons);
    }
}
