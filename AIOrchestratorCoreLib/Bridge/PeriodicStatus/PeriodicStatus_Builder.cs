using AIOrchestratorCoreLib.Formatting;

namespace AIOrchestratorCoreLib.Bridge.PeriodicStatus;

/// <summary>
/// THE PERIODIC STATUS, RE-PORTED FROM MASTER (plan 03 Task 8, 2026-09-23). Master's
/// <c>Build_PeriodicStatusText</c> — last seen in <c>14c1cdb^</c>, where the fork deleted it — word for
/// word: the header, the same roster <c>/status</c> answers with, and the ledger's current task.
///
/// <para>
/// WHY IT IS BACK. The fork removed it on 2026-09-09 (ten messages in five and a half hours in one
/// topic, three of them identical at 19:00, 19:30 and 20:00 with every member closed). The owner,
/// 2026-09-23: *"he removed the status message, but I liked it, so we should be able to opt in"* —
/// answer D1 (a): re-port it under <c>phone.status.periodic</c>, on under classic, off under quiet.
/// What made it a waterfall was never its content but that it posted unconditionally; that half is
/// not re-ported (see <see cref="PeriodicStatusSweepModel"/>'s change gate).
/// </para>
/// <para>
/// OUT OF THE ENGINE, where it used to be a private method, because a builder there cannot be reached
/// by the suite. The roster itself stays the engine's — it reads usage files and channels, and it is
/// shared with <c>/status</c> — and arrives here as text.
/// </para>
/// </summary>
public static class PeriodicStatus_Builder
{
    /// <summary>
    /// Just the word: the counts lead the body (the same line /status shows), and printing them in the
    /// header as well put the same figures twice in one message. It is also the channel entry's
    /// subject, which is how the mirror knows to render the body without the ⚙ App label.
    /// </summary>
    public const string HEADER = Mirroring.MirrorText_Formatter.STATUS_SUBJECT_PREFIX;

    /// <param name="memberStatusText">The roster block — counts line, supervisor row, one row per member.</param>
    /// <param name="currentTaskText">The ledger's in-progress task, or null when nothing is <c>[&gt;]</c>.</param>
    public static string Build(string memberStatusText, string? currentTaskText)
    {
        var body = currentTaskText == null
            ? memberStatusText
            : $"{memberStatusText}\n- now: {TextSummary_Formatter.Summarize_Task(currentTaskText, TextSummary_Formatter.CARD_TASK_WORDS)}";

        return $"{HEADER}\n{body}";
    }
}
