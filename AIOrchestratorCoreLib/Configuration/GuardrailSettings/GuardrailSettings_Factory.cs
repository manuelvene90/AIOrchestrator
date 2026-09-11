namespace AIOrchestratorCoreLib.Configuration.GuardrailSettings;

public static class GuardrailSettings_Factory
{
    /// <summary>
    /// What counts as high risk out of the box.
    ///
    /// <para>
    /// The four the owner named — push, deploy, spend, and a recursive delete — expressed as the
    /// words an agent actually writes when it asks about one. They are SUBSTRINGS, matched
    /// case-insensitively, so "git push origin main" and "ready to push?" both land. False
    /// positives cost one typed code; a false negative costs an unattended approval of the exact
    /// operation this list exists to slow down, so the list errs wide on purpose.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> DEFAULT_HIGH_RISK_PATTERNS =
    [
        "push",
        "deploy",
        "release",
        "publish",
        "production",
        "rm -rf",
        "force-push",
        "force push",
        "--force",
        "reset --hard",
        "drop table",
        "delete branch",
    ];

    /// <summary>Long enough to fetch the phone from another room; short enough to be a second gesture.</summary>
    public const int DEFAULT_HIGH_RISK_CODE_EXPIRY_MINUTES = 10;

    /// <summary>The owner's number: above it a new turn buys a failure, not a result.</summary>
    public const double DEFAULT_DISPATCH_PAUSE_THRESHOLD_PERCENT = 95;

    /// <summary>
    /// Twelve hours. A question asked at the end of a working day must still be tappable the next
    /// morning — the owner reads this system on a phone and answers when they wake up — but a
    /// keyboard that is live a week later is furniture, not a decision.
    /// </summary>
    public const int DEFAULT_BUTTON_EXPIRY_MINUTES = 720;

    public static IGuardrailSettings Create(
        IReadOnlyList<string>? highRiskPatterns,
        int? highRiskCodeExpiryMinutes,
        double? dispatchPauseThresholdPercent,
        int? buttonExpiryMinutes)
    {
        return new GuardrailSettingsModel(
            // An EMPTY list in config means "nothing is high risk", which is a choice the owner is
            // allowed to make explicitly; a MISSING key means they never said, and gets the
            // defaults. Null and empty are therefore deliberately not the same thing here.
            highRiskPatterns ?? DEFAULT_HIGH_RISK_PATTERNS,
            Positive_OrDefault(highRiskCodeExpiryMinutes, DEFAULT_HIGH_RISK_CODE_EXPIRY_MINUTES),
            dispatchPauseThresholdPercent is > 0 and <= 100 ? dispatchPauseThresholdPercent.Value : DEFAULT_DISPATCH_PAUSE_THRESHOLD_PERCENT,
            Positive_OrDefault(buttonExpiryMinutes, DEFAULT_BUTTON_EXPIRY_MINUTES));
    }

    public static IGuardrailSettings Create_Default()
    {
        return Create(null, null, null, null);
    }

    /// <summary>
    /// A zero or negative duration is a config typo that would DISABLE the guard it configures —
    /// an expiry of 0 refuses every tap, a threshold of 0 pauses for ever. Falling back to the
    /// default is the only reading that cannot be worse than saying nothing.
    /// </summary>
    static int Positive_OrDefault(int? value, int fallback)
    {
        return value is > 0 ? value.Value : fallback;
    }
}
