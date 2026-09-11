using AIOrchestratorCoreLib.Composition.HostOptions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Composition;

public class HostOptionsFactoryTests
{
    const string PROFILE = "/home/owner";

    static string? No_Environment(string _) => null;

    [Fact]
    public void NoArgumentsAndNoEnvironment_MeansTheWpfAppsPaths()
    {
        var options = HostOptions_Factory.Create_FromArguments([], No_Environment, PROFILE);

        Assert.Equal(Path.Combine(PROFILE, ".claude", "supervision"), options.SupervisionRoot);
        Assert.Equal(Path.Combine(PROFILE, ".claude"), options.ClaudeHome);
    }

    [Fact]
    public void TheEnvironment_OverridesTheDefaults_TheWayAUnitFileOrAPlistConfiguresAService()
    {
        string? Environment(string name) => name switch
        {
            HostOptions_Factory.SUPERVISION_ROOT_ENV => "/srv/aiorch/supervision",
            HostOptions_Factory.CLAUDE_HOME_ENV => "/srv/aiorch/claude",
            _ => null,
        };

        var options = HostOptions_Factory.Create_FromArguments([], Environment, PROFILE);

        Assert.Equal("/srv/aiorch/supervision", options.SupervisionRoot);
        Assert.Equal("/srv/aiorch/claude", options.ClaudeHome);
    }

    [Fact]
    public void TheCommandLine_OverridesTheEnvironment()
    {
        string? Environment(string name) => "/from/env";

        var options = HostOptions_Factory.Create_FromArguments(
            ["--root", "/from/args/root", "--claude-home=/from/args/claude"], Environment, PROFILE);

        Assert.Equal("/from/args/root", options.SupervisionRoot);
        Assert.Equal("/from/args/claude", options.ClaudeHome);
    }

    [Fact]
    public void AnEmptyEnvironmentValue_IsAbsent_NotAnEmptyPath()
    {
        var options = HostOptions_Factory.Create_FromArguments([], _ => "  ", PROFILE);

        Assert.Equal(Path.Combine(PROFILE, ".claude", "supervision"), options.SupervisionRoot);
    }

    [Theory]
    [InlineData("--root")]
    [InlineData("--root=")]
    [InlineData("--claude-home")]
    public void AnOptionWithoutItsDirectory_IsAnError_NotASilentDefault(string dangling)
    {
        Assert.Throws<ArgumentException>(() => HostOptions_Factory.Create_FromArguments([dangling], No_Environment, PROFILE));
    }
}
