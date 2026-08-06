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
            _kitCommandsFolder, _kitStatuslineFile, _commandsTargetFolder, _statuslineTargetFile);
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
            _statuslineTargetFile);

        Assert.Empty(installer);
    }
}
