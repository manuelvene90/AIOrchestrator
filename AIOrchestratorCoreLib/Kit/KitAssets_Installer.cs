namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Self-installs the kit assets the agents depend on, so launching the app is enough:
/// - EVERY role command in the shipped kit folder into ~/.claude/commands (the folder is globbed,
///   never a hard-coded list, so a role added to the kit cannot be silently left uninstalled)
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

    /// <summary>
    /// Copies BYTES, never text. Text would corrupt two things at once: ReadAllText strips a BOM
    /// and WriteAllText writes none back, so a BOM-carrying source (statusline.ps1 needs one —
    /// Windows PowerShell 5.1 reads a BOM-less UTF-8 script as the machine's ANSI codepage and
    /// mis-parses every em-dash) arrives without it; and because the comparison is BOM-blind, an
    /// already-stripped target reads as identical and is never repaired. Bytes also keep the
    /// hooks' shebang first, where a BOM would stop them executing.
    /// </summary>
    static bool Copy_IfChanged(string sourceFile, string targetFile)
    {
        var sourceBytes = File.ReadAllBytes(sourceFile);

        if (File.Exists(targetFile) && File.ReadAllBytes(targetFile).AsSpan().SequenceEqual(sourceBytes))
            return false;

        File.WriteAllBytes(targetFile, sourceBytes);
        return true;
    }
}
