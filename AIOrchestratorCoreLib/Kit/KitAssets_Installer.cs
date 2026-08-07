namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Self-installs the kit assets the agents depend on, so launching the app is enough:
/// - the role commands (/supervisor, /implementer, /general-supervisor) into ~/.claude/commands
/// - the status line script into the supervision root
/// Runs at every app startup; files are overwritten when their content changed (the shipped kit
/// is the source of truth). The one thing left to install.ps1 is wiring the status line into
/// ~/.claude/settings.json (merging a user settings file is not the app's business).
/// </summary>
public static class KitAssets_Installer
{
    /// <summary>Returns a human-readable list of what was installed/updated (empty = nothing to do).</summary>
    public static IReadOnlyList<string> Ensure_Installed(
        string kitCommandsFolder,
        string kitStatuslineFile,
        string claudeCommandsFolder,
        string statuslineTargetFile,
        string kitHooksFolder,
        string claudeHooksFolder)
    {
        List<string> installedFiles = [];

        if (Directory.Exists(kitHooksFolder))
        {
            Directory.CreateDirectory(claudeHooksFolder);

            foreach (var sourceFile in Directory.EnumerateFiles(kitHooksFolder, "*.sh"))
            {
                var targetFile = Path.Combine(claudeHooksFolder, Path.GetFileName(sourceFile));

                if (Copy_IfChanged(sourceFile, targetFile))
                    installedFiles.Add(targetFile);
            }
        }

        if (Directory.Exists(kitCommandsFolder))
        {
            Directory.CreateDirectory(claudeCommandsFolder);

            foreach (var sourceFile in Directory.EnumerateFiles(kitCommandsFolder, "*.md"))
            {
                var targetFile = Path.Combine(claudeCommandsFolder, Path.GetFileName(sourceFile));

                if (Copy_IfChanged(sourceFile, targetFile))
                    installedFiles.Add(targetFile);
            }
        }

        if (File.Exists(kitStatuslineFile))
        {
            var targetFolder = Path.GetDirectoryName(statuslineTargetFile);

            if (targetFolder != null)
                Directory.CreateDirectory(targetFolder);

            if (Copy_IfChanged(kitStatuslineFile, statuslineTargetFile))
                installedFiles.Add(statuslineTargetFile);
        }

        return installedFiles;
    }

    static bool Copy_IfChanged(string sourceFile, string targetFile)
    {
        var sourceContent = File.ReadAllText(sourceFile);

        if (File.Exists(targetFile) && File.ReadAllText(targetFile) == sourceContent)
            return false;

        File.WriteAllText(targetFile, sourceContent);
        return true;
    }
}
