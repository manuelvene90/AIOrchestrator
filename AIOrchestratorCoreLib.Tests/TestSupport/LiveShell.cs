using System.Diagnostics;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.TestSupport;

/// <summary>
/// A PowerShell shell that is actually alive, for fixtures that need a pid the watchdog will believe
/// (it only trusts a pid whose process is one of those shells, because Windows recycles pids).
///
/// <para>
/// A machine with neither <c>powershell</c> nor <c>pwsh</c> cannot run such a test. It SKIPS rather
/// than throws, and that is decision 20 said in xUnit's own vocabulary rather than against it: the
/// requirement is to refuse to run and say WHY, never to pass on a pid that meant nothing. A throw
/// satisfies the first half and breaks the second — it reports a defect where there is none, and a
/// red that is always red is a red nobody reads. The repo already states this pattern in
/// <c>RequiresChannelToolFactAttribute</c>; this is the same shape for the same reason.
/// </para>
///
/// <para>
/// Probed ONCE per test run and cached: the probe starts a shell that exits immediately, so the cost
/// is one process, and a machine does not grow a PowerShell halfway through a suite.
/// </para>
/// </summary>
public static class LiveShell
{
    /// <summary>Null when a shell could be started; otherwise the sentence a skipped test shows.</summary>
    public static readonly string? Unrunnable_Reason = Probe();

    static string? Probe()
    {
        foreach (var shell in new[] { "powershell", "pwsh" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo(shell, "-NoProfile -Command exit")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                })!;

                process.WaitForExit(milliseconds: 10_000);
                return null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }

        return "Neither powershell nor pwsh could be started — this test needs a live shell pid and cannot run here.";
    }
}

/// <summary>Runs only where <see cref="LiveShell"/> found a shell to start.</summary>
public sealed class RequiresLiveShellFactAttribute : FactAttribute
{
    public RequiresLiveShellFactAttribute()
    {
        if (LiveShell.Unrunnable_Reason != null)
            Skip = LiveShell.Unrunnable_Reason;
    }
}
