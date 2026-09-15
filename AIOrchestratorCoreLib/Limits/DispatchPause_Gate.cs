using System.Globalization;

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
    /// A lift covers the window it lifted: a pause that would end NO LATER than the lifted one is
    /// the same episode (or an earlier-resetting one) and stays lifted. A window that comes back
    /// later is a new episode and still pauses — otherwise lifting a five-hour pause would silently
    /// switch off the weekly guard for the rest of those five hours.
    /// </summary>
    public static bool Is_CoveredByLift(DateTime candidatePauseUntilUtc, DateTime? liftedUntilUtc)
    {
        return liftedUntilUtc != null && candidatePauseUntilUtc <= liftedUntilUtc.Value;
    }

    /// <summary>
    /// The resume instant in words. THE DATE IS NAMED ONCE IT IS MORE THAN A DAY AWAY: on 2026-09-15
    /// a weekly pause said "Resuming automatically at 08:00 UTC", which reads as tomorrow morning and
    /// meant six days later. The one formatter for every pause line — never a second copy.
    /// </summary>
    public static string Describe_ResumeAt(DateTime resumeAtUtc, DateTime nowUtc)
    {
        var format = resumeAtUtc - nowUtc < TimeSpan.FromDays(1) ? "HH:mm" : "ddd d MMM HH:mm";

        return $"{resumeAtUtc.ToString(format, CultureInfo.InvariantCulture)} UTC";
    }

    /// <summary>
    /// What the owner is told, once. It carries the TIME, because "you are over your limit" is not
    /// something they can act on and "back at 19:40" is — and the command that lifts it now.
    /// </summary>
    public static string Describe_Pause(string windowName, double percent, DateTime resumeAtUtc, DateTime nowUtc)
    {
        return $"⏸ Dispatch PAUSED — the {windowName} window is at {percent:0.#}%. No new sessions are started or respawned; work already running finishes. Resuming automatically at {Describe_ResumeAt(resumeAtUtc, nowUtc)}. Send /resume to lift it now.";
    }

    /// <summary>
    /// Said at startup when the pause came back from engine state. Every session is down after a
    /// restart, so a restored pause is what decides whether ANY of them comes back — and before this
    /// line the only thing the log said was "Bridge started".
    /// </summary>
    public static string Describe_RestoredPause(string? reason, DateTime pausedUntilUtc, DateTime nowUtc)
    {
        return $"⏸ Dispatch is still PAUSED from before this restart — {reason ?? "a usage limit was reached"}. Sessions are not started or restored until {Describe_ResumeAt(pausedUntilUtc, nowUtc)}. Send /resume to lift it now.";
    }

    /// <summary>The owner lifted it (/resume, or the app's button).</summary>
    public static string Describe_Lift(string? reason, DateTime liftedUntilUtc, DateTime nowUtc)
    {
        return $"▶ Dispatch pause lifted — sessions are started and restored again (it paused because {reason ?? "a usage limit was reached"}). That window will not pause dispatch again before {Describe_ResumeAt(liftedUntilUtc, nowUtc)}; a window that resets later still can.";
    }

    public static string Describe_Resume(string reason)
    {
        return $"▶ Dispatch resumed — {reason}";
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
