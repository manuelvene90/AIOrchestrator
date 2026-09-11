using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Running.RunnerConfigs;

/// <summary>
/// The whole runner configuration: one <see cref="IRoleRunnerConfig"/> per role, plus the limits
/// the print dispatcher works under. Read live through the config provider like every other
/// setting, so an edit to config.json applies to the next spawn and the next turn.
/// </summary>
public interface IRunnerConfigs
{
    IRoleRunnerConfig Get_ForRole(SessionRoles role);

    /// <summary>Print turns running at once across every orchestration (a `claude -p` is ~270 MB RSS while it runs).</summary>
    int MaxConcurrentTurns { get; }

    /// <summary>Print turns running at once inside one orchestration.</summary>
    int MaxConcurrentTurnsPerOrchestration { get; }

    /// <summary>A print turn that outlives this is killed (process tree) and re-queued.</summary>
    TimeSpan TurnTimeout { get; }

    /// <summary>Entries landing within this window of each other ride one turn.</summary>
    TimeSpan CoalesceWindow { get; }

    /// <summary>
    /// THE LONGEST A MEMBER'S ORDINARY ENTRY IS HELD BEFORE IT BUYS ITS SUPERVISOR A TURN — the digest
    /// window of <see cref="PendingTraffic.WakeUp_Policy"/>.
    ///
    /// <para>
    /// Measured on the VPS 6–9 Sep 2026: 247 of an orchestration supervisor's ~400 wake-ups came from
    /// member traffic, and one wake-up is on the order of 1 M input tokens (2.5 calls × ~398 k mean
    /// context). Held for a few minutes, several members' reports ride ONE turn instead of buying one
    /// each. The owner is never held, a member declaring itself blocked is never held, and a held entry
    /// rides whatever starts the next turn — so this delays nothing but the moment a report is read.
    /// </para>
    /// <para>
    /// ZERO OR NEGATIVE TURNS THE DIGEST OFF, restoring one-entry-one-turn. It is a real setting rather
    /// than an accident of parsing: this is the one lever in the token plan that changes WHEN a
    /// supervisor reads its crew, so the owner keeps a way to put it back without a deployment.
    /// </para>
    /// </summary>
    TimeSpan MemberDigestWindow { get; }

    /// <summary>
    /// How long a LIVING stream process may say nothing before it is treated as hung, killed and
    /// resumed. Shorter than <see cref="TurnTimeout"/> on purpose: a process that is alive and mute
    /// is a different animal from a turn that is genuinely thinking for half an hour, and waiting
    /// out the turn timeout to notice it costs the owner a supervisor for that whole time.
    /// </summary>
    TimeSpan SilenceLimit { get; }

    /// <summary>
    /// THE MEMORY CEILING ONE SESSION MAY REACH BEFORE THE KERNEL KILLS IT — written the way
    /// systemd writes a size ("3G", "3072M"), or one of the words <c>none</c>/<c>off</c>/<c>0</c>
    /// for no ceiling at all.
    ///
    /// <para>
    /// Observed on the VPS 2026-09-07: sessions are spawned as CHILDREN of the daemon and therefore
    /// share its cgroup, so a 6.5 GB allocator inside one implementer had the whole
    /// <c>aiorchestrator</c> unit OOM-killed three times in ten minutes — the bridge, every other
    /// session, and every turn in flight, for one session's mistake. A session that exceeds this
    /// must die ALONE.
    /// </para>
    /// <para>
    /// It is a STRING rather than a byte count because it is written by a human into config.json and
    /// handed to systemd verbatim; parsing it to a number and back would introduce a spelling this
    /// host invented. See <see cref="SessionSandbox.MemorySize_Parser"/> for the reading.
    /// </para>
    /// </summary>
    string SessionMemoryMax { get; }

    /// <summary>
    /// Configurations the loader REFUSED, in the owner's own terms — one line each. A refusal is not a
    /// parse error (config.json still loads, the role falls back to terminal): it is a setting that
    /// would have done something the app must not do, and the only unacceptable outcome is applying it
    /// silently.
    ///
    /// <para>
    /// THE READER IS <c>PrintTurnDispatcherModel.Report_ConfigRejections_Once</c>, which logs each
    /// distinct line once. This docstring said "logged once by the launcher" for four stages and no
    /// launcher did: <c>grep -rn Rejections</c> on 2026-09-10 found the model, the factory, this
    /// interface and five test files, so every refusal the loader wrote reached nobody at all
    /// (adversarial review; CLAUDE.md decision 21 — a silence is not an acceptable outcome). Naming
    /// the reader here is deliberate: this is a list whose whole value is that somebody prints it, so
    /// the next reader can check the claim in one grep instead of believing it.
    /// </para>
    /// </summary>
    IReadOnlyList<string> Rejections { get; }
}
