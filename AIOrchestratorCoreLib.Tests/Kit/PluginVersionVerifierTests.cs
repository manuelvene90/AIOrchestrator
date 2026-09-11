using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.Kit.PluginGate;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// The verdict, and the sentence a human is shown for it. The message assertions are not cosmetic:
/// "expected / found / where / what to run" is the whole difference between this check and the
/// "plugin mismatch" that would send someone hunting through four copies again.
/// </summary>
public class PluginVersionVerifierTests
{
    [Fact]
    public void TheRightVersion_Enabled_Passes_AndSaysNothing()
    {
        var reading = new InstalledPluginReading("1.0.0", "/cache/aiorch/1.0.0", enabled: true, null);

        var verdict = PluginVersion_Verifier.Decide(reading, "1.0.0");

        Assert.Equal(PluginVerdicts.Ok, verdict);
        Assert.Null(PluginVersion_Verifier.Describe(verdict, reading, "1.0.0", KitPlugin.ID));
    }

    [Fact]
    public void AWrongVersion_NamesExpected_Found_Where_AndTheCommandThatFixesIt()
    {
        var reading = new InstalledPluginReading("0.9.2", "/cache/aiorch/0.9.2", enabled: true, null);

        var verdict = PluginVersion_Verifier.Decide(reading, "1.0.0");
        var message = PluginVersion_Verifier.Describe(verdict, reading, "1.0.0", KitPlugin.ID);

        Assert.Equal(PluginVerdicts.VersionMismatch, verdict);
        Assert.Contains("1.0.0", message);
        Assert.Contains("0.9.2", message);
        Assert.Contains("/cache/aiorch/0.9.2", message);
        Assert.Contains(KitPlugin.UPDATE_COMMAND, message);
    }

    [Fact]
    public void NotInstalledAtAll_SaysSo_AndSaysHowToInstallIt()
    {
        var reading = new InstalledPluginReading(null, null, enabled: false, null);

        var verdict = PluginVersion_Verifier.Decide(reading, "1.0.0");
        var message = PluginVersion_Verifier.Describe(verdict, reading, "1.0.0", KitPlugin.ID);

        Assert.Equal(PluginVerdicts.NotInstalled, verdict);
        Assert.Contains(KitPlugin.INSTALL_COMMAND, message);
    }

    [Fact]
    public void TheRightVersion_ButDisabled_IsRefused_BecauseSessionsWouldLoadNothing()
    {
        var reading = new InstalledPluginReading("1.0.0", "/cache", enabled: false, null);

        var verdict = PluginVersion_Verifier.Decide(reading, "1.0.0");

        Assert.Equal(PluginVerdicts.Disabled, verdict);
        Assert.Contains("DISABLED", PluginVersion_Verifier.Describe(verdict, reading, "1.0.0", KitPlugin.ID));
    }

    [Fact]
    public void AWrongVersionThatIsAlsoDisabled_IsReportedAsTheVersion_BecauseThatIsWhatTheyMustFix()
    {
        var reading = new InstalledPluginReading("0.9.2", "/cache", enabled: false, null);

        Assert.Equal(PluginVerdicts.VersionMismatch, PluginVersion_Verifier.Decide(reading, "1.0.0"));
    }

    [Fact]
    public void AnUnreadableRecord_NeverPasses_AndSaysItCannotTell()
    {
        var reading = new InstalledPluginReading(null, null, false, "installed_plugins.json could not be read: bad json");

        var verdict = PluginVersion_Verifier.Decide(reading, "1.0.0");
        var message = PluginVersion_Verifier.Describe(verdict, reading, "1.0.0", KitPlugin.ID);

        Assert.Equal(PluginVerdicts.Unreadable, verdict);
        Assert.Contains("CANNOT TELL", message);
        Assert.Contains("bad json", message);
    }

    [Fact]
    public void TheGate_LetsSessionsThroughOnlyOnOk_OrWhenTheCheckNeverRan()
    {
        var gate = PluginGate_Factory.Create();
        Assert.True(gate.Spawning_Allowed);          // never checked — allowed, and logged as such

        gate.Record(PluginVerdicts.Ok, null);
        Assert.True(gate.Spawning_Allowed);

        gate.Record(PluginVerdicts.VersionMismatch, "wrong version");
        Assert.False(gate.Spawning_Allowed);
        Assert.Equal("wrong version", gate.Refusal);

        gate.Record(PluginVerdicts.NotInstalled, "absent");
        Assert.False(gate.Spawning_Allowed);

        gate.Record(PluginVerdicts.Disabled, "off");
        Assert.False(gate.Spawning_Allowed);

        gate.Record(PluginVerdicts.Unreadable, "cannot tell");
        Assert.False(gate.Spawning_Allowed);
    }
}
