namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Installs the ONE asset that is not part of the plugin: the status line script.
///
/// Everything else this class used to copy — the six role protocols, the append helper, the seven
/// hooks — now ships inside the aiorch plugin and is installed once by `claude plugin install`,
/// which is the entire point of the stage. Copying them at every app start was the mechanism behind
/// decisions 17, 18 and 23: four derived copies of every file, and three evenings lost to telling
/// them apart.
///
/// The status line stays here because it is NOT a plugin component: `statusLine` is a key in
/// ~/.claude/settings.json pointing at a script path, so something has to put a script at a path.
/// It is copied as BYTES, never as text — ReadAllText strips a BOM and WriteAllText writes none
/// back, and statusline.ps1 needs one (Windows PowerShell 5.1 reads a BOM-less UTF-8 script as the
/// machine's ANSI codepage and mis-parses every em-dash). Because the comparison is bytes too, an
/// already-stripped target reads as different and is repaired rather than passing as identical.
/// </summary>
public static class KitAssets_Installer
{
    /// <summary>
    /// Dropped beside the installed status line, naming the app that put it there. Sessions read the
    /// INSTALLED copy while their author edits the branch source, and telling those apart has cost
    /// this project whole evenings — so the installed side carries its own provenance rather than
    /// leaving everyone to infer it from mtimes (decision 18).
    /// </summary>
    public const string PROVENANCE_FILE_NAME = ".installed-by.txt";

    /// <summary>Returns what was installed/updated (empty = already current).</summary>
    public static IReadOnlyList<string> Ensure_Installed(
        string kitStatuslineFile,
        string statuslineTargetFile,
        string? installerDescription = null)
    {
        List<string> installedFiles = [];

        var targetFolder = Path.GetDirectoryName(statuslineTargetFile);

        if (targetFolder != null)
            Directory.CreateDirectory(targetFolder);

        if (File.Exists(kitStatuslineFile) && Copy_IfChanged(kitStatuslineFile, statuslineTargetFile))
            installedFiles.Add(statuslineTargetFile);

        Write_Provenance_BestEffort(targetFolder, installerDescription);

        return installedFiles;
    }

    /// <summary>
    /// Written on EVERY run, not only when something was copied: the question it answers is "which
    /// app owns what is here now", and an unchanged folder is exactly the case where that is hardest
    /// to work out. Never counted as an installed file — it is provenance, not a kit asset.
    /// </summary>
    static void Write_Provenance_BestEffort(string? targetFolder, string? installerDescription)
    {
        if (installerDescription == null || targetFolder == null || !Directory.Exists(targetFolder))
            return;

        try
        {
            File.WriteAllText(Path.Combine(targetFolder, PROVENANCE_FILE_NAME), $"{installerDescription}\n");
        }
        catch
        {
            // A missing note is cosmetic; installing must never fail on it.
        }
    }

    static bool Copy_IfChanged(string sourceFile, string targetFile)
    {
        var sourceBytes = File.ReadAllBytes(sourceFile);

        if (File.Exists(targetFile) && File.ReadAllBytes(targetFile).AsSpan().SequenceEqual(sourceBytes))
            return false;

        File.WriteAllBytes(targetFile, sourceBytes);
        return true;
    }
}
