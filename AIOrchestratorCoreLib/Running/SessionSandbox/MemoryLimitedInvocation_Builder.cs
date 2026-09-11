using AIOrchestratorCoreLib.Running.ClaudeInvocation;

namespace AIOrchestratorCoreLib.Running.SessionSandbox;

/// <summary>
/// WRAPS ONE SESSION IN ITS OWN MEMORY-LIMITED CGROUP. Pure: an invocation in, an invocation out,
/// no probing, no files, no clock — so the command line the VPS will actually run is asserted
/// character for character rather than argued about.
///
/// <para>
/// WHY (VPS, 2026-09-07): a supervisor asked an implementer to re-measure a suite "under memory
/// pressure", the implementer ran a 6.5 GB allocator, and the kernel's OOM killer took the
/// <c>aiorchestrator</c> unit — three times in ten minutes. Sessions are spawned as CHILDREN of the
/// daemon, so they share its cgroup, and the OOM killer scores a cgroup: one session's allocation
/// killed the bridge, every other session, and every turn in flight. The blast radius has to be one
/// session, and per CLAUDE.md decision 21 that is enforced at the point of effect — the spawn —
/// never in prose in a role protocol a session can simply not follow.
/// </para>
/// <para>
/// <c>--scope</c> rather than a service: the process stays a child of the caller and its stdio pipes
/// stay wired straight through, which is the entire transport for both runners. <c>--user</c>
/// because the daemon runs as <c>orch</c>, an ordinary user, and a system scope would need root.
/// <c>--quiet</c> because systemd-run otherwise prints the transient unit's name on stderr, and this
/// runner's stderr is read as the CLI's.
/// </para>
/// </summary>
public static class MemoryLimitedInvocation_Builder
{
    public const string SYSTEMD_RUN = "systemd-run";

    /// <summary>What an absent <c>runners.sessionMemoryMax</c> means. Sized for the 8 GB VPS: three concurrent turns still fit under the unit.</summary>
    public const string DEFAULT_MEMORY_MAX = "3G";

    /// <summary>
    /// The plain invocation wrapped in a transient scope. Throws on a size it cannot read rather
    /// than silently spawning unlimited: the caller decides whether to limit AT ALL, and having
    /// decided to, an unreadable ceiling is a bug in the caller's own validation.
    /// </summary>
    public static IClaudeInvocation Wrap(IClaudeInvocation plain, string memoryMax)
    {
        return ClaudeInvocation_Factory.Create(SYSTEMD_RUN, Build_LeadingArguments(plain, memoryMax));
    }

    /// <summary>
    /// Everything before the CLI's own flags:
    /// <c>--user --scope --quiet -p MemoryMax=3G -p MemorySwapMax=1536M -- claude</c>.
    ///
    /// <para>
    /// THE <c>--</c> IS LOAD-BEARING and is the same lesson <see cref="Spawning.SpawnCommand_Builder"/>
    /// wrote down: without it systemd-run reads the CLI's own <c>-p</c>-shaped flags as more of its
    /// properties. Everything after it is the command.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Build_LeadingArguments(IClaudeInvocation plain, string memoryMax)
    {
        var swapMax = MemorySize_Parser.Halve_OrNull(memoryMax)
            ?? throw new ArgumentException($"memoryMax '{memoryMax}' is not a size this can read, so no swap ceiling can be derived from it");

        List<string> leading =
        [
            "--user",
            "--scope",
            "--quiet",
            "-p", $"MemoryMax={memoryMax.Trim()}",

            // WITHOUT THIS THE LIMIT IS A SPEED BUMP. MemoryMax alone lets the cgroup page to swap
            // instead of dying, so a runaway allocator keeps allocating and keeps the whole machine
            // thrashing — which is the failure being prevented, only slower.
            "-p", $"MemorySwapMax={swapMax}",
            "--",
            plain.Executable,
        ];

        leading.AddRange(plain.LeadingArguments);

        return leading;
    }

    /// <summary>The whole line, for the log — the first question after a session dies is "what was run".</summary>
    public static string Describe(IClaudeInvocation invocation)
    {
        return string.Join(' ', new[] { invocation.Executable }.Concat(invocation.LeadingArguments));
    }
}
