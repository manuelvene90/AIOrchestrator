using System.ComponentModel;
using System.Diagnostics;
using AIOrchestratorCoreLib.Spawning.SpawnCommand;

namespace AIOrchestratorCoreLib.Spawning.SessionSpawner;

internal sealed class SessionSpawnerModel : ISessionSpawner
{
    public int? Spawn(ISpawnCommand command)
    {
        try
        {
            return Start_Process(command);
        }
        catch (Win32Exception)
        {
            // wt.exe not installed — retry as a plain PowerShell window.
            var fallback = SpawnCommand_Builder.Build_PowershellFallback(command);
            return Start_Process(fallback);
        }
    }

    static int? Start_Process(ISpawnCommand command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command.Executable,
            WorkingDirectory = command.WorkingDirectory,
            UseShellExecute = true,
        };

        foreach (var argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);

        var process = Process.Start(startInfo);

        return process?.Id;
    }
}
