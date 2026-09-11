using System.Diagnostics;
using System.Text;

namespace ClaudeContract.Tests;

/// <summary>What one process invocation returned — the evidence every assertion here is made on.</summary>
public sealed class ProcessRun(int exitCode, string stdout, string stderr, TimeSpan elapsed, bool timedOut)
{
    public int ExitCode { get; } = exitCode;
    public string Stdout { get; } = stdout;
    public string Stderr { get; } = stderr;
    public TimeSpan Elapsed { get; } = elapsed;
    public bool TimedOut { get; } = timedOut;

    public string Describe()
    {
        return $"exit={ExitCode} timedOut={TimedOut} elapsed={Elapsed.TotalSeconds:F1}s\n--- stdout ---\n{Stdout}\n--- stderr ---\n{Stderr}";
    }
}

/// <summary>
/// Runs a CLI the way the bridge does: no shell, arguments as a list (no quoting), stdin either
/// closed or fed a prompt, stdout/stderr captured, a hard timeout that kills the process tree.
/// Every <c>CLAUDECODE*</c> / <c>CLAUDE_CODE_*</c> variable is scrubbed from the child: the suite
/// is often run from inside a Claude Code session and a nested session refuses to start.
/// </summary>
public static class Process_Runner
{
    public static ProcessRun Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string? stdin,
        IReadOnlyDictionary<string, string>? environment,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // All THREE, like the bridge's own runner. Windows defaults stdin to the console's OEM
            // code page, so without this a Live measurement of a prompt carrying non-ASCII — every
            // channel header has an em-dash — would be taken under an encoding production never
            // uses, and the suite would pin a contract the bridge does not exercise.
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        foreach (var key in startInfo.Environment.Keys.ToList())
        {
            if (key.StartsWith("CLAUDECODE", StringComparison.Ordinal) || key.StartsWith("CLAUDE_CODE_", StringComparison.Ordinal))
                startInfo.Environment.Remove(key);
        }

        if (environment != null)
        {
            foreach (var pair in environment)
                startInfo.Environment[pair.Key] = pair.Value;
        }

        var stopwatch = Stopwatch.StartNew();

        using var process = Process.Start(startInfo)
            ?? throw new Exception($"Process.Start returned null for '{executable}'");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (stdin != null)
            process.StandardInput.Write(stdin);

        process.StandardInput.Close();

        var timedOut = !process.WaitForExit((int)timeout.TotalMilliseconds);

        if (timedOut)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Exited between the timeout and the kill.
            }

            process.WaitForExit();
        }

        stopwatch.Stop();

        return new ProcessRun(timedOut ? -1 : process.ExitCode, stdoutTask.Result, stderrTask.Result, stopwatch.Elapsed, timedOut);
    }
}
