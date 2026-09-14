using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIOrchestratorCoreLib.Running.TurnLiveness;

/// <summary>
/// Whether a print turn is STUCK IN A LOOP — repeating the same tool call and getting the same answer —
/// read from the tail of the turn's MAIN transcript.
///
/// <para>
/// WHY A SECOND SIGNAL. The silence brake kills a turn that shows no sign of life, and a looping agent
/// is the one case it can never catch: every repeated call appends to the transcript, so the turn reads
/// as alive for ever and only the two-hour ceiling stops it. The research the owner asked for
/// (2026-09-11) names the other half of best practice as detection by REPETITION, not by time.
/// </para>
/// <para>
/// THE RULES ARE OPENHANDS' STUCKDETECTOR (https://github.com/All-Hands-AI/OpenHands,
/// <c>openhands/controller/stuck.py</c>), taken from the project's description and NOT measured here —
/// no looping turn of ours has been replayed against these thresholds yet. Three of its patterns are
/// kept: the same action with the same observation <see cref="REPEAT_STEPS"/> times; the same action
/// failing <see cref="ERROR_REPEAT_STEPS"/> times; two actions alternating with unchanged observations
/// over <see cref="PING_PONG_STEPS"/> steps. Its monologue and context-window patterns are not: a
/// print turn has no user messages to interleave, and the CLI compacts its own context.
/// </para>
/// <para>
/// THE OBSERVATION IS WHAT MAKES IT SAFE. Polling a build with the same command is legitimate work as
/// long as the answer changes; only an unchanged answer is a loop. So a step is a tool call PAIRED with
/// its result, and a call still waiting for its result is not a step — a long command in flight is work
/// the brake must leave alone.
/// </para>
/// <para>
/// ONLY THIS TURN COUNTS. A resumed session's transcript carries every earlier turn; steps whose call
/// line is stamped before <c>sinceUtc</c> are dropped, the same floor the silence brake keeps (its first
/// version measured from the previous turn and killed 17 idle supervisors on 2026-09-09).
/// </para>
/// <para>
/// ONLY THE MAIN AGENT. Sub-agents write their own files (<c>&lt;session&gt;/subagents/*.jsonl</c>), and
/// lines carrying a truthy <c>isSidechain</c> are skipped — a sub-agent's calls interleaved with the
/// parent's would break a real loop apart or stitch two unrelated agents into a false one.
/// </para>
/// <para>
/// NOTHING PRIVATE LEAVES. Tool inputs and results are hashed or compared, never quoted: the reason
/// names the tool and the count only, because it lands in logs and may reach the owner's phone.
/// </para>
/// </summary>
public static class TranscriptLoop_Detector
{
    /// <summary>Identical call, identical result, this many times in a row (OpenHands: 4).</summary>
    public const int REPEAT_STEPS = 4;

    /// <summary>Identical call, failing each time, this many times in a row (OpenHands: 3).</summary>
    public const int ERROR_REPEAT_STEPS = 3;

    /// <summary>A,B,A,B,A,B with unchanged results — three round trips (OpenHands: 6).</summary>
    public const int PING_PONG_STEPS = 6;

    /// <summary>
    /// How much of the transcript's END is read. The rules need at most the last
    /// <see cref="PING_PONG_STEPS"/> steps; 512 KB holds many times that even with large tool results,
    /// and bounds the cost of a poll on a session whose transcript has grown to tens of megabytes.
    /// </summary>
    public const int TAIL_BYTES = 512 * 1024;

    /// <summary>Hex characters of SHA-256 kept per observation — 64 bits, ample for comparing a handful.</summary>
    const int OBSERVATION_HASH_LENGTH = 16;

    /// <summary>A tool name comes from the file; it is shown, so it is bounded.</summary>
    const int MAX_TOOL_NAME_LENGTH = 64;

    /// <summary>
    /// A one-line reason when the turn's main agent is looping, else null. Null also when the file is
    /// missing, unreadable or unparseable: null means "this signal says nothing", and the brake reads it
    /// as no loop — a detector that throws, or guesses, into the turn loop is worse than one that is quiet.
    /// </summary>
    public static string? Describe_Loop_OrNull(string transcriptPath, DateTime sinceUtc)
    {
        if (string.IsNullOrWhiteSpace(transcriptPath))
            return null;

        string? tail;

        try
        {
            tail = Read_Tail_OrNull(transcriptPath);
        }
        catch
        {
            // Missing (a turn that has not written yet), locked, or vanished mid-read. The CLI owns this
            // file; nothing here may fail because of its state.
            return null;
        }

        if (tail == null)
            return null;

        return Describe_Loop_OrNull(Read_Steps(tail, To_Utc(sinceUtc)));
    }

