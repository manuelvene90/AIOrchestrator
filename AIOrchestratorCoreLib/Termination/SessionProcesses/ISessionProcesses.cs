namespace AIOrchestratorCoreLib.Termination.SessionProcesses;

/// <summary>
/// Whether a spawned session's shell is running, and stopping it — by the pid file the shell wrote.
/// An interface so the limit rescue can be driven by a test without a process table (the engine
/// tests seed transcripts, never real sessions); production is <see cref="SessionTerminator"/>.
/// </summary>
public interface ISessionProcesses
{
    bool Is_Alive(string pidFilePath);

    /// <summary>
    /// When the running shell started — the pid file's write time, since the shell writes it once at
    /// spawn — or null when there is no pid file to read.
    /// </summary>
    DateTime? Read_StartedUtc_OrNull(string pidFilePath);

    /// <summary>Stops the shell and everything under it, closes its window, and deletes the pid file.</summary>
    void Kill_Tree(string pidFilePath);
}
