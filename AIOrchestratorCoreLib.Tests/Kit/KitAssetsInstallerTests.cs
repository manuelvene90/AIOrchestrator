using System.Text;
using AIOrchestratorCoreLib.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

public class KitAssetsInstallerTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _kitCommandsFolder;
    readonly string _kitStatuslineFile;
    readonly string _commandsTargetFolder;
    readonly string _statuslineTargetFile;

    public KitAssetsInstallerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-kit-tests-{Guid.NewGuid():N}");
        _kitCommandsFolder = Path.Combine(_tempRoot, "kit", "commands");
        _kitStatuslineFile = Path.Combine(_tempRoot, "kit", "statusline.ps1");
        _commandsTargetFolder = Path.Combine(_tempRoot, "claude", "commands");
        _statuslineTargetFile = Path.Combine(_tempRoot, "supervision", "statusline.ps1");

        Directory.CreateDirectory(_kitCommandsFolder);
        File.WriteAllText(Path.Combine(_kitCommandsFolder, "supervisor.md"), "supervisor protocol v1");
        File.WriteAllText(Path.Combine(_kitCommandsFolder, "general-supervisor.md"), "general protocol v1");
        File.WriteAllText(_kitStatuslineFile, "statusline v1");
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    IReadOnlyList<string> Run_Installer()
    {
        return KitAssets_Installer.Ensure_Installed(
            _kitCommandsFolder, _kitStatuslineFile, _commandsTargetFolder, _statuslineTargetFile,
            Path.Combine(_tempRoot, "kit", "hooks"), Path.Combine(_tempRoot, "claude", "hooks"));
    }

    /// <summary>
    /// The append helper lives beside the role commands that tell sessions to run it. If the
    /// installer does not carry it across, every one of those instructions points at a path that
    /// does not exist and the sessions fall back to the unlocked appends the helper replaced —
    /// a protocol that ships as documentation only.
    /// </summary>
    [Fact]
    public void Ensure_Installed_CarriesTheAppendHelperAcrossToo_NotOnlyTheMarkdown()
    {
        File.WriteAllText(Path.Combine(_kitCommandsFolder, "channel-append.sh"), "#!/bin/bash\necho helper v1\n");

        Run_Installer();

        var installedHelper = Path.Combine(_commandsTargetFolder, "channel-append.sh");

        Assert.True(File.Exists(installedHelper), "the append helper was not installed beside the role commands that call it");
        Assert.Equal("#!/bin/bash\necho helper v1\n", File.ReadAllText(installedHelper));
    }

    [Fact]
    public void Ensure_Installed_FreshMachine_CopiesEverything()
    {
        var installed = Run_Installer();

        Assert.Equal(3, installed.Count);
        Assert.Equal("supervisor protocol v1", File.ReadAllText(Path.Combine(_commandsTargetFolder, "supervisor.md")));
        Assert.Equal("general protocol v1", File.ReadAllText(Path.Combine(_commandsTargetFolder, "general-supervisor.md")));
        Assert.Equal("statusline v1", File.ReadAllText(_statuslineTargetFile));
    }

    [Fact]
    public void Ensure_Installed_SecondRunUnchanged_CopiesNothing()
    {
        Run_Installer();

        var secondRun = Run_Installer();

        Assert.Empty(secondRun);
    }

    [Fact]
    public void Ensure_Installed_KitContentChanged_OverwritesTarget()
    {
        Run_Installer();
        File.WriteAllText(Path.Combine(_kitCommandsFolder, "supervisor.md"), "supervisor protocol v2");

        var secondRun = Run_Installer();

        Assert.Single(secondRun);
        Assert.Equal("supervisor protocol v2", File.ReadAllText(Path.Combine(_commandsTargetFolder, "supervisor.md")));
    }

    [Fact]
    public void Ensure_Installed_MissingKitFolder_ReturnsEmptyWithoutThrowing()
    {
        var installer = KitAssets_Installer.Ensure_Installed(
            Path.Combine(_tempRoot, "does-not-exist"),
            Path.Combine(_tempRoot, "also-missing.ps1"),
            _commandsTargetFolder,
            _statuslineTargetFile,
            Path.Combine(_tempRoot, "no-hooks"),
            Path.Combine(_tempRoot, "claude", "hooks"));

        Assert.Empty(installer);
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
        // The same copy path carries the *.sh hooks, where a BOM before the shebang stops the
        // script being executable. Preserving bytes must mean preserving their absence too.
        var kitHooksFolder = Path.Combine(_tempRoot, "kit", "hooks");
        var hooksTargetFolder = Path.Combine(_tempRoot, "claude", "hooks");
        var sourceBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes("#!/usr/bin/env bash\nexit 0\n");
        Assert.NotEqual(0xEF, sourceBytes[0]);

        Directory.CreateDirectory(kitHooksFolder);
        File.WriteAllBytes(Path.Combine(kitHooksFolder, "supervisor-ledger-check.sh"), sourceBytes);

        KitAssets_Installer.Ensure_Installed(
            _kitCommandsFolder, _kitStatuslineFile, _commandsTargetFolder, _statuslineTargetFile,
            kitHooksFolder, hooksTargetFolder);

        Assert.Equal(
            sourceBytes,
            File.ReadAllBytes(Path.Combine(hooksTargetFolder, "supervisor-ledger-check.sh")));
    }

    [Fact]
    public void Ensure_Installed_CopiesTheEnforcementHooks()
    {
        var kitHooksFolder = Path.Combine(_tempRoot, "kit", "hooks");
        var hooksTargetFolder = Path.Combine(_tempRoot, "claude", "hooks");

        Directory.CreateDirectory(kitHooksFolder);
        File.WriteAllText(Path.Combine(kitHooksFolder, "supervisor-ledger-check.sh"), "#!/usr/bin/env bash\nexit 0\n");

        var installed = KitAssets_Installer.Ensure_Installed(
            _kitCommandsFolder, _kitStatuslineFile, _commandsTargetFolder, _statuslineTargetFile,
            kitHooksFolder, hooksTargetFolder);

        Assert.Contains(installed, file => file.EndsWith("supervisor-ledger-check.sh", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(hooksTargetFolder, "supervisor-ledger-check.sh")));
    }
}
