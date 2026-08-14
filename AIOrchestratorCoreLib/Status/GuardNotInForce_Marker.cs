namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// The marker a hook — or, since 2026-08-14, a watcher — drops when it cannot evaluate its predicate,
/// and the app's reading of it.
///
/// The writer writes a FACT — a few lines, no timestamp, no JSON — and the APP writes the record. That
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
    /// What the marker's writer did when it could not decide, used when the marker does not say.
    ///
    /// It is the hooks' consequence and it is true of them by contract — a hook that cannot evaluate
    /// its predicate ALLOWS, because hooks advise and the app enforces. It stopped being true of every
    /// writer on 2026-08-14, when the watcher became the second one: a watcher allows no call, there
    /// is no call, and describing its failed read that way would put a confident false clause in front
    /// of a true one. Writers that are not hooks say what they did on the marker's sixth line; older
    /// three- and five-line markers keep rendering exactly as they did.
    /// </summary>
    public const string ALLOWED_THE_CALL = "ALLOWED the call";

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

        var identity = Describe_Identity(Line_OrNull(lines, 3), Line_OrNull(lines, 4));
        var consequence = Line_OrNull(lines, 5) ?? ALLOWED_THE_CALL;

        if (predicate == null)
            return $"{hook} could not evaluate its rule and {consequence} — that guard is not in force.{identity}";

        if (reason == null)
            return $"{hook} could not evaluate {predicate} and {consequence} — that guard is not in force.{identity}";

        return $"{hook} could not evaluate {predicate} ({reason}) and {consequence} — that guard is not in force.{identity}";
    }

    /// <summary>
    /// WHO tripped the guard and WHICH COPY of the script wrote the marker — the fields whose absence
    /// made a test indistinguishable from an incident.
    ///
    /// On 2026-08-13 three alerts named `reviewer-readonly-check.sh` and were read as the shipped
    /// guard failing. They were not: the reason text they carried existed only in an implementer's
    /// UNMERGED worktree copy, so what had actually happened was a branch being exercised. The alert
    /// had no field capable of saying so, and a supervisor briefed a reviewer to hunt a payload that
    /// did not exist.
    ///
    /// The md5 is what settles that question — compared against the installed copy it separates "a
    /// session is testing its own build" from "the guard you are relying on is not running". The
    /// comparison is deliberately left to the reader: this component cannot see the installed copy,
    /// and a guess about which file is authoritative is exactly the invented cause the tail beside
    /// this one was deleted for.
    ///
    /// BOTH ARE OPTIONAL. Older hooks write three lines, a truncated write may stop anywhere, and a
    /// machine that cannot fork may have no md5sum — so a missing field drops its clause and changes
    /// nothing else. A marker that exists at all is still a fact worth reporting.
    /// </summary>
    static string Describe_Identity(string? member, string? fingerprint)
    {
        if (member == null && fingerprint == null)
            return "";

        if (fingerprint == null)
            return $" Tripped by {member}.";

        if (member == null)
            return $" The script that wrote this has md5 {fingerprint} — compare it with the installed copy before reading this as a live failure.";

        return $" Tripped by {member}, from a script with md5 {fingerprint} — compare it with the installed copy before reading this as a live failure.";
    }

    static string? Line_OrNull(IReadOnlyList<string> lines, int index)
    {
        if (index >= lines.Count)
            return null;

        var line = lines[index].Trim();

        return string.IsNullOrEmpty(line) ? null : line;
    }
}
