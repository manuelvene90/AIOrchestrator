using System.Text;
using AIOrchestratorCoreLib.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// The installer now installs ONE thing — the status line — because everything else it used to copy
/// ships inside the plugin. Every test here is one of the eleven this class had; the two that
/// asserted the copying of role commands and hooks moved to where those properties now live
/// (<see cref="RoleHooksAreShippedTests"/>), rather than being deleted with the code they watched.
/// </summary>
public class KitAssetsInstallerTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _kitStatuslineFile;
    readonly string _statuslineTargetFile;

    public KitAssetsInstallerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-kit-tests-{Guid.NewGuid():N}");
        _kitStatuslineFile = Path.Combine(_tempRoot, "kit", "statusline", "statusline.ps1");
        _statuslineTargetFile = Path.Combine(_tempRoot, "supervision", "statusline.ps1");

        Directory.CreateDirectory(Path.GetDirectoryName(_kitStatuslineFile)!);
        File.WriteAllText(_kitStatuslineFile, "statusline v1");
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    IReadOnlyList<string> Run_Installer(string? installerDescription = null)
    {
        return KitAssets_Installer.Ensure_Installed(_kitStatuslineFile, _statuslineTargetFile, installerDescription);
    }

    string Provenance_File => Path.Combine(Path.GetDirectoryName(_statuslineTargetFile)!, KitAssets_Installer.PROVENANCE_FILE_NAME);

    /// <summary>
    /// SESSIONS READ THE INSTALLED COPY WHILE THEIR AUTHOR EDITS THE BRANCH SOURCE, and telling the
    /// two apart has cost this project whole evenings — an edit is not delivery until an app startup
    /// has put it here. The installed side carries the identity of the app that filled it, so the
    /// question stops being answered by comparing mtimes.
    /// </summary>
    [Fact]
    public void Ensure_Installed_NamesTheAppThatInstalledTheStatusLine()
    {
        Run_Installer("build 22:43 · bin\\Debug — C:\\repos\\AIOrchestrator\\bin\\Debug\\");

        Assert.True(File.Exists(Provenance_File), "the installed status line does not say which app put it there");
        Assert.Contains("build 22:43 · bin\\Debug", File.ReadAllText(Provenance_File));
    }

    /// <summary>
    /// Rewritten even when NOTHING was copied: "which app owns what is here now" is hardest to answer
    /// precisely in the run where the folder was already up to date, which is most runs.
    /// </summary>
    [Fact]
    public void Ensure_Installed_SecondRunUnchanged_StillRefreshesTheProvenance()
    {
        Run_Installer("build 18:35 · bin\\Debug - Copia");

        var installed = Run_Installer("build 22:43 · bin\\Debug");

        Assert.Empty(installed);
        Assert.Contains("build 22:43 · bin\\Debug", File.ReadAllText(Provenance_File));
    }

    /// <summary>The note is provenance, not a kit asset — counting it would log an install every run.</summary>
    [Fact]
    public void Ensure_Installed_TheProvenanceNote_IsNotReportedAsAnInstalledAsset()
    {
        var installed = Run_Installer("build 22:43 · bin\\Debug");

        Assert.DoesNotContain(installed, file => file.EndsWith(KitAssets_Installer.PROVENANCE_FILE_NAME, StringComparison.Ordinal));
    }

    [Fact]
    public void Ensure_Installed_FreshMachine_InstallsTheStatusLine()
    {
        var installed = Run_Installer();

        Assert.Single(installed);
        Assert.Equal("statusline v1", File.ReadAllText(_statuslineTargetFile));
    }

    [Fact]
    public void Ensure_Installed_SecondRunUnchanged_CopiesNothing()
    {
        Run_Installer();

        Assert.Empty(Run_Installer());
    }

    [Fact]
    public void Ensure_Installed_KitContentChanged_OverwritesTarget()
    {
        Run_Installer();
        File.WriteAllText(_kitStatuslineFile, "statusline v2");

        var secondRun = Run_Installer();

        Assert.Single(secondRun);
        Assert.Equal("statusline v2", File.ReadAllText(_statuslineTargetFile));
    }

    [Fact]
    public void Ensure_Installed_MissingSourceScript_ReturnsEmptyWithoutThrowing()
    {
        var installed = KitAssets_Installer.Ensure_Installed(
            Path.Combine(_tempRoot, "also-missing.ps1"),
            _statuslineTargetFile);

        Assert.Empty(installed);
    }

    [Fact]
    public void Ensure_Installed_SourceHasBom_TargetKeepsIt()
    {
        // Windows PowerShell 5.1 reads a BOM-less UTF-8 .ps1 as the machine's ANSI codepage. On a
        // Windows-1252 box the em-dash (E2 80 94) decodes to three characters whose last is 0x94 —
        // a smart quote, which the parser honours as a string delimiter, so every quoted string
        // containing one breaks. The kit ships statusline.ps1 WITH a BOM for exactly that reason,
        // so installing must not drop it.
        // GetBytes never emits the preamble — encoderShouldEmitUTF8Identifier only governs
        // GetPreamble — so the BOM has to be prepended explicitly or the test passes vacuously.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var sourceBytes = utf8.GetPreamble()
            .Concat(utf8.GetBytes("Write-Output \"model — folder\"\n"))
            .ToArray();
        File.WriteAllBytes(_kitStatuslineFile, sourceBytes);

        Run_Installer();

        Assert.Equal(sourceBytes, File.ReadAllBytes(_statuslineTargetFile));
    }

    [Fact]
    public void Ensure_Installed_SourceHasNoBom_TargetGainsNone()
    {
        // The same copy path carries statusline.sh, where a BOM before the shebang stops the script
        // being executable. Preserving bytes must mean preserving their absence too.
        var posixSource = Path.Combine(_tempRoot, "kit", "statusline", "statusline.sh");
        var posixTarget = Path.Combine(_tempRoot, "supervision", "statusline.sh");
        var sourceBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes("#!/usr/bin/env bash\nexit 0\n");
        Assert.NotEqual(0xEF, sourceBytes[0]);

        File.WriteAllBytes(posixSource, sourceBytes);

        KitAssets_Installer.Ensure_Installed(posixSource, posixTarget);

        Assert.Equal(sourceBytes, File.ReadAllBytes(posixTarget));
    }
}
