namespace AIOrchestratorCoreLib.Limits;

/// <summary>
/// Whether the app should stop STARTING work because the account is about to run out of allowance.
///
/// <para>
/// WHAT IT CHANGES. Today crossing a limit produces an alert and nothing else, so the sessions keep
/// being launched and respawned into a wall: sixty sessions hitting the same limit are sixty
/// identical errors, sixty terminals showing a failure, and a crash-loop counter climbing on every
/// one of them for a cause that has nothing to do with any of them. Pausing turns that into one
/// message with a time on it.
/// </para>
/// <para>
/// WORK IN FLIGHT IS NEVER KILLED. The pause stops new launches and respawns; a session already
/// running finishes what it is doing. Stopping mid-turn would spend the tokens and keep nothing.
/// </para>
/// <para>
/// IT RESUMES BY ITSELF, at <c>resets_at</c>. A pause a human has to lift is a pause that outlives
/// its cause — the owner is asleep when the five-hour window rolls over, and the machine knows to
/// the second when the allowance comes back.
/// </para>
/// <para>
/// AND DND MUST NOT REACH IT. Mute means "do not disturb me", not "stop managing the account": the
/// alert is outbound and is rightly suppressed while muted, but the pause and the resume are not
/// messages, and the check that computes them has to sit ABOVE the mute gate. That is a placement
/// rule in the engine, so it is stated here — where the person changing this logic is standing —
/// as well as at the call site.
/// </para>
/// </summary>
public static class DispatchPause_Gate
{
    /// <summary>
    /// A pause with no known reset. Long enough that it is clearly not a blip, short enough that a
    /// missing <c>resets_at</c> cannot strand the app: it re-evaluates when this lapses, and pauses
    /// again if the percentage is still over.
    /// </summary>
    public const int FALLBACK_PAUSE_MINUTES = 30;

    /// <summary>
    /// When a usage reading should pause the dispatcher, and until when. Null means keep running.
    /// </summary>
    public static DateTime? Decide_PauseUntil_OrNull(
        double currentPercent,
        DateTime? windowResetsAtUtc,
        double thresholdPercent,
        DateTime nowUtc)
    {
        if (currentPercent < thresholdPercent)
            return null;

        return Resolve_ResumeAt(windowResetsAtUtc, nowUtc);
    }

    /// <summary>
    /// The pause a rate-limit REFUSAL earns — a 429, or an api_error_status carrying one.
    ///
    /// <para>
    /// Separate from the percentage path because it needs no reading at all: the account has already
    /// said no, which is a fact, where a percentage is a probe file's opinion of one. A refusal with
    /// no reset time gets the fallback window.
    /// </para>
    /// </summary>
    public static DateTime Decide_PauseUntil_ForRateLimit(DateTime? windowResetsAtUtc, DateTime nowUtc)
    {
        return Resolve_ResumeAt(windowResetsAtUtc, nowUtc);
    }

    /// <summary>
    /// Whether a stored pause is still in force. <c>&gt;=</c> so it lifts AT its instant, not one
    /// tick after — the rule <see cref="Telegram.TelegramAttempt_Gate.Is_AttemptDue"/> states.
    /// </summary>
    public static bool Is_Paused(DateTime? pausedUntilUtc, DateTime nowUtc)
    {
        return pausedUntilUtc != null && nowUtc < pausedUntilUtc.Value;
    }

    /// <summary>
    /// What the owner is told, once. It carries the TIME, because "you are over your limit" is not
    /// something they can act on and "back at 19:40" is.
    /// </summary>
    public static string Describe_Pause(string windowName, double percent, DateTime resumeAtUtc, DateTime nowUtc)
    {
        return $"⏸ Dispatch PAUSED — the {windowName} window is at {percent:0.#}%. No new sessions are started or respawned; work already running finishes. Resuming automatically at {Describe_ResumeInstant(resumeAtUtc, nowUtc)}.";
    }

    /// <summary>
    /// THE ONE RENDERING OF A RESUME INSTANT — dated, and with the distance. "Resuming at 03:00 UTC"
    /// with no day is how a pause five days out read as tonight's on 2026-09-11, and nobody moved on
    /// it for six hours; the same wording sat under the 2026-09-18 block. Two copies of this existed
    /// (the alert and /limits); decision 12's rule is one formatter, so the second copy calls this.
    /// </summary>
    public static string Describe_ResumeInstant(DateTime resumeAtUtc, DateTime nowUtc)
    {
        var remaining = resumeAtUtc - nowUtc;
        var distance = remaining <= TimeSpan.Zero ? "due now" : $"in {Describe_Distance(remaining)}";

        return $"{resumeAtUtc:yyyy-MM-dd HH:mm} UTC ({distance})";
    }

