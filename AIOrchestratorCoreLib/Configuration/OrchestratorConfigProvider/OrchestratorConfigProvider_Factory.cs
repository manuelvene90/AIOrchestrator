using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;

public static class OrchestratorConfigProvider_Factory
{
    public static IOrchestratorConfigProvider Create(ISupervisionPaths paths)
    {
        return new OrchestratorConfigProviderModel(paths);
    }
}
