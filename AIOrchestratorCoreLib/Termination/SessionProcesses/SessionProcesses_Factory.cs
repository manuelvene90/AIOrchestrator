namespace AIOrchestratorCoreLib.Termination.SessionProcesses;

public static class SessionProcesses_Factory
{
    /// <summary>The real process table of this machine, through <see cref="SessionTerminator"/>.</summary>
    public static ISessionProcesses Create_ForThisHost()
    {
        return new SessionProcessesModel();
    }
}