    /// <summary>
    /// The rules alone, on steps already parsed — oldest first. A step's <c>Signature</c> is the call
    /// (tool name + canonical input), its <c>Observation</c> the result; <c>ToolName</c> is only ever
    /// used to word the reason.
    /// </summary>
    public static string? Describe_Loop_OrNull(IReadOnlyList<(string Signature, string Observation, bool IsError, string ToolName)> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        // Checked first because it needs the fewest steps: polled every few seconds, a failing call
        // repeated is caught here before the plain repeat rule could see it.
        if (Is_ErrorRepeat(steps))
            return $"the same {Describe_Tool(steps[^1].ToolName)} call failed {ERROR_REPEAT_STEPS} times in a row";

        if (Is_Repeat(steps))
            return $"the same {Describe_Tool(steps[^1].ToolName)} call returned the same result {REPEAT_STEPS} times in a row";

        if (Is_PingPong(steps))
        {
            var first = Describe_Tool(steps[^PING_PONG_STEPS].ToolName);
            var second = Describe_Tool(steps[^(PING_PONG_STEPS - 1)].ToolName);
            var pair = first == second ? $"two {first}" : $"the same {first} and {second}";

            return $"alternated between {pair} calls, with the same results, {PING_PONG_STEPS / 2} times";
        }

        return null;
    }

    static bool Is_ErrorRepeat(IReadOnlyList<(string Signature, string Observation, bool IsError, string ToolName)> steps)
    {
        if (steps.Count < ERROR_REPEAT_STEPS)
            return false;

        var last = steps[^1];

        // The error TEXT may differ (a timestamp, a temp path) — the same call failing is the loop.
        for (var back = 1; back <= ERROR_REPEAT_STEPS; back++)
            if (!steps[^back].IsError || steps[^back].Signature != last.Signature)
                return false;

        return true;
    }

    static bool Is_Repeat(IReadOnlyList<(string Signature, string Observation, bool IsError, string ToolName)> steps)
    {
        if (steps.Count < REPEAT_STEPS)
            return false;

        for (var back = 2; back <= REPEAT_STEPS; back++)
            if (!Is_SameStep(steps[^back], steps[^1]))
                return false;

        return true;
    }

    static bool Is_PingPong(IReadOnlyList<(string Signature, string Observation, bool IsError, string ToolName)> steps)
    {
        if (steps.Count < PING_PONG_STEPS)
            return false;

        var a = steps[^PING_PONG_STEPS];
        var b = steps[^(PING_PONG_STEPS - 1)];

        // A == B is a plain repeat, and it is the repeat rule's to judge at its own threshold.
        if (Is_SameStep(a, b))
            return false;

        for (var offset = 0; offset < PING_PONG_STEPS; offset++)
            if (!Is_SameStep(steps[steps.Count - PING_PONG_STEPS + offset], offset % 2 == 0 ? a : b))
                return false;

        return true;
    }

    static bool Is_SameStep(
        (string Signature, string Observation, bool IsError, string ToolName) left,
        (string Signature, string Observation, bool IsError, string ToolName) right)
    {
        return left.Signature == right.Signature
            && left.Observation == right.Observation
            && left.IsError == right.IsError;
    }

    /// <summary>
    /// The last <see cref="TAIL_BYTES"/> of the file as text, starting on a line boundary. Cut at the
    /// BYTE level: '\n' never occurs inside a multi-byte UTF-8 sequence, so dropping up to the first one
    /// cannot split a character. The last line may still be half-written by the CLI; it fails to parse
    /// and is skipped like any other bad line.
    /// </summary>
    static string? Read_Tail_OrNull(string path)
    {
        // ReadWrite | Delete: the CLI is appending to this file while we read, and must never be blocked.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        var length = stream.Length;
        var start = Math.Max(0, length - TAIL_BYTES);
        var startsMidLine = false;

        if (start > 0)
        {
            stream.Seek(start - 1, SeekOrigin.Begin);
            startsMidLine = stream.ReadByte() != '\n';
        }

        var buffer = new byte[length - start];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        var offset = 0;

        if (startsMidLine)
        {
            var firstNewline = Array.IndexOf(buffer, (byte)'\n', 0, read);

            if (firstNewline < 0)
                return null;

            offset = firstNewline + 1;
        }

        return Encoding.UTF8.GetString(buffer, offset, read - offset);
    }

    /// <summary>
    /// The main agent's completed steps, in call order. A call is kept only when its line is stamped at
    /// or after <paramref name="sinceUtc"/> and a result with its id has been written.
    /// </summary>
    static List<(string Signature, string Observation, bool IsError, string ToolName)> Read_Steps(string text, DateTime sinceUtc)
    {
        var calls = new List<(string Id, string Signature, string ToolName)>();
        var seenCallIds = new HashSet<string>(StringComparer.Ordinal);
        var results = new Dictionary<string, (string Observation, bool IsError)>(StringComparer.Ordinal);

        foreach (var line in text.Split('\n'))
        {
            try
            {
                Read_Line(line, sinceUtc, calls, seenCallIds, results);
            }
            catch
            {
                // One malformed line (half-written, a shape this CLI version did not use, a duplicate
                // key) says nothing about the others. Skipping it can only HIDE a loop, never invent one.
            }
        }

        var steps = new List<(string Signature, string Observation, bool IsError, string ToolName)>();

        foreach (var call in calls)
            if (results.TryGetValue(call.Id, out var result))
                steps.Add((call.Signature, result.Observation, result.IsError, call.ToolName));

        return steps;
    }

