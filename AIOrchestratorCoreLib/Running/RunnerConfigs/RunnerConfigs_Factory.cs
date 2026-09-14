using AIOrchestratorCoreLib.Running.RoleRunnerConfig;

namespace AIOrchestratorCoreLib.Running.RunnerConfigs;

public static class RunnerConfigs_Factory
{
    public const int DEFAULT_MAX_CONCURRENT_TURNS = 10;
    public const int DEFAULT_MAX_CONCURRENT_TURNS_PER_ORCHESTRATION = 3;
    public static readonly TimeSpan DEFAULT_TURN_TIMEOUT = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DEFAULT_COALESCE_WINDOW = TimeSpan.FromSeconds(3);

    /// <summary>
    /// FIVE MINUTES OF DIGEST, the number the spec proposed (§C4) and the one the measurement
    /// supports. Measured on the VPS 6–9 Sep 2026: 247 of a supervisor's ~400 wake-ups were member
    /// traffic and a wake-up costs on the order of 1 M input tokens, so the saving scales with how
    /// many reports fall inside one window — while the cost of the window is only how late a report
    /// is READ, and every member that is genuinely stuck says so with a marker and is never held.
    /// Longer starts to be a supervisor that has stopped following its crew; shorter stops catching
    /// two members finishing near each other, which is the case this exists for.
    /// </summary>
    public static readonly TimeSpan DEFAULT_MEMBER_DIGEST_WINDOW = TimeSpan.FromMinutes(5);

    /// <summary>
    /// THE CEILING THE DIGEST MAY NOT BE CONFIGURED PAST, because above it the app starts complaining
    /// about a delay it is itself causing.
    ///
    /// <para>
    /// THE COUPLING, MEASURED BY A REVIEW ON 2026-09-09. <c>BridgeEngineModel</c> tells a supervisor it
    /// owes a member a verdict once that member's channel has been quiet for
    /// <see cref="Status.Nudge_Windows.IMPLEMENTER_NUDGE_MINUTES"/> — REFERENCED, not restated, since
    /// 2026-09-10: it was a private <c>const int</c> in that file and this doc carried a second copy of
    /// the number, which is the copy nobody updates. The relationship between the two is now pinned by a
    /// test (<c>RunnerConfigsJsonTests</c>) rather than by this sentence. The quiet clock runs from the
    /// member's REPORT, so a digest of D minutes leaves that window minus D for the supervisor's turn to
    /// be released, run and file its verdict. Probed
    /// at D = 10: at minute 9 the app considered the supervisor 9.6 min late on a verdict for a report
    /// IT WAS ITSELF HOLDING, and spent that quiet spell's single nudge token on the false alarm — so a
    /// genuinely stalled supervisor in the same spell got nothing. The nudge is agent-audience and the
    /// owner never sees it, so decision 15 is not in play; a false alarm that consumes the true one's
    /// token is a defect on its own.
    /// </para>
    /// <para>
    /// FIVE, WHICH IS ALSO THE DEFAULT, and the equality is the point rather than a coincidence: at
    /// D = 5 a turn has three minutes to be released, run and answer [estimate — no measurement of
    /// supervisor turn latency after a digest release exists yet], and there is no larger value that
    /// leaves it time to answer at all. So the digest can be turned DOWN freely — that is the safe
    /// direction, toward the behaviour before 2026-09-09 — and turning it UP is refused with a line
    /// naming the nudge rather than applied silently. Raising it is a change to the PAIR, not to this
    /// number alone.
    /// </para>
    /// <para>
    /// ENFORCED AT THE CONFIG READER AND NOT IN <see cref="Create"/>, exactly the way the memory-size
    /// ceiling is. This is a rule about what an OPERATOR may write into <c>config.json</c>: a throw
    /// here would turn a hand-typed number into an app that will not start, and would also refuse the
    /// tests that legitimately drive longer windows on an injected clock.
    /// </para>
    /// </summary>
    public static readonly TimeSpan MAX_MEMBER_DIGEST_WINDOW = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Two minutes of total silence from a living stream process. Long enough that a turn thinking
    /// hard, or running a slow tool, is never mistaken for a hung one — the CLI emits assistant and
    /// hook events throughout a working turn, so real silence means real silence — and short enough
    /// that the owner's phone line comes back inside a phone call rather than inside the 30-minute
    /// turn timeout.
    /// </summary>
    public static readonly TimeSpan DEFAULT_SILENCE_LIMIT = TimeSpan.FromMinutes(2);

