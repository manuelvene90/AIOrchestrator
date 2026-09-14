using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FakeClaude;

/// <summary>
/// The fake's PERSISTENT mode: <c>-p --input-format stream-json --output-format stream-json</c>.
/// One process, stdin carrying one JSON line per user message, stdout carrying the NDJSON event
/// stream, and one <c>result</c> event closing every turn — the shape MEASURED against Claude Code
/// 2.1.261/2.1.263 (supervisor-mode MEASUREMENTS.md §M9, re-measured 2026-09-06 on this machine).
///
/// <para>
/// THE COST IS CUMULATIVE PER PROCESS, and that is the fake's job to say. Measured on 2.1.263:
/// three turns in one process reported <c>total_cost_usd</c> 0.017715 → 0.024654 → 0.029215 while
/// <c>usage</c> stayed per-turn, and a NEW process resuming the same transcript started again from
/// 0.005228. A bridge reading the field as a per-turn cost therefore over-reports every turn after
/// the first; it must difference against the previous result OF THE SAME PROCESS. The fake models
/// exactly that, so a bridge that gets it wrong fails offline instead of in a channel.
/// </para>
/// <para>
/// STRICTER THAN THE REAL CLI WHERE THE REAL ONE WAS NOT MEASURED: a malformed stdin line, a
/// non-user message type and <c>--output-format</c> other than stream-json are all refused rather
/// than guessed at, so a bridge that passes this fake never leans on an unmeasured shape.
/// </para>
/// </summary>
public static class StreamJson_Responder
{
    public const string INPUT_FORMAT = "stream-json";
    public const string OUTPUT_FORMAT = "stream-json";

    /// <summary>Reported before the turn's own events; its response precedes `init`.</summary>
    public const string HOOK_BEFORE_TURN = "UserPromptSubmit";

    /// <summary>Started before the result and ANSWERED AFTER IT — see the write site.</summary>
    public const string HOOK_AFTER_TURN = "Stop";

