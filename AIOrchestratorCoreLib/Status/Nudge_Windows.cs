namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// THE NUDGE WINDOWS, IN ONE PLACE, BECAUSE ANOTHER RULE IS BUILT ON TOP OF THEM.
///
/// <para>
/// <see cref="IMPLEMENTER_NUDGE_MINUTES"/> lived as a private <c>const int</c> inside
/// <c>BridgeEngineModel</c>, which was fine while it governed only the engine. It stopped being fine
/// on 2026-09-09, when the member digest gained a ceiling DERIVED from it
/// (<c>RunnerConfigs_Factory.MAX_MEMBER_DIGEST_WINDOW</c>): holding a member's report for longer than
/// this window makes the app tell a supervisor it is late on a verdict for a report the app itself is
/// holding, and spend that quiet spell's single nudge token on the false alarm. A private constant
/// cannot be referenced, so the ceiling's own documentation RESTATED the number in prose — two copies
/// of one fact, the second of which nobody would update.
/// </para>
/// <para>
/// So it is here, and the relationship is pinned by a test rather than by a sentence
/// (<c>RunnerConfigsJsonTests</c>): change this number and the ceiling's test goes red, which is the
/// point — raising one is a change to the PAIR.
/// </para>
/// <para>
/// <c>ORPHAN_CONFIRM_MINUTES = 6</c> is the engine's other window and is deliberately NOT moved here:
/// nothing outside the engine is built on it, and <c>Nudge_Decider</c> records why both windows want a
/// SEAM (injectable clocks) rather than a shared home before they can be tested at all. Moving a
/// constant nobody else reads would be motion without a reader.
/// </para>
/// </summary>
public static class Nudge_Windows
{
    /// <summary>
    /// How long an implementer may leave a brief unanswered before the app nudges it. The quiet clock
    /// runs from the member's own last entry, so a digest of D minutes leaves this minus D for the
    /// supervisor's turn to be released, run, and file its verdict.
    /// </summary>
    public const int IMPLEMENTER_NUDGE_MINUTES = 8;
}
