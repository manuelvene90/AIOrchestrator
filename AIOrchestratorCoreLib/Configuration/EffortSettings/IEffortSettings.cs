using AIOrchestratorCoreLib.Running;

namespace AIOrchestratorCoreLib.Configuration.EffortSettings;

/// <summary>
/// THE ROLE DEFAULT FOR <c>claude --effort</c>, as DATA — the <c>effort</c> block of config.json,
/// resolved through the settings catalogue and the preset beneath it.
///
/// <para>
/// IT USED TO BE A COMPILED CONSTANT (moved 2026-09-12, plan 02 task 7). The xhigh the owner asked
/// for on 2026-09-09 — "XHigh effort in each solo and sup session" — lived in
/// <c>SpawnCommand_Builder.SUPERVISION_EFFORT_LEVEL</c>, which meant changing it needed a rebuilt app
/// actually running (CLAUDE.md decision 23), while the MODEL standing beside it on the same command
/// line was already read live from config.json. One dial, two different places to turn it. The value
/// an untouched machine gets is unchanged: <c>classic</c> carries <c>effort.supervisor</c> and
/// <c>effort.solo</c> at xhigh, and an absent <c>preset</c> key means classic.
/// </para>
/// <para>
/// NULL IS AN ANSWER, NOT AN ABSENCE, and that is the whole reason this reads
/// <c>_OrNull</c> rather than returning a level called "default": the builders emit <c>--effort</c>
/// only when a level is set, so null leaves the CLI's own default in charge. An explicit JSON
/// <c>null</c> in the block therefore MEANS "no flag" and stops the resolver falling through to the
/// preset — which is how an owner takes xhigh back off a role that <c>classic</c> gives it to.
/// </para>
/// <para>
/// THIS IS THE ROLE DEFAULT ONLY. The layer above it is the per-orchestration <c>/effort</c> dial
/// (<c>session.json</c>'s <c>supervisorEffortOverride</c> / <c>implementerEffortOverride</c>,
/// CLAUDE.md decision 24), and it is applied where both are in reach —
/// <c>OrchestrationLauncherModel</c>, which holds the session and the config provider — never here.
/// </para>
/// </summary>
public interface IEffortSettings
{
    /// <summary>
    /// The <c>--effort</c> level a session of this role spawns with, or null for no flag at all.
    /// Throws naming the role for a value <see cref="SessionRoles"/> grew and this map did not — a
    /// silent null there would be a role spawning on the CLI's default with nothing saying so.
    /// </summary>
    string? Get_ForRole_OrNull(SessionRoles role);
}
