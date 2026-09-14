using System.Text;
using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Running.StreamTurn;

/// <summary>
/// Reads one line of the stream, TOLERANTLY — the same doctrine as
/// <see cref="Limits.LimitData_Parser"/> and for a stronger reason: this format is not documented
/// anywhere, so a field that moved is a fact to survive, not a turn to lose. Every reader here
/// answers null rather than throwing, and a line that is not JSON at all is simply not an event.
///
/// <para>
/// THE SHAPES ARE MEASURED, not assumed (2.1.263, 2026-09-06, and §M9 of the supervisor-mode study
/// before it). Two of them contradict what their names suggest and are pinned in the contract test
/// because of it: <c>system/init</c> arrives once per TURN, not once per process, and the
/// <c>Stop</c> hook's <c>hook_response</c> lands AFTER the turn's <c>result</c> — so a reader that
/// frames a turn as "everything up to the result" files that event under the next turn, and one
/// that waits for it never returns. The framing here is deliberately the first: the result closes
/// the turn, and a straggler belongs to whatever comes next.
/// </para>
/// </summary>
public static class StreamEvent_Reader
{
    public const string TYPE_RESULT = "result";
    public const string TYPE_ASSISTANT = "assistant";
    public const string TYPE_USER = "user";
    public const string TYPE_SYSTEM = "system";
    public const string TYPE_RATE_LIMIT_EVENT = "rate_limit_event";
    public const string SUBTYPE_INIT = "init";
    public const string SUBTYPE_HOOK_STARTED = "hook_started";
    public const string SUBTYPE_HOOK_RESPONSE = "hook_response";
    public const string RATE_LIMIT_INFO_KEY = "rate_limit_info";
    public const string REPLAY_KEY = "isReplay";

    public static JsonObject? Parse_OrNull(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            return JsonNode.Parse(line) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    public static string? Read_Type(JsonObject json)
    {
        return Read_String_OrNull(json, "type");
    }

    public static string? Read_Subtype(JsonObject json)
    {
        return Read_String_OrNull(json, "subtype");
    }

    public static bool Is_Result(JsonObject json)
    {
        return Read_Type(json) == TYPE_RESULT;
    }

    public static bool Is_RateLimitEvent(JsonObject json)
    {
        return Read_Type(json) == TYPE_RATE_LIMIT_EVENT;
    }

    /// <summary>
    /// One of OUR messages written back by the CLI (<see cref="StreamJson_Words.REPLAY_USER_MESSAGES_FLAG"/>).
    /// A tool result is a <c>user</c> event too, and carries no flag — which is why the flag, not the
    /// type, is the test.
    /// </summary>
    public static bool Is_UserReplay(JsonObject json)
    {
        return Read_Type(json) == TYPE_USER && Read_Bool(json, REPLAY_KEY);
    }

    /// <summary>The text a result event closed its turn on. Empty when it carried none.</summary>
    public static string Read_ResultText(JsonObject json)
    {
        return Is_Result(json) ? Read_String_OrNull(json, "result") ?? string.Empty : string.Empty;
    }

    /// <summary>A result the CLI itself flagged as an error — a login failure, an API error — rather than an answer.</summary>
    public static bool Is_ErrorResult(JsonObject json)
    {
        return Is_Result(json) && Read_Bool(json, "is_error");
    }

    public static string? Read_SessionId(JsonObject json)
    {
        return Read_String_OrNull(json, "session_id");
    }

    /// <summary>`Stop`, `PreToolUse:Bash`, … — null when the event is not a hook event.</summary>
    public static string? Read_HookName(JsonObject json)
    {
        return Read_Subtype(json) is SUBTYPE_HOOK_STARTED or SUBTYPE_HOOK_RESPONSE ? Read_String_OrNull(json, "hook_name") : null;
    }

    /// <summary>The <c>rate_limit_info</c> object, verbatim, for the limits path to read. Null when absent.</summary>
    public static JsonObject? Read_RateLimitInfo_OrNull(JsonObject json)
    {
        return Is_RateLimitEvent(json) ? json[RATE_LIMIT_INFO_KEY] as JsonObject : null;
    }

    /// <summary>Every text block of an assistant message, joined. Empty when the message carried none (a pure tool call).</summary>
    public static string Read_AssistantText(JsonObject json)
    {
        var text = new StringBuilder();

        foreach (var block in Read_ContentBlocks(json))
        {
            if (Read_String_OrNull(block, "type") == "text")
                text.Append(Read_String_OrNull(block, "text") ?? string.Empty);
        }

        return text.ToString();
    }

    /// <summary>The tools an assistant message asked for — what the owner sees in a /tail as "what it is doing".</summary>
    public static IReadOnlyList<string> Read_ToolNames(JsonObject json)
    {
        List<string> tools = [];

        foreach (var block in Read_ContentBlocks(json))
        {
            if (Read_String_OrNull(block, "type") == "tool_use" && Read_String_OrNull(block, "name") is string name)
                tools.Add(name);
        }

        return tools;
    }

    static IReadOnlyList<JsonObject> Read_ContentBlocks(JsonObject json)
    {
        List<JsonObject> blocks = [];

        if ((json["message"] as JsonObject)?["content"] is not JsonArray content)
            return blocks;

        foreach (var element in content)
        {
            if (element is JsonObject block)
                blocks.Add(block);
        }

        return blocks;
    }

    /// <summary>False for a missing or non-boolean value: an event we cannot read as flagged is read as not flagged.</summary>
    static bool Read_Bool(JsonObject json, string key)
    {
        try
        {
            return json[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
        }
        catch
        {
            return false;
        }
    }

    static string? Read_String_OrNull(JsonObject json, string key)
    {
        try
        {
            return json[key]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }
}
