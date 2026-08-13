using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// Pins the MECHANISM, not just the outcome.
///
/// <para>
/// The defect these guard against is that the app's own wake wrote to the transcript, so the probe
/// testing whether a session was alive is what made it look alive. The single most important case
/// here is <see cref="An_enqueue_is_never_activity"/>: if someone simplifying this reader ever
/// counts an enqueue as a record like any other, that test reddens, and the two-and-a-half-hour
/// outage of 2026-08-13 cannot be reintroduced quietly.
/// </para>
///
/// <para>
/// Fixtures use the REAL record shapes and, where marked, the real timestamps from that incident.
/// </para>
/// </summary>
public class TranscriptActivityReaderTests
{
    static string Activity(string stamp, string type = "assistant")
        => $$"""{"type":"{{type}}","timestamp":"{{stamp}}","uuid":"u"}""";

    static string QueueOp(string stamp, string operation)
        => $$"""{"type":"queue-operation","operation":"{{operation}}","timestamp":"{{stamp}}","sessionId":"s"}""";

    static string Join(params string[] lines) => string.Join("\n", lines);

    static DateTime Utc(string stamp) => DateTime.Parse(stamp).ToUniversalTime();

    [Fact]
    public void An_enqueue_is_never_activity()
    {
        // The app knocking is not the session answering. If this ever passes as activity, the
        // 2026-08-13 self-referential defect is back.
        var text = Join(
            Activity("2026-08-13T13:04:53.337Z"),
            QueueOp("2026-08-13T13:41:12.021Z", "enqueue"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: false);

        Assert.Equal(Utc("2026-08-13T13:04:53.337Z"), activity.LastActivityUtc);
        Assert.Equal(Utc("2026-08-13T13:41:12.021Z"), activity.OldestUnansweredWakeUtc);
    }

    [Fact]
    public void A_dequeue_is_activity_because_the_session_performs_it()
    {
        var text = Join(
            Activity("2026-08-13T13:41:09.625Z"),
            QueueOp("2026-08-13T13:41:56.541Z", "enqueue"),
            QueueOp("2026-08-13T13:42:01.109Z", "dequeue"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: false);

        Assert.Equal(Utc("2026-08-13T13:42:01.109Z"), activity.LastActivityUtc);
        Assert.Null(activity.OldestUnansweredWakeUtc);
    }

    [Fact]
    public void A_remove_consumes_the_wake_too()
    {
        // Enqueues do not pair 1:1 with dequeues — imp-2's live tail held 9 enqueue / 7 dequeue /
        // 2 remove. A reader that only recognised `dequeue` would report a healthy session deaf.
        var text = Join(
            Activity("2026-08-13T13:41:09.625Z"),
            QueueOp("2026-08-13T13:41:56.541Z", "enqueue"),
            QueueOp("2026-08-13T13:42:01.109Z", "remove"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: false);

        Assert.Equal(Utc("2026-08-13T13:42:01.109Z"), activity.LastActivityUtc);
        Assert.Null(activity.OldestUnansweredWakeUtc);
    }

    [Fact]
    public void Stampless_settings_records_are_skipped_not_scanned_past()
    {
        // `mode`, `permission-mode` and `last-prompt` carry no timestamp and are a fifth of a live
        // tail. A scan that stopped at the first one would answer from whatever preceded it.
        var text = Join(
            Activity("2026-08-13T13:04:53.337Z"),
            """{"type":"mode","mode":"default"}""",
            """{"type":"permission-mode","permissionMode":"bypassPermissions"}""",
            """{"type":"last-prompt","prompt":"x"}""",
            QueueOp("2026-08-13T13:41:12.021Z", "enqueue"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: false);

        Assert.Equal(Utc("2026-08-13T13:04:53.337Z"), activity.LastActivityUtc);
        Assert.Equal(Utc("2026-08-13T13:41:12.021Z"), activity.OldestUnansweredWakeUtc);
    }

    [Fact]
    public void A_wake_arriving_mid_turn_is_not_unanswered()
    {
        // A busy session legitimately leaves a wake queued until its turn ends, so "undrained
        // enqueue" ALONE would kill the sessions that are working hardest. Activity after the
        // enqueue is what excludes them.
        var text = Join(
            Activity("2026-08-13T13:00:00.000Z"),
            QueueOp("2026-08-13T13:01:00.000Z", "enqueue"),
            Activity("2026-08-13T13:02:00.000Z", "user"),
            Activity("2026-08-13T13:03:00.000Z"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: false);

        Assert.Equal(Utc("2026-08-13T13:03:00.000Z"), activity.LastActivityUtc);
        Assert.Null(activity.OldestUnansweredWakeUtc);
    }

    [Fact]
    public void A_session_nobody_wrote_to_has_no_unanswered_wake_however_long_it_waits()
    {
        // The legitimately quiet member — STANDING BY, nothing addressed to it. Spared by the
        // mechanism rather than by trusting a marker it wrote about itself.
        var text = Join(
            Activity("2026-08-13T11:00:00.000Z"),
            Activity("2026-08-13T11:00:05.000Z", "system"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: false);

        Assert.Null(activity.OldestUnansweredWakeUtc);
        Assert.False(TranscriptActivity_Reader.Is_DeafToWakes(activity, Utc("2026-08-13T17:00:00.000Z"), 14));
    }

    [Fact]
    public void The_oldest_unanswered_wake_dates_the_silence_not_the_newest()
    {
        // Taking the newest would restart the clock on every nudge — the self-feeding shape this
        // class exists to remove, rebuilt one layer up.
        var text = Join(
            Activity("2026-08-13T13:05:20.857Z"),
            QueueOp("2026-08-13T13:41:11.708Z", "enqueue"),
            QueueOp("2026-08-13T13:49:10.343Z", "enqueue"),
            QueueOp("2026-08-13T16:07:14.536Z", "enqueue"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: false);

        Assert.Equal(Utc("2026-08-13T13:41:11.708Z"), activity.OldestUnansweredWakeUtc);
    }

    [Fact]
    public void Activity_clears_earlier_wakes_so_only_the_current_silence_counts()
    {
        var text = Join(
            QueueOp("2026-08-13T13:00:00.000Z", "enqueue"),
            Activity("2026-08-13T13:00:01.000Z"),
            QueueOp("2026-08-13T13:30:00.000Z", "enqueue"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: false);

        Assert.Equal(Utc("2026-08-13T13:30:00.000Z"), activity.OldestUnansweredWakeUtc);
    }

    [Fact]
    public void No_activity_in_the_window_means_UNKNOWN_and_unknown_never_kills()
    {
        // The window may simply not reach back to the session's last turn. Not knowing must read as
        // alive: a respawn destroys in-context reasoning that exists in exactly one place.
        var text = Join(
            QueueOp("2026-08-13T13:00:00.000Z", "enqueue"),
            QueueOp("2026-08-13T13:10:00.000Z", "enqueue"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: false);

        Assert.False(activity.SawActivity);
        Assert.False(TranscriptActivity_Reader.Is_DeafToWakes(activity, Utc("2026-08-13T17:00:00.000Z"), 14));
    }

    [Fact]
    public void A_half_read_first_line_is_dropped_when_the_window_started_mid_file()
    {
        var text = Join(
            """rue,"timestamp":"2026-08-13T09:00:00.000Z"}""",
            Activity("2026-08-13T13:04:53.337Z"),
            QueueOp("2026-08-13T13:41:12.021Z", "enqueue"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: true);

        Assert.Equal(Utc("2026-08-13T13:04:53.337Z"), activity.LastActivityUtc);
    }

    [Fact]
    public void A_malformed_line_is_skipped_and_the_scan_continues()
    {
        var text = Join(
            Activity("2026-08-13T13:04:53.337Z"),
            "{not json at all",
            "",
            QueueOp("2026-08-13T13:41:12.021Z", "enqueue"));

        var activity = TranscriptActivity_Reader.Parse_Tail(text, startedMidFile: false);

        Assert.Equal(Utc("2026-08-13T13:04:53.337Z"), activity.LastActivityUtc);
        Assert.Equal(Utc("2026-08-13T13:41:12.021Z"), activity.OldestUnansweredWakeUtc);
    }

    [Fact]
    public void The_incident_replayed_rev2_reads_DEAF_and_imp2_reads_HEALTHY()
    {
        // Real records from 2026-08-13. rev-2 was limit-stalled from 13:05:20Z and every later wake
        // sat unconsumed; imp-2 never hit the limit and drained its queue throughout. Evaluated at
        // 16:00Z, which is what the app had before the supervisor's 18:07 briefs.
        var evaluatedAt = Utc("2026-08-13T16:00:00.000Z");

        var deafSession = TranscriptActivity_Reader.Parse_Tail(
            Join(
                Activity("2026-08-13T13:05:20.857Z"),
                QueueOp("2026-08-13T13:41:11.708Z", "enqueue"),
                QueueOp("2026-08-13T13:49:10.343Z", "enqueue")),
            startedMidFile: false);

        var healthySession = TranscriptActivity_Reader.Parse_Tail(
            Join(
                QueueOp("2026-08-13T13:41:09.625Z", "enqueue"),
                QueueOp("2026-08-13T13:41:09.633Z", "dequeue"),
                QueueOp("2026-08-13T13:41:56.541Z", "enqueue"),
                QueueOp("2026-08-13T13:42:01.109Z", "dequeue")),
            startedMidFile: false);

        Assert.True(TranscriptActivity_Reader.Is_DeafToWakes(deafSession, evaluatedAt, 14));
        Assert.False(TranscriptActivity_Reader.Is_DeafToWakes(healthySession, evaluatedAt, 14));
    }

    [Fact]
    public void The_threshold_is_a_floor_not_a_suggestion()
    {
        var activity = TranscriptActivity_Reader.Parse_Tail(
            Join(
                Activity("2026-08-13T13:00:00.000Z"),
                QueueOp("2026-08-13T13:01:00.000Z", "enqueue")),
            startedMidFile: false);

        Assert.False(TranscriptActivity_Reader.Is_DeafToWakes(activity, Utc("2026-08-13T13:14:00.000Z"), 14));
        Assert.True(TranscriptActivity_Reader.Is_DeafToWakes(activity, Utc("2026-08-13T13:15:00.000Z"), 14));
    }

    [Fact]
    public void A_missing_transcript_is_unknown_rather_than_dead()
    {
        var activity = TranscriptActivity_Reader.Read(Path.Combine(Path.GetTempPath(), $"aiorch-absent-{Guid.NewGuid():N}.jsonl"));

        Assert.False(activity.SawActivity);
        Assert.Null(activity.LastActivityUtc);
        Assert.False(TranscriptActivity_Reader.Is_DeafToWakes(activity, DateTime.UtcNow, 14));
    }

    [Fact]
    public void Read_parses_only_the_tail_and_tolerates_a_writer_holding_the_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aiorch-transcript-{Guid.NewGuid():N}.jsonl");

        try
        {
            // Older-than-the-window records that must NOT be reachable, then the recent tail.
            var filler = new List<string>();

            for (var i = 0; i < 400; i++)
                filler.Add($$"""{"type":"assistant","timestamp":"2026-08-13T09:00:00.000Z","padding":"{{new string('x', 1200)}}"}""");

            filler.Add(Activity("2026-08-13T13:04:53.337Z"));
            filler.Add(QueueOp("2026-08-13T13:41:12.021Z", "enqueue"));

            File.WriteAllText(path, string.Join("\n", filler));

            Assert.True(new FileInfo(path).Length > TranscriptActivity_Reader.TAIL_WINDOW_BYTES);

            // Held open by a "session" still appending, which is the normal state of these files.
            using var writerHolding = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

            var activity = TranscriptActivity_Reader.Read(path);

            Assert.Equal(Utc("2026-08-13T13:04:53.337Z"), activity.LastActivityUtc);
            Assert.Equal(Utc("2026-08-13T13:41:12.021Z"), activity.OldestUnansweredWakeUtc);
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
