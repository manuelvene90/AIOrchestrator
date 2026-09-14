using System.Text.Json;
using System.Text.Json.Nodes;

namespace FakeClaude;

/// <summary>
/// The non-result events of the stream, in the shapes captured from the real CLI on 2026-09-06
/// (2.1.263, `/tmp/aiorch-1b-probe/events.jsonl`): <c>system/init</c>, the
/// <c>system/hook_started</c> + <c>system/hook_response</c> pair carrying <c>hook_name</c> and
/// <c>hook_event</c>, <c>assistant</c> with a content block list, and the top-level
/// <c>rate_limit_event</c> whose <c>rate_limit_info.unifiedWindows</c> is the only place a headless
/// session ever states its limits.
///
/// Key sets are the measured ones INCLUDING fields the bridge never reads, for the same reason
/// <see cref="ResultJson_Builder"/> keeps them: a parser written against the fake must meet the
/// same surface as the real thing.
/// </summary>
public static class StreamEventJson_Builder
{
    public const string TYPE_SYSTEM = "system";
    public const string TYPE_ASSISTANT = "assistant";
    public const string TYPE_RESULT = "result";
    public const string TYPE_RATE_LIMIT_EVENT = "rate_limit_event";
    public const string SUBTYPE_INIT = "init";
    public const string SUBTYPE_HOOK_STARTED = "hook_started";
    public const string SUBTYPE_HOOK_RESPONSE = "hook_response";

    public static string Build_Init(string sessionId, string model, string workingDirectory, string? permissionMode)
    {
        return Serialise(new JsonObject
        {
            ["type"] = TYPE_SYSTEM,
            ["subtype"] = SUBTYPE_INIT,
            ["cwd"] = workingDirectory,
            ["session_id"] = sessionId,
            ["tools"] = new JsonArray("Bash", "Read", "Write", "Edit"),
            ["mcp_servers"] = new JsonArray(),
            ["model"] = model,
            ["permissionMode"] = permissionMode ?? "bypassPermissions",
            ["slash_commands"] = new JsonArray(),
            ["apiKeySource"] = "none",
            ["output_style"] = "default",
            ["uuid"] = Guid.NewGuid().ToString(),
        });
    }

    public static string Build_HookStarted(string sessionId, string hookName)
    {
        return Serialise(new JsonObject
        {
            ["type"] = TYPE_SYSTEM,
            ["subtype"] = SUBTYPE_HOOK_STARTED,
            ["hook_id"] = Guid.NewGuid().ToString(),
            ["hook_name"] = hookName,
            ["hook_event"] = Read_HookEvent(hookName),
            ["uuid"] = Guid.NewGuid().ToString(),
            ["session_id"] = sessionId,
        });
    }

    public static string Build_HookResponse(string sessionId, string hookName)
    {
        return Serialise(new JsonObject
        {
            ["type"] = TYPE_SYSTEM,
            ["subtype"] = SUBTYPE_HOOK_RESPONSE,
            ["hook_id"] = Guid.NewGuid().ToString(),
            ["hook_name"] = hookName,
            ["hook_event"] = Read_HookEvent(hookName),
            ["exit_code"] = 0,
            ["uuid"] = Guid.NewGuid().ToString(),
            ["session_id"] = sessionId,
        });
    }

    public static string Build_Assistant(string sessionId, string model, string text)
    {
        return Serialise(new JsonObject
        {
            ["type"] = TYPE_ASSISTANT,
            ["message"] = new JsonObject
            {
                ["model"] = model,
                ["id"] = $"msg_{Guid.NewGuid():N}",
                ["type"] = "message",
                ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
                ["stop_reason"] = "end_turn",
            },
            ["uuid"] = Guid.NewGuid().ToString(),
            ["session_id"] = sessionId,
        });
    }

    /// <summary>
    /// AN ASSISTANT MESSAGE THAT ASKS FOR A TOOL — text-free, <c>stop_reason</c> <c>tool_use</c>, in
    /// the shape the real CLI emits when the model calls one. Scripted between two text messages it
    /// is the "further events followed it" half of the superseded-final rule.
    /// </summary>
    public static string Build_AssistantToolUse(string sessionId, string model, string toolName)
    {
        return Serialise(new JsonObject
        {
            ["type"] = TYPE_ASSISTANT,
            ["message"] = new JsonObject
            {
                ["model"] = model,
                ["id"] = $"msg_{Guid.NewGuid():N}",
                ["type"] = "message",
                ["role"] = "assistant",
                ["content"] = new JsonArray(new JsonObject
                {
                    ["type"] = "tool_use",
                    ["id"] = $"toolu_{Guid.NewGuid():N}",
                    ["name"] = toolName,
                    ["input"] = new JsonObject { ["description"] = "scripted" },
                }),
                ["stop_reason"] = "tool_use",
            },
            ["uuid"] = Guid.NewGuid().ToString(),
            ["session_id"] = sessionId,
        });
    }

