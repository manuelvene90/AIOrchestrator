using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Xunit;

namespace ClaudeContract.Tests.Stub;

/// <summary>
/// The PERSISTENT transport, offline: one FakeClaude process, several messages on stdin, one
/// <c>result</c> per message. Every assertion here mirrors a measurement in the Live twin
/// (<c>Live/ClaudeCliStreamContractLiveTests</c>) — this category says the fake honours the
/// contract, that one says the contract is still the CLI's.
/// </summary>
public class FakeClaudeStreamStubTests : IDisposable
{
    static readonly TimeSpan TURN_TIMEOUT = TimeSpan.FromSeconds(30);

    readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"fake-claude-stream-{Guid.NewGuid():N}");

    public FakeClaudeStreamStubTests()
    {
        Directory.CreateDirectory(_workingDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
        catch
        {
            // A fake still exiting may hold the folder for a moment.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TwoMessagesOnStdin_ProduceTwoResults_FromOneLivingProcess()
    {
        using var stream = Start();

        var first = stream.Send_AndReadUntilResult("first", TURN_TIMEOUT);
        var second = stream.Send_AndReadUntilResult("second", TURN_TIMEOUT);

        Assert.Equal("result", first[^1]["type"]!.GetValue<string>());
        Assert.Equal("result", second[^1]["type"]!.GetValue<string>());
        Assert.False(stream.HasExited, "the process exited after answering; the whole point of this transport is that it does not");

        // ONE init PER TURN — measured, and the opposite of what the word suggests. A reader that
        // treated it as the process's opening banner would see the second one as a restart.
        Assert.Single(first.Where(Is_Init));
        Assert.Single(second.Where(Is_Init));

        stream.Close_Stdin();
        Assert.Equal(0, stream.Wait_ForExit(TURN_TIMEOUT));
    }

    [Fact]
    public void TheHookEventsAreReportedInTheStream_WithTheirNames()
    {
        using var stream = Start();

        var first = stream.Send_AndReadUntilResult("hello", TURN_TIMEOUT);
        var second = stream.Send_AndReadUntilResult("again", TURN_TIMEOUT);

        Assert.Contains(first, json => json["hook_name"]?.GetValue<string>() == "UserPromptSubmit" && json["subtype"]?.GetValue<string>() == "hook_response");

        // The Stop hook STARTS inside the turn and is ANSWERED AFTER the result — so the response
        // belonging to turn 1 is read while turn 2 is being collected. Measured on the real CLI;
        // asserted here because a reader that frames turns on "everything before the result" files
        // it under the wrong turn, and one that waits for it hangs.
        Assert.Contains(first, json => json["hook_name"]?.GetValue<string>() == "Stop" && json["subtype"]?.GetValue<string>() == "hook_started");
        Assert.DoesNotContain(first, json => json["hook_name"]?.GetValue<string>() == "Stop" && json["subtype"]?.GetValue<string>() == "hook_response");
        Assert.Contains(second, json => json["hook_name"]?.GetValue<string>() == "Stop" && json["subtype"]?.GetValue<string>() == "hook_response");
    }

    [Fact]
    public void WithoutIncludeHookEvents_NoHookEventIsReported()
    {
        // The flag is what turns them on: a bridge that forgets it must not silently see nothing
        // and conclude the hooks did not run.
        using var stream = Start(includeHookEvents: false);

        var events = stream.Send_AndReadUntilResult("hello", TURN_TIMEOUT);

        Assert.DoesNotContain(events, json => json["subtype"]?.GetValue<string>() is "hook_started" or "hook_response");
    }

    [Fact]
    public void TheCostIsCumulativePerProcess_SoTheBridgeMustDifferenceIt()
    {
        // MEASURED on 2.1.263, three equal turns in one process: 0.017715 → 0.024654 → 0.029215,
        // while `usage` stayed per-turn. The fake says the same, so a bridge that reads the field as
        // a per-turn cost fails here rather than over-reporting every turn after the first in a
        // channel nobody re-reads.
        Write_Scenario("""
        { "default": { "total_cost_usd": 0.01 } }
        """);

        using var stream = Start();

        var costs = new[] { "a", "b", "c" }
            .Select(text => stream.Send_AndReadUntilResult(text, TURN_TIMEOUT)[^1]["total_cost_usd"]!.GetValue<double>())
            .ToList();

        Assert.Equal(0.01, costs[0], 6);
        Assert.Equal(0.02, costs[1], 6);
        Assert.Equal(0.03, costs[2], 6);
    }

    [Fact]
    public void ANewProcessResumingTheSameTranscript_StartsItsCostAgainFromZero()
    {
        // Also measured: the cumulative figure belongs to the PROCESS, not to the transcript
        // (0.005228 for the first turn of a resumed process, against 0.018060 for a fresh one). A
        // bridge that carried its baseline across a restart would report a negative turn cost.
        Write_Scenario("""
        { "default": { "total_cost_usd": 0.01 } }
        """);

        var sessionId = Guid.NewGuid().ToString();

        using (var first = Start(sessionId: sessionId))
        {
            first.Send_AndReadUntilResult("a", TURN_TIMEOUT);
            var second = first.Send_AndReadUntilResult("b", TURN_TIMEOUT);
            Assert.Equal(0.02, second[^1]["total_cost_usd"]!.GetValue<double>(), 6);
            first.Close_Stdin();
            first.Wait_ForExit(TURN_TIMEOUT);
        }

        using var resumed = Start(resumeSessionId: sessionId);
        var afterResume = resumed.Send_AndReadUntilResult("c", TURN_TIMEOUT);

        Assert.Equal(0.01, afterResume[^1]["total_cost_usd"]!.GetValue<double>(), 6);
        Assert.Equal(sessionId, afterResume[^1]["session_id"]!.GetValue<string>());
    }

    [Fact]
    public void AnInjectedRateLimitEvent_CarriesTheMeasuredWindowShape()
    {
        Write_Scenario("""
        {
          "turns": [
            { "result": "with limits",
              "rate_limit": { "status": "allowed", "resetsAt": 1788652200, "rateLimitType": "five_hour",
                              "unifiedWindows": { "five_hour": { "utilization": 0.73, "resetsAt": 1788652200 },
                                                  "seven_day": { "utilization": 0.21, "resetsAt": 1788922800 } } } }
          ]
        }
        """);

        using var stream = Start();

        var events = stream.Send_AndReadUntilResult("go", TURN_TIMEOUT);
        var rateLimit = events.SingleOrDefault(json => json["type"]?.GetValue<string>() == "rate_limit_event");

        Assert.True(rateLimit != null, $"no rate_limit_event in:\n{stream.Describe()}");

        var windows = (JsonObject)rateLimit!["rate_limit_info"]!["unifiedWindows"]!;

        Assert.Equal(0.73, windows["five_hour"]!["utilization"]!.GetValue<double>(), 6);
        Assert.Equal(1788652200L, windows["five_hour"]!["resetsAt"]!.GetValue<long>());
        Assert.Equal(0.21, windows["seven_day"]!["utilization"]!.GetValue<double>(), 6);
    }

    [Fact]
    public void ARateLimitEventIsNotEmittedEveryTurn_WhichIsWhyTheBridgeRemembersTheLastOne()
    {
        Write_Scenario("""
        {
          "turns": [
            { "result": "one", "rate_limit": { "unifiedWindows": { "five_hour": { "utilization": 0.5, "resetsAt": 1788652200 } } } },
            { "result": "two" }
          ]
        }
        """);

        using var stream = Start();

        var first = stream.Send_AndReadUntilResult("a", TURN_TIMEOUT);
        var second = stream.Send_AndReadUntilResult("b", TURN_TIMEOUT);

        Assert.Contains(first, json => json["type"]?.GetValue<string>() == "rate_limit_event");
        Assert.DoesNotContain(second, json => json["type"]?.GetValue<string>() == "rate_limit_event");
    }

    [Fact]
    public void GarbageMidStream_DoesNotSwallowTheResultThatFollowsIt()
    {
        // The format is undocumented, so the bridge's parser must skip what it cannot read rather
        // than abandon the turn. The fake can emit exactly that.
        Write_Scenario("""
        { "turns": [ { "result": "after garbage", "stdout_prefix": "not json at all\n" } ] }
        """);

        using var stream = Start();

        var events = stream.Send_AndReadUntilResult("go", TURN_TIMEOUT);

        Assert.Equal("after garbage", events[^1]["result"]!.GetValue<string>());
        Assert.Contains(stream.RawLines, line => line.Contains("not json at all", StringComparison.Ordinal));
    }

    [Fact]
    public void AMalformedStdinLine_IsRefused_RatherThanGuessedAt()
    {
        using var stream = Start();

        stream.Send_RawLine("{\"type\":\"assistant\"}");

        Assert.Equal(1, stream.Wait_ForExit(TURN_TIMEOUT));
        Assert.Contains("refusing", stream.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StreamInputWithoutVerbose_IsRefused()
    {
        using var stream = StreamProcess.Start(
            ClaudeCli.Fake(),
            ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--name", "no-verbose"],
            _workingDirectory);

        Assert.Equal(1, stream.Wait_ForExit(TURN_TIMEOUT));
        Assert.Contains("--verbose", stream.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void StreamInputWithAPlainJsonOutput_IsRefused()
    {
        using var stream = StreamProcess.Start(
            ClaudeCli.Fake(),
            ["-p", "--input-format", "stream-json", "--output-format", "json", "--verbose", "--name", "wrong-output"],
            _workingDirectory);

        Assert.Equal(1, stream.Wait_ForExit(TURN_TIMEOUT));
        Assert.Contains("stream-json", stream.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void AReclaimedSessionId_IsRefused_HereToo()
    {
        // Same rule as the print runner's: --session-id is spendable exactly once, and the retry
        // path has to resume instead. A stream process that dies mid-conversation and is restarted
        // is the common case, so this is the measure that decides whether it can come back at all.
        var sessionId = Guid.NewGuid().ToString();

        using (var first = Start(sessionId: sessionId))
        {
            first.Send_AndReadUntilResult("a", TURN_TIMEOUT);
            first.Close_Stdin();
            first.Wait_ForExit(TURN_TIMEOUT);
        }

        using var second = Start(sessionId: sessionId);

        Assert.Equal(1, second.Wait_ForExit(TURN_TIMEOUT));
        Assert.Contains("already in use", second.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void ATurnThatNamesAnExitCode_KillsTheProcessMidConversation()
    {
        // The fallback ladder's trigger: the transport itself fails, not the turn.
        Write_Scenario("""
        { "turns": [ { "result": "ok" }, { "exit_code": 3, "stderr": "stream died" } ] }
        """);

        using var stream = Start();

        stream.Send_AndReadUntilResult("a", TURN_TIMEOUT);
        stream.Send("b");

        Assert.Equal(3, stream.Wait_ForExit(TURN_TIMEOUT));
        Assert.Contains("stream died", stream.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Sigterm_EndsTheProcessPromptly()
    {
        using var stream = Start();

        stream.Send_AndReadUntilResult("a", TURN_TIMEOUT);

        var isSigterm = stream.Request_Termination();
        var exitCode = stream.Wait_ForExit(TimeSpan.FromSeconds(10));

        // 128+15, the same code the real CLI was measured at. Windows has no SIGTERM at all, so
        // there the process is killed instead and only "it ended" is measurable — asserted as the
        // separate, weaker claim it is rather than folded into one that could pass for two reasons.
        if (isSigterm)
            Assert.Equal(143, exitCode);
        else
            Assert.True(stream.HasExited, "the process did not end after Kill on a platform without SIGTERM");
    }

    static bool Is_Init(JsonObject json)
    {
        return json["type"]?.GetValue<string>() == "system" && json["subtype"]?.GetValue<string>() == "init";
    }

    void Write_Scenario(string json)
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "fake-claude-scenario.json"), json);
    }

    StreamProcess Start(bool includeHookEvents = true, string? sessionId = null, string? resumeSessionId = null)
    {
        List<string> arguments = ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--name", "aiorch-stream-stub"];

        if (includeHookEvents)
            arguments.Add("--include-hook-events");

        if (resumeSessionId != null)
        {
            arguments.Add("--resume");
            arguments.Add(resumeSessionId);
        }
        else
        {
            arguments.Add("--session-id");
            arguments.Add(sessionId ?? Guid.NewGuid().ToString());
        }

        return StreamProcess.Start(ClaudeCli.Fake(), arguments, _workingDirectory);
    }
}
