namespace AIOrchestratorCoreLib.Bridge.Decisions;

/// <summary>
/// Whether a question the classifier called high risk is actually LOCKED behind the read-back code,
/// and the one log line that says why — or why not.
///
/// <para>
/// TWO FACTS, KEPT APART ON PURPOSE. <see cref="HighRisk_Classifier"/> and the asker's
/// <c>RISK: high</c> decide what a question IS; the <c>highRiskConfirmation</c> setting decides what the
/// app DOES about it. Owner, 2026-09-23 (ai-orchestrator-29 entry [9]): <i>"He added an annoying
/// feature where I'm asked to enter a code when a requested change is impactful, I don't want
/// that."</i> Before this switch there was no way to say it: <c>highRiskPatterns: []</c> silenced only
/// the pattern half, and the question contract requires a <c>RISK:</c> line on every question, so an
/// asker's <c>high</c> still locked it. With the switch off the classification is still made and still
/// LOGGED — the owner who turns the code back on can read which questions it would have caught — but
/// nothing downstream sees a lock: the default is kept, the terms say nothing about a code, the buttons
/// act on a tap.
/// </para>
/// <para>
/// OUT OF <c>BridgeEngineModel.cs</c> (code-conventions: a piece the stage touches moves out), with the
/// two pre-existing log wordings moved verbatim — <c>QuestionContractProbeTests</c> reads "declared by
/// the asker" from the log.
/// </para>
/// </summary>
public static class HighRiskLock_Policy
{
    /// <summary>The words the off-switch's log line opens its reason with, public so a test names them from here.</summary>
    public const string CONFIRMATION_OFF_WORDS = "high-risk confirmation is off";

    /// <summary>Locked only when the question is high risk AND the owner has the code on.</summary>
    public static bool Is_Locked(string? matchedPattern, bool declaredHighRisk, bool confirmationOn)
    {
        return confirmationOn && Is_Classified(matchedPattern, declaredHighRisk);
    }

    /// <summary>
    /// The Info line for a question that is, or would have been, locked; null for an ordinary question,
    /// which is most of them and needs no line. Log only — never Telegram (decision 15): the owner
    /// cannot act on "this would have locked" at the moment it is asked.
    /// </summary>
    public static string? Describe_OrNull(string? matchedPattern, bool declaredHighRisk, bool confirmationOn)
    {
        if (!Is_Classified(matchedPattern, declaredHighRisk))
            return null;

        var why = matchedPattern != null
            ? $"matched '{matchedPattern}'"
            : "declared by the asker, no pattern matched";

        return confirmationOn
            ? $"Question classified HIGH RISK ({why}) — a tap will require the read-back code"
            : $"Question would have been HIGH RISK ({why}), but {CONFIRMATION_OFF_WORDS} — a tap decides it, no code";
    }

    static bool Is_Classified(string? matchedPattern, bool declaredHighRisk)
    {
        return declaredHighRisk || matchedPattern != null;
    }
}
