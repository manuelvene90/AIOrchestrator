using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running.StreamTurn;

namespace AIOrchestratorCoreLib.Running.TurnResult;

/// <summary>
/// WHAT COUNTS AS A FINAL MESSAGE THAT WAS THEN SUPERSEDED — one rule, read by BOTH transports, so
/// a stream turn and a print turn can never disagree about which text is lost.
///
/// <para>
/// The defect it exists for, measured 3× on 2026-09-09/10: a session writes its final report, a
/// BACKGROUND sub-agent returns, the CLI re-opens the turn, a later message is produced, and THAT
/// becomes the <c>result</c> — the only text the bridge ever filed. A 19,771-character report and a
/// nine-agent review were lost this way, and both turns reported success.
/// </para>
/// <para>
/// FINAL-LOOKING = an assistant message the session could have ended on: it carries text, it asks
/// for no tool, it is the session's own (not a sub-agent's), and it did not stop for a tool call or
/// a token ceiling. The last two guards are the ones this rule adds over the naive reading:
/// <list type="bullet">
/// <item>A sub-agent's assistant messages travel in the SAME stream, marked by
/// <c>parent_tool_use_id</c> / <c>isSidechain</c>. They are text-only by nature, and filing them
/// would put every sub-agent's chatter in the channel — the opposite of the fix.</item>
/// <item><c>stop_reason</c> <c>tool_use</c> or <c>max_tokens</c> says outright that the message did
/// NOT end the turn. Absent or null is not evidence either way and stays final-looking.</item>
/// </list>
/// </para>
/// <para>
/// THE DEDUPE RULE, and it is deliberately one-directional: a final-looking text is NOT superseded
/// when the result text is identical to it or BEGINS with it (both compared trimmed, ordinal). That
/// covers the ordinary turn — the last assistant message and the result are the same string — and a
/// result that merely appended to it. A text the result does not begin with is content the result
/// does not contain, so it is filed. Nothing is deduped in the other direction: a result that is a
/// prefix of an earlier, longer message means the LONGER one was lost, which is exactly the case
/// this rule was written for.
/// </para>
/// </summary>
public static class SupersededFinals_Rule
{
    public const string PARENT_TOOL_USE_ID_KEY = "parent_tool_use_id";
    public const string SIDECHAIN_KEY = "isSidechain";
    public const string STOP_REASON_TOOL_USE = "tool_use";
    public const string STOP_REASON_MAX_TOKENS = "max_tokens";

    /// <summary>
    /// True when this event is an assistant message the session could have ended on. False for every
    /// other event type, for a pure tool call, for a sub-agent's message and for one that stopped
    /// because a tool was wanted.
    /// </summary>
    public static bool Is_FinalLooking(JsonObject json)
    {
        if (StreamEvent_Reader.Read_Type(json) != StreamEvent_Reader.TYPE_ASSISTANT)
            return false;

        if (Is_SubAgentMessage(json))
            return false;

        if (StreamEvent_Reader.Read_ToolNames(json).Count > 0)
            return false;

        if (Read_StopReason(json) is STOP_REASON_TOOL_USE or STOP_REASON_MAX_TOKENS)
            return false;

        return StreamEvent_Reader.Read_AssistantText(json).Trim().Length > 0;
    }

    /// <summary>
    /// The final-looking texts, in the order they arrived, that the result did NOT keep — see the
    /// dedupe rule in the class remark. Empty whenever the turn ended on the message it wrote.
    /// </summary>
    public static IReadOnlyList<string> Select_Superseded(IReadOnlyList<string> finalLookingInOrder, string? resultText)
    {
        if (finalLookingInOrder.Count == 0)
            return [];

        var result = (resultText ?? string.Empty).Trim();
        List<string> superseded = [];

        foreach (var text in finalLookingInOrder)
        {
            var candidate = (text ?? string.Empty).Trim();

            if (candidate.Length == 0)
                continue;

            // Identical, or the result merely carried on from it: already filed as the entry.
            if (result.Length > 0 && result.StartsWith(candidate, StringComparison.Ordinal))
                continue;

            superseded.Add(candidate);
        }

        return superseded;
    }

    /// <summary>
    /// A sub-agent's message rides the same stream as the session's own. Two markers, either of
    /// which is enough: the tool call it belongs to, and the sidechain flag.
    /// </summary>
    static bool Is_SubAgentMessage(JsonObject json)
    {
        if (json[PARENT_TOOL_USE_ID_KEY] is JsonValue parent && parent.TryGetValue<string>(out var parentId) && !string.IsNullOrWhiteSpace(parentId))
            return true;

        try
        {
            return json[SIDECHAIN_KEY]?.GetValue<bool>() == true;
        }
        catch
        {
            return false;
        }
    }

    static string? Read_StopReason(JsonObject json)
    {
        try
        {
            return (json["message"] as JsonObject)?["stop_reason"]?.GetValue<string>();
        }
        catch
        {
            return null;
        }
    }
}
