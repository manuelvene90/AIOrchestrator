namespace AIOrchestratorCoreLib.Bridge;

/// <summary>
/// WHAT HAPPENS TO ONE SUPERVISOR ENTRY ON AN ORCHESTRATION'S OWNER CHANNEL — the answer
/// <see cref="OwnerPush_Policy.Decide"/> gives and the engine's mirror SWITCHES on (plan 03 Task 2).
///
/// <para>
/// A VALUE, NOT A BOOLEAN. <see cref="OwnerPush_Policy.Should_Push"/> could only say "send" or "not",
/// and "not" had to mean two different things at once: the owner's own words quoted back (never to be
/// replayed) and narration that is owed to them later (held for the turn-end digest). When the filter
/// was removed on 2026-09-09 the second meaning vanished with it; when <c>phone.push = filtered</c>
/// brought it back, a bool would have put the difference back into an <c>if</c> inline in a
/// 16 000-line engine. The spec (§7.1) asked for exactly this seam.
/// </para>
/// </summary>
public enum OwnerPushDecisions
{
    /// <summary>Mirrored to the phone now, ringing — a question, a BLOCKED, a file, the greeting, THE answer; or anything at all under <c>everything</c>.</summary>
    SendNow,

    /// <summary>
    /// Not sent now and not lost: filed into the suppressed-entry store and handed to the owner as one
    /// message when the turn they were waiting on ends. Only <c>filtered</c> ever answers this.
    /// </summary>
    HoldForDigest,

    /// <summary>Never sent and never filed — an empty body, or the owner's own words quoted back at them.</summary>
    Drop,
}
