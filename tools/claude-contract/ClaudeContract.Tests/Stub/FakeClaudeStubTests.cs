using System.Text.Json.Nodes;
using Xunit;

namespace ClaudeContract.Tests.Stub;

/// <summary>
/// The fake honours the flags the bridge sends and answers in the measured shape. Offline, seconds.
/// Each test gets its own working directory, because the fake keeps its invocation log and reads
/// its scenario from there.
/// </summary>
public class FakeClaudeStubTests : IDisposable
{
    static readonly TimeSpan TIMEOUT = TimeSpan.FromSeconds(60);
    const string SESSION_ID = "11111111-2222-3333-4444-555555555555";

    readonly string _workDir;

    public FakeClaudeStubTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), $"fake-claude-stub-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        Directory.Delete(_workDir, recursive: true);
    }

    [Fact]
    public void JsonResult_CarriesTheMeasuredKeys_AndNoRateLimits()
    {
        var run = Run(["-p", "--output-format", "json", "--session-id", SESSION_ID, "--model", "haiku"], stdin: "hello");

        Assert.Equal(0, run.ExitCode);
        var root = Parse_Object(run.Stdout);

        foreach (var key in new[] { "type", "subtype", "is_error", "duration_ms", "duration_api_ms", "num_turns", "result", "session_id", "total_cost_usd", "usage", "modelUsage", "permission_denials", "api_error_status", "uuid" })
            Assert.True(root.ContainsKey(key), $"missing key '{key}' in {run.Stdout}");

        Assert.DoesNotContain("rate_limits", run.Stdout);
        Assert.Equal("result", root["type"]!.GetValue<string>());
        Assert.Equal("DONE", root["result"]!.GetValue<string>());
        Assert.Equal(SESSION_ID, root["session_id"]!.GetValue<string>());
        Assert.NotNull(root["usage"]!["cache_read_input_tokens"]);
        Assert.NotNull(root["modelUsage"]!["claude-haiku-4-5-20251001"]);
    }

    /// <summary>
    /// A session has to be CLAIMED before it can be resumed, and this test used to skip that step: it
    /// resumed an id the fake had never issued and expected exit 0. MEASURED against 2.1.263 on
    /// 2026-09-06, the real CLI answers <c>No conversation found with session ID: &lt;uuid&gt;</c> and exits 1,
    /// so what the test pinned was the fake being more permissive than the thing it stands in for — the
    /// exact gap that let a real defect stay green once already.
    /// </summary>
    [Fact]
    public void Resume_EchoesTheResumedSessionId()
    {
        Assert.Equal(0, Run(["-p", "--output-format", "json", "--session-id", SESSION_ID], stdin: "first").ExitCode);

        var run = Run(["-p", "--output-format", "json", "--resume", SESSION_ID], stdin: "again");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(SESSION_ID, Parse_Object(run.Stdout)["session_id"]!.GetValue<string>());
    }

    /// <summary>
    /// The other half, which the fake did not model at all: resuming an id nothing ever created is refused.
    /// MEASURED against 2.1.263 on 2026-09-06 — exit 1, <c>No conversation found with session ID: &lt;uuid&gt;</c>.
    /// The bridge claims an id BEFORE starting the process, so a first turn that dies before the CLI makes
    /// the transcript leaves every retry resuming nothing; without this the suite could not see it.
    /// </summary>
    [Fact]
    public void ResumingASessionThatWasNeverCreated_IsRefused()
    {
        var run = Run(["-p", "--output-format", "json", "--resume", "3f1810a4-bf77-47d7-b7cc-9e612da7ce92"], stdin: "again");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("No conversation found with session ID", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TextOutput_IsTheBareResult()
    {
        var run = Run(["-p"], stdin: "hello");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal("DONE", run.Stdout.Trim());
    }

    [Fact]
    public void PositionalPrompt_AfterTheTerminator_IsNotSwallowedByDisallowedTools()
    {
        var run = Run(["-p", "--disallowedTools", "Write", "Edit", "NotebookEdit", "--", "/reviewer orch/rev-1"], stdin: null);

        Assert.Equal(0, run.ExitCode);
        var record = Read_LastInvocation();
        Assert.Equal("/reviewer orch/rev-1", record["prompt"]!.GetValue<string>());
        Assert.Equal("argument", record["prompt_source"]!.GetValue<string>());
    }

    [Fact]
    public void StdinPrompt_IsUsedWhenThereIsNoPositional()
    {
        var run = Run(["-p", "--name", "imp-1"], stdin: "New traffic in your channel\n\nentry body");

        Assert.Equal(0, run.ExitCode);
        var record = Read_LastInvocation();
        Assert.Equal("stdin", record["prompt_source"]!.GetValue<string>());
        Assert.StartsWith("New traffic", record["prompt"]!.GetValue<string>());
    }

    [Fact]
    public void Scenario_DelayAndExitCode_AreHonoured()
    {
        Write_Scenario("""{"default":{"delay_ms":400,"exit_code":3,"result":"slow"}}""");

        var run = Run(["-p", "--output-format", "json"], stdin: "x");

        Assert.Equal(3, run.ExitCode);
        Assert.True(run.Elapsed >= TimeSpan.FromMilliseconds(400), $"elapsed {run.Elapsed}");
        Assert.Equal("slow", Parse_Object(run.Stdout)["result"]!.GetValue<string>());
    }

    [Fact]
    public void Scenario_ApiErrorStatus_MarksTheTurnAsAnError()
    {
        Write_Scenario("""{"default":{"is_error":true,"api_error_status":429,"stderr":"rate limited"}}""");

        var run = Run(["-p", "--output-format", "json"], stdin: "x");

        Assert.Equal(1, run.ExitCode);
        var root = Parse_Object(run.Stdout);
        Assert.True(root["is_error"]!.GetValue<bool>());
        Assert.Equal(429, root["api_error_status"]!.GetValue<int>());
        Assert.Equal("error_during_execution", root["subtype"]!.GetValue<string>());
        Assert.Contains("rate limited", run.Stderr);
    }

    [Fact]
    public void Scenario_OmitFields_RemovesThem_SoParsersCanBeProvedTolerant()
    {
        Write_Scenario("""{"default":{"omit_fields":["total_cost_usd","usage"],"stdout_prefix":"Warning: something on stdout\n"}}""");

        var run = Run(["-p", "--output-format", "json"], stdin: "x");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("Warning:", run.Stdout);
        var jsonLine = run.Stdout.Split('\n').Last(line => line.TrimStart().StartsWith('{'));
        var root = Parse_Object(jsonLine);
        Assert.False(root.ContainsKey("total_cost_usd"));
        Assert.False(root.ContainsKey("usage"));
        Assert.True(root.ContainsKey("result"));
    }

    [Fact]
    public void Scenario_TurnsPlayInOrder_ThenTheDefault()
    {
        Write_Scenario("""{"turns":[{"result":"first"},{"result":"second"}],"default":{"result":"rest"}}""");

        Assert.Equal("first", Run(["-p"], stdin: "1").Stdout.Trim());
        Assert.Equal("second", Run(["-p"], stdin: "2").Stdout.Trim());
        Assert.Equal("rest", Run(["-p"], stdin: "3").Stdout.Trim());
    }

    [Fact]
    public void Scenario_PerNameTurns_CountIndependently()
    {
        Write_Scenario("""{"sessions":{"imp-1":{"turns":[{"result":"one-a"},{"result":"one-b"}]},"imp-2":{"turns":[{"result":"two-a"}]}}}""");

        Assert.Equal("one-a", Run(["-p", "--name", "imp-1"], stdin: "x").Stdout.Trim());
        Assert.Equal("two-a", Run(["-p", "--name", "imp-2"], stdin: "x").Stdout.Trim());
        Assert.Equal("one-b", Run(["-p", "--name", "imp-1"], stdin: "x").Stdout.Trim());
        Assert.Equal("DONE", Run(["-p", "--name", "imp-2"], stdin: "x").Stdout.Trim());
    }

    [Fact]
    public void Hooks_AreLoggedInTheProbeFormat_WithResumeSourceOnResume()
    {
        Write_Scenario("""{"hooks_log":"hooks.log"}""");

        Run(["-p", "--session-id", SESSION_ID], stdin: "x");
        Run(["-p", "--resume", SESSION_ID], stdin: "y");

        var lines = File.ReadAllLines(Path.Combine(_workDir, "hooks.log"));

        Assert.Equal(12, lines.Length);
        foreach (var hook in new[] { "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Stop", "SessionEnd" })
            Assert.Equal(2, lines.Count(line => line.StartsWith($"=== {hook} ")));
        Assert.Contains(lines, line => line.StartsWith("=== SessionStart ") && line.EndsWith("source=startup"));
        Assert.Contains(lines, line => line.StartsWith("=== SessionStart ") && line.EndsWith("source=resume"));
    }

    /// <summary>
    /// A ONE-SHOT PRINT TURN CAN SPEAK NDJSON TOO — the format the bridge switched to at stage 19, so
    /// both transports put the same events on the wire and a superseded final report is visible on
    /// either. <c>--verbose</c> is demanded here because the real CLI demands it.
    /// </summary>
    [Fact]
    public void PrintStreamJson_EmitsInit_TheAssistantMessages_ThenTheResult()
    {
        Write_Scenario("""
        {"default":{"result":"the later one","assistant_messages":["the report","the later one"],"tool_use_between":true}}
        """);

        // The id of the refused run is spent all the same — a refused invocation is still one the
        // bridge made, which is why the fake logs it before refusing.
        Assert.NotEqual(0, Run(["-p", "--output-format", "stream-json", "--session-id", "99999999-8888-7777-6666-555555555555"], stdin: "x").ExitCode);

        var run = Run(["-p", "--output-format", "stream-json", "--verbose", "--session-id", SESSION_ID], stdin: "x");
        Assert.Equal(0, run.ExitCode);

        var events = run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonNode.Parse(line)!.AsObject()).ToList();

        Assert.Equal("system", events[0]["type"]!.GetValue<string>());
        Assert.Equal("init", events[0]["subtype"]!.GetValue<string>());
        Assert.Equal(["assistant", "assistant", "assistant", "result"], events.Skip(1).Select(json => json["type"]!.GetValue<string>()));

        // text, tool_use, text — the middle one is what "further events followed it" looks like.
        Assert.Equal("the report", Read_Text(events[1]));
        Assert.Equal("tool_use", (events[2]["message"] as JsonObject)!["content"]!.AsArray()[0]!["type"]!.GetValue<string>());
        Assert.Equal("the later one", Read_Text(events[3]));
        Assert.Equal("the later one", events[4]["result"]!.GetValue<string>());
    }

    static string Read_Text(JsonObject assistantEvent)
    {
        return string.Concat((assistantEvent["message"] as JsonObject)!["content"]!.AsArray()
            .Where(block => block!["type"]!.GetValue<string>() == "text")
            .Select(block => block!["text"]!.GetValue<string>()));
    }

    [Fact]
    public void InvocationLog_RecordsFlagsPromptCwdAndAiorchEnvironment()
    {
        Dictionary<string, string> environment = new() { ["AIORCH_ROLE"] = "implementer", ["AIORCH_RUNNER"] = "print" };

        Run(["-p", "--output-format", "json", "--name", "orch-1-imp-1", "--session-id", SESSION_ID, "--settings", "/tmp/s.json", "--permission-mode", "acceptEdits"], stdin: "prompt text", environment);

        var record = Read_LastInvocation();
        Assert.Equal(1, record["n"]!.GetValue<int>());
        Assert.Equal("orch-1-imp-1", record["name"]!.GetValue<string>());
        Assert.Equal(SESSION_ID, record["session_id"]!.GetValue<string>());
        Assert.Equal("/tmp/s.json", record["settings"]!.GetValue<string>());
        Assert.Equal("acceptEdits", record["permission_mode"]!.GetValue<string>());
        Assert.Equal("prompt text", record["prompt"]!.GetValue<string>());
        // Identity of the folder, not its spelling: macOS reports /var as /private/var.
        File.WriteAllText(Path.Combine(_workDir, "cwd-marker"), "");
        Assert.True(File.Exists(Path.Combine(record["cwd"]!.GetValue<string>(), "cwd-marker")), $"cwd '{record["cwd"]}' is not the working directory handed to the fake");
        Assert.Equal("implementer", record["env"]!["AIORCH_ROLE"]!.GetValue<string>());
        Assert.Equal("print", record["env"]!["AIORCH_RUNNER"]!.GetValue<string>());
        Assert.Contains("--output-format", record["args"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public void Refuses_TheCombinationsTheBridgeMustNeverSend()
    {
        Assert.NotEqual(0, Run(["--output-format", "json"], stdin: "x").ExitCode);
        Assert.NotEqual(0, Run(["-p", "--session-id", "not-a-uuid"], stdin: "x").ExitCode);
        Assert.NotEqual(0, Run(["-p", "--session-id", SESSION_ID, "--resume", SESSION_ID], stdin: "x").ExitCode);
        Assert.NotEqual(0, Run(["-p"], stdin: "").ExitCode);
        Assert.NotEqual(0, Run(["-p", "--no-such-flag"], stdin: "x").ExitCode);
    }

    /// <summary>
    /// A REUSED --session-id IS REFUSED, because the real CLI refuses it: measured on 2.1.261
    /// (2026-09-06) as `Error: Session ID &lt;uuid&gt; is already in use.`, exit 1, while `--resume` of
    /// that same session works. The fake accepted it, so a bridge that re-claimed an id on a retry
    /// passed this suite and stalled in production — the exact failure this project exists to catch.
    /// </summary>
    [Fact]
    public void AReusedSessionId_IsRefused_JustAsTheRealCliRefusesIt()
    {
        Assert.Equal(0, Run(["-p", "--session-id", SESSION_ID], stdin: "first").ExitCode);

        var second = Run(["-p", "--session-id", SESSION_ID], stdin: "again");

        Assert.Equal(1, second.ExitCode);
        Assert.Contains($"Session ID {SESSION_ID} is already in use", second.Stderr);

        // ...and resuming it is the way through, which is what the bridge does on a retry.
        Assert.Equal(0, Run(["-p", "--resume", SESSION_ID], stdin: "resumed").ExitCode);
    }

    [Fact]
    public void Version_IdentifiesItselfAsTheFake()
    {
        var run = Run(["--version"], stdin: null);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("FakeClaude", run.Stdout);
        Assert.StartsWith("2.1.261", run.Stdout);
    }

    ProcessRun Run(IReadOnlyList<string> arguments, string? stdin, IReadOnlyDictionary<string, string>? environment = null)
    {
        return ClaudeCli.Run(ClaudeCli.Fake(), arguments, _workDir, stdin, environment, TIMEOUT);
    }

    void Write_Scenario(string json)
    {
        File.WriteAllText(Path.Combine(_workDir, "fake-claude-scenario.json"), json);
    }

    JsonObject Read_LastInvocation()
    {
        var lines = File.ReadAllLines(Path.Combine(_workDir, "fake-claude-invocations.jsonl"));
        return Parse_Object(lines.Last(line => line.Length > 0));
    }

    static JsonObject Parse_Object(string json)
    {
        return JsonNode.Parse(json) as JsonObject ?? throw new Exception($"not a JSON object: {json}");
    }
}
