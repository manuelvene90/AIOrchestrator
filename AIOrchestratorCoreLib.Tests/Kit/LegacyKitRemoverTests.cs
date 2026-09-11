using AIOrchestratorCoreLib.Kit;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Kit;

/// <summary>
/// THE UPGRADE HAZARD, PINNED. Measured on CLI 2.1.263: when ~/.claude/commands/supervisor.md and
/// the plugin's supervisor skill both answer to /supervisor, the LOCAL FILE WINS. Every machine that
/// upgrades to the plugin still has those files, so without this removal the stage would ship a host
/// that reports the new kit installed while every session reads the old text — a version number
/// actively lying, which is decisions 18 and 23 in their worst form.
/// </summary>
public class LegacyKitRemoverTests : IDisposable
{
    readonly string _home = Path.Combine(Path.GetTempPath(), $"aiorch-legacy-{Guid.NewGuid():N}");

    public LegacyKitRemoverTests()
    {
        Directory.CreateDirectory(Path.Combine(_home, "commands"));
        Directory.CreateDirectory(Path.Combine(_home, "hooks"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
            Directory.Delete(_home, recursive: true);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EveryRoleProtocolTheOldInstallerCopied_IsMovedAside()
    {
        foreach (var name in LegacyKit_Remover.LEGACY_COMMAND_FILES)
            File.WriteAllText(Path.Combine(_home, "commands", name), "stale");

        var (removed, stillThere) = LegacyKit_Remover.Remove(_home);

        Assert.Empty(stillThere);
        Assert.Equal(LegacyKit_Remover.LEGACY_COMMAND_FILES.Count, removed.Count);
        Assert.Empty(LegacyKit_Remover.Find_ShadowingCommands(_home));
    }

    [Fact]
    public void EveryHookTheOldInstallerCopied_IsMovedAside()
    {
        foreach (var name in LegacyKit_Remover.LEGACY_HOOK_FILES)
            File.WriteAllText(Path.Combine(_home, "hooks", name), "stale");

        LegacyKit_Remover.Remove(_home);

        foreach (var name in LegacyKit_Remover.LEGACY_HOOK_FILES)
            Assert.False(File.Exists(Path.Combine(_home, "hooks", name)), name);
    }

    /// <summary>
    /// It deletes the app's own footprint and NOTHING ELSE. Those folders are the person's, and a
    /// sweep that took a file they wrote themselves would be a far worse bug than the one it fixes.
    /// </summary>
    [Fact]
    public void SomebodyElsesCommandsAndHooks_AreNeverTouched()
    {
        File.WriteAllText(Path.Combine(_home, "commands", "my-own-command.md"), "mine");
        File.WriteAllText(Path.Combine(_home, "hooks", "my-own-hook.sh"), "mine");
        File.WriteAllText(Path.Combine(_home, "commands", "supervisor.md"), "stale");

        LegacyKit_Remover.Remove(_home);

        Assert.True(File.Exists(Path.Combine(_home, "commands", "my-own-command.md")));
        Assert.True(File.Exists(Path.Combine(_home, "hooks", "my-own-hook.sh")));
        Assert.False(File.Exists(Path.Combine(_home, "commands", "supervisor.md")));
    }

    /// <summary>
    /// NOTHING IS EVER UNLINKED. The names this sweep acts on are generic — reviewer.md, solo.md,
    /// communicator.md — so a file this app never wrote can carry one, and deleting it would destroy
    /// somebody's own work unrecoverably at an app start they did not ask for. Moving it aside breaks
    /// the shadow just as completely (a command is resolved by its .md name, and that name is gone)
    /// while leaving every byte on disk next to the original.
    /// </summary>
    [Fact]
    public void EveryFileItTouches_IsStillOnDisk_UnderTheMovedAsideName()
    {
        var mine = Path.Combine(_home, "commands", "reviewer.md");
        File.WriteAllText(mine, "a reviewer command somebody wrote for something else entirely");

        LegacyKit_Remover.Remove(_home);

        Assert.False(File.Exists(mine), "it must no longer answer the slash word");
        Assert.True(File.Exists(mine + LegacyKit_Remover.MOVED_ASIDE_SUFFIX), "and it must not be gone");
        Assert.Equal(
            "a reviewer command somebody wrote for something else entirely",
            File.ReadAllText(mine + LegacyKit_Remover.MOVED_ASIDE_SUFFIX));
    }

    /// <summary>A second app start must not fail because the first one already put one aside.</summary>
    [Fact]
    public void ASecondRun_OverwritesWhatTheFirstPutAside_RatherThanFailing()
    {
        var file = Path.Combine(_home, "commands", "supervisor.md");
        File.WriteAllText(file, "first");
        LegacyKit_Remover.Remove(_home);

        File.WriteAllText(file, "second");
        var (movedAside, stillThere) = LegacyKit_Remover.Remove(_home);

        Assert.Empty(stillThere);
        Assert.Contains(movedAside, f => f.EndsWith("supervisor.md", StringComparison.Ordinal));
        Assert.Equal("second", File.ReadAllText(file + LegacyKit_Remover.MOVED_ASIDE_SUFFIX));
    }

    [Fact]
    public void AFreshMachineWithNoLegacyFolders_RemovesNothingAndDoesNotThrow()
    {
        var fresh = Path.Combine(Path.GetTempPath(), $"aiorch-fresh-{Guid.NewGuid():N}");

        var (removed, stillThere) = LegacyKit_Remover.Remove(fresh);

        Assert.Empty(removed);
        Assert.Empty(stillThere);
        Assert.Empty(LegacyKit_Remover.Find_ShadowingCommands(fresh));
    }

    /// <summary>
    /// The shadow is read by LOOKING, after the removal, never by trusting that the delete worked —
    /// a delete that threw and a delete that succeeded must not produce the same verdict.
    /// </summary>
    [Fact]
    public void AStaleProtocolThatSurvives_IsReportedAsAShadow_AndRefusedByTheVerifier()
    {
        File.WriteAllText(Path.Combine(_home, "commands", "supervisor.md"), "stale");

        var shadowing = LegacyKit_Remover.Find_ShadowingCommands(_home);

        Assert.Single(shadowing);

        var reading = new InstalledPluginReading(KitPlugin.EXPECTED_VERSION, "/cache", enabled: true, null);
        var verdict = PluginVersion_Verifier.Decide(reading, KitPlugin.EXPECTED_VERSION, shadowing);

        Assert.Equal(PluginVerdicts.Shadowed, verdict);
        Assert.Contains("supervisor.md", PluginVersion_Verifier.Describe(verdict, reading, KitPlugin.EXPECTED_VERSION, KitPlugin.ID, shadowing));
    }
}
