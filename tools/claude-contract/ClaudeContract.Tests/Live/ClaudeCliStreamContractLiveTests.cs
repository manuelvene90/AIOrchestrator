using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;

namespace ClaudeContract.Tests.Live;

/// <summary>
/// The PERSISTENT transport measured against the real binary — supervisor-mode MEASUREMENTS.md §M9,
/// repeated here so a CLI upgrade that changes the stream's shape is caught by CI instead of by a
/// supervisor that stops answering the owner's phone.
///
/// The format is UNDOCUMENTED (the headless page describes multi-turn as "re-invoke with
/// --resume"), which is precisely why it is pinned: the bridge is allowed to depend on it only
/// because this test fails the moment it moves. Six claims, all from §M9 plus two measured on
/// 2026-09-06 that the study did not take:
///   1. two messages on one process's stdin produce two <c>result</c> events
///   2. hooks run AND are reported as <c>system/hook_started</c> + <c>hook_response</c>, named
///   3. a <c>rate_limit_event</c>, when emitted, carries <c>unifiedWindows</c> with utilisations and reset instants
///   4. the session id is the caller's, stable across every turn of the process
///   5. SIGTERM ends it with 143; closing stdin ends it with 0
///   6. a NEW process with <c>--resume</c> keeps the transcript's memory
///   7. (new) <c>total_cost_usd</c> is CUMULATIVE within a process and starts again in the next one
///   8. (new) a slash command sent as the first user message runs as the session's role command
///
/// One class so xunit runs them sequentially: they share one CLI and one account.
/// </summary>
public class ClaudeCliStreamContractLiveTests(ITestOutputHelper output)
{
    static readonly TimeSpan TURN_TIMEOUT = TimeSpan.FromSeconds(180);
    const string MODEL = "haiku";

    readonly ITestOutputHelper _output = output;

