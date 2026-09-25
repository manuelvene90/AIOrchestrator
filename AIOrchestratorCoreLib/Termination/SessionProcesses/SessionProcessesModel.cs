namespace AIOrchestratorCoreLib.Termination.SessionProcesses;

internal sealed class SessionProcessesModel : ISessionProcesses
{
    public bool Is_Alive(string pidFilePath)
    {
        return SessionTerminator.Is_SessionAlive(pidFilePath);
    }

    public DateTime? Read_StartedUtc_OrNull(string pidFilePath)
    {
        try
        {
            return File.Exists(pidFilePath) ? File.GetLastWriteTimeUtc(pidFilePath) : null;
        }
        catch
        {
            // Unreadable is "cannot tell", which the rescue reads as "not started since the refusal".
            return null;
        }
    }

    public void Kill_Tree(string pidFilePath)
    {
        SessionTerminator.Kill_SessionTree_ByPidFile(pidFilePath);
    }
}
