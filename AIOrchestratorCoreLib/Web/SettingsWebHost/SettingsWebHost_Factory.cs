using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Web.SettingsWebHost;

public static class SettingsWebHost_Factory
{
    /// <summary>
    /// Built, not started — nothing is bound until a host calls <see cref="ISettingsWebHost.Run_Async"/>.
    /// <paramref name="configProvider"/> is the change signal for <c>web.token</c> (ruling P10): a new instance
    /// from <c>Get_Current()</c> means config.json or secrets.json changed, and the token is re-read from disk.
    /// </summary>
    public static ISettingsWebHost Create(ISupervisionPaths paths, IOrchestratorConfigProvider configProvider, IOrchestrationLog log)
    {
        return new SettingsWebHostModel(paths, configProvider, log);
    }
}
