using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running.StreamTurn;

namespace AIOrchestratorCoreLib.Running.TurnLog;

/// <summary>
/// THE SCREENSHOT, IN WORDS. A bridge-driven session has no terminal window to photograph, so this
/// renders its turn log the way the owner used to read one: what it called, what it said, how the
/// turn ended and what it cost. Plain text — no model is involved, so /tail and /log cost nothing
/// and cannot be slow.
///
/// <para>
/// Two views because two questions. <see cref="Format_Tail"/> answers "what is it doing" — the last
/// handful of events, one line each. <see cref="Format_LastTurn"/> answers "what happened in that
/// turn" — everything from one request id, including the text it finally wrote. Both are capped
/// well under Telegram's 4096 so the chunker rarely has to split them at all.
/// </para>
/// </summary>
public static class TurnLog_Formatter
{
    public const int DEFAULT_TAIL_EVENTS = 24;
    public const int MAX_LINE_LENGTH = 220;
    public const int MAX_TOTAL_LENGTH = 3800;

    public static string Format_Tail(string memberId, IReadOnlyList<JsonObject> records)
    {
        if (records.Count == 0)
            return $"{memberId}: no turns recorded yet — it has not run since the bridge started keeping this log";

        List<string> lines = [$"{memberId} — last {records.Count} event(s)"];

        foreach (var record in records)
        {
            if (Describe_Event_OrNull(record) is string line)
                lines.Add(line);
        }

        return Cap(string.Join('\n', lines));
    }

    public static string Format_LastTurn(string memberId, IReadOnlyList<JsonObject> records)
    {
        if (records.Count == 0)
            return $"{memberId}: no turns recorded yet";

        var requestId = TurnLog_Store.Read_RequestId_OrNull(records[^1]) ?? "unknown";

        List<string> lines = [$"{memberId} — turn {requestId}"];

        foreach (var record in records)
        {
            if (Describe_Event_OrNull(record, verbose: true) is string line)
                lines.Add(line);
        }

        return Cap(string.Join('\n', lines));
    }

    /// <summary>
    /// One event as one line, or null for the ones that would be noise (the per-turn <c>init</c>,
    /// the started half of a hook whose response is coming, a user echo). Null is a decision, not a
    /// failure: a tail padded with bookkeeping is a tail nobody reads.
    /// </summary>
    public static string? Describe_Event_OrNull(JsonObject record, bool verbose = false)
    {
        var at = TurnLog_Store.Read_At_OrNull(record);
        var stamp = at == null ? string.Empty : $"{at.Value.ToLocalTime():HH:mm:ss} ";
        var type = StreamEvent_Reader.Read_Type(record);
        var subtype = StreamEvent_Reader.Read_Subtype(record);

        if (type == StreamEvent_Reader.TYPE_RESULT)
            return stamp + Describe_Result(record);

        if (type == StreamEvent_Reader.TYPE_ASSISTANT)
        {
            var tools = StreamEvent_Reader.Read_ToolNames(record);
            var text = Single_Line(StreamEvent_Reader.Read_AssistantText(record));

            if (tools.Count > 0)
                return $"{stamp}→ {string.Join(", ", tools)}";

            return text.Length == 0 ? null : $"{stamp}“{Trim(text)}”";
        }

        if (type == StreamEvent_Reader.TYPE_RATE_LIMIT_EVENT)
        {
            var info = StreamEvent_Reader.Read_RateLimitInfo_OrNull(record);
            return info == null ? null : $"{stamp}limits: {Limits.RateLimitEvent_Translator.Describe(info)}";
        }

        if (subtype == StreamEvent_Reader.SUBTYPE_HOOK_RESPONSE)
            return verbose ? $"{stamp}hook {StreamEvent_Reader.Read_HookName(record)}" : null;

        if (type == "unparsed")
            return $"{stamp}? {Trim(Single_Line(Read_String_OrEmpty(record, "raw")))}";

        return null;
    }

    static string Describe_Result(JsonObject record)
    {
        var isError = Read_Bool_OrFalse(record, "is_error");
        // The turn's OWN cost where the writer knew it (the stream stamps it, print's record is
        // per turn by construction), never the process's running total — which is what
        // total_cost_usd is in a stream and what made this line disagree with the channel.
        var cost = Read_Double_OrNull(record, StreamSessionProcess_Words.TURN_COST_KEY) ?? Read_Double_OrNull(record, "total_cost_usd");
        var durationMs = Read_Double_OrNull(record, "duration_ms");
        var text = Single_Line(Read_String_OrEmpty(record, "result"));

        List<string> parts = [isError ? "turn ERROR" : "turn ok"];

        if (durationMs != null)
            parts.Add($"{durationMs.Value / 1000:0.0} s");

        if (cost != null)
            parts.Add($"{cost.Value:0.0000} USD");

        var head = string.Join(" · ", parts);

        return text.Length == 0 ? head : $"{head} — “{Trim(text)}”";
    }

    static string Cap(string text)
    {
        return text.Length <= MAX_TOTAL_LENGTH ? text : text[..MAX_TOTAL_LENGTH] + "\n…";
    }

    static string Trim(string text)
    {
        return text.Length <= MAX_LINE_LENGTH ? text : text[..MAX_LINE_LENGTH] + "…";
    }

    static string Single_Line(string text)
    {
        return text.Replace("\r", string.Empty).Replace('\n', ' ').Trim();
    }

    static string Read_String_OrEmpty(JsonObject record, string key)
    {
        try
        {
            return record[key]?.GetValue<string>() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    static bool Read_Bool_OrFalse(JsonObject record, string key)
    {
        try
        {
            return record[key]?.GetValue<bool>() ?? false;
        }
        catch
        {
            return false;
        }
    }

    static double? Read_Double_OrNull(JsonObject record, string key)
    {
        try
        {
            return record[key]?.GetValue<double>();
        }
        catch
        {
            return null;
        }
    }
}
