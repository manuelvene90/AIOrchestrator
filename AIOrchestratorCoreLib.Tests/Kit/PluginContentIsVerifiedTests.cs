using AIOrchestratorCoreLib.Build;
using AIOrchestratorCoreLib.Kit;
using AIOrchestratorCoreLib.Kit.PluginGate;
using AIOrchestratorCoreLib.Logging.OrchestrationLog;
using AIOrchestratorCoreLib.SupervisionPaths;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE VERSION NUMBER IS NOT THE KIT. This is decision 18 — "say which copy you read" — one floor
/// below where it was written.
///
/// <para>
/// MEASURED 2026-09-07 on this Mac, CLI 2.1.263, in a throwaway Claude home: install a plugin from a
/// local-directory marketplace, commit a change to it WITHOUT bumping
/// <c>kit/.claude-plugin/plugin.json</c>, then run <c>claude plugin update aiorch</c>. It answers
/// *"aiorch is already at the latest version (1.0.0)"*, the cached files still hold the old text, and
/// the recorded <c>gitCommitSha</c> still names the old commit. <c>claude plugin marketplace update</c>
/// first changes nothing. Only uninstall-then-install refreshes either. That is exactly what happened
/// on the VPS on 2026-09-07: the installer said "installed and enabled 1.0.0" and the daemon said
/// "Kit check OK" over a cache still holding stage 1c.
/// </para>
/// <para>
/// So the host compares CONTENT, by the only content identity both sides have: the commit. The
/// installed record carries <c>gitCommitSha</c> — present even for a local-directory marketplace, and
/// NOT exposed by <c>claude plugin list --json</c>, which is why the reader reads the files — and the
/// binary carries the commit it was built from, stamped into AssemblyInformationalVersion.
/// </para>
/// </summary>
public class PluginContentIsVerifiedTests : IDisposable
{
    /// <summary>A real forty-hex sha shape, and deliberately not any commit of this repository.</summary>
    const string OLD_COMMIT = "1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c1c";

    const string NEW_COMMIT = "d2a405b4bf1058fe440e1ed596e00eee675fecda";

    readonly string _temp;
    readonly string _kit;
    readonly string _claudeHome;
    readonly ISupervisionPaths _paths;

    public PluginContentIsVerifiedTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), $"aiorch-plugin-content-{Guid.NewGuid():N}");
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

    // ── The reading, from a real installed_plugins.json shape ────────────────────────────────

    [Fact]
    public void TheInstalledRecordsCommit_IsRead_FromTheFileTheCliWrites()
    {
        Write_InstalledPlugins(KitPlugin.EXPECTED_VERSION, OLD_COMMIT);

        Assert.Equal(OLD_COMMIT, InstalledPlugin_Reader.Read(_claudeHome, KitPlugin.ID).CommitSha);
    }

    /// <summary>An older CLI, or an install with no git behind it, records none — and that is not a lie.</summary>
    [Fact]
    public void ARecordWithNoCommit_ReadsAsNull_NotAsAnEmptyString()
    {
        Write_InstalledPlugins(KitPlugin.EXPECTED_VERSION, commitSha: null);

        Assert.Null(InstalledPlugin_Reader.Read(_claudeHome, KitPlugin.ID).CommitSha);
    }

    // ── The verdict ──────────────────────────────────────────────────────────────────────────

    /// <summary>THE DEFECT: the right number over the wrong text, which today reads as OK.</summary>
    [Fact]
    public void AStaleCacheAtTheRightVersion_IsAContentMismatch_NotAnOk()
    {
        var reading = new InstalledPluginReading(KitPlugin.EXPECTED_VERSION, "/cache", enabled: true, null, OLD_COMMIT);

        Assert.Equal(
            PluginVerdicts.ContentMismatch,
            PluginVersion_Verifier.Decide(reading, KitPlugin.EXPECTED_VERSION, null, NEW_COMMIT));
    }

    [Fact]
    public void TheSameCommitOnBothSides_IsOk()
    {
        var reading = new InstalledPluginReading(KitPlugin.EXPECTED_VERSION, "/cache", enabled: true, null, NEW_COMMIT);

        Assert.Equal(
            PluginVerdicts.Ok,
            PluginVersion_Verifier.Decide(reading, KitPlugin.EXPECTED_VERSION, null, NEW_COMMIT));
    }

    /// <summary>
    /// AN UNANSWERABLE QUESTION IS NOT A REFUSAL. A build with no stamp, or a record with no sha,
    /// leaves the content unknown; refusing there would strand every machine that builds from a
    /// tarball. The OK line says so out loud instead — asserted below.
    /// </summary>
    [Theory]
    [InlineData(null, NEW_COMMIT)]
    [InlineData(OLD_COMMIT, null)]
    [InlineData(null, null)]
    public void WhenEitherSideCannotAnswer_TheContentCheckIsSkipped_NotFailed(string? installedSha, string? buildSha)
    {
        var reading = new InstalledPluginReading(KitPlugin.EXPECTED_VERSION, "/cache", enabled: true, null, installedSha);

        Assert.Equal(PluginVerdicts.Ok, PluginVersion_Verifier.Decide(reading, KitPlugin.EXPECTED_VERSION, null, buildSha));
    }

    /// <summary>
    /// The refusal has to name the update that will NOT work. The operator's reflex is
    /// `claude plugin update`, which answers "already at the latest version" and changes nothing — so
    /// a message that only said "reinstall" would be followed by an update, a success line, and the
    /// same refusal on the next start.
    /// </summary>
    [Fact]
    public void TheContentRefusal_NamesBothCommits_ThePath_AndSaysUpdateWillNotWork()
    {
        var reading = new InstalledPluginReading(KitPlugin.EXPECTED_VERSION, "/cache/aiorch/1.0.0", enabled: true, null, OLD_COMMIT);
        var verdict = PluginVersion_Verifier.Decide(reading, KitPlugin.EXPECTED_VERSION, null, NEW_COMMIT);

        var message = PluginVersion_Verifier.Describe(verdict, reading, KitPlugin.EXPECTED_VERSION, KitPlugin.ID, null, NEW_COMMIT);

        Assert.NotNull(message);
        Assert.Contains(OLD_COMMIT, message);
        Assert.Contains(NEW_COMMIT, message);
        Assert.Contains("/cache/aiorch/1.0.0", message);
        Assert.Contains("will NOT fix it", message);
        Assert.Contains("uninstall", message);
    }

    /// <summary>
    /// A version mismatch still wins when somehow both are wrong: it is the more actionable message,
    /// and the same ordering the disabled check already follows.
    /// </summary>
    [Fact]
    public void AWrongVersion_IsStillReportedAsAVersionMismatch_EvenWithAWrongCommitToo()
    {
        var reading = new InstalledPluginReading("0.9.0", "/cache", enabled: true, null, OLD_COMMIT);

        Assert.Equal(PluginVerdicts.VersionMismatch, PluginVersion_Verifier.Decide(reading, "1.0.0", null, NEW_COMMIT));
    }

    // ── End to end, through the startup check ────────────────────────────────────────────────

    /// <summary>
    /// THE FAKE OLD CACHE, driven through the real startup sequence: the gate closes, spawning stops,
    /// and the owner is handed the four things — expected, found, where, and the command. This is the
    /// verdict the VPS should have got on 2026-09-07 and did not.
    /// </summary>
    [Fact]
    public void AnOldCache_ClosesTheGate_AndTellsTheOperatorToReinstall()
    {
        Assert_TheBuildIsStamped();

        Write_InstalledPlugins(KitPlugin.EXPECTED_VERSION, OLD_COMMIT);
        Enable_Plugin();

        var gate = PluginGate_Factory.Create();

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, OrchestrationLog_Factory.Create(_paths), gate);

        Assert.Equal(PluginVerdicts.ContentMismatch, gate.Verdict);
        Assert.False(gate.Spawning_Allowed);
        Assert.Contains(OLD_COMMIT, gate.Refusal);
        Assert.Contains("uninstall", gate.Refusal);
    }

    /// <summary>The other half: the cache taken from THIS build's commit opens the gate.</summary>
    [Fact]
    public void ACacheFromThisBuildsOwnCommit_OpensTheGate()
    {
        Assert_TheBuildIsStamped();

        Write_InstalledPlugins(KitPlugin.EXPECTED_VERSION, BuildCommit_Reader.Read_RunningBuildCommit_OrNull());
        Enable_Plugin();

        var gate = PluginGate_Factory.Create();

        KitAssets_Bootstrapper.Ensure_Installed(_kit, _claudeHome, _paths, OrchestrationLog_Factory.Create(_paths), gate);

        Assert.Equal(PluginVerdicts.Ok, gate.Verdict);
        Assert.True(gate.Spawning_Allowed);
    }

    // ── The stamp itself ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// PINS THE MSBUILD TARGET. Without it every assertion above still passes — the content check
    /// simply never runs — which is the shape of green that let the original defect live: a check
    /// that cannot fail because it never asks. If this goes red on a machine building WITHOUT git,
    /// that is expected and the host then reports "content NOT VERIFIED" rather than refusing.
    /// </summary>
    [Fact]
    public void ThisBuildCarriesTheCommitItWasBuiltFrom()
    {
        Assert.NotNull(BuildCommit_Reader.Read_RunningBuildCommit_OrNull());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("1.0.0", null)]
    [InlineData("1.0.0+", null)]
    [InlineData("1.0.0+nightly", null)]
    [InlineData("1.0.0+abc12", null)]
    [InlineData("1.0.0+ABC1234", "abc1234")]
    [InlineData("1.0.0-beta.1+deadbeef", "deadbeef")]
    [InlineData("1.0.0+label+deadbeef", "deadbeef")]
    public void TheStampIsReadOffTheInformationalVersion_OrRefused(string? informational, string? expected)
    {
        Assert.Equal(expected, BuildCommit_Reader.Extract_CommitSha_OrNull(informational));
    }

    /// <summary>
    /// An abbreviation on either side names the same commit. Both sides are full shas today, so this
    /// could be equality — it is a prefix check because the day one of them is shortened, equality
    /// would produce a host that refuses to spawn while its message shows two identical-looking shas.
    /// </summary>
    [Theory]
    [InlineData(NEW_COMMIT, NEW_COMMIT, true)]
    [InlineData(NEW_COMMIT, "d2a405b", true)]
    [InlineData("D2A405B", NEW_COMMIT, true)]
    [InlineData(NEW_COMMIT, OLD_COMMIT, false)]
    [InlineData(NEW_COMMIT, "d2a40", false)]
    [InlineData(NEW_COMMIT, null, false)]
    [InlineData(null, null, false)]
    public void AnAbbreviatedShaStillNamesTheSameCommit(string? left, string? right, bool same)
    {
        Assert.Equal(same, BuildCommit_Reader.Names_TheSameCommit(left, right));
    }

    static void Assert_TheBuildIsStamped()
    {
        Assert.True(
            BuildCommit_Reader.Read_RunningBuildCommit_OrNull() != null,
            "this build carries no commit stamp, so the content check is skipped and this test would "
            + "be green without proving anything — see ThisBuildCarriesTheCommitItWasBuiltFrom");
    }

    void Write_InstalledPlugins(string version, string? commitSha)
    {
        var sha = commitSha == null ? "" : $", \"gitCommitSha\": \"{commitSha}\"";

        Directory.CreateDirectory(Path.Combine(_claudeHome, "plugins"));

        File.WriteAllText(
            Path.Combine(_claudeHome, "plugins", InstalledPlugin_Reader.INSTALLED_PLUGINS_FILE),
            $$"""
            { "version": 2, "plugins": { "{{KitPlugin.ID}}": [ { "scope": "user", "installPath": "/cache/aiorch/1.0.0", "version": "{{version}}"{{sha}} } ] } }
            """);
    }

    void Enable_Plugin()
    {
        File.WriteAllText(
            Path.Combine(_claudeHome, "settings.json"),
            $$"""{ "enabledPlugins": { "{{KitPlugin.ID}}": true } }""");
    }
}