    /// <summary>
    /// FIFTEEN MINUTES, from the members' own numbers (VPS, all sessions 2026-09-07 → 09-11): 95 % of
    /// member sessions never go quiet for more than 10 minutes, counting their sub-agents' calls. A
    /// limit in that region leaves essentially every healthy turn alone — and it is quiet with NO
    /// command running as well, which the 10-minute figure did not even subtract.
    /// </summary>
    public static readonly TimeSpan DEFAULT_MEMBER_SILENCE_LIMIT = TimeSpan.FromMinutes(15);

    /// <summary>
    /// TWO HOURS — the owner's choice of 2026-09-11 ("implement the best practice"), after the
    /// research that the ceiling be longer than the longest legitimate run. Nobody has measured how
    /// long the 58 cut turns needed [not measured]: the successful ones topped out at 28.5 min
    /// only because 30 was the wall, so the real distribution is unknown and the number is a
    /// generous backstop, not a fit. The cost the owner accepted: a service stop may wait this long
    /// for a member turn to finish (<see cref="ShutdownGrace_Rule"/>).
    /// </summary>
    public static readonly TimeSpan DEFAULT_MEMBER_TURN_TIMEOUT = TimeSpan.FromHours(2);

    /// <summary>
    /// A DAY, past which the member silence limit and the member ceiling are refused at the config
    /// reader: neither is a policy anyone can mean beyond it, and the reader compares the NUMBER
    /// against this before any <c>TimeSpan</c> exists (<c>TimeSpan.FromMinutes(1e11)</c> throws).
    /// </summary>
    public static readonly TimeSpan MAX_MEMBER_SILENCE_LIMIT = TimeSpan.FromDays(1);

    public static readonly TimeSpan MAX_MEMBER_TURN_TIMEOUT = TimeSpan.FromDays(1);

    /// <summary>
    /// Three gigabytes per session. Measured against the shape the VPS actually runs: 8 GB total,
    /// the daemon and its bridge under 300 MB, and <c>printRunner.maxConcurrentTurns</c> defaulting
    /// to 10 — so this is not a budget that adds up to the machine, it is the point past which ONE
    /// session is doing something nobody asked for. A `claude -p` measured ~270 MB resident while a
    /// turn ran, so a session reaching 3 G is an order of magnitude out.
    /// </summary>
    public const string DEFAULT_SESSION_MEMORY_MAX = SessionSandbox.MemoryLimitedInvocation_Builder.DEFAULT_MEMORY_MAX;

