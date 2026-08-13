using AIOrchestratorCoreLib.Status;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Status;

/// <summary>
/// The probe at the level the engine and the UI actually call it.
///
/// <para>
/// <see cref="A_wake_the_session_ignored_does_not_make_it_look_busy"/> is the regression test for
/// 2026-08-13: under the old mtime read, an enqueue landing 0.92 s after a nudge made a limit-stalled
/// session report "working right now" for the next two minutes, and made the escalation conclude its
/// monitor was fine. Six sessions were deaf for two and a half hours behind that reading.
/// </para>
/// </summary>
public class SessionActivityProbeTests : IDisposable
{
    readonly string _tempRoot;

    public SessionActivityProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-probe-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    static string Stamp(DateTime utc) => utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    /// <summary>Writes a transcript plus the .usage.json that points at it, as the status line does.</summary>
    string Write_Session(string name, params string[] transcriptLines)
    {
        var sessionFolder = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(sessionFolder);

        var transcriptPath = Path.Combine(sessionFolder, "transcript.jsonl");
        File.WriteAllText(transcriptPath, string.Join("\n", transcriptLines));

        var usagePath = Path.Combine(sessionFolder, ".usage.json");
        File.WriteAllText(usagePath, $$"""{"transcript_path":"{{transcriptPath.Replace("\\", "\\\\")}}","version":"2.1.229"}""");

        return usagePath;
    }

    static string Activity(DateTime utc) => $$"""{"type":"assistant","timestamp":"{{Stamp(utc)}}","uuid":"u"}""";

    static string QueueOp(DateTime utc, string operation)
        => $$"""{"type":"queue-operation","operation":"{{operation}}","timestamp":"{{Stamp(utc)}}","sessionId":"s"}""";

    [Fact]
    public void A_wake_the_session_ignored_does_not_make_it_look_busy()
    {
        var now = DateTime.UtcNow;

        // The incident shape exactly: the last real turn is ancient, an earlier wake has sat
        // unanswered for twenty minutes, and the newest growth is the enqueue our own nudge caused
        // seconds ago. Under the mtime read, that last enqueue WAS the liveness signal.
        var usageFile = Write_Session(
            "deaf",
            Activity(now.AddHours(-3)),
            QueueOp(now.AddMinutes(-20), "enqueue"),
            QueueOp(now.AddSeconds(-5), "enqueue"));

        // Not busy: nothing in that file is the session acting.
        Assert.False(SessionActivity_Probe.Is_MidTurn(usageFile));
        Assert.Equal(Stamp(now.AddHours(-3)), Stamp(SessionActivity_Probe.Get_LastActivityUtc_OrNull(usageFile)!.Value));

        // And the silence is dated by the OLDEST unanswered wake, so a fresh nudge cannot restart
        // the clock on the thing it is nudging about.
        Assert.Equal(Stamp(now.AddMinutes(-20)), Stamp(SessionActivity_Probe.Get_OldestUnansweredWakeUtc_OrNull(usageFile)!.Value));
        Assert.True(SessionActivity_Probe.Is_DeafToWakes(usageFile, now, 14));
    }

    [Fact]
    public void A_session_that_is_really_working_reads_mid_turn()
    {
        var now = DateTime.UtcNow;

        var usageFile = Write_Session(
            "busy",
            Activity(now.AddMinutes(-10)),
            QueueOp(now.AddSeconds(-30), "enqueue"),
            Activity(now.AddSeconds(-3)));

        Assert.True(SessionActivity_Probe.Is_MidTurn(usageFile));

        // The wake arrived mid-turn and is legitimately still queued — that must not read as deaf.
        Assert.False(SessionActivity_Probe.Is_DeafToWakes(usageFile, now, 14));
    }

    [Fact]
    public void Picking_the_wake_up_clears_it_even_if_the_session_then_says_nothing()
    {
        var now = DateTime.UtcNow;

        // The obedient member: woken, read it, had nothing worth an entry. Forbidden from writing an
        // acknowledgment, so the channel shows silence and only the dequeue distinguishes it.
        var usageFile = Write_Session(
            "read-it-said-nothing",
            Activity(now.AddHours(-2)),
            QueueOp(now.AddHours(-1), "enqueue"),
            QueueOp(now.AddHours(-1).AddSeconds(1), "dequeue"));

        Assert.Null(SessionActivity_Probe.Get_OldestUnansweredWakeUtc_OrNull(usageFile));
        Assert.False(SessionActivity_Probe.Is_DeafToWakes(usageFile, now, 14));
        Assert.False(SessionActivity_Probe.Is_MidTurn(usageFile));
    }

    [Fact]
    public void A_member_nobody_wrote_to_is_never_deaf_however_long_it_stands_by()
    {
        var now = DateTime.UtcNow;

        var usageFile = Write_Session("standing-by", Activity(now.AddHours(-6)));

        Assert.Null(SessionActivity_Probe.Get_OldestUnansweredWakeUtc_OrNull(usageFile));
        Assert.False(SessionActivity_Probe.Is_DeafToWakes(usageFile, now, 14));
    }

    [Fact]
    public void A_missing_usage_file_is_no_opinion_rather_than_death()
    {
        var absent = Path.Combine(_tempRoot, "never-ran", ".usage.json");

        Assert.Null(SessionActivity_Probe.Get_LastActivityUtc_OrNull(absent));
        Assert.False(SessionActivity_Probe.Is_MidTurn(absent));
        Assert.False(SessionActivity_Probe.Is_DeafToWakes(absent, DateTime.UtcNow, 14));
    }

    [Fact]
    public void Without_a_transcript_path_the_usage_files_own_mtime_still_reports_activity()
    {
        // Older Claude Code versions omit transcript_path. The status line rewrites this file on
        // every render, so its mtime still says turns are being taken — and nothing the APP does
        // rewrites it, so unlike the old transcript mtime it cannot be moved by our own nudge.
        var sessionFolder = Path.Combine(_tempRoot, "no-transcript-path");
        Directory.CreateDirectory(sessionFolder);

        var usagePath = Path.Combine(sessionFolder, ".usage.json");
        File.WriteAllText(usagePath, """{"version":"2.1.100"}""");

        Assert.True(SessionActivity_Probe.Is_MidTurn(usagePath));

        // It carries no wake information, so this path can report activity but never deafness.
        Assert.Null(SessionActivity_Probe.Get_OldestUnansweredWakeUtc_OrNull(usagePath));
        Assert.False(SessionActivity_Probe.Is_DeafToWakes(usagePath, DateTime.UtcNow, 14));
    }

    [Fact]
    public void A_transcript_path_pointing_nowhere_falls_back_rather_than_reporting_deaf()
    {
        var sessionFolder = Path.Combine(_tempRoot, "dangling-transcript");
        Directory.CreateDirectory(sessionFolder);

        var usagePath = Path.Combine(sessionFolder, ".usage.json");
        File.WriteAllText(usagePath, """{"transcript_path":"Z:\\gone\\missing.jsonl"}""");

        Assert.False(SessionActivity_Probe.Is_DeafToWakes(usagePath, DateTime.UtcNow, 14));
        Assert.NotNull(SessionActivity_Probe.Get_LastActivityUtc_OrNull(usagePath));
    }
}
