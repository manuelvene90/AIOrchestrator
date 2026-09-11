using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Configuration;

public class OrchestratorConfigProviderTests : IDisposable
{
    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly IOrchestratorConfigProvider _provider;

    public OrchestratorConfigProviderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-provider-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        _provider = OrchestratorConfigProvider_Factory.Create(_paths);
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Get_Current_NoConfigFile_ReturnsEmptyConfig()
    {
        Assert.Empty(_provider.Get_Current().Repos);
    }

    [Fact]
    public void Get_Current_ConfigWrittenAtRuntime_IsPickedUp_TheGeneralSupervisorSeedsIt()
    {
        var before = _provider.Get_Current();
        Assert.Empty(before.Repos);

        File.WriteAllText(_paths.ConfigFile, """{"repos":[{"name":"CRM","path":"C:\\somewhere"}]}""");
        File.SetLastWriteTimeUtc(_paths.ConfigFile, DateTime.UtcNow.AddSeconds(2));

        var after = _provider.Get_Current();

        Assert.Single(after.Repos);
        Assert.Equal("CRM", after.Repos[0].Name);
    }

    [Fact]
    public void Get_Current_UnchangedFiles_ReturnsSameInstance_ReferenceEqualityIsTheChangeCheck()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[{"name":"CRM","path":"C:\\somewhere"}]}""");

        var first = _provider.Get_Current();
        var second = _provider.Get_Current();

        Assert.Same(first, second);
    }

    [Fact]
    public void Get_Current_FileChanged_ReturnsNewInstance()
    {
        File.WriteAllText(_paths.ConfigFile, """{"repos":[{"name":"CRM","path":"C:\\somewhere"}]}""");
        var first = _provider.Get_Current();

        File.WriteAllText(_paths.ConfigFile, """{"repos":[{"name":"CRM","path":"C:\\somewhere"},{"name":"Arb Studio","path":"C:\\arb"}]}""");
        File.SetLastWriteTimeUtc(_paths.ConfigFile, DateTime.UtcNow.AddSeconds(2));

        var second = _provider.Get_Current();

        Assert.NotSame(first, second);
        Assert.Equal(2, second.Repos.Count);
    }
}
