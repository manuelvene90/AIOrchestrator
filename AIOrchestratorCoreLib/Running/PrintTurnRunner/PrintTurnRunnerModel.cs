using System.Diagnostics;
using System.Text;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.SessionSandbox;
using AIOrchestratorCoreLib.Running.TurnResult;

namespace AIOrchestratorCoreLib.Running.PrintTurnRunner;

/// <summary>
/// Process.Start with an argument LIST (no shell, no quoting), UTF-8 both ways, stdin fed and
/// closed, stdout/stderr drained concurrently so a chatty turn cannot deadlock on a full pipe.
/// Every <c>CLAUDECODE*</c> / <c>CLAUDE_CODE_*</c> variable is scrubbed from the child: a bridge
/// started from inside a Claude Code session would otherwise spawn sessions that refuse to nest.
///
/// <para>
/// THE INVOCATION IS RESOLVED THROUGH <see cref="ISessionSandbox"/> AT EVERY START, never held
/// pre-wrapped: the ceiling is read live from config.json like every other runner setting, so an
/// operator raising it on the VPS applies to the next turn rather than to the next restart.
/// </para>
/// </summary>
internal sealed class PrintTurnRunnerModel(IClaudeInvocation invocation, ISessionSandbox sandbox) : IPrintTurnRunner
{
    readonly IClaudeInvocation _invocation = invocation;
    readonly ISessionSandbox _sandbox = sandbox;

    public async Task<ITurnResult> Run_Async(
        IReadOnlyList<string> arguments,
        string? stdinPrompt,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        var invocation = _sandbox.Wrap(_invocation);

        using var process = Process.Start(Build_StartInfo(invocation, arguments, workingDirectory, environment))
            ?? throw new Exception($"Process.Start returned null for '{invocation.Executable}'");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            if (stdinPrompt != null)
                await process.StandardInput.WriteAsync(stdinPrompt);
        }
        catch (IOException)
        {
            // The process exited before reading its prompt — the exit code will say why.
        }

        process.StandardInput.Close();

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var timedOut = false;
        var cancelled = false;

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            // THE TWO CANCELLATIONS ARE NOT THE SAME EVENT, and the token says which is which: the
            // linked source fires both for the per-turn timeout and for the app shutting down. Read
            // as one, closing the app while a turn runs was recorded as a TIMEOUT — a failed
            // attempt, a `turn_ended … timeout` entry in the member's channel, and three ordinary
            // restarts were enough to stall a session that had done nothing wrong. The process is
            // killed either way; only the report differs.
            cancelled = cancellationToken.IsCancellationRequested;
            timedOut = !cancelled;
            Kill_Tree_BestEffort(process);
            process.WaitForExit();
        }

        // Drained before anything is thrown: the readers complete once the process is gone, and an
        // abandoned one is an unobserved task.
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();

        if (cancelled)
            cancellationToken.ThrowIfCancellationRequested();

        // READ AS A STREAM, because that is what the command line now asks for (stage 19). The
        // reader falls back to the legacy single-document reading when there is no `result` line, so
        // a caller that still passes `--output-format json` — the runner's own unit tests do — is
        // parsed exactly as it always was.
        return TurnResult_Parser.Parse_Stream(timedOut ? -1 : process.ExitCode, timedOut, stdout, stderr, stopwatch.Elapsed);
    }

    static ProcessStartInfo Build_StartInfo(IClaudeInvocation invocation, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string> environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.Executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var leading in invocation.LeadingArguments)
            startInfo.ArgumentList.Add(leading);

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var key in startInfo.Environment.Keys.ToList())
        {
            if (key.StartsWith("CLAUDECODE", StringComparison.Ordinal) || key.StartsWith("CLAUDE_CODE_", StringComparison.Ordinal))
                startInfo.Environment.Remove(key);
        }

        foreach (var pair in environment)
            startInfo.Environment[pair.Key] = pair.Value;

        return startInfo;
    }

    static void Kill_Tree_BestEffort(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Exited between the timeout and the kill.
        }
    }
}
