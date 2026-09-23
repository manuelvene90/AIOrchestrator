using AIOrchestratorCoreLib.Logging.OrchestrationLog;

namespace AIOrchestratorCoreLib.Bridge.CommandBars;

public static class CommandBars_Factory
{
    /// <summary>Nothing said yet; <paramref name="log"/> receives each refusal line, once.</summary>
    public static ICommandBars Create(IOrchestrationLog log)
    {
        return new CommandBarsModel(log);
    }
}
