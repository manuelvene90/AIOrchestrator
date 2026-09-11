using AIOrchestratorCoreLib.Running.TurnResult;

namespace AIOrchestratorCoreLib.Running.PrintTurnRunner;

/// <summary>
/// Runs ONE print turn as a process and returns what came back. The process seam of the print
/// runner: the dispatcher decides what to run and when, this decides how a process is started,
/// fed, timed and killed.
/// </summary>
public interface IPrintTurnRunner
{
    /// <summary>
    /// <paramref name="stdinPrompt"/> null means stdin is closed at once (the prompt is positional);
    /// otherwise the text is written and stdin closed. A turn that outlives <paramref name="timeout"/>
    /// — or the token — is killed as a tree and comes back with <c>TimedOut</c> set.
    /// </summary>
    Task<ITurnResult> Run_Async(
        IReadOnlyList<string> arguments,
        string? stdinPrompt,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