    /// <summary>
    /// THE TURN'S ASSISTANT EVENTS, in order — ONE copy for both responders, so a print turn and a
    /// stream turn of the same scenario emit the same events. A turn that names no
    /// <c>assistant_messages</c> emits the measured default: one message carrying the result text.
    /// </summary>
    public static IReadOnlyList<string> Build_AssistantSequence(FakeClaudeTurn turn, string sessionId, string model)
    {
        var messages = turn.AssistantMessages.Count > 0 ? turn.AssistantMessages : [turn.Result];
        List<string> events = [];

        for (var i = 0; i < messages.Count; i++)
        {
            if (i > 0 && turn.ToolUseBetween)
                events.Add(Build_AssistantToolUse(sessionId, model, "Task"));

            events.Add(Build_Assistant(sessionId, model, messages[i]));
        }

        return events;
    }

    /// <summary>
    /// The limits event. <paramref name="rateLimit"/> is the scenario's own
    /// <c>rate_limit_info</c> — injected verbatim, so a test can pin a utilisation, a reset instant
    /// or a shape the parser has never seen without this builder having an opinion about it.
    /// </summary>
    public static string Build_RateLimitEvent(string sessionId, JsonObject rateLimit)
    {
        return Serialise(new JsonObject
        {
            ["type"] = TYPE_RATE_LIMIT_EVENT,
            ["rate_limit_info"] = rateLimit.DeepClone(),
            ["uuid"] = Guid.NewGuid().ToString(),
            ["session_id"] = sessionId,
        });
    }

    /// <summary>The measured default: five_hour and seven_day utilisations as FRACTIONS, with unix reset instants.</summary>
    public static JsonObject Build_RateLimitInfo(double fiveHourUtilization, double sevenDayUtilization, long fiveHourResetsAt, long sevenDayResetsAt)
    {
        return new JsonObject
        {
            ["status"] = "allowed",
            ["resetsAt"] = fiveHourResetsAt,
            ["rateLimitType"] = "five_hour",
            ["overageStatus"] = "rejected",
            ["overageDisabledReason"] = "org_level_disabled",
            ["isUsingOverage"] = false,
            ["unifiedWindows"] = new JsonObject
            {
                ["five_hour"] = new JsonObject { ["utilization"] = fiveHourUtilization, ["resetsAt"] = fiveHourResetsAt },
                ["seven_day"] = new JsonObject { ["utilization"] = sevenDayUtilization, ["resetsAt"] = sevenDayResetsAt },
            },
        };
    }

    /// <summary>
    /// ONE OF THE BRIDGE'S MESSAGES WRITTEN BACK, as <c>--replay-user-messages</c> makes the real CLI
    /// do. Measured 2026-09-11 on 2.1.268: it follows <c>init</c> (and the rate-limit event when
    /// there is one) and precedes the turn's first assistant event.
    /// </summary>
    public static string Build_UserReplay(string sessionId, string text)
    {
        return Serialise(new JsonObject
        {
            ["type"] = "user",
            ["message"] = new JsonObject { ["role"] = "user", ["content"] = text },
            ["parent_tool_use_id"] = null,
            ["session_id"] = sessionId,
            ["uuid"] = Guid.NewGuid().ToString(),
            ["isReplay"] = true,
        });
    }

    /// <summary>
    /// A BACKGROUND TASK FINISHING — what makes the real CLI start a turn nobody asked for. Measured
    /// 2026-09-11 on 2.1.268: this event, then <c>init</c>, then the turn, with no echo in it.
    /// </summary>
    public static string Build_TaskNotification(string sessionId, string taskId)
    {
        return Serialise(new JsonObject
        {
            ["type"] = TYPE_SYSTEM,
            ["subtype"] = "task_notification",
            ["task_id"] = taskId,
            ["status"] = "completed",
            ["uuid"] = Guid.NewGuid().ToString(),
            ["session_id"] = sessionId,
        });
    }

    /// <summary>`PreToolUse:Bash` names event `PreToolUse`; a bare name is its own event.</summary>
    static string Read_HookEvent(string hookName)
    {
        var colon = hookName.IndexOf(':');
        return colon < 0 ? hookName : hookName[..colon];
    }

    static string Serialise(JsonObject node)
    {
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
