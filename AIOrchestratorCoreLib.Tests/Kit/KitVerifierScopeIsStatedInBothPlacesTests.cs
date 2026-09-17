using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// TWO COMPONENTS, ONE QUESTION, TWO FILE SETS — AND ONE OF THEM SAID "BYTE-IDENTICAL" ABOUT A
/// SUBSET. Decision 12's shape, measured on the production VPS on 2026-09-17 (read from
/// <c>journalctl -u aiorchestrator</c>, orch@159.195.254.120).
///
/// <para>
/// At 20:03:22 the daemon logged *"Kit check OK — aiorch@aiorch-local 1.0.0 at
/// /home/orch/.claude/plugins/cache/aiorch-local/aiorch/1.0.0 · content verified — the installed
/// files are byte-identical to this build's kit"*. Minutes later <c>bash kit/install.sh</c>, against
/// that same cache, printed *"aiorch: the installed copy differs from this checkout —
/// REINSTALLING"*. Both verdicts were true of their own scope: <c>kit/install.sh</c> was the ONLY
/// kit file changed between the running build's commit and HEAD
/// (<c>git diff --stat f3f1dfe..40f2797 -- kit/</c>), it IS present in the plugin cache, and
/// <see cref="KitContent_Digest"/> does not read it. The defect was the SENTENCE.
/// </para>
/// <para>
/// Nothing was at risk that day — a setup script is not text a session loads — and that is the
/// reason this is fixed as a CLAIM rather than as an incident: the next file to fall outside the
/// digest might be one a session reads, and the log would have said "byte-identical" about it too.
/// </para>
/// <para>
/// THE SCOPES STAY DIFFERENT, DELIBERATELY. <c>install.sh</c>'s verdict is "reinstall": cheap,
/// correct for any drift, and it stops nobody. The host's verdict is "refuse to spawn": it stops all
/// work on the machine. Widening the host to the whole tree — the option that would make the two
/// agree — would let a stale <c>README.md</c> or a <c>statusline/fixtures</c> file refuse every
/// session, which is the 2026-09-07 incident reached from the other side. So the host's claim was
/// narrowed to the size of its check, the one genuine hole in that check was closed (the channel
/// grammar, which a session DOES read), and the difference between the two scopes is now stated in
/// both places and pinned here — the only thing that stops them drifting apart in silence again.
/// </para>
/// </summary>
public class KitVerifierScopeIsStatedInBothPlacesTests : IDisposable
{
    readonly string _temp;
    readonly string _kit;
    readonly string _installed;
    readonly string _claudeHome;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationLog _log;

