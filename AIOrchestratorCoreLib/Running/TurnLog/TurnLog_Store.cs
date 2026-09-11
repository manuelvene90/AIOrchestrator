using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnResult;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.TurnLog;

/// <summary>
/// WHAT THE OWNER USED TO TAKE A SCREENSHOT OF. A terminal session shows its work in a window; a
/// bridge-driven one has no window, and until now the only durable trace of a turn was the entry it
/// finally wrote — so "what is it doing right now" had no answer and "why did that turn fail" had a
/// 600-character tail of stderr. This file is the trace: one JSON line per stream event, or one per
/// print turn, beside the session's own state file.
///
/// <para>
/// IT IS A RING, NOT AN ARCHIVE. A supervisor left running for a day writes a lot of events, and
/// nothing here is worth unbounded disk: past <see cref="MAX_BYTES"/> the file is rolled to a single
/// <c>.1</c> sibling and started again, so at most two files exist and the reader spans both. The
/// channel remains the record; this is the tail.
/// </para>
/// <para>
/// A FAILURE TO WRITE IT IS NEVER A FAILURE OF THE TURN. Every method here swallows its I/O errors:
/// losing the ability to answer /tail is not a reason to lose the work.
/// </para>
/// <para>
/// LINES ARE BUFFERED, NOT WRITTEN ONE BY ONE. A streaming turn emits a line per tool call, per tool
/// result and per assistant chunk — hundreds in a long turn — and each one used to cost a
/// <c>CreateDirectory</c>, a stat and an open/append/close of its own. They now accumulate per file
/// and land in one append per <see cref="FLUSH_AFTER_MILLISECONDS"/>, which for a burst is one write
/// instead of dozens and for a slow trickle is still one write per line.
/// </para>
/// <para>
/// NOTHING WAITS FOR THE TIMER TO BE READ, and that is the part that had to be true rather than
/// likely: <see cref="Read_LastRecords"/> flushes before it reads, so /tail and "what is it doing
/// right now" see the buffer as if it were the file. A turn's RESULT flushes as it is written, so a
/// finished turn is complete on disk the moment it finishes; <see cref="Flush_All"/> on shutdown
/// covers the rest. What a crash can still lose is at most the last few hundred milliseconds of
/// events of a turn that was running — the same exposure the process already has for any write in
/// flight, and never the result line.
/// </para>
/// </summary>
public static class TurnLog_Store
{
    public const string FILE_NAME = "turns.jsonl";
    public const string ROLLED_SUFFIX = ".1";
    public const long MAX_BYTES = 4 * 1024 * 1024;

    /// <summary>
    /// How long a line may sit in the buffer before the next append writes it out. Short enough that
    /// a human reading the tail of a live turn cannot perceive it, long enough that a burst of
    /// streamed events collapses into one write.
    /// </summary>
    public const int FLUSH_AFTER_MILLISECONDS = 200;

    /// <summary>
    /// Buffered bytes that force a write regardless of the clock, so a turn that streams a large
    /// tool result cannot hold an unbounded amount of text in memory waiting for a tick of it.
    /// </summary>
    public const int FLUSH_AFTER_BYTES = 64 * 1024;

    public const string KIND_KEY = "aiorch_kind";
    public const string KIND_TURN = "turn";
    public const string KIND_EVENT = "event";

    /// <summary>
    /// A CLOSING TURN, not a turn: the one short print turn that resumes a transcript the deadline
    /// killed and asks it where it got to. It carries the killed turn's request id with
    /// <c>-closing</c> on the end, so /tail shows the pair together and nothing has to compare costs
    /// between two records that look like two attempts at the same work.
    /// </summary>
    public const string KIND_CLOSING_TURN = "closing";
    public const string REQUEST_ID_KEY = "aiorch_request_id";
    public const string AT_KEY = "aiorch_at";

    public static string Get_File(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        var stateFile = PrintSessionState_Store.Get_StateFile(paths, role, orchId, memberId);
        var folder = Path.GetDirectoryName(stateFile) ?? paths.Root;

        // The supervisor's and communicator's state files share the orchestration folder under a
        // role prefix, so their logs must too, or two roles would write one file.
        var prefix = Path.GetFileName(stateFile).Replace(PrintSessionState_Store.STATE_FILE_NAME, string.Empty);

        return Path.Combine(folder, prefix + FILE_NAME);
    }

