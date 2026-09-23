using AIOrchestratorCoreLib.Composition.OrchestratorServices;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Composition;

/// <summary>
/// The shared composition root builds the whole graph from a root alone — the WPF app and the
/// daemon both call exactly this, so what is pinned here is what both hosts get.
/// </summary>
public class OrchestratorServicesFactoryTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), $"aiorch-services-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void EveryService_IsBuilt_FromTheRootAlone_WithNoTelegramConfigured()
    {
        var paths = SupervisionPaths_Factory.Create(_root);

        var services = OrchestratorServices_Factory.Create(paths);

        Assert.Same(paths, services.Paths);
        Assert.NotNull(services.ConfigProvider);
        Assert.NotNull(services.Log);
        Assert.NotNull(services.Store);
        Assert.NotNull(services.Launcher);
        Assert.NotNull(services.Engine);

        // Built, and NOT started: composition binds no socket — each host decides when Run_Async begins (plan 04 Task 7).
        Assert.NotNull(services.SettingsWebHost);
        Assert.False(services.SettingsWebHost.IsListening);
        Assert.Null(services.SettingsWebHost.ListeningOn_OrNull);
        Assert.False(services.ConfigProvider.Get_Current().Is_TelegramConfigured());
    }
}
