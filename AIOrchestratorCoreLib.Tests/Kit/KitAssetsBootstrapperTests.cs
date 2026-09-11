using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// The startup sequence shared by the WPF app and the daemon. It used to COPY the kit into
/// ~/.claude; it now CHECKS an installed plugin and CLEANS UP what the old builds copied. The three
/// tests this class had are all still here, each asserting the same intent against the new
/// mechanism, plus the two properties the change introduced (removal, and the gate).
/// </summary>
public class KitAssetsBootstrapperTests : IDisposable
{
    readonly string _temp;
    readonly string _kit;
    readonly string _claudeHome;
    readonly ISupervisionPaths _paths;

    public KitAssetsBootstrapperTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), $"aiorch-bootstrap-{Guid.NewGuid():N}");
        _kit = Path.Combine(_temp, "kit");
        _claudeHome = Path.Combine(_temp, "claude-home");
        _paths = SupervisionPaths_Factory.Create(Path.Combine(_temp, "supervision"));

        Directory.CreateDirectory(Path.Combine(_kit, "statusline"));
        Directory.CreateDirectory(Path.Combine(_claudeHome, "commands"));
        Directory.CreateDirectory(Path.Combine(_claudeHome, "hooks"));
        File.WriteAllText(Path.Combine(_kit, "statusline", "statusline.ps1"), "# ps1\n");
        File.WriteAllText(Path.Combine(_kit, "statusline", "statusline.sh"), "#!/usr/bin/env bash\n");
    }

    public void Dispose()
    {
        Directory.Delete(_temp, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void WindowsGetsThePowerShellScript_EveryOtherOSGetsTheBashTwin()
    {
        Assert.Equal("statusline.ps1", KitAssets_Bootstrapper.Pick_StatuslineScriptName(isWindows: true));
        Assert.Equal("statusline.sh", KitAssets_Bootstrapper.Pick_StatuslineScriptName(isWindows: false));
    }

    [Fact]
    public void TheStatusLine_LandsWhereClaudeCodeReadsIt_AndIsWiredIntoSettings()
    {
        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, OrchestrationLog_Factory.Create(_paths));

        var expectedScript = KitAssets_Bootstrapper.Pick_StatuslineScriptName(OperatingSystem.IsWindows());
        var installedScript = Path.Combine(_paths.Root, expectedScript);

        Assert.True(File.Exists(installedScript), $"expected the {expectedScript} twin installed at {installedScript}");

        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(_claudeHome, "settings.json"))) as JsonObject
            ?? throw new Exception("settings.json should be a JSON object");

        Assert.Contains(expectedScript, settings["statusLine"]?["command"]?.GetValue<string>() ?? "");
    }

    /// <summary>
    /// THE UPGRADE PATH, and the reason this is not merely tidying: a stale local command WINS the
    /// slash word over a plugin skill (measured), so a machine that still has them would run the old
    /// protocols under a version number claiming otherwise.
    /// </summary>
    [Fact]
    public void TheKitTheOldBuildsCopiedByHand_IsRemoved_AndItsSettingsEntriesUnwired()
    {
        var hookFile = Path.Combine(_claudeHome, "hooks", "supervisor-ledger-check.sh");

        File.WriteAllText(Path.Combine(_claudeHome, "commands", "supervisor.md"), "# the OLD supervisor protocol\n");
        File.WriteAllText(Path.Combine(_claudeHome, "commands", "channel-append.sh"), "#!/usr/bin/env bash\n");
        File.WriteAllText(hookFile, "#!/usr/bin/env bash\n");
        AgentHookSettings_Wirer.Ensure_Wired(Path.Combine(_claudeHome, "settings.json"), hookFile, AgentHookSettings_Wirer.STOP_EVENT, null);

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, OrchestrationLog_Factory.Create(_paths));

        Assert.False(File.Exists(Path.Combine(_claudeHome, "commands", "supervisor.md")), "the stale protocol would have been read INSTEAD of the plugin");
        Assert.False(File.Exists(Path.Combine(_claudeHome, "commands", "channel-append.sh")));
        Assert.False(File.Exists(hookFile));

        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(_claudeHome, "settings.json"))) as JsonObject;
        Assert.Null(settings!["hooks"]?["Stop"]);
    }

    /// <summary>
    /// With no plugin installed the gate closes: no session starts against a kit this host cannot
    /// find. The bridge is untouched — that is how the owner is told.
    /// </summary>
    [Fact]
    public void NoPluginInstalled_ClosesTheGate_AndNamesWhatToRun()
    {
        var gate = PluginGate_Factory.Create();

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, OrchestrationLog_Factory.Create(_paths), gate);

        Assert.Equal(PluginVerdicts.NotInstalled, gate.Verdict);
        Assert.False(gate.Spawning_Allowed);
        Assert.Contains(KitPlugin.INSTALL_COMMAND, gate.Refusal);
    }

    [Fact]
    public void TheExpectedPluginInstalledAndEnabled_OpensTheGate()
    {
        Directory.CreateDirectory(Path.Combine(_claudeHome, "plugins"));
        File.WriteAllText(Path.Combine(_claudeHome, "plugins", InstalledPlugin_Reader.INSTALLED_PLUGINS_FILE), $$"""
            { "version": 2, "plugins": { "{{KitPlugin.ID}}": [ { "scope": "user", "installPath": "/cache", "version": "{{KitPlugin.EXPECTED_VERSION}}" } ] } }
            """);
        File.WriteAllText(Path.Combine(_claudeHome, "settings.json"), $$"""
            { "enabledPlugins": { "{{KitPlugin.ID}}": true } }
            """);

        var gate = PluginGate_Factory.Create();

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, OrchestrationLog_Factory.Create(_paths), gate);

        Assert.Equal(PluginVerdicts.Ok, gate.Verdict);
        Assert.True(gate.Spawning_Allowed);
        Assert.Null(gate.Refusal);
    }

    /// <summary>
    /// THE GATE IS RECORDED ON EVERY PATH. Verify_Plugin used to be the last of four steps inside one
    /// try, so anything the three before it threw jumped past the only call that takes a verdict: the
    /// gate stayed Unchecked, Unchecked ALLOWS, and every session spawned against a kit this host had
    /// never verified. Here the housekeeping is made to fail — the commands folder is replaced by a
    /// FILE, so the sweep cannot enumerate it — and a verdict must still exist afterwards.
    /// </summary>
    [Fact]
    public void HousekeepingThatFails_StillLeavesAVerdict_NeverAnUncheckedGate()
    {
        Directory.Delete(Path.Combine(_claudeHome, "commands"), recursive: true);
        File.WriteAllText(Path.Combine(_claudeHome, "commands"), "not a folder");
        File.WriteAllText(Path.Combine(_claudeHome, "settings.json"), "{ not json");

        var gate = PluginGate_Factory.Create();

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, OrchestrationLog_Factory.Create(_paths), gate);

        Assert.NotEqual(PluginVerdicts.Unchecked, gate.Verdict);
        Assert.False(gate.Spawning_Allowed);
        Assert.NotNull(gate.Refusal);
    }

    /// <summary>
    /// An unreadable settings.json is REPORTED, not passed over. On that machine the legacy hook
    /// entries survive and would fire for every session, so silence there is the predicate that
    /// could not be evaluated pretending it was.
    /// </summary>
    [Fact]
    public void AnUnreadableSettingsFile_IsReported_BecauseTheLegacyHooksAreStillWired()
    {
        File.WriteAllText(Path.Combine(_claudeHome, "settings.json"), "{ not json");

        var log = OrchestrationLog_Factory.Create(_paths);
        var problems = new List<string>();
        log.EntryLogged += entry => { if (entry.Level != AIOrchestratorCoreLib.Logging.LogLevels.Info) problems.Add(entry.Message); };

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, log);

        Assert.Contains(problems, message => message.Contains("STILL WIRED"));
    }

    [Fact]
    public void AMissingKit_IsLogged_AndNeverThrows_BecauseTheBridgeMustStartAnyway()
    {
        var log = OrchestrationLog_Factory.Create(_paths);
        var problems = new List<string>();
        log.EntryLogged += entry => { if (entry.Level != AIOrchestratorCoreLib.Logging.LogLevels.Info) problems.Add(entry.Message); };

        KitAssets_Bootstrapper.Ensure_Installed(Path.Combine(_temp, "no-such-kit"), _claudeHome, _paths, log);

        Assert.Contains(problems, message => message.Contains("status line NOT installed"));
    }
}