    /// <summary>One line of the raw stream, with the request it belongs to stamped on it.</summary>
    public static void Append_StreamEvent(string logFile, string requestId, string rawLine)
    {
        var json = StreamTurn.StreamEvent_Reader.Parse_OrNull(rawLine);

        // A line that is not JSON is still evidence — kept as a string so the file stays readable
        // line by line and the reader never has to guess whether a line parses.
        var record = json ?? new JsonObject { ["type"] = "unparsed", ["raw"] = rawLine };

        // A STREAMED TURN ENDS WITH ITS OWN result EVENT — there is no Append_TurnResult on that path
        // — so this is where a stream turn becomes complete on disk. Flushing on it is what keeps
        // "the result line is never buffered" true for both kinds of turn.
        Append(logFile, Stamp(record, KIND_EVENT, requestId), flushNow: Is_ResultRecord(record));
    }

    /// <summary>One whole print turn: its result document, or what came back instead of one.</summary>
    public static void Append_TurnResult(string logFile, string requestId, ITurnResult result)
    {
        Append_TurnResult(logFile, requestId, result, KIND_TURN);
    }

    /// <summary>The closing turn's result, tagged <see cref="KIND_CLOSING_TURN"/> — same record, different word.</summary>
    public static void Append_ClosingTurnResult(string logFile, string requestId, ITurnResult result)
    {
        Append_TurnResult(logFile, requestId, result, KIND_CLOSING_TURN);
    }

    static void Append_TurnResult(string logFile, string requestId, ITurnResult result, string kind)
    {
        // THE RESULT LINE, not the whole of stdout. Both transports now speak NDJSON, and a blob of
        // several lines parses as nothing — which would file every print turn as the synthesised
        // record below and quietly drop the fields /tail reads.
        var record = TurnResult.TurnResult_Parser.Find_TurnDocument_OrNull(result.RawStdout) ?? new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = result.Subtype,
            ["is_error"] = result.IsError,
            ["result"] = result.ResultText,
            ["session_id"] = result.SessionId,
            ["total_cost_usd"] = result.TotalCostUsd,
            ["duration_ms"] = result.DurationMs,
            ["exit_code"] = result.ExitCode,
            ["stderr_tail"] = Tail(result.RawStderr, 2000),
        };

