using System.Text.Json;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.SessionFiles;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Channels.StatusLog;

/// <summary>
/// THE APP'S BOOKKEEPING ABOUT ONE SESSION, out of its channel. Measured on the VPS over nine days
/// (2026-09-15, plan 01's report): 4 955 app-authored entries against 1 122 from members and 714 from
/// the owner, and **zero turns in the whole window were started by an app entry** — yet every session
/// re-read all of them at boot. This file is where the ones nobody has to answer go instead.
///
/// <para>
/// IT IS NOT A SECOND CHANNEL AND NOT A SECOND LOG. <c>orchestrator.log.jsonl</c> is the operator's
/// record of the orchestration and <c>turns.jsonl</c> is the transcript of one session's turns; this
/// is the set of statements the app has made TO one session. The channel remains the human-readable
/// register and the wake mechanism (2026-09-15 one-wake-model spec, §3.4).
/// </para>
/// <para>
/// A RECORD CARRIES THE ENTRY THE APP WOULD HAVE APPENDED, rendered exactly as
/// <see cref="ChannelAppender"/> renders one, so every reader downstream is the one that already
/// exists: <see cref="ChannelEntry_Parser"/> parses it, <see cref="ChannelEntry_Digest"/> identifies
/// it, <see cref="PrintTurn_Trigger.Is_AgentNote"/> recognises it and
/// <c>PrintTurnPrompt_Builder</c> renders it. Storing loose fields instead would have meant a second
/// implementation of each (decision 12). The JSON envelope is what makes a body containing a channel
/// header safe to store — a plain concatenation would split such a record in two.
/// </para>
/// <para>
/// EVERY METHOD SWALLOWS ITS I/O. Losing a bookkeeping line is never a reason to lose a turn — the
/// same ruling <see cref="Running.StatePack.StatePack_Writer"/> makes about the pack. A log that
/// cannot be read reads as empty and the session falls back to what it had before this existed.
/// </para>
/// </summary>
public static class StatusLog_Store
{
    public const string FILE_NAME = "status.jsonl";

    /// <summary>
    /// The cursor key the notes of this log are delivered under, in the session's state file. It
    /// begins with a dot so it can never collide with a member id or with
    /// <c>TurnSource_Factory.OWNER_KEY</c> — a spoke is named by a word an agent typed.
    /// </summary>
    public const string CURSOR_KEY = ".status";

    /// <summary>
    /// How many records are kept. Above this the file is rewritten with the newest
    /// <see cref="MAX_RECORDS"/>, which is what <see cref="Channel_Compactor"/> does to a channel and
    /// what the cursor's prune already absorbs. Sized so that a session's whole 2 h note window
    /// (<see cref="PrintTurn_Trigger.AGENT_NOTE_WINDOW"/>) cannot fall out of it at any rate this
    /// system has ever produced: the busiest channel measured on the VPS wrote 4 955 app entries over
    /// nine days across 119 channels.
    /// </summary>
    public const int MAX_RECORDS = 400;

    const string INDEX_KEY = "i";
    const string STAMP_KEY = "at";
    const string SUBJECT_KEY = "subject";
    const string BODY_KEY = "body";

    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return SessionFile_Locator.Get_File(paths, role, orchId, memberId, FILE_NAME);
    }

    /// <summary>
    /// Appends one record. Returns whether it landed — the callers that record state on the strength
    /// of a note must check it, exactly as they check <see cref="ChannelAppender.Append_AppEntry"/>.
    /// </summary>
    public static bool Append(string logFile, string subject, string body, DateTime nowLocal)
    {
        try
        {
            var folder = Path.GetDirectoryName(logFile);

            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            var record = new JsonObject
            {
                [INDEX_KEY] = Count_Records(logFile) + 1,
                [STAMP_KEY] = nowLocal.ToString("yyyy-MM-dd HH:mm"),
                [SUBJECT_KEY] = AppEntryAudience_Tag.Apply(subject, AppEntryAudiences.Agent),
                [BODY_KEY] = body.Trim(),
            };

            File.AppendAllText(logFile, record.ToJsonString() + "\n");
            Trim_IfNeeded(logFile);

            return true;
        }
        catch
        {
            // Swallowed by design — see the type header.
            return false;
        }
    }

    /// <summary>
    /// The log's records as channel entries, oldest first. Empty for a log that is absent, empty or
    /// unreadable — never an exception (decision 21: a reader that cannot evaluate its input says so
    /// by answering nothing, and the caller's fallback is what the system did before this existed).
    /// </summary>
    public static IReadOnlyList<IChannelEntry> Read_Entries(string logFile)
    {
        try
        {
            if (!File.Exists(logFile))
                return [];

            List<IChannelEntry> entries = [];

            foreach (var line in File.ReadLines(logFile))
            {
                var entry = Parse_OrNull(line);

                if (entry != null)
                    entries.Add(entry);
            }

            return entries;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// A line that is not a record is SKIPPED, not fatal. The file is appended to by one process, but
    /// a crash mid-write can leave a partial last line, and one torn line must not blind a session to
    /// the four hundred good ones above it.
    /// </summary>
    static IChannelEntry? Parse_OrNull(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            if (JsonNode.Parse(line) is not JsonObject record)
                return null;

            var index = record[INDEX_KEY]?.GetValue<int>() ?? 0;
            var stamp = record[STAMP_KEY]?.GetValue<string>();
            var subject = record[SUBJECT_KEY]?.GetValue<string>();
            var body = record[BODY_KEY]?.GetValue<string>() ?? string.Empty;

            if (stamp == null || subject == null)
                return null;

            // RENDERED THE WAY ChannelAppender RENDERS ONE, then parsed by the parser that reads a
            // channel. Building an IChannelEntry by hand here would be a second implementation of the
            // entry's shape, and the one thing this file must never do is disagree with the appender.
            var text = $"## [{index}] FROM app — {stamp} — {subject}\n\n{body}\n";

            return ChannelEntry_Parser.Parse_All(text).FirstOrDefault();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static int Count_Records(string logFile)
    {
        // The INDEX ONLY, and it is diagnostic (decision 12: nothing decides delivery from a number).
        // Identity is the digest, exactly as it is for a channel entry.
        return File.Exists(logFile) ? File.ReadLines(logFile).Count(line => !string.IsNullOrWhiteSpace(line)) : 0;
    }

    static void Trim_IfNeeded(string logFile)
    {
        var lines = File.ReadAllLines(logFile).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

        if (lines.Count <= MAX_RECORDS)
            return;

        // ATOMIC, so a session reading while this runs sees the old file or the new one, never half
        // of each — the same guarantee StatePack_Writer gives the pack.
        Atomic_FileWriter.Write_AllText(logFile, string.Join('\n', lines.Skip(lines.Count - MAX_RECORDS)) + "\n");
    }
}
