namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// What a HIGH-RISK question costs the owner: always a tap, and a typed read-back code only when the
/// <c>highRiskConfirmation</c> setting keeps it. One place for the three answers the engine needs —
/// is it high risk, does it need the code, may it take a default — plus the log line that explains them.
///
/// <para>
/// TWO FACTS, KEPT APART ON PURPOSE. <see cref="HighRisk_Classifier"/> and the asker's
/// <c>RISK: high</c> decide what a question IS; the setting decides only whether a CODE follows the tap.
/// Owner, 2026-09-23 (ai-orchestrator-29 entry [9]): <i>"He added an annoying feature where I'm asked
/// to enter a code when a requested change is impactful, I don't want that."</i> Before the switch there
/// was no way to say it: <c>highRiskPatterns: []</c> silenced only the pattern half, and the question
/// contract requires a <c>RISK:</c> line on every question, so an asker's <c>high</c> still locked it.
/// </para>
/// <para>
/// THE CODE GOES, THE TAP STAYS (ruling R21, plan 03 task 15 fix round 1). The first cut of the switch
/// made an unlocked question ordinary in EVERY use, so under classic a "merge and push" question
/// carrying <c>DEADLINE: 30m</c> / <c>DEFAULT: 1</c> merged and pushed itself thirty minutes later with
/// nobody touching the phone. The owner asked to stop TYPING, not to lose the decision — so a high-risk
/// question never takes a default whatever the setting says (<see cref="Resolve_DefaultIndex_OrNull"/>),
/// and lapses as a deny exactly as it always has.
/// </para>
/// <para>
/// OUT OF <c>BridgeEngineModel.cs</c> (code-conventions: a piece the stage touches moves out), with the
/// two pre-existing log wordings moved verbatim — <c>QuestionContractProbeTests</c> reads "declared by
/// the asker" from the log.
/// </para>
/// </summary>
public static class HighRiskLock_Policy
{
    /// <summary>The words the off-switch's log line carries, public so a test names them from here.</summary>
    public const string CONFIRMATION_OFF_WORDS = "high-risk confirmation is off";

    /// <summary>
    /// Declared by the asker OR matched by a pattern — never declared INSTEAD of detected. Independent of
    /// the setting: this is what the question is, and it is what the open-question record carries, so the
    /// deadline sweep denies it and <c>/pending</c> lists it as high risk.
    /// </summary>
    public static bool Is_HighRisk(string? matchedPattern, bool declaredHighRisk)
    {
        return declaredHighRisk || matchedPattern != null;
    }

    /// <summary>Whether a tap opens the read-back code: only when the question is high risk AND the owner keeps the code on.</summary>
    public static bool Needs_Code(string? matchedPattern, bool declaredHighRisk, bool confirmationOn)
    {
        return confirmationOn && Is_HighRisk(matchedPattern, declaredHighRisk);
    }

    /// <summary>
    /// The declared default, or null for a high-risk question — WHATEVER the setting says. The agent may
    /// well have written <c>DEFAULT: 1</c> on a push question in good faith; a default is a decision taken
    /// with nobody at the phone, and removing the code must not bring that back.
    /// </summary>
    public static int? Resolve_DefaultIndex_OrNull(int? declaredDefaultIndex, string? matchedPattern, bool declaredHighRisk)
    {
        return Is_HighRisk(matchedPattern, declaredHighRisk) ? null : declaredDefaultIndex;
    }

    /// <summary>
    /// The Info line for a high-risk question; null for an ordinary one, which is most of them and needs
    /// no line. Log only — never Telegram (decision 15).
    /// </summary>
    public static string? Describe_OrNull(string? matchedPattern, bool declaredHighRisk, bool confirmationOn)
    {
        if (!Is_HighRisk(matchedPattern, declaredHighRisk))
            return null;

        var why = matchedPattern != null
            ? $"matched '{matchedPattern}'"
            : "declared by the asker, no pattern matched";

        return confirmationOn
            ? $"Question classified HIGH RISK ({why}) — a tap will require the read-back code"
            : $"Question classified HIGH RISK ({why}) — {CONFIRMATION_OFF_WORDS}, so one tap decides it; it still takes no default";
    }
}
