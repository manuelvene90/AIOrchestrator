namespace AIOrchestratorCoreLib.Mirroring;

/// <summary>
/// THE ONE MESSAGE THAT SAYS WHAT NEVER ARRIVED, and carries it.
///
/// <para>
/// After thirty minutes of failing sends the mirror used to CONFIRM the append it could not
/// deliver — which advances the cursor past those entries for ever — and write one Error line to a
/// log file on a machine the owner does not read. From the phone it was indistinguishable from
/// nothing having happened: the conversation simply stopped, which is the exact failure the owner
/// reported as being "cut off, with no way to know".
/// </para>
/// <para>
/// SO THE ENTRIES ARE KEPT AND DELIVERED LATE, as ONE document rather than as the burst that would
/// arrive if they were replayed as messages. Decision 14's neighbour: a catch-up that scrolls the
/// owner's phone for a minute is a second failure, not a recovery.
/// </para>
/// <para>
/// IT IS BUILT FROM WHAT THE CHANNEL ALREADY SAID — subject, time and body, verbatim. Nothing is
/// summarised here: this file is the audit trail of an outage, and a summary of it would be the one
/// copy of the thing it is standing in for.
/// </para>
/// </summary>
public static class UndeliveredDigest_Builder
{
    /// <summary>
    /// Past this, the parked list stops growing and says so. An outage long enough to fill it is
    /// one the channel file itself is the record of; what must never happen is a bridge that runs
    /// out of memory holding a backlog nobody can read yet.
    /// </summary>
    public const int MAX_PARKED_ENTRIES = 200;

    public const string FILE_NAME = "undelivered-entries.md";

    /// <summary>The caption, which is the ONLY part the owner reads before deciding to open the file.</summary>
    public static string Build_CaptionHtml(int entryCount, DateTime firstUtc, DateTime lastUtc)
    {
        var span = firstUtc == lastUtc
            ? $"at {firstUtc:HH:mm}"
            : $"from {firstUtc:HH:mm} to {lastUtc:HH:mm}";

        return $"⚠️ {entryCount} {(entryCount == 1 ? "entry" : "entries")} {span} UTC did not reach you — Telegram kept refusing. They are attached.";
    }

    /// <summary>
    /// <paramref name="entries"/> is oldest first, as they were appended. Each is
    /// (whenUtc, author, subject, body) as the channel recorded it.
    /// </summary>
    public static byte[] Build_Content(IReadOnlyList<(DateTime WhenUtc, string Author, string Subject, string Body)> entries)
    {
        var text = new System.Text.StringBuilder();

        text.Append("# Entries that never reached the phone\n\n");
        text.Append("Telegram refused every send for the whole retry window, so these were held back and are delivered here.\n");
        text.Append("They are also in the channel file, in their place, which stays the record of record.\n");

        foreach (var entry in entries)
        {
            text.Append($"\n---\n\n## {entry.WhenUtc:yyyy-MM-dd HH:mm} UTC — {entry.Author} — {entry.Subject}\n\n");
            text.Append(entry.Body.TrimEnd());
            text.Append('\n');
        }

        return new System.Text.UTF8Encoding(false).GetBytes(text.ToString());
    }
}
