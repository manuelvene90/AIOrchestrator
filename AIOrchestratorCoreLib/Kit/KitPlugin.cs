namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// WHO THE KIT IS, as the app expects to find it installed. These four words are the whole contract
/// between a built host and the protocols its sessions will actually read.
///
/// EXPECTED_VERSION is not decoration and it is not a build number: it is the assertion "the kit I
/// was built against is the kit that is installed". <see cref="KitPluginIdentityTests"/> holds it
/// equal to kit/.claude-plugin/plugin.json, so a protocol change that forgets to bump the manifest
/// fails the suite rather than shipping a host that certifies a kit it has never seen.
///
/// IT IS NOT ENOUGH ON ITS OWN, AND SAYING SO IS THE 2026-09-07 CORRECTION. A number only catches the
/// change that remembered to bump it. On the VPS on 2026-09-07 the installer said "installed and
/// enabled 1.0.0" and the daemon said "Kit check OK" over a plugin cache still holding stage 1c —
/// because `claude plugin update` compares the version string and this constant compares the number,
/// not the text. The content question is answered beside it, by comparing the installed record's
/// gitCommitSha with the commit the host was built from (<see cref="Build.BuildCommit_Reader"/>).
///
/// Decisions 17/18/23 are why this exists at all. The kit used to travel as four derived copies —
/// branch source, build output, installed folder, and whatever binary happened to be running — and
/// each of the three decisions was written after an evening lost to telling them apart. A plugin has
/// ONE installed copy with a version stamped on it, so the question becomes a comparison instead of
/// an investigation.
/// </summary>
public static class KitPlugin
{
    public const string NAME = "aiorch";
    public const string MARKETPLACE = "aiorch-local";

    /// <summary>The id both `claude plugin list --json` and installed_plugins.json key on.</summary>
    public const string ID = $"{NAME}@{MARKETPLACE}";

    /// <summary>Must equal the "version" in kit/.claude-plugin/plugin.json. Bump both together.</summary>
    public const string EXPECTED_VERSION = "1.0.0";

    /// <summary>What to tell a human to run when the installed copy is not this one.</summary>
    public const string UPDATE_COMMAND = $"claude plugin update {NAME}";

    /// <summary>
    /// What to run when the version is right and the TEXT is not — the only thing that works.
    ///
    /// MEASURED 2026-09-07, CLI 2.1.263: `claude plugin update` compares the version STRING, so a
    /// commit that changes a role protocol without bumping plugin.json leaves the cached copy
    /// untouched and reports "already at the latest version (1.0.0)"; `claude plugin marketplace
    /// update` first does not help either. Uninstall-then-install is what refreshes both the files
    /// and the recorded gitCommitSha. `kit/install.sh` does this by itself now.
    /// </summary>
    public const string REINSTALL_COMMAND =
        $"bash kit/install.sh   (or by hand: claude plugin uninstall {NAME} && claude plugin install {ID} --scope user -y)";

    /// <summary>What to tell a human to run when it is not installed at all.</summary>
    public const string INSTALL_COMMAND = "bash kit/install.sh   (Windows: pwsh kit/install.ps1)";
}
