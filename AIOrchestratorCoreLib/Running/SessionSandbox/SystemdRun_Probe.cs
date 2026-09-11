using System.Diagnostics;

namespace AIOrchestratorCoreLib.Running.SessionSandbox;

/// <summary>
/// ASKS THE MACHINE, ONCE, whether it can actually put a child in a memory-limited scope — and
/// caches the answer for the life of the host, because the answer cannot change while it runs and
/// probing per spawn would add a process start to every turn.
///
/// <para>
/// IT RUNS THE REAL COMMAND rather than looking for the binary on PATH. Three separate things have
/// to be true — <c>systemd-run</c> is installed, the per-user manager is reachable (a container, an
/// ssh session with no lingering user manager, or a bare <c>docker exec</c> has the binary and no
/// bus), and this kernel's cgroup v2 accepts <c>MemoryMax</c> — and only one of them is a file on
/// disk. Decision 20's rule applies to the probe as much as to a harness: a check that cannot
/// evaluate its predicate says so and falls back, it does not certify the absence of the thing it
/// was testing.
/// </para>
/// </summary>
public static class SystemdRun_Probe
{
    /// <summary>Long enough for a bus round trip on a loaded 8 GB VPS, short enough not to stall a startup.</summary>
    public const int PROBE_TIMEOUT_MILLISECONDS = 5_000;

    /// <summary>The command a working user manager runs in a scope without side effects.</summary>
    public const string PROBE_TARGET = "/bin/true";

    static readonly Lock LOCK = new();
    static bool? _answer;

    /// <summary>The cached answer, probing on the first call. Never throws: anything it cannot do reads as "no".</summary>
    public static bool Is_Available()
    {
        lock (LOCK)
        {
            _answer ??= Probe();
            return _answer.Value;
        }
    }

    /// <summary>Forgets the cached answer. For tests only — a host's machine does not change under it.</summary>
    public static void Forget_ForTests()
    {
        lock (LOCK)
            _answer = null;
    }

    /// <summary>
    /// The whole probe, on one line so a reader can see exactly what was asked:
    /// <c>systemd-run --user --scope --quiet -p MemoryMax=64M -- /bin/true</c>. The tiny ceiling is
    /// deliberate — it proves the property is accepted rather than merely tolerated.
    /// </summary>
    static bool Probe()
    {
        // Windows and macOS have no cgroups; asking would cost a failed process start per host.
        if (!OperatingSystem.IsLinux())
            return false;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = MemoryLimitedInvocation_Builder.SYSTEMD_RUN,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            foreach (var argument in new[] { "--user", "--scope", "--quiet", "-p", "MemoryMax=64M", "--", PROBE_TARGET })
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);

            if (process == null)
                return false;

            if (!process.WaitForExit(PROBE_TIMEOUT_MILLISECONDS))
            {
                Kill_BestEffort(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            // Not installed, no bus, no cgroup controller — all the same answer, and none of them
            // is a reason to refuse to start a session.
            return false;
        }
    }

    static void Kill_BestEffort(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // It exited between the wait and the kill.
        }
    }
}
