using System.Diagnostics;
using AIOrchestratorCoreLib.Spawning;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.WindowFocus;

namespace AIOrchestratorCoreLib.Termination;

/// <summary>
/// Kills spawned agent sessions via their PID files (written by the spawned shells themselves).
/// Kill is tree-wide (the shell AND the claude process it hosts) and is followed by CLOSING the
/// session's terminal window — Windows Terminal keeps a "press enter to restart" pane open after
/// a non-graceful exit. Used when the app closes (all sessions die with it) and when an
/// orchestration/implementer is closed (its elements die). PID files are deleted afterwards so
/// the watchdog treats the slot as cleanly stopped.
/// </summary>
public static class SessionTerminator
{
    /// <summary>The spawned session host is always a PowerShell shell — the pid-recycling guard.</summary>
    static readonly IReadOnlySet<string> SHELL_PROCESS_NAMES = new HashSet<string> { "powershell", "pwsh" };

    public static void Kill_SessionTree_ByPidFile(string pidFilePath)
    {
        try
        {
            if (!File.Exists(pidFilePath))
                return;

            var pidText = File.ReadAllText(pidFilePath).Trim();

            if (int.TryParse(pidText, out var pid))
            {
                var process = Process.GetProcessById(pid);

                // Windows RECYCLES pids: a stale pid file can point at somebody else's process by
                // now, and killing a whole tree by mistake is not a risk worth taking. Session
                // hosts are always the spawned shell.
                if (!SHELL_PROCESS_NAMES.Contains(process.ProcessName.ToLowerInvariant()))
                    return;

                process.Kill(entireProcessTree: true);

                // A pending prompt does not protect a process from Kill, but the window close
                // below must not race the tree teardown — wait (bounded) for actual death.
                process.WaitForExit(3000);
            }
        }
        catch
        {
            // Already dead, or a recycled pid we cannot touch — either way the slot is free.
        }
        finally
        {
            Close_TerminalWindow_BestEffort(pidFilePath);

            try
            {
                File.Delete(pidFilePath);
            }
            catch
            {
                // Best effort — a stale pid file just triggers one extra watchdog liveness check.
            }
        }
    }

    static void Close_TerminalWindow_BestEffort(string pidFilePath)
    {
        try
        {
            var titleFragment = Build_TitleFragment_OrNull(pidFilePath);

            if (titleFragment != null)
                TerminalWindow_Focuser.Try_Close_ByTitleFragment(titleFragment);
        }
        catch
        {
            // A window left open is cosmetic; termination must never fail on it.
        }
    }

    /// <summary>
    /// Derives the spawn title from the pid-file location (the layout is the contract). Public so the
    /// suite can assert it against what <see cref="Spawning.SpawnCommand_Builder"/> actually spawned:
    /// this function used to spell the title itself, and its copy drifted from the spawner's for the
    /// solo — "SOLO-1 · orch" here against "SOLO · orch" on the window, so that window was never
    /// found and never closed. Both sides read <see cref="SessionWindowTitle_Builder"/> now.
    /// </summary>
    public static string? Build_TitleFragment_OrNull(string pidFilePath)
    {
        var containingFolder = Path.GetDirectoryName(pidFilePath);
        if (containingFolder == null)
            return null;

        var containingFolderName = Path.GetFileName(containingFolder);
        var fileName = Path.GetFileName(pidFilePath);

        if (containingFolderName == "general")
            return SessionWindowTitle_Builder.GENERAL_TITLE;

        if (fileName == ".supervisor.pid")
            return SessionWindowTitle_Builder.Build_ForSupervisor(containingFolderName);

        if (fileName == ".communicator.pid")
            return SessionWindowTitle_Builder.Build_ForCommunicator(containingFolderName);

        var orchFolderName = Path.GetFileName(Path.GetDirectoryName(containingFolder) ?? "");
        if (orchFolderName.Length == 0)
            return null;

        return SessionWindowTitle_Builder.Build_ForMember(containingFolderName, orchFolderName);
    }

    /// <summary>App shutdown: every session the orchestrator ever spawned dies with it.</summary>
    public static void Kill_AllSessions(ISupervisionPaths paths)
    {
        if (!Directory.Exists(paths.Root))
            return;

        foreach (var pidFile in Directory.EnumerateFiles(paths.Root, "*.pid", SearchOption.AllDirectories))
            Kill_SessionTree_ByPidFile(pidFile);
    }

    /// <summary>Closing an orchestration closes ALL its elements: supervisor + every implementer.</summary>
    public static void Kill_OrchestrationSessions(ISupervisionPaths paths, string orchId)
    {
        var orchFolder = paths.Get_OrchestrationFolder(orchId);

        if (!Directory.Exists(orchFolder))
            return;

        foreach (var pidFile in Directory.EnumerateFiles(orchFolder, "*.pid", SearchOption.AllDirectories))
            Kill_SessionTree_ByPidFile(pidFile);
    }
}