        // The whole turn, and the last thing written about it: never left in a buffer.
        Append(logFile, Stamp(record, kind, requestId), flushNow: true);
    }

    /// <summary>
    /// The last <paramref name="count"/> lines, oldest first, spanning the rolled sibling when the
    /// live file alone does not hold enough. Empty for a session that has not run yet — which is an
    /// answer, not an error.
    /// </summary>
    public static IReadOnlyList<JsonObject> Read_LastRecords(string logFile, int count)
    {
        // BEFORE READING, ALWAYS. The buffer is not a cache of the file — it is text that belongs in
        // the file and is not there yet, so a read that skipped this would answer "what is it doing
        // right now" with everything except the most recent thing it did.
        Flush(logFile);

        List<string> lines = [.. Read_Lines(logFile + ROLLED_SUFFIX), .. Read_Lines(logFile)];
        List<JsonObject> records = [];

        foreach (var line in lines.Skip(Math.Max(0, lines.Count - count)))
        {
            if (StreamTurn.StreamEvent_Reader.Parse_OrNull(line) is JsonObject record)
                records.Add(record);
        }

        return records;
    }

    /// <summary>
    /// Every record of the LAST turn — from the first record carrying the newest request id. Keyed
    /// on the request id rather than on a result boundary because a straggler event (the <c>Stop</c>
    /// hook answers after the result, measured) would otherwise open a phantom turn of its own.
    /// </summary>
    public static IReadOnlyList<JsonObject> Read_LastTurn(string logFile, int maxRecords)
    {
        var records = Read_LastRecords(logFile, maxRecords);
        var newest = records.LastOrDefault(record => Read_RequestId_OrNull(record) != null);

        if (newest == null)
            return records;

        var requestId = Read_RequestId_OrNull(newest);

        return [.. records.Where(record => Read_RequestId_OrNull(record) == requestId)];
    }

    public static string? Read_RequestId_OrNull(JsonObject record)
    {
        try
        {
            return record[REQUEST_ID_KEY]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }

    public static DateTime? Read_At_OrNull(JsonObject record)
    {
        try
        {
            return DateTime.TryParse(record[AT_KEY]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at) ? at.ToUniversalTime() : null;
        }
        catch
        {
            return null;
        }
    }

    static JsonObject Stamp(JsonObject record, string kind, string requestId)
    {
        var stamped = record.DeepClone().AsObject();

        stamped[KIND_KEY] = kind;
        stamped[REQUEST_ID_KEY] = requestId;
        stamped[AT_KEY] = DateTime.UtcNow.ToString("o");

        return stamped;
    }

    /// <summary>
    /// Text written but not yet on disk, per log file, with the moment that file was last flushed.
    /// <para>
    /// ONE LOCK FOR BUFFERING AND FOR WRITING, held across the append itself. Two flushes of one file
    /// running concurrently would interleave their text and the file is read line by line, so the
    /// order has to be the order they were buffered in. It costs nothing worth measuring: a flush
    /// happens at most once per <see cref="FLUSH_AFTER_MILLISECONDS"/> per file, and what queues
    /// behind it is a StringBuilder append.
    /// </para>
    /// </summary>
    sealed class PendingLines
    {
        public readonly System.Text.StringBuilder Text = new();
        public long LastFlushedAtTicks = Environment.TickCount64;
    }

    static readonly Lock _bufferLock = new();
    static readonly Dictionary<string, PendingLines> _pendingByFile = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Writes out every buffered line of every log file. For shutdown — the loops have stopped, so
    /// nothing will arrive to trigger the next flush, and the trace of the turns that were running
    /// when the app stopped is the tail most worth keeping.
    /// </summary>
    public static void Flush_All()
    {
        lock (_bufferLock)
        {
            foreach (var logFile in _pendingByFile.Keys.ToList())
                Flush_Locked(logFile);
        }
    }

    static void Append(string logFile, JsonObject record, bool flushNow)
    {
        string line;

        try
        {
            line = record.ToJsonString() + "\n";
        }
        catch
        {
            // See the class remark: the tail is never worth the turn. A record that cannot be
            // serialised is dropped here rather than taking the turn down with it.
            return;
        }

        lock (_bufferLock)
        {
            if (!_pendingByFile.TryGetValue(logFile, out var pending))
                _pendingByFile[logFile] = pending = new PendingLines();

            pending.Text.Append(line);

            var due = flushNow
                || pending.Text.Length >= FLUSH_AFTER_BYTES
                || Environment.TickCount64 - pending.LastFlushedAtTicks >= FLUSH_AFTER_MILLISECONDS;

            if (due)
                Flush_Locked(logFile);
        }
    }

    /// <summary>Writes out one file's buffered lines, if it has any.</summary>
    static void Flush(string logFile)
    {
        lock (_bufferLock)
            Flush_Locked(logFile);
    }

    static void Flush_Locked(string logFile)
    {
        if (!_pendingByFile.TryGetValue(logFile, out var pending) || pending.Text.Length == 0)
            return;

        var text = pending.Text.ToString();

        try
        {
            var folder = Path.GetDirectoryName(logFile);

            if (!string.IsNullOrEmpty(folder))
            {
                Diagnostics.TickIo_Counters.Count_TurnLogFileSyscall();
                Directory.CreateDirectory(folder);
            }

            Roll_IfTooBig(logFile);

            Diagnostics.TickIo_Counters.Count_TurnLogFileSyscall();
            File.AppendAllText(logFile, text);
        }
        catch
        {
            // See the class remark: the tail is never worth the turn. The buffer is cleared below
            // either way — holding text back for a disk that just refused it would grow without
            // bound and retry the same refusal on every line of the turn.
        }

        pending.Text.Clear();
        pending.LastFlushedAtTicks = Environment.TickCount64;
    }

    /// <summary>
    /// Rolls the file when it has outgrown the ring. Checked once per FLUSH rather than once per
    /// line, so the live file can overshoot <see cref="MAX_BYTES"/> by at most one buffer — see
    /// <see cref="FLUSH_AFTER_BYTES"/> — which is 1.5% of the ring and buys the writes this saves.
    /// </summary>
    static void Roll_IfTooBig(string logFile)
    {
        try
        {
            Diagnostics.TickIo_Counters.Count_TurnLogFileSyscall();

            if (!File.Exists(logFile) || new FileInfo(logFile).Length < MAX_BYTES)
                return;

            File.Move(logFile, logFile + ROLLED_SUFFIX, overwrite: true);
        }
        catch
        {
            // A file that cannot be rolled keeps growing; that is better than losing the turn.
        }
    }

    /// <summary>
    /// Whether this record is a turn's <c>result</c> — the last line of a streamed turn. Read off the
    /// record rather than off the raw line so an unparsed line can never be mistaken for one.
    /// </summary>
    static bool Is_ResultRecord(JsonObject record)
    {
        try
        {
            return record["type"]?.GetValue<string>() == "result";
        }
        catch
        {
            return false;
        }
    }

    static IReadOnlyList<string> Read_Lines(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllLines(path) : [];
        }
        catch
        {
            return [];
        }
    }

    static string Tail(string text, int maxLength)
    {
        var trimmed = (text ?? string.Empty).Trim();

        return trimmed.Length <= maxLength ? trimmed : "…" + trimmed[^maxLength..];
    }
}