    public static int Run(FakeClaudeArguments parsed, IReadOnlyList<string> rawArgs, string workingDirectory)
    {
        if (parsed.OutputFormat != OUTPUT_FORMAT)
        {
            Console.Error.WriteLine($"FakeClaude: --input-format {INPUT_FORMAT} was only ever measured with --output-format {OUTPUT_FORMAT} (got '{parsed.OutputFormat ?? "none"}') — refusing");
            return 1;
        }

        if (!parsed.Verbose)
        {
            // The real CLI refuses --output-format=stream-json without --verbose; the bridge always
            // sends it. Refused here so a bridge that stops sending it is caught offline.
            Console.Error.WriteLine("Error: --output-format=stream-json requires --verbose");
            return 1;
        }

        var scenario = FakeClaudeScenario.Load(workingDirectory);
        var logPath = Invocation_Logger.Resolve_LogPath(workingDirectory);

        if (parsed.SessionId != null && Invocation_Logger.Has_ClaimedSessionId(logPath, parsed.SessionId))
        {
            Console.Error.WriteLine($"Error: Session ID {parsed.SessionId} is already in use.");
            return 1;
        }

        Invocation_Logger.Append(logPath, rawArgs, parsed, prompt: string.Empty, workingDirectory, Invocation_Logger.KIND_STREAM_START);

        var sessionId = parsed.SessionId ?? parsed.Resume ?? Guid.NewGuid().ToString();
        var model = Model_Ids.Resolve(parsed.Model);
        var output = Console.Out;

        // Per PROCESS, never per transcript — see the class remark.
        var cumulativeCostUsd = 0.0;

        string? line;

        while ((line = Console.In.ReadLine()) != null)
        {
            if (line.Trim().Length == 0)
                continue;

            var text = Read_UserText_OrNull(line);

            if (text == null)
            {
                Console.Error.WriteLine($"FakeClaude: stdin line is not a {{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":…}}}} message — refusing: {Trim(line, 200)}");
                return 1;
            }

            var messageNumber = Invocation_Logger.Append(logPath, rawArgs, parsed, text, workingDirectory, Invocation_Logger.KIND_STREAM_MESSAGE).ForName;
            var turn = scenario.Resolve_Turn(parsed.Name, messageNumber);

            // A turn that names an exit code with no result is a STRUCTURAL failure: the process
            // dies mid-conversation, which is the fallback ladder's trigger.
            if (turn.ExitCode != null && turn.ExitCode.Value != 0)
            {
                if (turn.Stderr.Length > 0)
                    Console.Error.WriteLine(turn.Stderr);

                return turn.ExitCode.Value;
            }

            if (turn.UnpromptedResultBefore != null)
                cumulativeCostUsd = Write_UnpromptedTurn(output, parsed, turn, turn.UnpromptedResultBefore, sessionId, model, workingDirectory, cumulativeCostUsd);

            if (turn.DelayMilliseconds > 0)
                Thread.Sleep(turn.DelayMilliseconds);

            // MEASURED (2026-09-06, 2.1.263): the UserPromptSubmit hooks come first, then `init` —
            // once PER TURN, not once per process, which is the opposite of what the name suggests
            // and the first thing a reader frames turns on gets wrong.
            if (parsed.IncludeHookEvents)
                Write_HookPair(output, sessionId, HOOK_BEFORE_TURN);

            Write_Event(output, StreamEventJson_Builder.Build_Init(sessionId, model, workingDirectory, parsed.PermissionMode));

            // Garbage the parser must survive: a line that is not JSON at all, emitted mid-stream.
            if (turn.StdoutPrefix.Length > 0)
            {
                output.Write(turn.StdoutPrefix);
                output.Flush();
            }

            if (turn.RateLimit != null)
                Write_Event(output, StreamEventJson_Builder.Build_RateLimitEvent(sessionId, turn.RateLimit));

            if (parsed.ReplayUserMessages && !turn.NoReplay)
                Write_Event(output, StreamEventJson_Builder.Build_UserReplay(sessionId, text));

            foreach (var assistantEvent in StreamEventJson_Builder.Build_AssistantSequence(turn, sessionId, model))
                Write_Event(output, assistantEvent);

            // The Stop hook STARTS before the result and its response lands AFTER it — measured in
            // the same run, where turn 1's second `hook_response Stop` arrived two events into turn
            // 2. A reader that frames a turn as "everything up to the result" therefore files a late
            // event under the next turn, and one that waits for the Stop response never returns.
            if (parsed.IncludeHookEvents)
                Write_Event(output, StreamEventJson_Builder.Build_HookStarted(sessionId, HOOK_AFTER_TURN));

            cumulativeCostUsd += turn.TotalCostUsd;
            Write_Event(output, ResultJson_Builder.Build(turn, sessionId, model, numTurns: 1, cumulativeCostUsd));

            if (parsed.IncludeHookEvents)
                Write_Event(output, StreamEventJson_Builder.Build_HookResponse(sessionId, HOOK_AFTER_TURN));

            if (turn.UnpromptedResultAfter != null)
                cumulativeCostUsd = Write_UnpromptedTurn(output, parsed, turn, turn.UnpromptedResultAfter, sessionId, model, workingDirectory, cumulativeCostUsd);
        }

        // Stdin closed — measured: EXIT 0.
        return 0;
    }

    /// <summary>
    /// A TURN NOBODY ASKED FOR, in the measured shape: the task notification, <c>init</c>, the text,
    /// the result — and no echo, because no message of the bridge's started it. Returns the new
    /// running cost: the unprompted turn spends like any other.
    /// </summary>
    static double Write_UnpromptedTurn(TextWriter output, FakeClaudeArguments parsed, FakeClaudeTurn turn, string text, string sessionId, string model, string workingDirectory, double cumulativeCostUsd)
    {
        Write_Event(output, StreamEventJson_Builder.Build_TaskNotification(sessionId, $"bg{Guid.NewGuid():N}"[..10]));
        Write_Event(output, StreamEventJson_Builder.Build_Init(sessionId, model, workingDirectory, parsed.PermissionMode));
        Write_Event(output, StreamEventJson_Builder.Build_Assistant(sessionId, model, text));

        var unpromptedTurn = new FakeClaudeTurn { Result = text, TotalCostUsd = turn.UnpromptedCostUsd };
        cumulativeCostUsd += unpromptedTurn.TotalCostUsd;
        Write_Event(output, ResultJson_Builder.Build(unpromptedTurn, sessionId, model, numTurns: 1, cumulativeCostUsd));

        return cumulativeCostUsd;
    }

    /// <summary>
    /// The text of a user message, or null when the line is not one. Accepts both content shapes the
    /// SDK writes: a plain string and a list of <c>{"type":"text","text":…}</c> blocks.
    /// </summary>
    public static string? Read_UserText_OrNull(string line)
    {
        JsonObject? root;

        try
        {
            root = JsonNode.Parse(line) as JsonObject;
        }
        catch
        {
            return null;
        }

        if (root == null || root["type"]?.GetValue<string>() != "user")
            return null;

        var content = (root["message"] as JsonObject)?["content"];

        if (content is JsonValue value && value.TryGetValue<string>(out var plain))
            return plain;

        if (content is not JsonArray blocks)
            return null;

        var text = new StringBuilder();

        foreach (var block in blocks)
        {
            if (block is JsonObject blockObject && blockObject["type"]?.GetValue<string>() == "text")
                text.Append(blockObject["text"]?.GetValue<string>() ?? string.Empty);
        }

        return text.ToString();
    }

    static void Write_HookPair(TextWriter output, string sessionId, string hookName)
    {
        Write_Event(output, StreamEventJson_Builder.Build_HookStarted(sessionId, hookName));
        Write_Event(output, StreamEventJson_Builder.Build_HookResponse(sessionId, hookName));
    }

    static void Write_Event(TextWriter output, string json)
    {
        output.Write(json);
        output.Write('\n');
        output.Flush();
    }

    static string Trim(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "…";
    }
}

/// <summary>The model id the CLI reports for a short name — one copy for both modes.</summary>
public static class Model_Ids
{
    public static string Resolve(string? model)
    {
        return (model ?? "haiku").ToLowerInvariant() switch
        {
            "haiku" => "claude-haiku-4-5-20251001",
            "sonnet" => "claude-sonnet-4-5-20250929",
            "opus" => "claude-opus-4-1-20250805",
            var other => other,
        };
    }
}
