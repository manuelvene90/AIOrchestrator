using System.Diagnostics;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Termination;

/// <summary>
/// Kills spawned agent sessions via their PID files (written by the spawned shells themselves).
/// Kill is tree-wide: the shell AND the claude process it hosts. Used when the app closes (all
/// sessions die with it) and when an orchestration/implementer is closed (its elements die).
/// PID files are deleted afterwards so the watchdog treats the slot as cleanly stopped.
/// </summary>
public static class SessionTerminator
{
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
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already dead, or a recycled pid we cannot touch — either way the slot is free.
        }
        finally
        {
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
