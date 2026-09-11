namespace AIOrchestratorCoreLib.Kit;

/// <summary>
/// Turns a reading of the installed kit into a verdict AND the sentence a human is shown. Pure: no
/// files, no clock, no logging — so every branch below is asserted rather than argued.
///
/// The message always names the same four things, because those are exactly what the evenings lost
/// to decisions 18 and 23 were spent guessing: what was EXPECTED, what was FOUND, WHERE the found
/// copy lives, and the COMMAND that fixes it. "Plugin mismatch" on its own is the silence again.
/// </summary>
public static class PluginVersion_Verifier
{
    /// <summary>
    /// <paramref name="shadowingCommands"/> is what <see cref="LegacyKit_Remover.Find_ShadowingCommands"/>
    /// found still sitting in ~/.claude/commands. It is checked FIRST because it is the only failure
    /// here that a correct version number actively hides.
    /// </summary>
    /// <param name="expectedCommitSha">
    /// The commit this host was BUILT from (<see cref="Build.BuildCommit_Reader"/>). Null when the
    /// build carried no stamp, and then the content check is SKIPPED rather than failed — a host that
    /// cannot answer the question must not answer it with a refusal. The caller says so out loud
    /// instead; see <c>KitAssets_Bootstrapper</c>.
    /// </param>
    /// <param name="contentMatches">
    /// WHAT THE FILES SAY, when they could be read: true when the installed tree is byte-identical
    /// to the one this host ships (<see cref="KitContent_Digest"/>), false when it is not, null when
    /// one of the two trees could not be digested.
    ///
    /// IT OUTRANKS THE COMMIT, in both directions, and that is the 2026-09-07 correction. The commit
    /// is a proxy for the content: on the VPS a stage that touched no file under <c>kit/</c> still
    /// moved HEAD, the recorded gitCommitSha differed from the build's, and a kit that was identical
    /// was refused — no session could start, over a number. Where the content itself is known the
    /// proxy has no vote; where it is not, the commit is still the best question available.
    /// </param>
    public static PluginVerdicts Decide(
        InstalledPluginReading reading,
        string expectedVersion,
        IReadOnlyList<string>? shadowingCommands = null,
        string? expectedCommitSha = null,
        bool? contentMatches = null)
    {
        if (shadowingCommands is { Count: > 0 })
            return PluginVerdicts.Shadowed;

        if (reading.UnreadableReason != null)
            return PluginVerdicts.Unreadable;

        if (!reading.Is_Installed)
            return PluginVerdicts.NotInstalled;

        if (reading.Version != expectedVersion)
            return PluginVerdicts.VersionMismatch;

        // THE NUMBER IS RIGHT; IS THE TEXT? Asked of the FILES first, because they are the thing the
        // question is actually about — the commit below is only ever a stand-in for them.
        if (contentMatches == true)
            return reading.Enabled ? PluginVerdicts.Ok : PluginVerdicts.Disabled;

        if (contentMatches == false)
            return PluginVerdicts.ContentMismatch;

        // Neither tree could be digested, so the proxy is the best question left. Only asked when
        // BOTH sides can answer it: an unstamped build or an install record with no gitCommitSha
        // leaves the question open, and an open question is reported by the caller, not converted
        // into a refusal here. Checked after the version because a wrong version is the more
        // actionable message when somehow both are wrong.
        if (expectedCommitSha != null
            && reading.CommitSha != null
            && !Build.BuildCommit_Reader.Names_TheSameCommit(reading.CommitSha, expectedCommitSha))
        {
            return PluginVerdicts.ContentMismatch;
        }

        // Checked LAST on purpose: an operator whose plugin is both out of date and switched off is
        // better told about the version, because updating is what they have to do either way.
        return reading.Enabled ? PluginVerdicts.Ok : PluginVerdicts.Disabled;
    }

    /// <summary>
    /// The refusal a human reads, in the log and on their phone. Null when nothing is wrong —
    /// callers use that null as "spawning is allowed" rather than re-deciding the verdict.
    /// </summary>
    public static string? Describe(
        PluginVerdicts verdict,
        InstalledPluginReading reading,
        string expectedVersion,
        string pluginId,
        IReadOnlyList<string>? shadowingCommands = null,
        string? expectedCommitSha = null,
        bool? contentMatches = null)
    {
        var found = reading.Version ?? "nothing installed";
        var where = reading.InstallPath ?? "no install path on record";

        return verdict switch
        {
            PluginVerdicts.Ok => null,

            PluginVerdicts.Unchecked => null,

            PluginVerdicts.NotInstalled =>
                $"The {pluginId} kit is NOT INSTALLED under this Claude home, so no session would read a role protocol. "
                + $"Expected {expectedVersion}. Install it: {KitPlugin.INSTALL_COMMAND}",

            PluginVerdicts.VersionMismatch =>
                $"The installed {pluginId} kit is the WRONG VERSION — expected {expectedVersion}, found {found}, at {where}. "
                + $"Update it: {KitPlugin.UPDATE_COMMAND}",

            // NAMES THE UPDATE THAT WILL NOT WORK, on purpose. The operator's reflex is `claude plugin
            // update aiorch`, and it answers "already at the latest version (1.0.0)" while changing
            // nothing (measured 2026-09-07, CLI 2.1.263) — so a message that only said "reinstall"
            // would be followed by an update, a success line, and the same refusal on the next start.
            // NAMES WHICH QUESTION FAILED, because the two are not equally strong and an operator
            // has to know which one they are being refused over: the files differing is a fact, the
            // commits differing is a suspicion that the file comparison could not confirm.
            PluginVerdicts.ContentMismatch when contentMatches == false =>
                $"The installed {pluginId} kit is version {found} — the right NUMBER over the WRONG TEXT. Its files at "
                + $"{where} DIFFER from the ones this host ships, so its sessions would read protocols this build was "
                + $"not made against. `{KitPlugin.UPDATE_COMMAND}` will NOT fix it: it compares the version string and "
                + $"reports success. Reinstall it: {KitPlugin.REINSTALL_COMMAND}",

            PluginVerdicts.ContentMismatch =>
                $"The installed {pluginId} kit is version {found} — the right NUMBER over a TEXT THIS HOST COULD NOT "
                + $"COMPARE. It was taken from commit {reading.CommitSha}, and this host was built from "
                + $"{expectedCommitSha}, at {where}. `{KitPlugin.UPDATE_COMMAND}` will NOT fix it: it compares the "
                + $"version string and reports success. Reinstall it: {KitPlugin.REINSTALL_COMMAND}",

            PluginVerdicts.Disabled =>
                $"The {pluginId} kit is installed at {found} ({where}) but DISABLED, so sessions would load none of it. "
                + $"Enable it: claude plugin enable {KitPlugin.NAME}",

            PluginVerdicts.Shadowed =>
                "A STALE hand-installed copy of the role protocols is still in this Claude home and would be read INSTEAD of "
                + $"the installed {pluginId} kit — a local command wins the slash word over a plugin skill (measured). "
                + $"This host could not delete it. Remove it by hand: {string.Join(", ", shadowingCommands ?? [])}",

            PluginVerdicts.Unreadable =>
                $"The {pluginId} kit's install record could not be read, so this host CANNOT TELL which protocols its "
                + $"sessions would follow: {reading.UnreadableReason}",

            _ => $"Unhandled plugin verdict '{verdict}' — refusing rather than guessing.",
        };
    }
}
