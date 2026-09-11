using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.SessionSandbox;

namespace AIOrchestratorCoreLib.Running.PrintTurnRunner;

public static class PrintTurnRunner_Factory
{
    /// <summary>
    /// <paramref name="sandbox"/> omitted means NO isolation — the shape a unit test of the process
    /// seam itself wants. Every production path goes through
    /// <see cref="TurnExecutor.TurnExecutor_Factory.Create_All"/>, which builds one sandbox for both
    /// transports so they cannot disagree about the ceiling.
    /// </summary>
    public static IPrintTurnRunner Create(IClaudeInvocation invocation, ISessionSandbox? sandbox = null)
    {
        return new PrintTurnRunnerModel(invocation, sandbox ?? SessionSandbox_Factory.Create_None());
    }
}
