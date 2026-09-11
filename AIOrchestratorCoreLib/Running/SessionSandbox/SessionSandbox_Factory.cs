using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;

namespace AIOrchestratorCoreLib.Running.SessionSandbox;

public static class SessionSandbox_Factory
{
    /// <summary>The production shape: the configured ceiling, enforced where this machine can enforce it.</summary>
    public static ISessionSandbox Create_ForThisMachine(IOrchestratorConfigProvider configProvider, IOrchestrationLog log)
    {
        return new SessionSandboxModel(configProvider, SystemdRun_Probe.Is_Available, log);
    }

    /// <summary>
    /// The same model with the machine question answered by the caller — so both branches of a
    /// decision that depends on the OS are asserted on ONE machine, which is the rule the whole
    /// CoreLib follows for OS-dependent behaviour.
    /// </summary>
    public static ISessionSandbox Create_WithProbe(IOrchestratorConfigProvider configProvider, IOrchestrationLog log, Func<bool> isSystemdRunAvailable)
    {
        return new SessionSandboxModel(configProvider, isSystemdRunAvailable, log);
    }

    /// <summary>No isolation at all — what a caller with no config provider gets, and what every OS but Linux does.</summary>
    public static ISessionSandbox Create_None()
    {
        return new NoSessionSandboxModel();
    }
}