    static void Read_Line(
        string line,
        DateTime sinceUtc,
        List<(string Id, string Signature, string ToolName)> calls,
        HashSet<string> seenCallIds,
        Dictionary<string, (string Observation, bool IsError)> results)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (JsonNode.Parse(line) is not JsonObject entry)
            return;

        if (Is_Truthy(entry["isSidechain"]))
            return;

        if (entry["message"] is not JsonObject message || message["content"] is not JsonArray content)
            return;

        var type = Read_String_OrNull(entry["type"]);

        if (type == "assistant")
        {
            // No stamp, no proof the call belongs to this turn — dropped rather than counted.
            if (!Try_Read_TimestampUtc(entry["timestamp"], out var stampedUtc) || stampedUtc < sinceUtc)
                return;

            foreach (var block in content)
            {
                if (block is not JsonObject toolUse || Read_String_OrNull(toolUse["type"]) != "tool_use")
                    continue;

                var id = Read_String_OrNull(toolUse["id"]);
                var name = Read_String_OrNull(toolUse["name"]);

                // The CLI can write one message's blocks more than once; a call counts once.
                if (id == null || name == null || !seenCallIds.Add(id))
                    continue;

                calls.Add((id, name + "\n" + Serialize_Canonical(toolUse["input"]), name));
            }

            return;
        }

        if (type != "user")
            return;

        foreach (var block in content)
        {
            if (block is not JsonObject toolResult || Read_String_OrNull(toolResult["type"]) != "tool_result")
                continue;

            var id = Read_String_OrNull(toolResult["tool_use_id"]);

            if (id == null)
                continue;

            // is_error is absent on most successes (measured 2026-09-11: 143 absent, 378 false, 8 true
            // across five transcripts); absent reads as success.
            var isError = toolResult["is_error"] is JsonValue flag && flag.TryGetValue<bool>(out var raised) && raised;
            var observation = Hash_Observation(Serialize_Canonical(toolResult["content"])) + (isError ? "/error" : "/ok");

            results[id] = (observation, isError);
        }
    }

    /// <summary>
    /// The node as JSON with object keys sorted (ordinal) and no whitespace, so the same call written
    /// with its keys in another order — or with a character escaped differently — is the same call.
    /// </summary>
    static string Serialize_Canonical(JsonNode? node)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
            Write_Canonical(writer, node);

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    static void Write_Canonical(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;

            case JsonObject obj:
                writer.WriteStartObject();

                foreach (var property in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    Write_Canonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonArray array:
                writer.WriteStartArray();

                foreach (var item in array)
                    Write_Canonical(writer, item);

                writer.WriteEndArray();
                break;

            default:
                node.WriteTo(writer);
                break;
        }
    }

    static string Hash_Observation(string canonical)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..OBSERVATION_HASH_LENGTH];
    }

    static bool Try_Read_TimestampUtc(JsonNode? node, out DateTime stampedUtc)
    {
        stampedUtc = default;
        var text = Read_String_OrNull(node);

        return text != null
            && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out stampedUtc);
    }

    /// <summary>
    /// JSON truthiness, not just <c>true</c>: the field is the CLI's and its type is not ours to assume.
    /// Erring toward "sidechain" only drops lines, which can hide a loop but never invent one.
    /// </summary>
    static bool Is_Truthy(JsonNode? node)
    {
        if (node is not JsonValue value)
            return node != null;

        if (value.TryGetValue<bool>(out var flag))
            return flag;

        if (value.TryGetValue<string>(out var text))
            return text.Length > 0 && !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);

        if (value.TryGetValue<double>(out var number))
            return number != 0;

        return true;
    }

    static string? Read_String_OrNull(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    static string Describe_Tool(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return "unnamed";

        var printable = new string(toolName.Where(character => !char.IsControl(character)).ToArray()).Trim();

        if (printable.Length == 0)
            return "unnamed";

        return printable.Length <= MAX_TOOL_NAME_LENGTH ? printable : printable[..MAX_TOOL_NAME_LENGTH];
    }

    static DateTime To_Utc(DateTime moment)
    {
        return moment.Kind == DateTimeKind.Local ? moment.ToUniversalTime() : DateTime.SpecifyKind(moment, DateTimeKind.Utc);
    }
}
