using System.Globalization;
using AIOrchestratorCoreLib.Formatting;

namespace AIOrchestratorCoreLib.Running;

/// <summary>
/// HOW AN APPOINTMENT IS SAID, once, for every surface that says it — the Telegram subject the owner
/// reads on their phone, the channel body the session reads, and the line in
/// <c>orchestrator.log.jsonl</c>.
///
/// <para>
/// WHY IT EXISTS (F4, probe 2026-09-09). All three used to print the appointment as bare
/// <c>HH:mm</c> with no date: "resumes 03:00 UTC" for something twenty-four hours away. That is
/// CLAUDE.md decision 12's confident wrong number in its purest form — a true instant, formatted so
/// that the reader takes it for this morning. The delay is what the owner actually needs ("in 2 h
/// 15 min"), and the absolute instant is what makes it checkable, so both are said and the DATE
/// joins the absolute half as soon as the appointment is not today.
/// </para>
/// <para>
/// ONE IMPLEMENTATION, never a second copy — the same rule, and the same evidence, as
/// <c>SessionDuration_Formatter</c>, whose second copy in <c>SessionRows_Builder</c> is why a future
/// stamp rendered as "on task under a minute" indefinitely. The delay itself DELEGATES to that
/// formatter rather than restating "2 h 15 min" here.
/// </para>
/// </summary>
public static class LimitDeferral_Wording
{
    /// <summary>
    /// "in 2 h 15 min (03:00 UTC)", or "in 5 h 40 min (2026-09-10 00:30 UTC)" when the appointment
    /// falls on another UTC day. Both arguments must be UTC — the caller holds a state-file stamp and
    /// <c>DateTime.UtcNow</c>, and a naked local stamp here would put the date boundary in the wrong
    /// place by the machine's offset.
    /// </summary>
    public static string Describe_Appointment(DateTime retryAtUtc, DateTime nowUtc)
    {
        var absolute = retryAtUtc.Date == nowUtc.Date
            ? retryAtUtc.ToString("HH:mm 'UTC'", CultureInfo.InvariantCulture)
            : retryAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

        var delay = retryAtUtc - nowUtc;

        // DUE ALREADY IS SAID, NOT DRESSED UP AS A WAIT. It happens between the write and a reader
        // arriving late, and "in now (03:00 UTC)" reads as broken; SessionDuration_Formatter answers
        // "now" for a negative span for the same reason.
        return delay <= TimeSpan.Zero
            ? $"now ({absolute})"
            : $"in {SessionDuration_Formatter.Describe(delay)} ({absolute})";
    }
}
