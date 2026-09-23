namespace AIOrchestratorCoreLib.Configuration.GuardrailSettings;

/// <summary>
/// The three numbers, one list and one switch that govern how hard the app makes it to take an irreversible
/// decision from a phone, and when it stops spending an allowance it is about to exhaust.
///
/// <para>
/// ONE OBJECT RATHER THAN FOUR MORE PARAMETERS on an already twelve-wide config factory. They are
/// read together, they change together, and grouping them keeps the signature honest about what is
/// one concern.
/// </para>
/// <para>
/// EVERY FIELD HAS A DEFAULT THAT IS SAFE WITHOUT CONFIGURATION. An absent config.json must give
/// the guarded behaviour, not the unguarded one — a setting that only protects you once you have
/// heard of it protects nobody.
/// </para>
/// </summary>
public interface IGuardrailSettings
{
    /// <summary>
    /// Case-insensitive substrings that mark a decision as HIGH RISK when they appear in the text
    /// of the question being asked. Matched against the question, not against the option labels:
    /// what makes "yes" dangerous is what it is an answer to.
    /// </summary>
    IReadOnlyList<string> HighRiskPatterns { get; }

    /// <summary>How long the read-back code stays valid after the owner taps a high-risk option.</summary>
    int HighRiskCodeExpiryMinutes { get; }

    /// <summary>
    /// The percentage of a usage window at which the dispatcher stops starting new work. Above it,
    /// launching N more sessions buys N identical failures rather than N results.
    /// </summary>
    double DispatchPauseThresholdPercent { get; }

    /// <summary>
    /// How long an inline decision button stays tappable. Past it a tap is refused and logged: a
    /// keyboard that is still live a day later is a decision anyone holding the phone can take.
    /// </summary>
    int ButtonExpiryMinutes { get; }

    /// <summary>
    /// Whether a high-risk question costs the owner a typed 4-digit code at all. Off, a question the
    /// asker declared <c>RISK: high</c> or that matched <see cref="HighRiskPatterns"/> behaves like any
    /// other: it keeps its declared default, its terms say nothing about a code, and a tap decides it.
    ///
    /// <para>
    /// A SEPARATE SWITCH, NOT AN EMPTY PATTERN LIST. <c>highRiskPatterns: []</c> only silences the
    /// pattern half — the asker's own <c>RISK: high</c> still locked the question, and the question
    /// contract REQUIRES that line on every question, so there was no way to say "no code, ever".
    /// Owner, 2026-09-23 (ai-orchestrator-29 entry [9]): <i>"He added an annoying feature where I'm
    /// asked to enter a code when a requested change is impactful, I don't want that."</i> The shipped
    /// default stays ON — this interface's rule that an unconfigured guard is the guarded one — and
    /// <c>kit/presets/classic.json</c> states the owner's off.
    /// </para>
    /// </summary>
    bool HighRiskConfirmation { get; }
}
