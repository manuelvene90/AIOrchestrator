namespace AIOrchestratorCoreLib.Running.TurnLiveness;

internal sealed class TurnSilenceBrakeModel(
    TimeSpan silenceLimit,
    TimeSpan pollInterval,
    Func<DateTime?> readTranscriptLastWriteUtc,
    Func<int, int?> countDescendants,
    Func<DateTime, string?> describeLoopSince,
    Func<DateTime?> readSubAgentLastWriteUtc) : ITurnSilenceBrake
{
    readonly Func<DateTime?> _readSubAgentLastWriteUtc = readSubAgentLastWriteUtc;
    readonly Func<DateTime?> _readTranscriptLastWriteUtc = readTranscriptLastWriteUtc;
    readonly Func<int, int?> _countDescendants = countDescendants;
    readonly Func<DateTime, string?> _describeLoopSince = describeLoopSince;

    public TimeSpan SilenceLimit { get; } = silenceLimit;
    public TimeSpan PollInterval { get; } = pollInterval;

    public string? Decide_Kill_OrNull(DateTime nowUtc, DateTime turnStartedUtc, DateTime? lastOutputUtc, int processId)
    {
        // A LOOP IS CHECKED FIRST AND REGARDLESS OF LIFE, because a looping turn is the one that shows
        // life for ever: it writes its transcript on every step, so the silence test below would spare
        // it until the ceiling. Only THIS turn's steps count — a resumed transcript's earlier turns are
        // not evidence about this one.
        //
        // AND ONLY WHILE NOTHING ELSE MOVES. A member waiting on its own background work — a sub-agent
        // it fanned out to, a build it started with run_in_background — polls it, and a poll that
        // gets the same answer four times is WAITING, not looping (review finding, 2026-09-11: `sleep
        // 60` or a blocking TaskOutput repeated while the sub-agent writes and the build runs). So a
        // repetition kills only when no command runs below the turn and no sub-agent has written
        // within the silence limit; "cannot tell" is, as everywhere here, read as alive.
        // ONE READ OF THE PROCESS TABLE PER DECISION, whichever branch asks first: it is a walk of
        // every /proc entry, and the loop gate and the silence test below both need its answer.
        var descendantsRead = false;
        int? descendants = null;

        int? Descendants()
        {
            if (!descendantsRead)
            {
                descendants = _countDescendants(processId);
                descendantsRead = true;
            }

            return descendants;
        }

        var loop = _describeLoopSince(turnStartedUtc);

        if (loop != null && Is_NothingElseMoving(nowUtc, Descendants))
            return Describe_LoopKill(loop);

        // THE TURN'S OWN START IS THE FLOOR. A resumed transcript carries every earlier turn's writes,
        // and a stamp older than this turn says nothing about it — the stream brake's first version
        // measured from the PREVIOUS turn's last byte and killed 17 idle supervisors five seconds
        // into their next prompt (2026-09-09, fixed by b5f76072). Same trap, closed here by
        // construction.
        var lastSign = turnStartedUtc;

        if (lastOutputUtc > lastSign)
            lastSign = lastOutputUtc.Value;

        // Read only once the cheaper signal has run out: the transcript is a folder walk, and
        // output that arrived a second ago already answers the question.
        if (nowUtc - lastSign < SilenceLimit)
            return null;

        var transcript = _readTranscriptLastWriteUtc();

        if (transcript > lastSign)
            lastSign = transcript.Value;

        if (nowUtc - lastSign < SilenceLimit)
            return null;

        // A COMMAND STILL RUNNING IS WORK. A build or a suite writes nothing to the transcript until
        // it returns; measured 2026-09-11, an idle turn has no process below it and a working one has
        // its shell and the test runner. Null — the OS would not say — is read as alive.
        if (Descendants() is null or > 0)
            return null;

        return Describe_Kill(nowUtc - lastSign, SilenceLimit);
    }

    bool Is_NothingElseMoving(DateTime nowUtc, Func<int?> descendants)
    {
        var subAgentWrite = _readSubAgentLastWriteUtc();

        if (subAgentWrite != null && nowUtc - subAgentWrite.Value < SilenceLimit)
            return false;

        return descendants() == 0;
    }

    internal static string Describe_LoopKill(string loop)
    {
        return $"{LOOP_KILL_PREFIX}: {loop} — and was killed (the pattern is OpenHands' stuck detector; the silence brake is set by '{TurnSilenceBrake_Factory.CONFIG_KEY}' and switches this off with it)";
    }

    /// <summary>The words a loop kill starts with — the one place that tells the two kinds of brake kill apart.</summary>
    internal const string LOOP_KILL_PREFIX = "the turn was looping";

    internal static string Describe_Kill(TimeSpan silentFor, TimeSpan limit)
    {
        return $"the turn showed no sign of life for {silentFor.TotalMinutes:F1} min — no output, no write to its transcript or a sub-agent's, no command running below it — and was killed (the limit is {limit.TotalMinutes:F1} min, set by '{TurnSilenceBrake_Factory.CONFIG_KEY}' in config.json)";
    }
}
