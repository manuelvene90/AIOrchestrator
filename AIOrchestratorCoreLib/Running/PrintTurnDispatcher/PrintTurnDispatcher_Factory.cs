using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.TurnExecutor;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.PrintTurnDispatcher;

public static class PrintTurnDispatcher_Factory
{
    /// <summary>A failed attempt waits this long before the same turn is tried again.</summary>
    public static readonly TimeSpan DEFAULT_RETRY_BACKOFF = TimeSpan.FromSeconds(60);

    /// <summary>The production shape: both bridge-driven transports, wired from one `claude` invocation.</summary>
    public static IPrintTurnDispatcher Create(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IOrchestratorConfigProvider configProvider,
        IClaudeInvocation invocation,
        IOrchestrationLog log)
    {
        return Create(paths, store, configProvider, TurnExecutor_Factory.Create_All(paths, invocation, log, configProvider), log, DEFAULT_RETRY_BACKOFF);
    }

    /// <summary>The retry backoff is a parameter so the tests can exercise three attempts in seconds.</summary>
    public static IPrintTurnDispatcher Create(
        ISupervisionPaths paths,
        IOrchestrationSessionStore store,
        IOrchestratorConfigProvider configProvider,
        IReadOnlyList<ITurnExecutor> executors,
        IOrchestrationLog log,
        TimeSpan retryBackoff)
    {
        return new PrintTurnDispatcherModel(paths, store, configProvider, executors, log, retryBackoff);
    }
}
