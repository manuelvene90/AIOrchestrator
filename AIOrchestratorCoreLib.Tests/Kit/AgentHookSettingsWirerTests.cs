using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// The hooks are the enforcement levers of the whole protocol (the ledger Stop hook, the reviewer
/// read-only PreToolUse hook), so this guards that wiring them never damages the user's own
/// settings and never double-registers.
/// </summary>
public class AgentHookSettingsWirerTests : IDisposable
{
    readonly string _tempRoot;
    readonly string _settingsFile;
    readonly string _stopScript;
    readonly string _reviewerScript;

    public AgentHookSettingsWirerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-hookwirer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _settingsFile = Path.Combine(_tempRoot, "settings.json");
        _stopScript = Path.Combine(_tempRoot, "supervisor-ledger-check.sh");
        _reviewerScript = Path.Combine(_tempRoot, "reviewer-readonly-check.sh");
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    JsonObject Read_Settings()
    {
        return JsonNode.Parse(File.ReadAllText(_settingsFile)) as JsonObject
            ?? throw new Exception($"settings.json at '{_settingsFile}' should be a JSON object");
    }

    [Fact]
    public void Ensure_Wired_StopHook_TakesNoMatcher()
    {
        Assert.True(AgentHookSettings_Wirer.Ensure_Wired(_settingsFile, _stopScript, AgentHookSettings_Wirer.STOP_EVENT, null));

        var entry = Read_Settings()["hooks"]?["Stop"]?.AsArray().Single() as JsonObject
            ?? throw new Exception("expected one Stop entry");

        Assert.Null(entry["matcher"]);
        Assert.Contains("supervisor-ledger-check.sh", entry["hooks"]?[0]?["command"]?.GetValue<string>());
    }

    /// <summary>
    /// Without the Bash matcher the reviewer hook would either never fire or fire on every tool —
    /// the difference between enforcement and noise.
    /// </summary>
    [Fact]
    public void Ensure_Wired_ReviewerHook_IsRegisteredOnPreToolUse_MatchingBash()
    {
        Assert.True(AgentHookSettings_Wirer.Ensure_Wired(
            _settingsFile, _reviewerScript, AgentHookSettings_Wirer.PRE_TOOL_USE_EVENT, "Bash"));

        var entry = Read_Settings()["hooks"]?["PreToolUse"]?.AsArray().Single() as JsonObject
            ?? throw new Exception("expected one PreToolUse entry");

        Assert.Equal("Bash", entry["matcher"]?.GetValue<string>());
        Assert.Contains("reviewer-readonly-check.sh", entry["hooks"]?[0]?["command"]?.GetValue<string>());
    }

    [Fact]
    public void Ensure_Wired_BothHooks_CoexistAndNeitherDoubleRegisters()
    {
        AgentHookSettings_Wirer.Ensure_Wired(_settingsFile, _stopScript, AgentHookSettings_Wirer.STOP_EVENT, null);
        AgentHookSettings_Wirer.Ensure_Wired(_settingsFile, _reviewerScript, AgentHookSettings_Wirer.PRE_TOOL_USE_EVENT, "Bash");

        Assert.False(AgentHookSettings_Wirer.Ensure_Wired(_settingsFile, _stopScript, AgentHookSettings_Wirer.STOP_EVENT, null));
        Assert.False(AgentHookSettings_Wirer.Ensure_Wired(_settingsFile, _reviewerScript, AgentHookSettings_Wirer.PRE_TOOL_USE_EVENT, "Bash"));

        var hooks = Read_Settings()["hooks"] ?? throw new Exception("expected a hooks object");
        Assert.Single(hooks["Stop"]?.AsArray() ?? []);
        Assert.Single(hooks["PreToolUse"]?.AsArray() ?? []);
    }

    [Fact]
    public void Ensure_Wired_PreservesTheUsersOwnHooksAndBacksUpOnce()
    {
        File.WriteAllText(_settingsFile, """
        {
          "model": "opus",
          "hooks": { "PreToolUse": [ { "matcher": "Write", "hooks": [ { "type": "command", "command": "bash mine.sh" } ] } ] }
        }
        """);

        AgentHookSettings_Wirer.Ensure_Wired(_settingsFile, _reviewerScript, AgentHookSettings_Wirer.PRE_TOOL_USE_EVENT, "Bash");

        var root = Read_Settings();
        Assert.Equal("opus", root["model"]?.GetValue<string>());

        var entries = root["hooks"]?["PreToolUse"]?.AsArray() ?? throw new Exception("expected PreToolUse entries");
        Assert.Equal(2, entries.Count);
        Assert.Contains("mine.sh", entries[0]?["hooks"]?[0]?["command"]?.GetValue<string>());
        Assert.True(File.Exists($"{_settingsFile}.aiorch-backup"));
    }

    [Fact]
    public void Ensure_Wired_UnparseableSettings_AreLeftAlone()
    {
        File.WriteAllText(_settingsFile, "definitely not json");

        Assert.False(AgentHookSettings_Wirer.Ensure_Wired(_settingsFile, _stopScript, AgentHookSettings_Wirer.STOP_EVENT, null));
        Assert.Equal("definitely not json", File.ReadAllText(_settingsFile));
    }
}
