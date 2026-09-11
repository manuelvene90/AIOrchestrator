using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.PrintTurnRunner;
using AIOrchestratorCoreLib.Running.SessionSandbox;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.TurnExecutor;

public static class TurnExecutor_Factory
{
    public static ITurnExecutor Create_Print(ISupervisionPaths paths, IPrintTurnRunner turnRunner)
    {
        return new PrintTurnExecutorModel(paths, turnRunner);
    }

    /// <summary>
    /// <paramref name="fallback"/> is the rung below on <see cref="RunnerFallback_Ladder"/> — the
    /// print executor in production. It is a parameter rather than something this factory looks up
    /// so that the ladder stays a decision made once, in the open, by whoever wires the app.
    /// </summary>
    public static ITurnExecutor Create_Stream(
        ISupervisionPaths paths,
        IClaudeInvocation invocation,
        ITurnExecutor? fallback,
        IOrchestrationLog log,
        IOrchestratorConfigProvider configProvider,
        ISessionSandbox? sandbox = null)
    {
        return new StreamTurnExecutorModel(paths, invocation, fallback, log, configProvider, sandbox ?? SessionSandbox_Factory.Create_None());
    }

    /// <summary>
    /// The production set: both bridge-driven transports, with the stream's fallback wired to the
    /// print one. One call so the two hosts and the tests cannot wire a different ladder from each
    /// other.
    ///
    /// <para>
    /// ONE SANDBOX FOR BOTH, built here and nowhere else. A session that falls back from the stream
    /// transport to the print one must not thereby escape its memory ceiling — that is exactly the
    /// session already having trouble — and two sandboxes would also mean two copies of the
    /// "systemd-run is not usable here" line for one host.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ITurnExecutor> Create_All(
        ISupervisionPaths paths,
        IClaudeInvocation invocation,
        IOrchestrationLog log,
        IOrchestratorConfigProvider configProvider,
        ISessionSandbox? sandbox = null)
    {
        var resolved = sandbox ?? SessionSandbox_Factory.Create_ForThisMachine(configProvider, log);
        var print = Create_Print(paths, PrintTurnRunner_Factory.Create(invocation, resolved));

        return [print, Create_Stream(paths, invocation, print, log, configProvider, resolved)];
    }
}