    public static IRunnerConfigs Create(
        IReadOnlyDictionary<SessionRoles, IRoleRunnerConfig> roles,
        int maxConcurrentTurns,
        int maxConcurrentTurnsPerOrchestration,
        TimeSpan turnTimeout,
        TimeSpan coalesceWindow,
        TimeSpan? silenceLimit = null,
        IReadOnlyList<string>? rejections = null,
        string? sessionMemoryMax = null,
        TimeSpan? memberDigestWindow = null,
        TimeSpan? memberSilenceLimit = null,
        TimeSpan? memberTurnTimeout = null)
    {
        if (maxConcurrentTurns < 1)
            throw new ArgumentException($"maxConcurrentTurns must be >= 1, got {maxConcurrentTurns}");
        if (maxConcurrentTurnsPerOrchestration < 1)
            throw new ArgumentException($"maxConcurrentTurnsPerOrchestration must be >= 1, got {maxConcurrentTurnsPerOrchestration}");
        if (turnTimeout <= TimeSpan.Zero)
            throw new ArgumentException($"turnTimeout must be positive, got {turnTimeout}");
        if (coalesceWindow < TimeSpan.Zero)
            throw new ArgumentException($"coalesceWindow must not be negative, got {coalesceWindow}");
        if (silenceLimit != null && silenceLimit.Value <= TimeSpan.Zero)
            throw new ArgumentException($"silenceLimit must be positive, got {silenceLimit}");
        if (memberTurnTimeout != null && memberTurnTimeout.Value <= TimeSpan.Zero)
            throw new ArgumentException($"memberTurnTimeout must be positive, got {memberTurnTimeout}");

        // A NEGATIVE DIGEST IS NOT REFUSED, IT IS OFF. Zero and below both mean "one entry, one turn"
        // — the behaviour before 2026-09-09 — and the policy reads them that way (WakeUp_Policy tests
        // `digestWindow <= TimeSpan.Zero`), so there is nothing for a validator to protect here and a
        // throw would only turn a hand-typed minus into an app that will not start. REVIEW FINDING,
        // 2026-09-09: this sentence was true of the factory and FALSE of the config reader, which gave
        // a negative value the five-minute default back — the longest wait in answer to a request for
        // none. RunnerConfigs_Json now normalises it to zero, so the two agree.
        return new RunnerConfigsModel(
            roles, maxConcurrentTurns, maxConcurrentTurnsPerOrchestration, turnTimeout, coalesceWindow,
            memberDigestWindow ?? DEFAULT_MEMBER_DIGEST_WINDOW,
            silenceLimit ?? DEFAULT_SILENCE_LIMIT, sessionMemoryMax ?? DEFAULT_SESSION_MEMORY_MAX, rejections ?? [],
            Normalise_MemberSilenceLimit(memberSilenceLimit ?? DEFAULT_MEMBER_SILENCE_LIMIT),
            memberTurnTimeout ?? DEFAULT_MEMBER_TURN_TIMEOUT);
    }

    /// <summary>A negative limit is OFF, like zero — the same reading the digest window gets above.</summary>
    static TimeSpan Normalise_MemberSilenceLimit(TimeSpan limit)
    {
        return limit < TimeSpan.Zero ? TimeSpan.Zero : limit;
    }

    /// <summary>Terminal for every role, default limits — what an absent block means.</summary>
    public static IRunnerConfigs Create_Default()
    {
        return Create(new Dictionary<SessionRoles, IRoleRunnerConfig>(), DEFAULT_MAX_CONCURRENT_TURNS, DEFAULT_MAX_CONCURRENT_TURNS_PER_ORCHESTRATION, DEFAULT_TURN_TIMEOUT, DEFAULT_COALESCE_WINDOW);
    }

    /// <summary>The same limits with one role's block replaced.</summary>
    public static IRunnerConfigs Create_WithRole(IRunnerConfigs source, SessionRoles role, IRoleRunnerConfig roleConfig)
    {
        Dictionary<SessionRoles, IRoleRunnerConfig> roles = [];

        foreach (var known in SessionRole_Names.ALL)
            roles[known] = source.Get_ForRole(known);

        roles[role] = roleConfig;

        return Create(roles, source.MaxConcurrentTurns, source.MaxConcurrentTurnsPerOrchestration, source.TurnTimeout, source.CoalesceWindow, source.SilenceLimit, source.Rejections, source.SessionMemoryMax, source.MemberDigestWindow, source.MemberSilenceLimit, source.MemberTurnTimeout);
    }

    /// <summary>
    /// The same roles with the limits replaced. <paramref name="memberDigestWindow"/> is optional and
    /// defaults to KEEPING the source's, so a caller that only means to change a concurrency number
    /// cannot silently reset how long a supervisor holds its crew's reports.
    /// </summary>
    public static IRunnerConfigs Create_WithLimits(IRunnerConfigs source, int maxConcurrentTurns, int maxConcurrentTurnsPerOrchestration, TimeSpan turnTimeout, TimeSpan coalesceWindow, TimeSpan? memberDigestWindow = null)
    {
        Dictionary<SessionRoles, IRoleRunnerConfig> roles = [];

        foreach (var known in SessionRole_Names.ALL)
            roles[known] = source.Get_ForRole(known);

        return Create(roles, maxConcurrentTurns, maxConcurrentTurnsPerOrchestration, turnTimeout, coalesceWindow, source.SilenceLimit, source.Rejections, source.SessionMemoryMax, memberDigestWindow ?? source.MemberDigestWindow, source.MemberSilenceLimit, source.MemberTurnTimeout);
    }
}
