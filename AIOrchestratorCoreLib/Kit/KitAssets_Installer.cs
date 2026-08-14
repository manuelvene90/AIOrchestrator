namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Self-installs the kit assets the agents depend on, so launching the app is enough:
/// - EVERY role command in the shipped kit folder into ~/.claude/commands, plus the .sh helpers
///   sitting beside them (the folder is globbed, never a hard-coded list, so a role or a helper
///   added to the kit cannot be silently left uninstalled)
/// - the status line script into the supervision root
/// Runs at every app startup; files are overwritten when their content changed (the shipped kit
/// is the source of truth). The one thing left to install.ps1 is wiring the status line into
/// ~/.claude/settings.json (merging a user settings file is not the app's business).
/// </summary>
public static class KitAssets_Installer
{
    /// <summary>
    /// The file dropped beside the installed commands naming the app that put them there. Sessions
    /// read the INSTALLED copy while their author edits the branch source, and telling the two apart
    /// has cost this project whole evenings — so the installed folder now carries its own provenance
    /// instead of leaving everyone to infer it from mtimes.
    /// </summary>
    public const string PROVENANCE_FILE_NAME = ".installed-by.txt";

    /// <summary>Returns a human-readable list of what was installed/updated (empty = nothing to do).</summary>
    public static IReadOnlyList<string> Ensure_Installed(
        string kitCommandsFolder,
        string kitStatuslineFile,
        string claudeCommandsFolder,
        string statuslineTargetFile,
        string kitHooksFolder,
        string claudeHooksFolder,
        string? installerDescription = null)
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

            // .md AND .sh: the role commands, plus the append helper they instruct sessions to run.
            // Installing the instructions without the script they name would leave every session
            // pointing at a path that does not exist, and falling back to the unlocked append the
            // helper exists to replace.
            foreach (var sourceFile in Directory.EnumerateFiles(kitCommandsFolder, "*.*")
                         .Where(Is_InstallableCommandAsset))
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

        Write_Provenance_BestEffort(claudeCommandsFolder, installerDescription);

        return installedFiles;
    }

    /// <summary>
    /// Written on EVERY run, not only when something was copied: the question it answers is "which
    /// app owns what is here now", and an unchanged folder is exactly the case where that is hardest
    /// to work out. Never counted as an installed file — it is provenance, not a kit asset, and a
    /// startup log line listing it every time would be noise.
    /// </summary>
    static void Write_Provenance_BestEffort(string claudeCommandsFolder, string? installerDescription)
    {
        if (installerDescription == null || !Directory.Exists(claudeCommandsFolder))
            return;

        try
        {
            File.WriteAllText(Path.Combine(claudeCommandsFolder, PROVENANCE_FILE_NAME), $"{installerDescription}\n");
        }
        catch
        {
            // A missing note is cosmetic; installing the kit must never fail on it.
        }
    }

    static bool Is_InstallableCommandAsset(string filePath)
    {
        var extension = Path.GetExtension(filePath);

        return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sh", StringComparison.OrdinalIgnoreCase);
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
