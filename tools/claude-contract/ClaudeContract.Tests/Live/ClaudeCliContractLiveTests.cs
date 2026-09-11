using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace ClaudeContract.Tests.Live;

/// <summary>
/// MEASUREMENTS.md §M1-M7 repeated against the REAL `claude` on this machine, with the raw
/// evidence saved verbatim under <c>tools/claude-contract/last-run/&lt;stamp&gt;-live/</c>.
///
/// The six measures the bridge depends on, and where each is asserted:
///   1. hooks fire in <c>-p</c> ............................ <see cref="Print_ThenResume_HooksFire_JsonShape_SessionIdChosenByCaller"/>
///   2. hooks fire in <c>-p --resume</c> ................... same test
///   3. the print JSON carries usage/total_cost_usd/session_id and NO rate_limits ... same test
///   4. <c>--bg</c> runs a slash command as its first prompt  <see cref="Bg_RunsSlashCommand_AndHoldsAnExternalMessageWithoutAccept"/>
///   5. the messaging socket HOLDS an external message without crossSessionInbound: accept ... same test
///   6. ...and DELIVERS it with accept ....................... <see cref="Bg_WithCrossSessionInboundAccept_DeliversAnExternalMessage"/>
///   (+ §M7: an in-flight <c>-p</c> shows in <c>agents --json</c>  <see cref="Print_InFlight_IsListedByAgentsJson"/>)
///
/// Two things the bridge's PrintRunner relies on that the study did NOT measure are pinned in the
/// first test as well: the session id is CHOSEN BY THE CALLER (<c>--session-id</c>) and resumed
/// with <c>--resume</c>, and the resume turn takes its prompt from STDIN.
///
/// One class, so xunit runs these sequentially — they share one CLI and one account. Every
/// background session started here is stopped and removed in a finally block.
/// </summary>
public class ClaudeCliContractLiveTests(ITestOutputHelper output)
{
    static readonly TimeSpan PRINT_TIMEOUT = TimeSpan.FromSeconds(180);
    static readonly TimeSpan SHORT_TIMEOUT = TimeSpan.FromSeconds(60);
    const string MODEL = "haiku";

    readonly ITestOutputHelper _output = output;

    [LiveFact]
    public void Print_ThenResume_HooksFire_JsonShape_SessionIdChosenByCaller()
    {
        var probe = new ProbeWorkspace("live-print");
        var sessionId = Guid.NewGuid().ToString();

        try
        {
            // §M1 — with the bridge's own session id.
            var first = Claude(
                ["-p", "--model", MODEL, "--output-format", "json", "--dangerously-skip-permissions", "--name", "aiorch-contract-p", "--session-id", sessionId,
                 "Use the Bash tool to run exactly: echo PROBE_OK > out-p.txt   Then reply with the single word DONE."],
                probe.ProbeFolder, stdin: null, PRINT_TIMEOUT);

            probe.Save_Evidence("M1-print.txt", first.Describe());
            _output.WriteLine(first.Describe());

            Assert.True(first.ExitCode == 0, first.Describe());
            var firstJson = Parse_ResultJson(first.Stdout);

            Assert.Equal(sessionId, firstJson["session_id"]!.GetValue<string>());
            Assert.NotNull(firstJson["usage"]);
            Assert.NotNull(firstJson["total_cost_usd"]);
            Assert.DoesNotContain("rate_limits", first.Stdout);
            Assert.Equal("PROBE_OK", probe.Read_ProbeFile_OrEmpty("out-p.txt"));

            foreach (var hookEvent in ProbeWorkspace.HOOK_EVENTS)
                Assert.True(probe.Count_HookEvents(hookEvent) >= 1, $"hook {hookEvent} did not fire in -p; hooks.log:\n{Read_OrEmpty(probe.HooksLog)}");

            Assert.False(File.Exists(probe.StatuslineLog), "the status line was rendered in -p, which §M1 measured as never happening");

            var hooksAfterFirst = ProbeWorkspace.HOOK_EVENTS.ToDictionary(hookEvent => hookEvent, probe.Count_HookEvents);

            // §M2 — resumed by the caller's id, prompt on stdin (what the PrintRunner sends).
            var second = Claude(
                ["-p", "--model", MODEL, "--output-format", "json", "--dangerously-skip-permissions", "--resume", sessionId],
                probe.ProbeFolder,
                stdin: "Use the Bash tool to run exactly: echo PROBE_RESUME_OK > out-p2.txt   Then reply with the single word DONE.",
                PRINT_TIMEOUT);

            probe.Save_Evidence("M2-resume.txt", second.Describe());
            _output.WriteLine(second.Describe());

            Assert.True(second.ExitCode == 0, second.Describe());
            var secondJson = Parse_ResultJson(second.Stdout);

            Assert.Equal(sessionId, secondJson["session_id"]!.GetValue<string>());
            Assert.Equal("PROBE_RESUME_OK", probe.Read_ProbeFile_OrEmpty("out-p2.txt"));

            foreach (var hookEvent in ProbeWorkspace.HOOK_EVENTS)
                Assert.True(probe.Count_HookEvents(hookEvent) > hooksAfterFirst[hookEvent], $"hook {hookEvent} did not fire in -p --resume; hooks.log:\n{Read_OrEmpty(probe.HooksLog)}");

            Assert.Contains("\"source\":\"resume\"", Read_OrEmpty(probe.HookPayloadsLog).Replace(" ", ""));

            probe.Save_Evidence("M1-M2-hooks.log", Read_OrEmpty(probe.HooksLog));
        }
        finally
        {
            probe.Delete_TranscriptFolder_BestEffort();
        }
    }

