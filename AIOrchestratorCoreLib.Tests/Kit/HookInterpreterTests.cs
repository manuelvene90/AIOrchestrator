using System.Diagnostics;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// DECISION 19 AS A HARNESS. The hooks still shell out to python3 for their JSON, and on a mixed
/// machine `python3` can be a native Windows interpreter that cannot see the paths msys bash hands
/// it — `open('/tmp/x')` throws on a file bash just wrote, silently inside a heredoc, and the hook
/// "ran" and did nothing. This test does what a hook does: bash writes a file at a bash path and
/// python3 must read it back. Where the interpreter is the wrong one, the test FAILS naming it,
/// instead of every hook test above it passing on an interpreter that never executed.
///
/// If no hook uses python3 any more, the check is moot and passes; the status line twin already
/// needs none (pinned next door).
/// </summary>
public class HookInterpreterTests
{
    [Fact]
    public void ThePython3TheHooksRun_CanReadAFileBashJustWrote()
    {
        var hooksUsingPython = Directory.EnumerateFiles(Find_HooksFolder_OrFail(), "*.sh")
            .Where(file => File.ReadLines(file).Any(line => !line.TrimStart().StartsWith('#') && line.Contains("python3")))
            .Select(Path.GetFileName)
            .ToList();

        if (hooksUsingPython.Count == 0)
            return;

        const string probe =
            "f=$(mktemp) && printf 'stage-two' > \"$f\" && python3 -c 'import sys; print(open(sys.argv[1]).read())' \"$f\"; rc=$?; rm -f \"$f\"; exit $rc";

        var startInfo = new ProcessStartInfo
        {
            FileName = Bash_Locator.Find_OrFail(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(probe);

        using var process = Process.Start(startInfo) ?? throw new Exception("could not start bash");
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        Assert.True(process.WaitForExit(60_000), "the python3 probe did not exit within 60 s");

        Assert.True(
            process.ExitCode == 0 && output == "stage-two",
            $"python3 could not read a file bash created at a bash path (exit {process.ExitCode}: {error}). "
            + $"These hooks hand python3 such paths and would silently do nothing: {string.Join(", ", hooksUsingPython)}. "
            + "Decision 19: the python3 on PATH must be the one that sees the shell's filesystem (msys/Homebrew/distro python), not a native Windows interpreter.");
    }

    static string Find_HooksFolder_OrFail()
    {
        var folder = AppContext.BaseDirectory;

        for (var depth = 0; depth < 8; depth++)
        {
            var candidate = Path.Combine(folder, "kit", "hooks");

            if (Directory.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(folder);

            if (parent == null)
                break;

            folder = parent.FullName;
        }

        throw new Exception($"kit/hooks was not found walking up from {AppContext.BaseDirectory}");
    }
}
