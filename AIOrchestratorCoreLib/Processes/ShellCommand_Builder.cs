using System.Diagnostics;

namespace AIOrchestratorCoreLib.Processes;

/// <summary>
/// The ONE place that knows which shell runs a configured command line. Two callers used to
/// hard-code <c>cmd.exe /c</c> — the voice transcriber and the translator — which is correct on
/// Windows (PATH and .cmd shims resolve through cmd) and a guaranteed failure everywhere else,
/// where there is no cmd.exe to start. The shell is chosen at RUNTIME from the OS the process is
/// on, never by a compile-time switch, so one binary serves every OS the bridge runs on.
///
/// Callers add their own redirections on the returned start info; this only decides the shell
/// and hands the command line to it unchanged.
/// </summary>
public static class ShellCommand_Builder
{
    public const string WINDOWS_SHELL = "cmd.exe";
    public const string POSIX_SHELL = "/bin/sh";

    public static ProcessStartInfo Build_StartInfo(string commandLine)
    {
        return Build_StartInfo(commandLine, OperatingSystem.IsWindows());
    }

    /// <summary>
    /// The OS is a parameter so the choice is testable on any machine: the suite cannot run on
    /// three operating systems at once, but it can assert what each of them would be handed.
    /// </summary>
    public static ProcessStartInfo Build_StartInfo(string commandLine, bool isWindows)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            throw new ArgumentException("A shell command line must be non-empty", nameof(commandLine));

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (isWindows)
        {
            // Arguments as ONE string, on purpose: cmd's own parsing of what follows /c is what
            // makes `whisper {input}` and `claude -p` resolve the way they do from a prompt, and
            // re-quoting it through ArgumentList would change that.
            startInfo.FileName = WINDOWS_SHELL;
            startInfo.Arguments = $"/c {commandLine}";
        }
        else
        {
            // ArgumentList, not Arguments: the command line reaches sh -c as a single argv entry
            // with no re-quoting in between, so the quotes the caller wrote around {input} arrive
            // exactly as written.
            startInfo.FileName = POSIX_SHELL;
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(commandLine);
        }

        return startInfo;
    }
}
