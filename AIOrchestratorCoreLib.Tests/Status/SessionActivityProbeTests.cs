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

    /// <summary>
    /// THE INCIDENT THIS EXISTS FOR (owner, 2026-08-20): a session running a test was declared
    /// ORPHANED and respawned, losing its context — "I was seeing it, it was not unresponsive, I'm
    /// pretty sure it was still working."
    ///
    /// It was working. A session inside one long command writes NOTHING to its transcript while the
    /// command runs, so the freshness test cannot tell a six-minute build from a dead monitor. The
    /// last record here is three hours old — stale by every other measure — and the session is
    /// nonetheless mid-turn, because its tool call has no result yet.
    ///
    /// Written after a mutation test: removing the open-call clause from Is_MidTurn left the whole
    /// suite green, because the reader was covered and the CALLER was not.
    /// </summary>
    [Fact]
    public void ASessionInsideALongCommandIsMidTurn_HoweverStaleItsLastRecordIs()
    {
        var now = DateTime.UtcNow;

        var usageFile = Write_Session(
            "mid-command",
            Activity(now.AddHours(-3)),
            ToolUse(now.AddHours(-3).AddSeconds(1)));

        Assert.True(
            SessionActivity_Probe.Is_MidTurn(usageFile),
            "a session waiting on its own tool call was read as not working — that is what got one respawned");
    }

    /// <summary>
    /// And the other side: once the result comes back, a stale session IS stale. Without this the
    /// fix above would exempt every session that had ever made a tool call, which is all of them.
    /// </summary>
    [Fact]
    public void AFinishedCommandLeavesAStaleSessionStale()
    {
        var now = DateTime.UtcNow;

        var usageFile = Write_Session(
            "command-finished",
            ToolUse(now.AddHours(-3)),
            ToolResult(now.AddHours(-3).AddSeconds(9)));

        Assert.False(SessionActivity_Probe.Is_MidTurn(usageFile));
    }

    /// <summary>
    /// A SESSION WAITING ON ITS OWN SUB-AGENT IS WORKING. Measured 2026-09-14 on
    /// da-vinci-fintech-suite-31: the solo ended its turn with four background agents pending, one of
    /// them was still writing its transcript 46 seconds before the app declared the solo ORPHANED and
    /// killed it — agent and all. The parent's own transcript was ten minutes quiet, because a parent
    /// waiting on a background agent writes nothing. The sub-agent's transcript sits beside it, in a
    /// folder named after the session: `&lt;session&gt;.jsonl` and `&lt;session&gt;/subagents/agent-*.jsonl`.
    /// </summary>
    [Fact]
    public void A_sub_agent_still_writing_makes_its_session_read_as_working()
    {
        var now = DateTime.UtcNow;

        var usageFile = Write_Session("waiting-on-its-agent", Activity(now.AddMinutes(-10)));
        Write_SubAgent("waiting-on-its-agent", "agent-a1", Activity(now.AddSeconds(-5)));

        Assert.True(
            SessionActivity_Probe.Is_MidTurn(usageFile),
            "a session whose sub-agent wrote five seconds ago was read as not working — that is what got one killed");
        Assert.Equal(Stamp(now.AddSeconds(-5)), Stamp(SessionActivity_Probe.Get_LastActivityUtc_OrNull(usageFile)!.Value));
    }

    /// <summary>
    /// And a sub-agent that finished long ago proves nothing about now. Its file was written a moment
    /// ago in this fixture, so this also pins that the reading comes from what the transcript SAYS,
    /// never from when it was touched — the mtime lesson of 2026-08-13, not relearned one folder down.
    /// </summary>
    [Fact]
    public void A_sub_agent_that_finished_long_ago_does_not_make_its_session_look_busy()
    {
        var now = DateTime.UtcNow;

        var usageFile = Write_Session("agent-long-done", Activity(now.AddMinutes(-10)));
        Write_SubAgent("agent-long-done", "agent-b2", Activity(now.AddHours(-2)));

        Assert.False(SessionActivity_Probe.Is_MidTurn(usageFile));
        Assert.Equal(Stamp(now.AddMinutes(-10)), Stamp(SessionActivity_Probe.Get_LastActivityUtc_OrNull(usageFile)!.Value));
    }

    /// <summary>
    /// The newest sub-agent decides, among several. A long session accumulates dozens of finished
    /// agents; the one still running is what says the session is working.
    /// </summary>
    [Fact]
    public void Among_several_sub_agents_the_one_still_writing_decides()
    {
        var now = DateTime.UtcNow;

        var usageFile = Write_Session("many-agents", Activity(now.AddMinutes(-30)));
        Write_SubAgent("many-agents", "agent-old", Activity(now.AddHours(-1)));
        Write_SubAgent("many-agents", "agent-live", Activity(now.AddSeconds(-20)));
        File.SetLastWriteTimeUtc(SubAgentPath("many-agents", "agent-live"), now.AddSeconds(-20));
        File.SetLastWriteTimeUtc(SubAgentPath("many-agents", "agent-old"), now.AddHours(-1));

        Assert.True(SessionActivity_Probe.Is_MidTurn(usageFile));
    }

    /// <summary>
    /// SUB-AGENT EVIDENCE ONLY ADDS LIFE. A parent the probe cannot read is "no opinion", and an old
    /// sub-agent date must not replace that with a stamp the escalation would find older than its
    /// nudge — which would turn a session it leaves alone into one it reports deaf.
    /// </summary>
    [Fact]
    public void A_sub_agent_never_turns_an_unreadable_session_into_a_verdict()
    {
        var now = DateTime.UtcNow;

        var usageFile = Write_Session("parent-unreadable", QueueOp(now.AddMinutes(-20), "enqueue"));
        Write_SubAgent("parent-unreadable", "agent-c3", Activity(now.AddMinutes(-30)));

        Assert.Null(SessionActivity_Probe.Get_LastActivityUtc_OrNull(usageFile));
    }

    [Fact]
    public void A_session_whose_last_reply_was_a_limit_refusal_reads_as_blocked_on_the_limit()
    {
        var now = DateTime.UtcNow;

        var usageFile = Write_Session(
            "limit-blocked",
            Activity(now.AddMinutes(-31)),
            "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(now.AddMinutes(-30))
            + "\",\"uuid\":\"u\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"You've hit your weekly limit\"}]},"
            + "\"error\":\"rate_limit\",\"isApiErrorMessage\":true,\"apiErrorStatus\":429}");

        Assert.True(SessionActivity_Probe.Is_BlockedOnUsageLimit(usageFile));
    }

    [Fact]
    public void A_missing_usage_file_is_not_blocked_on_a_limit()
    {
        Assert.False(SessionActivity_Probe.Is_BlockedOnUsageLimit(Path.Combine(_tempRoot, "never-ran", ".usage.json")));
    }

    string SubAgentPath(string sessionName, string agentId)
        => Path.Combine(_tempRoot, sessionName, "transcript", "subagents", agentId + ".jsonl");

    /// <summary>Writes a sub-agent transcript where Claude Code puts it: beside the session's own, in a folder named after it.</summary>
    void Write_SubAgent(string sessionName, string agentId, params string[] lines)
    {
        var path = SubAgentPath(sessionName, agentId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join("\n", lines));
    }

    static string ToolUse(DateTime utc)
        => "{\"type\":\"assistant\",\"timestamp\":\"" + Stamp(utc)
         + "\",\"uuid\":\"u\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t1\"}]}}";

    static string ToolResult(DateTime utc)
        => "{\"type\":\"user\",\"timestamp\":\"" + Stamp(utc)
         + "\",\"uuid\":\"u\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\"}]}}";
}