    [LiveFact]
    public void Bg_RunsSlashCommand_AndHoldsAnExternalMessageWithoutAccept()
    {
        var probe = new ProbeWorkspace("live-bg-held");
        string? jobId = null;

        try
        {
            // §M3
            var started = Claude(["--bg", "--name", "aiorch-contract-bg", "/probe-hello bg-arg-1", "--model", MODEL, "--dangerously-skip-permissions"], probe.ProbeFolder, stdin: null, SHORT_TIMEOUT);
            probe.Save_Evidence("M3-bg-start.txt", started.Describe());
            _output.WriteLine(started.Describe());

            jobId = Parse_BackgroundJobId(started.Stdout);
            Assert.True(jobId != null, $"no 'backgrounded · <id>' line in:\n{started.Describe()}");

            Assert.True(Wait_Until(() => probe.Read_ProbeFile_OrEmpty("slash-out.txt") == "SLASH_ROLE_OK bg-arg-1", TimeSpan.FromSeconds(90)),
                $"--bg did not execute the slash command as its first prompt; slash-out.txt='{probe.Read_ProbeFile_OrEmpty("slash-out.txt")}'; logs:\n{Claude(["logs", jobId!], probe.ProbeFolder, null, SHORT_TIMEOUT).Stdout}");

            Assert.True(Wait_Until(() => probe.Count_HookEvents("Stop") >= 1, TimeSpan.FromSeconds(30)), $"no Stop hook in --bg; hooks.log:\n{Read_OrEmpty(probe.HooksLog)}");

            foreach (var hookEvent in new[] { "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "Stop" })
                Assert.True(probe.Count_HookEvents(hookEvent) >= 1, $"hook {hookEvent} did not fire in --bg; hooks.log:\n{Read_OrEmpty(probe.HooksLog)}");

            // §M6 Test A — an external script's message is HELD behind a dialog by default.
            var socketPath = Find_SocketPath_ForName("aiorch-contract-bg", probe.ProbeFolder);
            Assert.True(socketPath != null, "no ~/.claude/sessions/<pid>.json with name aiorch-contract-bg");

            Send_SocketMessage(socketPath!, "PROBE-A external script no-auth: use the Bash tool to run exactly: echo EXT_A_OK > ext-a.txt   then reply DONE");
            Thread.Sleep(TimeSpan.FromSeconds(20));

            var logs = Strip_Ansi(Claude(["logs", jobId!], probe.ProbeFolder, null, SHORT_TIMEOUT).Stdout);
            probe.Save_Evidence("M6A-held-logs.txt", logs);
            _output.WriteLine(logs);

            Assert.Equal(string.Empty, probe.Read_ProbeFile_OrEmpty("ext-a.txt"));
            Assert.Contains("Held message", logs);
        }
        finally
        {
            Stop_AndRemove(jobId, probe.ProbeFolder);
            probe.Delete_TranscriptFolder_BestEffort();
        }
    }

    [LiveFact]
    public void Bg_WithCrossSessionInboundAccept_DeliversAnExternalMessage()
    {
        var probe = new ProbeWorkspace("live-bg-accept");
        string? jobId = null;

        try
        {
            // §M6 Test C — inline JSON, exactly as measured.
            var started = Claude(["--bg", "--name", "aiorch-contract-bg2", "--settings", "{\"crossSessionInbound\":\"accept\"}", "/probe-hello bg2", "--model", MODEL, "--dangerously-skip-permissions"], probe.ProbeFolder, stdin: null, SHORT_TIMEOUT);
            probe.Save_Evidence("M6C-bg-start.txt", started.Describe());
            jobId = Parse_BackgroundJobId(started.Stdout);
            Assert.True(jobId != null, started.Describe());

            Assert.True(Wait_Until(() => probe.Read_ProbeFile_OrEmpty("slash-out.txt") == "SLASH_ROLE_OK bg2", TimeSpan.FromSeconds(90)), "the --bg session never ran its first prompt");
            Assert.True(Wait_Until(() => probe.Count_HookEvents("Stop") >= 1, TimeSpan.FromSeconds(30)), "the first turn never ended");

            var socketPath = Find_SocketPath_ForName("aiorch-contract-bg2", probe.ProbeFolder);
            Assert.True(socketPath != null, "no session registry entry named aiorch-contract-bg2");

            var sentAt = DateTime.UtcNow;
            Send_SocketMessage(socketPath!, "PROBE-C external script with accept: use the Bash tool to run exactly: echo EXT_C_OK > ext-c.txt   then reply DONE");

            Assert.True(Wait_Until(() => probe.Read_ProbeFile_OrEmpty("ext-c.txt") == "EXT_C_OK", TimeSpan.FromSeconds(60)),
                $"the message was not delivered with crossSessionInbound: accept; logs:\n{Strip_Ansi(Claude(["logs", jobId!], probe.ProbeFolder, null, SHORT_TIMEOUT).Stdout)}");

            var latency = DateTime.UtcNow - sentAt;
            probe.Save_Evidence("M6C-delivered.txt", $"sent {sentAt:o}\ndelivered (file written) within {latency.TotalSeconds:F1}s\nhooks.log:\n{Read_OrEmpty(probe.HooksLog)}");
            _output.WriteLine($"delivered within {latency.TotalSeconds:F1}s");
        }
        finally
        {
            Stop_AndRemove(jobId, probe.ProbeFolder);
            probe.Delete_TranscriptFolder_BestEffort();
        }
    }

    [LiveFact]
    public void Print_InFlight_IsListedByAgentsJson()
    {
        var probe = new ProbeWorkspace("live-agents");

        try
        {
            // §M7
            var slow = Task.Run(() => Claude(
                ["-p", "--name", "aiorch-contract-slow", "--model", MODEL, "--output-format", "json", "--dangerously-skip-permissions",
                 "Use the Bash tool to run exactly: sleep 20; echo P_SLOW_OK > p-slow.txt   Then reply with the single word DONE."],
                probe.ProbeFolder, stdin: null, PRINT_TIMEOUT));

            var listed = false;
            var agentsOutput = string.Empty;

            for (var attempt = 0; attempt < 12 && !listed; attempt++)
            {
                Thread.Sleep(TimeSpan.FromSeconds(3));
                agentsOutput = Claude(["agents", "--json"], probe.ProbeFolder, null, SHORT_TIMEOUT).Stdout;
                listed = agentsOutput.Contains("aiorch-contract-slow");
            }

            probe.Save_Evidence("M7-agents.json", agentsOutput);
            var result = slow.Result;
            probe.Save_Evidence("M7-print.txt", result.Describe());

            Assert.True(listed, $"the in-flight -p session never appeared in `claude agents --json`:\n{agentsOutput}");
            Assert.Equal("P_SLOW_OK", probe.Read_ProbeFile_OrEmpty("p-slow.txt"));
        }
        finally
        {
            probe.Delete_TranscriptFolder_BestEffort();
        }
    }

    // ----- helpers -----

    ProcessRun Claude(IReadOnlyList<string> arguments, string workingDirectory, string? stdin, TimeSpan timeout)
    {
        return ClaudeCli.Run(ClaudeCli.Real(), arguments, workingDirectory, stdin, null, timeout);
    }

    void Stop_AndRemove(string? jobId, string workingDirectory)
    {
        if (jobId == null)
            return;

        var stopped = Claude(["stop", jobId], workingDirectory, null, SHORT_TIMEOUT);
        var removed = Claude(["rm", jobId], workingDirectory, null, SHORT_TIMEOUT);
        _output.WriteLine($"cleanup: stop → {stopped.Stdout.Trim()} {stopped.Stderr.Trim()} | rm → {removed.Stdout.Trim()} {removed.Stderr.Trim()}");
    }

    /// <summary>
    /// HOW A POSITIONAL SLASH COMMAND AND STDIN MEET — measured 2026-09-09 on 2.1.263 (haiku): the CLI
    /// APPENDS the stdin text to the positional prompt in the same message, so the slash command's
    /// <c>$ARGUMENTS</c> is "positional-arg-1\nAdditional instruction…" — the whole stdin. Both halves
    /// reached the model (the probe wrote both files and replied DONE); neither was ignored or
    /// prepended. This is the fourth outcome the 4a preamble did not foresee, and the reason the fresh
    /// turn's memory travels in a FILE (`pack.md`, stage 4b) with NOTHING on stdin: a skill that
    /// interpolates <c>$ARGUMENTS</c> into a path (solo, communicator) would otherwise read the bridge's
    /// prose as part of the path. The assertion pins the measured shape; a CLI that starts separating
    /// the two would make it fail loudly, which is what a contract test is for.
    /// </summary>
    [LiveFact]
    public void Print_SlashCommandPositional_PlusStdin_IsAppendedIntoArguments_AndBothExecute()
    {
        var probe = new ProbeWorkspace("live-positional-plus-stdin");
        var sessionId = Guid.NewGuid().ToString();
        const string stdinInstruction = "Additional instruction arriving on stdin, part of the same message: after the slash command's own steps, also use the Bash tool to run exactly: echo STDIN_OK > stdin-out.txt   Then reply DONE.";

        try
        {
            var run = Claude(
                ["-p", "--model", MODEL, "--output-format", "json", "--dangerously-skip-permissions", "--name", "aiorch-contract-ps", "--session-id", sessionId,
                 "/probe-hello positional-arg-1"],
                probe.ProbeFolder,
                stdin: stdinInstruction,
                PRINT_TIMEOUT);

            probe.Save_Evidence("positional-plus-stdin.txt", run.Describe());
            _output.WriteLine(run.Describe());

            Assert.True(run.ExitCode == 0, run.Describe());

            var slashOut = probe.Read_ProbeFile_OrEmpty("slash-out.txt");
            Assert.StartsWith("SLASH_ROLE_OK positional-arg-1", slashOut);
            Assert.Contains("Additional instruction arriving on stdin", slashOut);
            Assert.Equal("STDIN_OK", probe.Read_ProbeFile_OrEmpty("stdin-out.txt"));
        }
        finally
        {
            probe.Delete_TranscriptFolder_BestEffort();
        }
    }

    static JsonObject Parse_ResultJson(string stdout)
    {
        var line = stdout.Split('\n').LastOrDefault(candidate => candidate.TrimStart().StartsWith('{'))
            ?? throw new Exception($"no JSON object line in stdout:\n{stdout}");

        return JsonNode.Parse(line) as JsonObject ?? throw new Exception($"not a JSON object: {line}");
    }

    static string? Parse_BackgroundJobId(string stdout)
    {
        var match = Regex.Match(Strip_Ansi(stdout), @"backgrounded\s*·\s*([0-9a-f]{8})");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>The socket of the session registered under this name — ~/.claude/sessions/&lt;pid&gt;.json, as §M3 read it.</summary>
    static string? Find_SocketPath_ForName(string name, string workingDirectory)
    {
        var sessionsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions");

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (Directory.Exists(sessionsFolder))
            {
                foreach (var file in Directory.GetFiles(sessionsFolder, "*.json"))
                {
                    try
                    {
                        var root = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;

                        if (root?["name"]?.GetValue<string>() == name && root["messagingSocketPath"]?.GetValue<string>() is { } socketPath)
                            return socketPath;
                    }
                    catch
                    {
                        // A registry file being written; try the next one or the next attempt.
                    }
                }
            }

            Thread.Sleep(1000);
        }

        return null;
    }

    /// <summary>The wire format of §M6: one JSON line, no auth line (optional on macOS/Linux — Windows named pipes need it and are UNVERIFIED).</summary>
    static void Send_SocketMessage(string socketPath, string content)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            throw new Exception("Sending on the messaging named pipe needs the auth line on Windows — not implemented here (UNVERIFIED in the study as well)");

        var message = new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = content },
        };

        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        socket.Send(Encoding.UTF8.GetBytes(message.ToJsonString() + "\n"));
        socket.Shutdown(SocketShutdown.Send);
    }

    static bool Wait_Until(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            Thread.Sleep(500);
        }

        return condition();
    }

    static string Read_OrEmpty(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    static string Strip_Ansi(string text)
    {
        return Regex.Replace(text, @"\x1B\[[0-9;?]*[ -/]*[@-~]", string.Empty);
    }
}
