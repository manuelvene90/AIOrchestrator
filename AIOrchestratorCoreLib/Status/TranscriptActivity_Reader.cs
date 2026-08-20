using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// What a session's transcript SAYS, rather than when it was last touched.
///
/// <para>
/// THE DEFECT THIS EXISTS TO KILL, measured on 2026-08-13. Liveness was read from the transcript's
/// mtime. The app nudges by writing to a member's channel; that write fires the member's file
/// watcher; the watcher enqueues a notification; and AN ENQUEUE WRITES A RECORD TO THE TRANSCRIPT.
/// So the act of testing whether a session was alive is what made it look alive. Measured on
/// `rev-2`: nudged at 13:49:09.422Z, transcript grew at 13:49:10.343Z — 0.92 s later, from the
/// enqueue, not from a turn. Six minutes on, the escalation read `lastActivity > nudgedUtc`,
/// concluded "alive, its monitor works", and cleared itself. Six sessions sat deaf for two and a
/// half hours behind that reasoning, and no threshold could have helped: every wake re-poisons the
/// signal at its source.
/// </para>
///
/// <para>
/// THE DISTINCTION THAT FIXES IT: the app can cause an ENQUEUE, it cannot cause a DEQUEUE. An
/// enqueue is the app knocking; a dequeue is the session answering the door. So an enqueue is never
/// activity, and everything else is. That also makes a dequeue a READ RECEIPT, which closes a hole
/// our own protocol opened — acknowledgment-only entries are forbidden, so "read it and had nothing
/// to say" and "never read it" are identical in the channel and distinguishable only here.
/// </para>
///
/// <para>
/// Record shapes verified against live transcripts (Claude Code 2.1.229, 2026-08-13): every
/// `queue-operation` carries `timestamp`, `operation` ∈ { enqueue, dequeue, remove } and
/// `sessionId`. `remove` is a THIRD consuming operation — enqueues do not pair 1:1 with dequeues
/// (`imp-2` held 9/7/2), so anything testing `enqueue == dequeue` is wrong. `assistant`, `user`,
/// `system`, `attachment` and the `file-history-*` records all carry timestamps; `mode`,
/// `permission-mode` and `last-prompt` carry NONE and are skipped rather than scanned past.
/// </para>
/// </summary>
public static class TranscriptActivity_Reader
{
    /// <summary>
    /// How much of the tail to parse. These files reach 8.7 MB and the mirror tick is 2 s, so the
    /// whole file is not an option. Measured at this size on that 8.7 MB file: ~115 records, well
    /// inside the tick.
    /// </summary>
    public const int TAIL_WINDOW_BYTES = 256 * 1024;

    const string QUEUE_OPERATION_TYPE = "queue-operation";
    const string ENQUEUE_OPERATION = "enqueue";

    /// <summary>
    /// The two facts the liveness decision needs, plus whether we are entitled to an opinion at all.
    ///
    /// <para>
    /// <paramref name="SawActivity"/> IS THE SAFETY INTERLOCK. False means the window held no
    /// activity record — the session's real last turn may simply be older than the bytes we read, or
    /// it may never have run at all. Both are "we do not know", and a caller must read not-knowing
    /// as ALIVE. A respawn destroys in-context reasoning that exists in exactly one place; wrongly
    /// sparing a deaf session costs one more nudge cycle. The asymmetry is not close, so no state
    /// but a positive determination may kill anything.
    /// </para>
    /// </summary>
    /// <param name="HasOpenToolCall">
    /// The session issued a tool call and its result has not come back — it is INSIDE a command
    /// right now.
    ///
    /// THIS IS THE ONE STATE THE REST OF THIS CLASS CANNOT SEE. Everything else here dates the last
    /// record; a session running one long command writes no records at all while it runs, so a
    /// six-minute build and a dead monitor produce identical silence. On 2026-08-20 the owner
    /// watched a session running a test get declared ORPHANED and respawned — "I was seeing it, it
    /// was not unresponsive, I'm pretty sure it was still working" — and it was: its context was
    /// destroyed for failing to answer during a command it had not finished.
    ///
    /// An unanswered tool_use is positive evidence of work, not an absence to be interpreted.
    /// </param>
    public readonly record struct TranscriptActivity(
        DateTime? LastActivityUtc,
        DateTime? OldestUnansweredWakeUtc,
        bool SawActivity,
        bool HasOpenToolCall = false)
    {
        /// <summary>Nothing read, nothing known — the shape every failure path returns.</summary>
        public static TranscriptActivity Unknown => new(null, null, false, false);
    }

