namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// The marker a hook drops when it cannot evaluate its predicate, and the app's reading of it.
///
/// The hook writes a FACT — three lines, no timestamp, no JSON — and the APP writes the record. That
/// split is structural rather than stylistic. The log panel is fed by an in-process event that a
/// separate process can never raise, so anything a hook writes to the log file is invisible until
/// somebody goes looking; and a record nobody sees until they already suspect a problem preserves
/// exactly the property being removed, because a guard that has stopped working looks like one that
/// is working.
///
/// Writing it app-side also means ONE rotation threshold and ONE low-disk rule instead of a copy in
/// the shell that could not honour the disk half at all.
/// </summary>
public static class GuardNotInForce_Marker
{
    public const string FILE_NAME = ".guard-not-in-force";

    /// <summary>
    /// Subject of the app entry. A CONSTANT because two places must agree on it exactly: the engine
    /// writes it and the mirror suppresses it by subject, so it never reaches Telegram — the owner
    /// cannot fix a hook mid-turn, and rule 15 keeps what they cannot act on off their phone.
    /// </summary>
    public const string ENTRY_SUBJECT = "a guard stopped working";

    /// <summary>
    /// Reads the marker into one sentence, or null when there is nothing to say.
    ///
    /// TOLERANT ON PURPOSE. The writer is a shell script running on a machine that, by the very
    /// nature of what it is reporting, may be unable to fork — so a truncated or empty marker is a
    /// realistic input rather than a hypothetical one. A marker that exists at all is a fact worth
    /// reporting, even when its detail is missing; the only thing that yields null is having nothing.
    /// </summary>
    public static string? Describe_OrNull(string markerText)
    {
        if (string.IsNullOrWhiteSpace(markerText))
            return null;

        var lines = markerText.Replace("\r", "").Split('\n');

        var hook = Line_OrNull(lines, 0) ?? "a hook";
        var predicate = Line_OrNull(lines, 1);
        var reason = Line_OrNull(lines, 2);

        if (predicate == null)
            return $"{hook} could not evaluate its rule and ALLOWED the call — that guard is not in force.";

        if (reason == null)
            return $"{hook} could not evaluate {predicate} and ALLOWED the call — that guard is not in force.";

        return $"{hook} could not evaluate {predicate} ({reason}) and ALLOWED the call — that guard is not in force.";
    }

    static string? Line_OrNull(IReadOnlyList<string> lines, int index)
    {
        if (index >= lines.Count)
            return null;

        var line = lines[index].Trim();

        return string.IsNullOrEmpty(line) ? null : line;
    }
}
