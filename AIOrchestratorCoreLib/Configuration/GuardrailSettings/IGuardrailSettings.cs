namespace AIOrchestratorCoreLib.Configuration.GuardrailSettings;

/// <summary>
/// The three numbers and one list that govern how hard the app makes it to take an irreversible
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
}