    /// <summary>
    /// Reads the tail of <paramref name="transcriptFilePath"/> and reports what it says. A missing,
    /// locked or half-written file yields <see cref="TranscriptActivity.Unknown"/> — never an
    /// exception, and never a deaf verdict.
    /// </summary>
    public static TranscriptActivity Read(string transcriptFilePath)
    {
        try
        {
            if (!File.Exists(transcriptFilePath))
                return TranscriptActivity.Unknown;

            // ReadWrite share: the session is appending to this file while we read it. Anything
            // stricter throws on a healthy session, which would report the busiest sessions as
            // unknown — the exact population we most need to read correctly.
            using var stream = new FileStream(transcriptFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var windowStart = Math.Max(0, stream.Length - TAIL_WINDOW_BYTES);
            stream.Seek(windowStart, SeekOrigin.Begin);

            using var reader = new StreamReader(stream);
            var tailText = reader.ReadToEnd();

            return Parse_Tail(tailText, startedMidFile: windowStart > 0);
        }
        catch
        {
            return TranscriptActivity.Unknown;
        }
    }

    /// <summary>
    /// The whole decision, as a pure function over JSONL text, so it can be pinned without a session
    /// on the machine.
    ///
    /// <para>
    /// <paramref name="startedMidFile"/> drops the first line, which a byte-offset seek almost
    /// always cuts in half. Left in, that fragment is merely unparseable and skipped — but only
    /// because every line here is independently parsed; a parser that tried to recover from it would
    /// be inventing a record.
    /// </para>
    /// </summary>
    public static TranscriptActivity Parse_Tail(string tailText, bool startedMidFile)
    {
        DateTime? lastActivityUtc = null;
        DateTime? oldestWakeSinceActivityUtc = null;

        // Flipped by whichever came LAST — a call opens it, its result closes it.
        var hasOpenToolCall = false;

        var lines = tailText.Split('\n');

        for (var index = startedMidFile ? 1 : 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();

            if (line.Length == 0)
                continue;

            if (!Try_ReadRecord(line, out var stampedUtc, out var isEnqueue, out var isToolUse, out var isToolResult))
                continue;

            if (isEnqueue)
            {
                // The OLDEST wake still unanswered is the one that dates the silence. Later enqueues
                // are the same unanswered condition restated, and taking the newest would restart
                // the clock on every nudge — which is the self-feeding shape this class exists to
                // remove, rebuilt one layer up.
                oldestWakeSinceActivityUtc ??= stampedUtc;
                continue;
            }

            if (isToolUse)
                hasOpenToolCall = true;
            else if (isToolResult)
                hasOpenToolCall = false;

            // Any non-enqueue record is the SESSION acting: a dequeue, a removal, a tool call, a
            // reply. It clears every wake before it — those were answered by definition.
            lastActivityUtc = stampedUtc;
            oldestWakeSinceActivityUtc = null;
        }

        return new TranscriptActivity(lastActivityUtc, oldestWakeSinceActivityUtc, lastActivityUtc != null, hasOpenToolCall);
    }

    /// <summary>
    /// True when this session was handed a wake it has not picked up for longer than
    /// <paramref name="thresholdMinutes"/>.
    ///
    /// <para>
    /// Every clause is load-bearing and none may be relaxed without re-reading the incident:
    /// <c>SawActivity</c> refuses to judge what we could not see; an unanswered wake is POSITIVE
    /// evidence rather than inferred from silence, so a member nobody has written to is never a
    /// candidate however long it has been quiet — which is how a legitimately idle session is spared
    /// by the mechanism instead of by trusting a marker it wrote about itself.
    /// </para>
    ///
    /// <para>
    /// KNOWN AND DECLARED: a session inside one very long tool call writes no records either, so it
    /// looks the same as a deaf one. The threshold, not cleverness about what a build looks like, is
    /// what keeps that safe — and the exposure is not new. The mtime probe this replaces had it too,
    /// except that an arriving enqueue falsely rescued the session. This removes a wrong rescue; it
    /// adds no new hazard.
    /// </para>
    /// </summary>
    public static bool Is_DeafToWakes(TranscriptActivity activity, DateTime nowUtc, int thresholdMinutes)
    {
        if (!activity.SawActivity || activity.OldestUnansweredWakeUtc == null)
            return false;

        return (nowUtc - activity.OldestUnansweredWakeUtc.Value).TotalMinutes >= thresholdMinutes;
    }

    static bool Try_ReadRecord(string line, out DateTime stampedUtc, out bool isEnqueue, out bool isToolUse, out bool isToolResult)
    {
        stampedUtc = default;
        isEnqueue = false;
        isToolUse = false;
        isToolResult = false;

        try
        {
            if (JsonNode.Parse(line) is not JsonObject record)
                return false;

            // `mode`, `permission-mode` and `last-prompt` carry no timestamp — a fifth of the
            // records in a live tail. They are settings echoes, not activity, and they are SKIPPED:
            // a scan that stopped at the first stampless record would answer from whatever happened
            // to precede one.
            var stampNode = record["timestamp"];

            if (stampNode == null)
                return false;

            if (!DateTime.TryParse(
                    stampNode.GetValue<string>(),
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var parsed))
                return false;

            stampedUtc = parsed.ToUniversalTime();

            isEnqueue = string.Equals(record["type"]?.GetValue<string>(), QUEUE_OPERATION_TYPE, StringComparison.Ordinal)
                && string.Equals(record["operation"]?.GetValue<string>(), ENQUEUE_OPERATION, StringComparison.Ordinal);

            // A tool call and its result are both ordinary stamped records; what matters is which
            // came last. Read from the message content rather than the record type, because both
            // arrive as plain assistant/user records.
            if (record["message"] is JsonObject message && message["content"] is JsonArray blocks)
            {
                foreach (var block in blocks)
                {
                    var kind = (block as JsonObject)?["type"]?.GetValue<string>();

                    if (string.Equals(kind, "tool_use", StringComparison.Ordinal))
                        isToolUse = true;
                    else if (string.Equals(kind, "tool_result", StringComparison.Ordinal))
                        isToolResult = true;
                }
            }

            return true;
        }
        catch
        {
            // A truncated or half-flushed line is not a record. It contributes nothing rather than
            // ending the scan.
            return false;
        }
    }
}
