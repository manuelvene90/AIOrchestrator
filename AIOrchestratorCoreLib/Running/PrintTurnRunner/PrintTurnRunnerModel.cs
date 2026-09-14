using System.Diagnostics;
using System.Text;
using AIOrchestratorCoreLib.Running.ClaudeInvocation;
using AIOrchestratorCoreLib.Running.SessionSandbox;
using AIOrchestratorCoreLib.Running.TurnLiveness;
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
        ITurnSilenceBrake? brake,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startedUtc = DateTime.UtcNow;

        var invocation = _sandbox.Wrap(_invocation);

        using var process = Process.Start(Build_StartInfo(invocation, arguments, workingDirectory, environment))
            ?? throw new Exception($"Process.Start returned null for '{invocation.Executable}'");

        // READ AS IT ARRIVES, NOT AT EXIT, so the silence brake can see output go by. Chunks rather
        // than lines: the text handed to the parser must be byte-for-byte what the process wrote, and
        // a line reader would normalise the newlines. The stamp is shared by both pipes — the stream
        // brake counts stderr as a sign of life too, and so does this one.
        long lastOutputTicks = 0;
        var stdoutTask = Drain_Async(process.StandardOutput, () => Interlocked.Exchange(ref lastOutputTicks, DateTime.UtcNow.Ticks));
        var stderrTask = Drain_Async(process.StandardError, () => Interlocked.Exchange(ref lastOutputTicks, DateTime.UtcNow.Ticks));

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
        string? brakeKill = null;

        try
        {
            if (brake == null)
                await process.WaitForExitAsync(timeoutSource.Token);
            else
                brakeKill = await Wait_UnderBrake_OrKillLine_Async(process, brake, startedUtc, () => Interlocked.Read(ref lastOutputTicks), timeoutSource.Token);

            if (brakeKill != null)
            {
                // KILLED LIKE A TIMEOUT, BECAUSE IT IS ONE — same tree kill, same exit -1, and a turn
                // that had been working before it went silent earns the same closing turn. Only the
                // reason differs, and the result carries it.
                timedOut = true;
                Kill_Tree_BestEffort(process);
                process.WaitForExit();
            }
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
        var result = TurnResult_Parser.Parse_Stream(timedOut ? -1 : process.ExitCode, timedOut, stdout, stderr, stopwatch.Elapsed);

        return brakeKill == null ? result : TurnResult_Factory.CreateFrom_BrakeKill(result, brakeKill);
    }

    /// <summary>
    /// Waits for the process to exit, asking the brake every <see cref="ITurnSilenceBrake.PollInterval"/>
    /// whether the turn is still alive. Returns the brake's kill line, or null when the process exited
    /// on its own. The deadline and the app's shutdown both arrive through <paramref name="token"/>
    /// and leave as the same <see cref="OperationCanceledException"/> they always did, so the caller's
    /// reading of which one it was is untouched.
    /// </summary>
    static async Task<string?> Wait_UnderBrake_OrKillLine_Async(
        Process process, ITurnSilenceBrake brake, DateTime startedUtc, Func<long> readLastOutputTicks, CancellationToken token)
    {
        while (true)
        {
            using var tick = CancellationTokenSource.CreateLinkedTokenSource(token);
            tick.CancelAfter(brake.PollInterval);

            try
            {
                await process.WaitForExitAsync(tick.Token);
                return null;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // Only the tick: time to look.
            }

            var lastOutputTicks = readLastOutputTicks();
            DateTime? lastOutputUtc = lastOutputTicks == 0 ? null : new DateTime(lastOutputTicks, DateTimeKind.Utc);

            var killLine = brake.Decide_Kill_OrNull(DateTime.UtcNow, startedUtc, lastOutputUtc, process.Id);

            if (killLine != null)
                return killLine;
        }
    }

    /// <summary>
    /// Everything the pipe carries, exactly as written, stamping each chunk as it lands. Ends when the
    /// process closes the pipe, which it does by exiting or by being killed.
    /// </summary>
    static async Task<string> Drain_Async(StreamReader reader, Action onChunk)
    {
        var text = new StringBuilder();
        var buffer = new char[8192];

        while (true)
        {
            var read = await reader.ReadAsync(buffer, CancellationToken.None);

            if (read == 0)
                return text.ToString();

            text.Append(buffer, 0, read);
            onChunk();
        }
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