    [LiveFact]
    public void Stream_TwoMessages_TwoResults_HooksReported_SessionIdStable_CostCumulative()
    {
        var probe = new ProbeWorkspace("live-stream");
        var sessionId = Guid.NewGuid().ToString();

        try
        {
            using var stream = Start(probe, ["--session-id", sessionId], "aiorch-contract-stream");

            var stopwatch = Stopwatch.StartNew();
            var first = stream.Send_AndReadUntilResult("Reply with exactly: STREAM_ONE", TURN_TIMEOUT);
            var firstElapsed = stopwatch.Elapsed;

            stopwatch.Restart();
            var second = stream.Send_AndReadUntilResult("Reply with exactly: STREAM_TWO", TURN_TIMEOUT);
            var secondElapsed = stopwatch.Elapsed;

            probe.Save_Evidence("M9-stream.jsonl", string.Join("\n", stream.RawLines));
            probe.Save_Evidence("M9-stream-latency.txt", $"turn 1 {firstElapsed.TotalSeconds:F3}s\nturn 2 {secondElapsed.TotalSeconds:F3}s\n");
            _output.WriteLine($"turn 1 {firstElapsed.TotalSeconds:F3}s, turn 2 {secondElapsed.TotalSeconds:F3}s");

            // 1 — two turns, one process.
            Assert.Equal("result", first[^1]["type"]!.GetValue<string>());
            Assert.Equal("result", second[^1]["type"]!.GetValue<string>());
            Assert.False(stream.HasExited, "the process exited between turns");
            Assert.Contains("STREAM_ONE", first[^1]["result"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Contains("STREAM_TWO", second[^1]["result"]!.GetValue<string>(), StringComparison.Ordinal);

            // 4 — the id is the caller's, on every turn.
            Assert.Equal(sessionId, first[^1]["session_id"]!.GetValue<string>());
            Assert.Equal(sessionId, second[^1]["session_id"]!.GetValue<string>());

            // 2 — the hooks ran (the probe's own log) and the stream named them.
            foreach (var hookEvent in new[] { "SessionStart", "UserPromptSubmit", "Stop" })
                Assert.True(probe.Count_HookEvents(hookEvent) >= 1, $"hook {hookEvent} did not fire in the stream; hooks.log:\n{Read_OrEmpty(probe.HooksLog)}");

            var hookResponses = first.Concat(second)
                .Where(json => json["subtype"]?.GetValue<string>() == "hook_response")
                .Select(json => json["hook_name"]?.GetValue<string>() ?? string.Empty)
                .ToList();

            Assert.Contains(hookResponses, name => name.StartsWith("Stop", StringComparison.Ordinal));

            // 7 — cumulative within the process. Asserted as an INEQUALITY, not an arithmetic
            // identity: what the bridge depends on is that the second figure includes the first, and
            // a per-turn field would be smaller, not merely different.
            var firstCost = first[^1]["total_cost_usd"]!.GetValue<double>();
            var secondCost = second[^1]["total_cost_usd"]!.GetValue<double>();

            Assert.True(secondCost > firstCost, $"total_cost_usd did not grow across turns of one process ({firstCost} → {secondCost}) — the bridge differences this field and would report a negative or doubled turn cost");

            // 5 — SIGTERM.
            var isSigterm = stream.Request_Termination();
            var exitCode = stream.Wait_ForExit(TimeSpan.FromSeconds(15));

            probe.Save_Evidence("M9-sigterm.txt", $"sigterm={isSigterm} exit={exitCode}\n");

            if (isSigterm)
                Assert.Equal(143, exitCode);
            else
                Assert.True(stream.HasExited, "the process did not end after Kill");
        }
        finally
        {
            probe.Delete_TranscriptFolder_BestEffort();
        }
    }

    [LiveFact]
    public void Stream_ASlashCommandAsTheFirstMessage_RunsAsTheRoleCommand()
    {
        // The bridge boots a stream session by sending its role command as the first user message —
        // there is no positional prompt in this mode. Nothing documents that a slash command is
        // honoured there; measured working on 2.1.263 and pinned here, because the alternative
        // (a session that never learns its role) fails silently.
        var probe = new ProbeWorkspace("live-stream-slash");

        try
        {
            using var stream = Start(probe, ["--session-id", Guid.NewGuid().ToString()], "aiorch-contract-stream-slash");

            var events = stream.Send_AndReadUntilResult("/probe-hello stream-arg-1", TURN_TIMEOUT);

            probe.Save_Evidence("M9-slash.jsonl", string.Join("\n", stream.RawLines));

            Assert.Equal("SLASH_ROLE_OK stream-arg-1", probe.Read_ProbeFile_OrEmpty("slash-out.txt"));
            Assert.Equal("result", events[^1]["type"]!.GetValue<string>());

            stream.Close_Stdin();
            Assert.Equal(0, stream.Wait_ForExit(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            probe.Delete_TranscriptFolder_BestEffort();
        }
    }

    [LiveFact]
    public void Stream_ANewProcessWithResume_KeepsTheTranscript_AndItsCostStartsAgain()
    {
        var probe = new ProbeWorkspace("live-stream-resume");
        var sessionId = Guid.NewGuid().ToString();

        try
        {
            double firstProcessCost;

            using (var first = Start(probe, ["--session-id", sessionId], "aiorch-contract-stream-r1"))
            {
                first.Send_AndReadUntilResult("Remember this word: PLATANO. Reply with exactly: NOTED", TURN_TIMEOUT);
                var second = first.Send_AndReadUntilResult("Reply with exactly: STILL_HERE", TURN_TIMEOUT);
                firstProcessCost = second[^1]["total_cost_usd"]!.GetValue<double>();

                first.Close_Stdin();
                Assert.Equal(0, first.Wait_ForExit(TimeSpan.FromSeconds(30)));
            }

            using var resumed = Start(probe, ["--resume", sessionId], "aiorch-contract-stream-r2");
            var afterResume = resumed.Send_AndReadUntilResult("What was the word I asked you to remember? Reply with the word alone.", TURN_TIMEOUT);

            probe.Save_Evidence("M9-resume.jsonl", string.Join("\n", resumed.RawLines));

            // 6 — the memory survived the process.
            Assert.Contains("PLATANO", afterResume[^1]["result"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(sessionId, afterResume[^1]["session_id"]!.GetValue<string>());

            // 7, second half — the baseline is the PROCESS's, so the resumed one starts below where
            // the first ended. A bridge carrying its baseline across a restart reports a negative cost.
            Assert.True(afterResume[^1]["total_cost_usd"]!.GetValue<double>() < firstProcessCost,
                "the resumed process continued the previous process's cumulative cost — the bridge's per-process baseline would be wrong");

            resumed.Close_Stdin();
            Assert.Equal(0, resumed.Wait_ForExit(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            probe.Delete_TranscriptFolder_BestEffort();
        }
    }

    static StreamProcess Start(ProbeWorkspace probe, IReadOnlyList<string> sessionArguments, string name)
    {
        return StreamProcess.Start(
            ClaudeCli.Real(),
            ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--include-hook-events",
             "--model", MODEL, "--dangerously-skip-permissions", "--name", name, .. sessionArguments],
            probe.ProbeFolder);
    }

    static string Read_OrEmpty(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }
}
