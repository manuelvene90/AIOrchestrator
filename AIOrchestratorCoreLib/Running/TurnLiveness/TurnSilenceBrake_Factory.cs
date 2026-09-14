using AIOrchestratorCoreLib.Running.ProcessTree;
using AIOrchestratorCoreLib.Running.RunnerConfigs;

namespace AIOrchestratorCoreLib.Running.TurnLiveness;

public static class TurnSilenceBrake_Factory
{
    /// <summary>The key as the operator types it, named in every kill line.</summary>
    public const string CONFIG_KEY = RunnerConfigs_Json.LIMITS_KEY + "." + RunnerConfigs_Json.MEMBER_SILENCE_MINUTES_KEY;

    /// <summary>
    /// At most fifteen seconds between looks: one folder walk and one process-table read per running
    /// turn, and against the fifteen-minute default a kill lands within a quarter-minute of the limit.
    /// A shorter limit is looked at more often — a quarter of it, never under a tenth of a second — so
    /// the kill stays close to the number the operator wrote, whatever they wrote.
    /// </summary>
    public static readonly TimeSpan MAX_POLL_INTERVAL = TimeSpan.FromSeconds(15);

    static TimeSpan Resolve_PollInterval(TimeSpan silenceLimit)
    {
        var quarter = silenceLimit / 4;

        if (quarter > MAX_POLL_INTERVAL)
            return MAX_POLL_INTERVAL;

        return quarter < TimeSpan.FromMilliseconds(100) ? TimeSpan.FromMilliseconds(100) : quarter;
    }

    /// <summary>
    /// THE ROLES THAT DO THE WORK, AND ONLY THOSE — the spec's "members only". A supervisor is idle by
    /// design and runs on the stream runner with its own heartbeat; the general supervisor and the
    /// communicator are owner-facing; a solo is the owner's direct line, and its turn ending is how a
    /// question reaches them, so it stays on the deadline until the owner decides otherwise.
    /// </summary>
    public static bool Applies_ToRole(SessionRoles role)
    {
        return role == SessionRoles.Implementer || role == SessionRoles.Reviewer;
    }

    /// <summary>
    /// The brake for one turn of <paramref name="role"/> on <paramref name="sessionId"/>, or null when
    /// it does not apply — a role outside <see cref="Applies_ToRole"/>, or the limit set to zero (off).
    /// <paramref name="childEnvironment"/> is the environment the turn's process is given: the claude
    /// home is read from it, because the transcript lands wherever THAT process's CLAUDE_CONFIG_DIR
    /// points, not wherever the app's own does.
    /// </summary>
    public static ITurnSilenceBrake? Create_ForTurn_OrNull(
        SessionRoles role, TimeSpan silenceLimit, string sessionId, IReadOnlyDictionary<string, string> childEnvironment)
    {
        if (!Applies_ToRole(role) || silenceLimit <= TimeSpan.Zero)
            return null;

        var claudeHome = TranscriptActivity_Reader.Resolve_ClaudeHome(childEnvironment);
        var loopWatch = new TranscriptLoopWatch(claudeHome, sessionId);

        return new TurnSilenceBrakeModel(
            silenceLimit,
            Resolve_PollInterval(silenceLimit),
            () => TranscriptActivity_Reader.Read_LastWriteUtc_OrNull(claudeHome, sessionId),
            ProcessDescendants_Reader.Count_Descendants_OrNull,
            loopWatch.Describe_Loop_OrNull,
            () => TranscriptActivity_Reader.Read_SubAgentLastWriteUtc_OrNull(claudeHome, sessionId));
    }

    /// <summary>True when <paramref name="brakeKill"/> is the loop detector's line rather than the silence brake's.</summary>
    public static bool Is_LoopKill(string brakeKill)
    {
        return brakeKill.StartsWith(TurnSilenceBrakeModel.LOOP_KILL_PREFIX, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same brake with its two readers supplied — so a test can stage "transcript silent, no
    /// child" without writing a transcript into the real claude home or leaving a process behind.
    /// </summary>
    public static ITurnSilenceBrake Create_WithReaders(
        TimeSpan silenceLimit, TimeSpan pollInterval, Func<DateTime?> readTranscriptLastWriteUtc, Func<int, int?> countDescendants, Func<DateTime, string?>? describeLoopSince = null, Func<DateTime?>? readSubAgentLastWriteUtc = null)
    {
        if (silenceLimit <= TimeSpan.Zero)
            throw new ArgumentException($"silenceLimit must be positive, got {silenceLimit}");
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentException($"pollInterval must be positive, got {pollInterval}");

        return new TurnSilenceBrakeModel(silenceLimit, pollInterval, readTranscriptLastWriteUtc, countDescendants, describeLoopSince ?? (_ => null), readSubAgentLastWriteUtc ?? (() => null));
    }
}