    static string Describe_Distance(TimeSpan span)
    {
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays} d {span.Hours} h";

        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours} h {span.Minutes} min";

        return $"{Math.Max(1, (int)Math.Ceiling(span.TotalMinutes))} min";
    }

    public static string Describe_Resume(string reason)
    {
        return $"▶ Dispatch resumed — {reason}";
    }

    /// <summary>
    /// The pause every live window together earns, or null when none is over the threshold.
    ///
    /// <para>
    /// THE BINDING WINDOW IS THE ONE THAT COMES BACK LAST, not the first one enumerated. Probe files
    /// are globbed, so dictionary order is arbitrary — and picking the first over-threshold window
    /// meant a weekly at 99% resetting in three days could lose to a five-hour at 96% resetting in
    /// twenty minutes. Dispatch would resume on the five-hour's clock, launch sessions into a weekly
    /// allowance that is still spent, and pause again.
    /// </para>
    /// <para>
    /// Here rather than in the engine because the SAME rule now runs twice — once to decide a pause
    /// and once to reconsider one that holds (<see cref="Reconsider_WhilePaused"/>) — and a second
    /// copy of the loop is a second place for the binding-window choice to drift.
    /// </para>
    /// </summary>
    public static (DateTime Until, string Window, double Percent)? Decide_BindingPause_OrNull(
        IReadOnlyDictionary<string, (double Percent, DateTime? WindowResetsAtUtc)> windows,
        double thresholdPercent,
        DateTime nowUtc)
    {
        (DateTime Until, string Window, double Percent)? binding = null;

        foreach (var pair in windows)
        {
            var candidate = Decide_PauseUntil_OrNull(pair.Value.Percent, pair.Value.WindowResetsAtUtc, thresholdPercent, nowUtc);

            if (candidate == null || (binding != null && candidate.Value <= binding.Value.Until))
                continue;

            binding = (candidate.Value, pair.Key, pair.Value.Percent);
        }

        return binding;
    }

    /// <summary>
    /// What a stored pause becomes when the probes are read AGAIN while it holds. Null means lift now.
    ///
    /// <para>
    /// ONE DIRECTION ONLY: the stored instant is a CEILING, never a floor. A fresh decision that would
    /// resolve EARLIER replaces it; one that would resolve LATER is discarded, so a still-high reading
    /// can never extend a pause past the reset it was measured against — the defect the old
    /// "never look again while paused" guard was built for stays fixed by construction. And NO
    /// READING AT ALL leaves the stored instant standing: silence is not a low number, and a folder
    /// with no probe in it says nothing about the account.
    /// </para>
    /// <para>
    /// Why it exists — measured twice. 2026-09-11: dispatch paused on a weekly reading of 95% until
    /// the 16th; the account was swapped at 14:00 and the new one read 70%; the pause held for a
    /// further 4 days 14 hours and two sessions and a reviewer never started. 2026-09-17 21:22: paused
    /// on a weekly 98% until 2026-09-21 03:00 UTC; the account was swapped and by 21:57 the same
    /// session reported 12%; a start-orchestration filed at 10:00 the next morning sat untouched
    /// through a restart, because the instant is persisted and the guard never asked the probes again.
    /// The spec: docs/superpowers/specs/2026-09-11-the-brake-that-cannot-be-lifted.md, §2.
    /// </para>
    /// </summary>
    public static DateTime? Reconsider_WhilePaused(DateTime storedUntilUtc, bool anyReading, DateTime? freshUntilUtc)
    {
        if (!anyReading)
            return storedUntilUtc;

        if (freshUntilUtc == null)
            return null;

        return freshUntilUtc.Value < storedUntilUtc ? freshUntilUtc.Value : storedUntilUtc;
    }

    static DateTime Resolve_ResumeAt(DateTime? windowResetsAtUtc, DateTime nowUtc)
    {
        // A reset already in the past is not a resume time, it is a stale probe reading. Falling
        // back keeps the pause meaningful instead of lifting it on the tick that set it.
        if (windowResetsAtUtc != null && windowResetsAtUtc.Value > nowUtc)
            return windowResetsAtUtc.Value;

        return nowUtc.AddMinutes(FALLBACK_PAUSE_MINUTES);
    }
}
