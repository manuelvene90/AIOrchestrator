using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE CHECK PROVES THE CONTENT, AND THE COMMIT IS ONLY EVER A STAND-IN FOR IT.
///
/// <para>
/// VPS, 2026-09-07: a stage that touched no file under <c>kit/</c> still moved the checkout's HEAD.
/// The CLI records the marketplace repository's HEAD as <c>gitCommitSha</c> at install time, the
/// startup check compared that against the commit the host was built from, they differed, and every
/// session on the machine was refused — over a kit that was byte-for-byte identical.
/// </para>
/// <para>
/// And the second half, which is what made it cost a day rather than a minute: the failed check
/// wrote its refusal onto the general channel, the passing check afterwards wrote nothing, so the
/// newest thing the general supervisor could read there was still the refusal. It kept reporting a
/// blocker that had been cleared.
/// </para>
/// </summary>
public class KitCheckProvesContentTests : IDisposable
{
    const string DIFFERENT_COMMIT = "1111111111111111111111111111111111111111";
    const string BUILD_COMMIT = "2222222222222222222222222222222222222222";

    readonly string _temp;
    readonly string _kit;
    readonly string _installed;
    readonly string _claudeHome;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLog _log;

    public KitCheckProvesContentTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), $"aiorch-kit-content-{Guid.NewGuid():N}");
        _kit = Path.Combine(_temp, "kit");
        _installed = Path.Combine(_temp, "installed-kit");
        _claudeHome = Path.Combine(_temp, "claude-home");
        _paths = SupervisionPaths_Factory.Create(Path.Combine(_temp, "supervision"));
        _log = OrchestrationLog_Factory.Create(_paths);

        Directory.CreateDirectory(Path.Combine(_kit, "statusline"));
        File.WriteAllText(Path.Combine(_kit, "statusline", "statusline.ps1"), "# ps1\n");
        File.WriteAllText(Path.Combine(_kit, "statusline", "statusline.sh"), "#!/usr/bin/env bash\n");

        Write_Kit(_kit, "the supervisor protocol\n");
        Write_Kit(_installed, "the supervisor protocol\n");

        Directory.CreateDirectory(Path.Combine(_claudeHome, "commands"));
        Directory.CreateDirectory(Path.Combine(_claudeHome, "hooks"));
        Write_InstallRecord(DIFFERENT_COMMIT);

        Directory.CreateDirectory(_paths.GeneralFolder);
        File.WriteAllText(_paths.GeneralChannelFile, "# GENERAL CHANNEL\n\n---\n");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>The pure rule, without files: identical content OUTRANKS a differing commit.</summary>
    [Fact]
    public void IdenticalFilesOverADifferingCommit_AreOk_NotAContentMismatch()
    {
        var reading = new InstalledPluginReading(KitPlugin.EXPECTED_VERSION, "/somewhere", enabled: true, null, DIFFERENT_COMMIT);

        Assert.Equal(
            PluginVerdicts.ContentMismatch,
            PluginVersion_Verifier.Decide(reading, KitPlugin.EXPECTED_VERSION, [], BUILD_COMMIT, contentMatches: null));

        Assert.Equal(
            PluginVerdicts.Ok,
            PluginVersion_Verifier.Decide(reading, KitPlugin.EXPECTED_VERSION, [], BUILD_COMMIT, contentMatches: true));
    }

    /// <summary>...and differing files over a MATCHING commit are still a mismatch: the files are the fact.</summary>
    [Fact]
    public void DifferingFilesOverAMatchingCommit_AreStillAMismatch()
    {
        var reading = new InstalledPluginReading(KitPlugin.EXPECTED_VERSION, "/somewhere", enabled: true, null, BUILD_COMMIT);

        Assert.Equal(
            PluginVerdicts.ContentMismatch,
            PluginVersion_Verifier.Decide(reading, KitPlugin.EXPECTED_VERSION, [], BUILD_COMMIT, contentMatches: false));
    }

    /// <summary>The digest is over the FILES: same bytes in the same places, same number, whatever the folder is called.</summary>
    [Fact]
    public void TwoCopiesOfTheSameKit_Digest_TheSame_AndOneChangedByteBreaksIt()
    {
        Assert.Equal(true, KitContent_Digest.Same_Content(_kit, _installed));

        Write_Kit(_installed, "the supervisor protocol, edited\n");

        Assert.Equal(false, KitContent_Digest.Same_Content(_kit, _installed));
    }

    /// <summary>A tree that is not a kit leaves the question OPEN — null, never "they differ".</summary>
    [Fact]
    public void AFolderThatIsNotAKit_LeavesTheQuestionOpen()
    {
        Assert.Null(KitContent_Digest.Compute_OrNull(Path.Combine(_temp, "no-such-folder")));
        Assert.Null(KitContent_Digest.Same_Content(_kit, null));
    }

    /// <summary>
    /// END TO END, and the sentence at the end is the point: with identical files and different
    /// commits the host says OK and the mismatch goes to the log as an ordinary fact, not as a
    /// refusal that stops every session.
    /// </summary>
    [Fact]
    public void AHostWhoseKitMatchesByCONTENT_StartsSessions_AndSaysWhyTheCommitsDiffer()
    {
        var gate = PluginGate_Factory.Create();

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, _log, gate);

        Assert.Equal(PluginVerdicts.Ok, gate.Verdict);
        Assert.True(gate.Spawning_Allowed);
        Assert.Contains("byte-identical", File.ReadAllText(_paths.GlobalLogFile));
    }

    /// <summary>
    /// A CHECK THAT FAILED AND THEN PASSES SAYS SO ON THE CHANNEL. Without this the refusal stays
    /// the last word the general supervisor can read, and it goes on reporting a cleared blocker —
    /// which from the owner's side is a supervisor that has stopped working.
    /// </summary>
    [Fact]
    public void AfterAFailedCheck_ThePassingOneWritesOneRecoveryEntry()
    {
        Write_Kit(_installed, "a DIFFERENT supervisor protocol\n");

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, _log, PluginGate_Factory.Create());

        Assert.Equal(PluginVerdicts.ContentMismatch, KitCheckHistory_Store.Read_LastVerdict_OrNull(_paths));
        Assert.Contains(Read_Entries(), entry => entry.Subject.Contains(KitAssets_Bootstrapper.REFUSAL_SUBJECT));
        Assert.DoesNotContain(Read_Entries(), entry => entry.Subject.Contains(KitAssets_Bootstrapper.RECOVERY_SUBJECT, StringComparison.Ordinal));

        // The operator reinstalls, the host restarts.
        Write_Kit(_installed, "the supervisor protocol\n");

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, _log, PluginGate_Factory.Create());

        var recoveries = Read_Entries().Where(entry => entry.Subject.Contains(KitAssets_Bootstrapper.RECOVERY_SUBJECT, StringComparison.Ordinal)).ToList();

        // AGENT-FACING, not a text to the owner: what it clears is a blocker the SUPERVISOR holds,
        // and an alert the owner cannot act on does not go to the phone (decision 15).
        Assert.All(recoveries, entry => Assert.True(AppEntryAudience_Tag.Is_AgentTagged(entry.Subject), $"'{entry.Subject}' is not agent-tagged, so it would be texted to the owner"));

        Assert.True(
            recoveries.Count == 1,
            $"expected exactly one recovery entry, found {recoveries.Count}: {string.Join(" | ", Read_Entries().Select(entry => entry.Subject))}");

        Assert.Equal(PluginVerdicts.Ok, KitCheckHistory_Store.Read_LastVerdict_OrNull(_paths));
    }

    /// <summary>A check that PASSES and passes again says nothing — a repeat that is a notification is a waterfall.</summary>
    [Fact]
    public void AHostThatWasNeverBroken_WritesNoRecoveryEntry()
    {
        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, _log, PluginGate_Factory.Create());
        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, _log, PluginGate_Factory.Create());

        Assert.DoesNotContain(Read_Entries(), entry => entry.Subject.Contains(KitAssets_Bootstrapper.RECOVERY_SUBJECT, StringComparison.Ordinal));
    }

    IReadOnlyList<IChannelEntry> Read_Entries()
    {
        return ChannelEntry_Parser.Parse_All(File.ReadAllText(_paths.GeneralChannelFile));
    }

    static void Write_Kit(string folder, string supervisorProtocol)
    {
        Directory.CreateDirectory(Path.Combine(folder, "skills", "supervisor"));
        Directory.CreateDirectory(Path.Combine(folder, "hooks"));
        Directory.CreateDirectory(Path.Combine(folder, ".claude-plugin"));

        File.WriteAllText(Path.Combine(folder, "skills", "supervisor", "SKILL.md"), supervisorProtocol);
        File.WriteAllText(Path.Combine(folder, "hooks", "supervisor-ledger-check.sh"), "#!/usr/bin/env bash\n");
        File.WriteAllText(
            Path.Combine(folder, ".claude-plugin", "plugin.json"),
            new JsonObject { ["name"] = KitPlugin.NAME, ["version"] = KitPlugin.EXPECTED_VERSION }.ToJsonString());
    }

    void Write_InstallRecord(string commitSha)
    {
        Directory.CreateDirectory(Path.Combine(_claudeHome, InstalledPlugin_Reader.PLUGINS_FOLDER));

        var record = new JsonObject
        {
            ["version"] = 2,
            ["plugins"] = new JsonObject
            {
                [KitPlugin.ID] = new JsonArray(new JsonObject
                {
                    ["scope"] = "user",
                    ["installPath"] = _installed,
                    ["version"] = KitPlugin.EXPECTED_VERSION,
                    ["gitCommitSha"] = commitSha,
                }),
            },
        };

        File.WriteAllText(
            Path.Combine(_claudeHome, InstalledPlugin_Reader.PLUGINS_FOLDER, InstalledPlugin_Reader.INSTALLED_PLUGINS_FILE),
            record.ToJsonString());

        File.WriteAllText(
            Path.Combine(_claudeHome, InstalledPlugin_Reader.SETTINGS_FILE),
            new JsonObject { [InstalledPlugin_Reader.ENABLED_PLUGINS_KEY] = new JsonObject { [KitPlugin.ID] = true } }.ToJsonString());
    }
}