    public KitVerifierScopeIsStatedInBothPlacesTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), $"aiorch-kit-scope-{Guid.NewGuid():N}");
        _kit = Path.Combine(_temp, "kit");
        _installed = Path.Combine(_temp, "installed-kit");
        _claudeHome = Path.Combine(_temp, "claude-home");
        _paths = SupervisionPaths_Factory.Create(Path.Combine(_temp, "supervision"));
        _log = OrchestrationLog_Factory.Create(_paths);

        Directory.CreateDirectory(Path.Combine(_kit, "statusline"));
        File.WriteAllText(Path.Combine(_kit, "statusline", "statusline.ps1"), "# ps1\n");
        File.WriteAllText(Path.Combine(_kit, "statusline", "statusline.sh"), "#!/usr/bin/env bash\n");

        Write_Kit(_kit);
        Write_Kit(_installed);

        Directory.CreateDirectory(Path.Combine(_claudeHome, "commands"));
        Directory.CreateDirectory(Path.Combine(_claudeHome, "hooks"));
        Write_InstallRecord();

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

    // ── The claim ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE DEFECT, END TO END: the passing line must not speak for files this host never opened.
    /// Asserted on the sentence that was measured false, and on the scope being named instead —
    /// a reader of the log has to be able to tell what was compared without reading the source.
    /// </summary>
    [Fact]
    public void ThePassingKitCheck_DoesNotClaimTheWholeKit_ItNamesWhatItCompared()
    {
        var gate = PluginGate_Factory.Create();

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, _log, gate);

        Assert.Equal(PluginVerdicts.Ok, gate.Verdict);

        var logged = File.ReadAllText(_paths.GlobalLogFile);

        Assert.Contains("Kit ok", logged);

        // The exact sentence from the VPS at 20:03:22. It was false about the cache in front of it.
        Assert.DoesNotContain("byte-identical to this build's kit", logged);

        // ...and what replaces it: a SCOPED subject. The whole honesty of this line is that its
        // subject is the files a session reads and never "the kit", so the claim cannot grow back.
        Assert.Contains($"{KitContent_Digest.SCOPE_SUBJECT} match this build", logged);

        // AND IT STAYS SHORT. The owner never acts on this line — it is written once per host start
        // and it says "nothing to do" — so it must not carry the five filenames the REFUSAL carries.
        // A heartbeat the size of an alarm is how a log stops being read (decision 14's instinct).
        Assert.DoesNotContain(KitContent_Digest.Describe_Scope(), logged);
    }

    /// <summary>
    /// THE ONE MESSAGE OF THIS FAMILY THAT REACHES THE OWNER'S PHONE, and the only part of it they
    /// act on is the command — which used to be the last words of a paragraph. The fix comes before
    /// the explanation, and the explanation still carries the file list, because there the reader
    /// has something to do with it.
    /// </summary>
    [Fact]
    public void TheRefusal_LeadsWithTheFix_AndTheReasonComesAfter()
    {
        var reading = new InstalledPluginReading(KitPlugin.EXPECTED_VERSION, "/cache/aiorch/1.0.0", enabled: true, null, null);

        var refusal = PluginVersion_Verifier.Describe(
            PluginVerdicts.ContentMismatch, reading, KitPlugin.EXPECTED_VERSION, KitPlugin.ID, null, null, contentMatches: false);

        Assert.NotNull(refusal);
        Assert.Contains("sessions cannot start", refusal);

        var fix = refusal.IndexOf("Fix:", StringComparison.Ordinal);
        var why = refusal.IndexOf("Why:", StringComparison.Ordinal);

        Assert.True(fix >= 0 && why > fix, $"the fix must come before the reason, and both must be there: '{refusal}'");
        Assert.Contains(KitPlugin.INSTALLER_COMMAND, refusal[fix..why]);
        Assert.Contains(KitContent_Digest.Describe_Scope(), refusal[why..]);
    }

    /// <summary>
    /// The recovery entry the general supervisor reads carried the same overstatement, and it is
    /// read as a LOG by an agent that will quote it. Same correction, same reason.
    /// </summary>
    [Fact]
    public void TheRecoveryEntry_MakesTheSameNarrowedClaim()
    {
        File.WriteAllText(Path.Combine(_installed, "skills", "supervisor", "SKILL.md"), "a DIFFERENT protocol\n");
        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, _log, PluginGate_Factory.Create());

        Write_Kit(_installed);
        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, _log, PluginGate_Factory.Create());

        var channel = File.ReadAllText(_paths.GeneralChannelFile);

        Assert.Contains(KitAssets_Bootstrapper.RECOVERY_SUBJECT, channel);
        Assert.DoesNotContain("byte-identical to this build's kit", channel);
    }

    // ── The one genuine hole in the check ────────────────────────────────────────────────────

    /// <summary>
    /// THE GRAMMAR IS TEXT A SESSION READS, so a stale one is exactly the drift this digest exists
    /// to catch — and it was invisible to it. <c>kit/bin/channel-append.sh</c> resolves its grammar
    /// at <c>../grammar/channel-grammar.json</c> relative to itself, out of the same plugin cache,
    /// and refuses typed entries without it. A cache holding an older grammar is a session writing
    /// channel entries against a shape this build was not made against, under a log line saying the
    /// content was verified.
    /// </summary>
    [Fact]
    public void AStaleChannelGrammar_IsAMismatch_NotAnOk()
    {
        File.WriteAllText(
            Path.Combine(_installed, "grammar", "channel-grammar.json"),
            new JsonObject { ["markers"] = new JsonObject { ["state"] = "STATE:" } }.ToJsonString());

        Assert.Equal(false, KitContent_Digest.Same_Content(_kit, _installed));

        var gate = PluginGate_Factory.Create();

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, _log, gate);

        Assert.Equal(PluginVerdicts.ContentMismatch, gate.Verdict);
        Assert.False(gate.Spawning_Allowed);
    }

    /// <summary>
    /// And the folder is the real one: the guard above is worthless if the kit's grammar moves and
    /// the scope keeps naming a folder nothing lives in. Reads the BRANCH SOURCE (decision 18) —
    /// <c>kit/</c> in this checkout, found from the test binary's own folder.
    /// </summary>
    [Fact]
    public void TheScope_NamesFoldersTheRealKitActuallyHas()
    {
        // EACH PATH RESOLVED ON ITS OWN, not `Find("kit")` then Combine. macOS is case-insensitive,
        // so `Find("kit")` matches this test project's own `Kit/` folder — which has no `skills` in
        // it — and the guard fails for a reason that has nothing to do with the kit. Asking for
        // `kit/skills` walks past that folder to the one that has it.
        foreach (var folder in KitContent_Digest.DIGESTED_FOLDERS)
        {
            var found = KitRepoFiles.Find(Path.Combine("kit", folder));

            Assert.True(found != null && Directory.Exists(found), $"kit/{folder} is in the verifier's scope and does not exist in this checkout");
        }

        foreach (var file in KitContent_Digest.DIGESTED_FILES)
        {
            var found = KitRepoFiles.Find(Path.Combine("kit", file));

            Assert.True(found != null && File.Exists(found), $"kit/{file} is in the verifier's scope and does not exist in this checkout");
        }

        // The reason `grammar` is in the scope, from the file that gives it: the channel tool reads
        // the grammar out of the folder beside it, which on a session is the plugin cache.
        var channelTool = KitRepoFiles.Find(Path.Combine("kit", "bin", "channel-append.sh"));

        Assert.NotNull(channelTool);

        var toolText = File.ReadAllText(channelTool);

        // Asserted in two pieces because the tool builds the path rather than writing it: it cd's to
        // `$(dirname $BASH_SOURCE)/../grammar` and appends the filename.
        Assert.Contains("/../grammar", toolText);
        Assert.Contains("channel-grammar.json", toolText);
    }

    // ── The two scopes cannot drift apart in silence ─────────────────────────────────────────

    /// <summary>
    /// BOTH INSTALLERS RESTATE THE HOST'S SCOPE, VERBATIM. They compare the WHOLE folder and they
    /// have to, so the two components genuinely differ; what must never happen again is a reader
    /// unable to tell why. Adding a folder to the digest and nowhere else turns this red.
    /// </summary>
    [Theory]
    [InlineData("install.sh", @"host_verified_scope='([^']*)'")]
    [InlineData("install.ps1", @"\$hostVerifiedScope = @\(([^)]*)\)")]
    public void EachInstaller_RestatesTheHostsScope_ExactlyAsTheHostDeclaresIt(string installer, string pattern)
    {
        var path = KitRepoFiles.Find(Path.Combine("kit", installer));

        Assert.NotNull(path);

        var match = Regex.Match(File.ReadAllText(path), pattern);

        Assert.True(match.Success, $"kit/{installer} does not state the host's verified scope — the two components would be free to disagree in silence again");

        var stated = string.Join(
            ' ',
            Regex.Matches(match.Groups[1].Value, @"[A-Za-z0-9_.\-/]+").Select(entry => entry.Value));

        Assert.Equal(KitContent_Digest.SCOPE_DECLARATION, stated);
    }

    /// <summary>
    /// ...and each installer says WHICH component a difference will be reported by, because "the
    /// installed copy differs" on its own is what made the two 2026-09-17 verdicts read as a
    /// contradiction rather than as two scopes.
    /// </summary>
    [Theory]
    [InlineData("install.sh")]
    [InlineData("install.ps1")]
    public void EachInstaller_SaysWhetherTheHostWillRefuseOrNot(string installer)
    {
        var path = KitRepoFiles.Find(Path.Combine("kit", installer));

        Assert.NotNull(path);

        var text = File.ReadAllText(path);

        Assert.Contains("OUTSIDE the set the host checks", text);
        Assert.Contains("INSIDE the set the host checks", text);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────

    static void Write_Kit(string folder)
    {
        Directory.CreateDirectory(Path.Combine(folder, "skills", "supervisor"));
        Directory.CreateDirectory(Path.Combine(folder, "hooks"));
        Directory.CreateDirectory(Path.Combine(folder, "bin"));
        Directory.CreateDirectory(Path.Combine(folder, "grammar"));
        Directory.CreateDirectory(Path.Combine(folder, ".claude-plugin"));

        File.WriteAllText(Path.Combine(folder, "skills", "supervisor", "SKILL.md"), "the supervisor protocol\n");
        File.WriteAllText(Path.Combine(folder, "hooks", "supervisor-ledger-check.sh"), "#!/usr/bin/env bash\n");
        File.WriteAllText(Path.Combine(folder, "bin", "channel-append.sh"), "#!/usr/bin/env bash\n");
        File.WriteAllText(
            Path.Combine(folder, "grammar", "channel-grammar.json"),
            new JsonObject { ["markers"] = new JsonObject { ["state"] = "STATE:" }, ["header"] = "index" }.ToJsonString());
        File.WriteAllText(
            Path.Combine(folder, ".claude-plugin", "plugin.json"),
            new JsonObject { ["name"] = KitPlugin.NAME, ["version"] = KitPlugin.EXPECTED_VERSION }.ToJsonString());

        // THE FILE FROM THE INCIDENT, present in the cache and OUTSIDE the digest's scope: the
        // fixtures carry it so the guard below is asserting over the real shape, where a whole-tree
        // comparison and this one genuinely disagree.
        File.WriteAllText(Path.Combine(folder, "install.sh"), "#!/usr/bin/env bash\n# the installer\n");
    }

    void Write_InstallRecord()
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
