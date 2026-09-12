using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;

public static class OrchestratorConfigProvider_Factory
{
    /// <summary>
    /// <paramref name="log"/> is optional so every existing call site keeps compiling, but the
    /// composition root passes the real one — see <see cref="OrchestratorConfigProviderModel"/>.
    /// </summary>
    public static IOrchestratorConfigProvider Create(ISupervisionPaths paths, IOrchestrationLog? log = null)
    {
        return new OrchestratorConfigProviderModel(paths, log);
    }
}
