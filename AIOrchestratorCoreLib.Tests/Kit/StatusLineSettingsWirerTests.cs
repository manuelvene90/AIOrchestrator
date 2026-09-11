using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

public class StatusLineSettingsWirerTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _settingsFile;
    readonly string _scriptPath;

    public StatusLineSettingsWirerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-wirer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _settingsFile = Path.Combine(_tempRoot, "settings.json");
        _scriptPath = Path.Combine(_tempRoot, "statusline.ps1");
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void Ensure_Wired_NoSettingsFile_CreatesItWithStatusLine()
    {
        var changed = StatusLineSettings_Wirer.Ensure_Wired(_settingsFile, _scriptPath);

        Assert.True(changed);

        var root = JsonNode.Parse(File.ReadAllText(_settingsFile)) as JsonObject
            ?? throw new Exception("settings.json should be a JSON object");
        Assert.Equal("command", root["statusLine"]?["type"]?.GetValue<string>());
        Assert.Contains(_scriptPath, root["statusLine"]?["command"]?.GetValue<string>());
    }

    [Fact]
    public void Ensure_Wired_ExistingSettings_PreservesOtherKeysAndBacksUp()
    {
        File.WriteAllText(_settingsFile, """{"theme":"dark","permissions":{"allow":["Bash(ls:*)"]}}""");

        var changed = StatusLineSettings_Wirer.Ensure_Wired(_settingsFile, _scriptPath);

        Assert.True(changed);

        var root = JsonNode.Parse(File.ReadAllText(_settingsFile)) as JsonObject
            ?? throw new Exception("settings.json should be a JSON object");
        Assert.Equal("dark", root["theme"]?.GetValue<string>());
        Assert.NotNull(root["permissions"]);
        Assert.NotNull(root["statusLine"]);
        Assert.True(File.Exists(_settingsFile + StatusLineSettings_Wirer.BACKUP_SUFFIX));
    }

    [Fact]
    public void Ensure_Wired_AlreadyWired_ChangesNothing()
    {
        StatusLineSettings_Wirer.Ensure_Wired(_settingsFile, _scriptPath);
        var contentAfterFirst = File.ReadAllText(_settingsFile);

        var secondRun = StatusLineSettings_Wirer.Ensure_Wired(_settingsFile, _scriptPath);

        Assert.False(secondRun);
        Assert.Equal(contentAfterFirst, File.ReadAllText(_settingsFile));
    }

    [Fact]
    public void Ensure_Wired_DifferentExistingStatusLine_IsOverwrittenWithBackup()
    {
        File.WriteAllText(_settingsFile, """{"statusLine":{"type":"command","command":"something-else"}}""");

        var changed = StatusLineSettings_Wirer.Ensure_Wired(_settingsFile, _scriptPath);

        Assert.True(changed);
        Assert.True(File.Exists(_settingsFile + StatusLineSettings_Wirer.BACKUP_SUFFIX));
        Assert.Contains("something-else", File.ReadAllText(_settingsFile + StatusLineSettings_Wirer.BACKUP_SUFFIX));
    }

    [Fact]
    public void Ensure_Wired_NonObjectSettingsFile_ThrowsInsteadOfClobbering()
    {
        File.WriteAllText(_settingsFile, "[1,2,3]");

        Assert.Throws<Exception>(() => StatusLineSettings_Wirer.Ensure_Wired(_settingsFile, _scriptPath));
    }

    /// <summary>
    /// The bash twin is wired through bash, decided by the script's extension — the installer
    /// picks the script per OS, and the command must agree with it by construction.
    /// </summary>
    [Fact]
    public void ABashScript_IsWiredThroughBash_AndAPowerShellOneThroughPowerShell()
    {
        Assert.Equal("bash \"/home/o/.claude/supervision/statusline.sh\"", StatusLineSettings_Wirer.Build_Command("/home/o/.claude/supervision/statusline.sh"));
        Assert.Equal("bash \"C:/Users/o/.claude/supervision/statusline.sh\"", StatusLineSettings_Wirer.Build_Command("C:\\Users\\o\\.claude\\supervision\\statusline.sh"));
        Assert.StartsWith("powershell -NoProfile", StatusLineSettings_Wirer.Build_Command("C:\\Users\\o\\.claude\\supervision\\statusline.ps1"));

        var shScript = Path.Combine(_tempRoot, "statusline.sh");
        StatusLineSettings_Wirer.Ensure_Wired(_settingsFile, shScript);
        var root = JsonNode.Parse(File.ReadAllText(_settingsFile)) as JsonObject
            ?? throw new Exception("settings.json should be a JSON object");
        Assert.StartsWith("bash \"", root["statusLine"]?["command"]?.GetValue<string>());
    }
}
