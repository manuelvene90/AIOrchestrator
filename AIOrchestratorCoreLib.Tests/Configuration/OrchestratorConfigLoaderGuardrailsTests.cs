using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.GuardrailSettings;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfig;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

/// <summary>
/// The additive-save contract (config.json/secrets.json survive a save from a build that does not
/// know every key in them) and the guardrail defaulting rules, exercised against real files on
/// disk through the same temp-root + IDisposable pattern <c>OrchestratorConfigLoaderTests</c> uses.
/// </summary>
public class OrchestratorConfigLoaderGuardrailsTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;

    public OrchestratorConfigLoaderGuardrailsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-guardrail-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    /// <summary>
    /// THE LOAD-BEARING ONE. <c>Save</c> used to rebuild config.json from the fields this build
    /// knows and write that over the top, so any key a NEWER build had written — one this build
    /// has never heard of — was silently deleted by the very next save. One toggle from the phone
    /// was enough to erase it. A round trip through Load + Save must leave an unknown
    /// key exactly as it was.
    /// </summary>
    [Fact]
    public void Save_LeavesAnUnknownConfigKey_UntouchedAndUnchanged()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"supervisorModel":"opus","somethingANewerBuildWrote":{"a":1}}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);
        OrchestratorConfig_Loader.Save(config, _paths);

        var root = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile))!.AsObject();

        Assert.NotNull(root["somethingANewerBuildWrote"]);
        Assert.Equal(1, root["somethingANewerBuildWrote"]!["a"]!.GetValue<int>());
    }

    /// <summary>The same guarantee for secrets.json — it is saved through the identical read-edit-write path.</summary>
    [Fact]
    public void Save_LeavesAnUnknownSecretsKey_UntouchedAndUnchanged()
    {
        File.WriteAllText(_paths.SecretsFile, """{"telegramBotToken":"old-token","somethingANewerBuildWrote":{"a":1}}""");

        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);
        OrchestratorConfig_Loader.Save(config, _paths);

        var root = JsonNode.Parse(File.ReadAllText(_paths.SecretsFile))!.AsObject();

        Assert.NotNull(root["somethingANewerBuildWrote"]);
        Assert.Equal(1, root["somethingANewerBuildWrote"]!["a"]!.GetValue<int>());
    }

    [Fact]
    public void GuardrailKeys_AreReadFromConfigJson()
    {
        File.WriteAllText(_paths.ConfigFile, """
            {
              "repos": [],
              "highRiskPatterns": ["custom1", "custom2"],
              "highRiskCodeExpiryMinutes": 15,
              "dispatchPauseThresholdPercent": 80,
              "buttonExpiryMinutes": 60
            }
            """);

        var guardrails = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Guardrails;

        Assert.Equal(["custom1", "custom2"], guardrails.HighRiskPatterns);
        Assert.Equal(15, guardrails.HighRiskCodeExpiryMinutes);
        Assert.Equal(80.0, guardrails.DispatchPauseThresholdPercent);
        Assert.Equal(60, guardrails.ButtonExpiryMinutes);
    }

    /// <summary>
    /// MISSING and EXPLICITLY EMPTY are different answers on purpose: missing means the owner
    /// never said anything and gets the guarded defaults, empty means they said "nothing is high
    /// risk" and that choice is theirs to make. Collapsing the two would make the guard either
    /// impossible to turn off (empty forced back to defaults) or impossible to keep (missing read
    /// as "off").
    /// </summary>
    [Fact]
    public void AnAbsentHighRiskPatternsKey_YieldsTheDefaultList()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[]}""");

        var guardrails = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Guardrails;

        Assert.Equal(GuardrailSettings_Factory.DEFAULT_HIGH_RISK_PATTERNS, guardrails.HighRiskPatterns);
    }

    [Fact]
    public void AnExplicitlyEmptyHighRiskPatternsArray_YieldsAnEmptyList()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"highRiskPatterns":[]}""");

        var guardrails = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Guardrails;

        Assert.Empty(guardrails.HighRiskPatterns);
    }

    /// <summary>
    /// A zero or negative duration would DISABLE the very guard it configures — an expiry of 0
    /// refuses every read-back code outright. Falling back to the default is the only reading of a
    /// typo that cannot be worse than the owner having said nothing.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void AZeroOrNegativeHighRiskCodeExpiry_FallsBackToTheDefault(int value)
    {
        File.WriteAllText(_paths.ConfigFile, $$"""{"repos":[],"highRiskCodeExpiryMinutes":{{value}}}""");

        var guardrails = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Guardrails;

        Assert.Equal(GuardrailSettings_Factory.DEFAULT_HIGH_RISK_CODE_EXPIRY_MINUTES, guardrails.HighRiskCodeExpiryMinutes);
    }

    /// <summary>A zero button expiry would make every decision button instantly stale — same rule, different field.</summary>
    [Fact]
    public void AZeroButtonExpiryMinutes_FallsBackToTheDefault()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"buttonExpiryMinutes":0}""");

        var guardrails = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Guardrails;

        Assert.Equal(GuardrailSettings_Factory.DEFAULT_BUTTON_EXPIRY_MINUTES, guardrails.ButtonExpiryMinutes);
    }

    /// <summary>
    /// A non-numeric value is a typo, not a choice — the only field this build parses with a
    /// number that also tolerates a non-numeric JSON value without throwing is the threshold
    /// percent (its reader is wrapped for exactly this). The factory's default is the only safe
    /// reading of a typo it cannot interpret.
    /// </summary>
    [Fact]
    public void ANonNumericDispatchPauseThresholdPercent_FallsBackToTheDefault()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[],"dispatchPauseThresholdPercent":"ninety-five"}""");

        var guardrails = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Guardrails;

        Assert.Equal(GuardrailSettings_Factory.DEFAULT_DISPATCH_PAUSE_THRESHOLD_PERCENT, guardrails.DispatchPauseThresholdPercent);
    }

    /// <summary>
    /// Out of [0, 100] is nonsensical for a percentage threshold — a pause that never fires (over
    /// 100) or that fires permanently (zero or negative) is not a value the owner can have meant.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(150)]
    public void AnOutOfRangeDispatchPauseThresholdPercent_FallsBackToTheDefault(double value)
    {
        File.WriteAllText(_paths.ConfigFile, $$"""{"repos":[],"dispatchPauseThresholdPercent":{{value}}}""");

        var guardrails = OrchestratorConfig_Loader.Load_OrEmpty(_paths).Guardrails;

        Assert.Equal(GuardrailSettings_Factory.DEFAULT_DISPATCH_PAUSE_THRESHOLD_PERCENT, guardrails.DispatchPauseThresholdPercent);
    }

    /// <summary>
    /// Refusing to save over a corrupt config.json would strand the owner with a broken file and
    /// no way to fix it short of editing it by hand — the app is their only remaining way back in,
    /// so a save must succeed and produce a fresh, valid file carrying the keys this build owns.
    /// </summary>
    [Fact]
    public void Save_OverACorruptConfigJson_StillSucceeds_AndWritesTheKnownKeys()
    {
        File.WriteAllText(_paths.ConfigFile, "{not json at all");

        var exception = Record.Exception(() => OrchestratorConfig_Loader.Save(OrchestratorConfig_Factory.Create_Empty(), _paths));

        Assert.Null(exception);

        var root = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile))!.AsObject();
        Assert.Equal("opus", root["supervisorModel"]!.GetValue<string>());
    }

    /// <summary>
    /// The guardrail keys have no UI and no command that changes them, so the only thing writing
    /// them back could do is materialise this build's defaults into the file as if the owner had
    /// chosen them — freezing a default that is meant to move when the app is updated. They are
    /// read, never owned, so a save must never introduce them.
    /// </summary>
    [Fact]
    public void Save_DoesNotWriteGuardrailKeys_WhenTheyWereAbsent()
    {
        var config = OrchestratorConfig_Loader.Load_OrEmpty(_paths);
        OrchestratorConfig_Loader.Save(config, _paths);

        var root = JsonNode.Parse(File.ReadAllText(_paths.ConfigFile))!.AsObject();

        Assert.Null(root["highRiskPatterns"]);
        Assert.Null(root["highRiskCodeExpiryMinutes"]);
        Assert.Null(root["dispatchPauseThresholdPercent"]);
        Assert.Null(root["buttonExpiryMinutes"]);
    }
}
