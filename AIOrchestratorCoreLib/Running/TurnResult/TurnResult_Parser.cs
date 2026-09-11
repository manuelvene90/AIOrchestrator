using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running.StreamTurn;

namespace AIOrchestratorCoreLib.Running.TurnResult;

/// <summary>
/// Reads the result document out of a print invocation's stdout. The document is the LAST line
/// that parses as a JSON object — the CLI may print warnings before it ("no stdin data received")
/// and a hook failure lands on stderr, not here. No document at all is still a result: the text
/// becomes the result text (text mode), and the exit code says whether it worked.
/// </summary>
public static class TurnResult_Parser
{
    /// <param name="finalLookingTexts">
    /// The assistant messages this turn could have ended on, oldest first — supplied by whichever
    /// transport watched the events go by. Null on every path that saw only a result document.
    /// </param>
    public static ITurnResult Parse(int exitCode, bool timedOut, string stdout, string stderr, TimeSpan elapsed, IReadOnlyList<string>? finalLookingTexts = null)
    {
        var document = Find_ResultDocument_OrNull(stdout);

        return Build(exitCode, timedOut, document, stdout, stderr, elapsed, finalLookingTexts);
    }

    /// <summary>
    /// THE NDJSON STREAM, read as one turn — the shape BOTH transports now speak
    /// (<c>--output-format stream-json --verbose</c>). The result is the LAST line whose
    /// <c>type</c> is <c>result</c>, never simply the last JSON object: the <c>Stop</c> hook's
    /// <c>hook_response</c> lands AFTER it (measured, see <see cref="StreamEvent_Reader"/>), so
    /// "the last object" reads a hook event as the turn's answer. Assistant events are tracked on
    /// the way past, which is the only moment a superseded final is still visible.
    ///
    /// <para>
    /// A stream with no <c>result</c> line at all falls back to the legacy reading — the last JSON
    /// object, else the raw text — because a turn that died before its result is still a turn to
    /// report, and this parser has never been allowed to throw one away.
    /// </para>
    /// </summary>
    public static ITurnResult Parse_Stream(int exitCode, bool timedOut, string stdout, string stderr, TimeSpan elapsed)
    {
        var (document, finalLooking) = Read_Stream(stdout);

        return Build(exitCode, timedOut, document ?? Find_ResultDocument_OrNull(stdout), stdout, stderr, elapsed, finalLooking);
    }

    /// <summary>
    /// The turn's own result document out of an NDJSON stream (or a single-document stdout), and the
    /// texts of every final-looking assistant message before it, oldest first.
    /// </summary>
    public static (JsonObject? Document, IReadOnlyList<string> FinalLookingTexts) Read_Stream(string stdout)
    {
        JsonObject? document = null;
        List<string> finalLooking = [];

        foreach (var raw in (stdout ?? string.Empty).Split('\n'))
        {
            var json = StreamEvent_Reader.Parse_OrNull(raw.Trim());

            // Not JSON, or JSON this version does not know: skipped, never fatal — the CLI prints
            // warnings into this stream and a hook can print anything at all.
            if (json == null)
                continue;

            if (StreamEvent_Reader.Is_Result(json))
            {
                document = json;
                continue;
            }

            if (SupersededFinals_Rule.Is_FinalLooking(json))
                finalLooking.Add(StreamEvent_Reader.Read_AssistantText(json));
        }

        return (document, finalLooking);
    }

    static ITurnResult Build(int exitCode, bool timedOut, JsonObject? document, string stdout, string stderr, TimeSpan elapsed, IReadOnlyList<string>? finalLookingTexts)
    {
        if (document == null)
        {
            var text = stdout.Trim();
            var plain = text.Length > 0 ? text : null;

            return TurnResult_Factory.Create(exitCode, timedOut, exitCode != 0 || timedOut, null, plain, null, null, null, null, null, null, stdout, stderr, elapsed,
                supersededFinals: SupersededFinals_Rule.Select_Superseded(finalLookingTexts ?? [], plain));
        }

        var resultText = Read_String(document, "result");

        return TurnResult_Factory.Create(
            exitCode,
            timedOut,
            Read_Bool(document, "is_error") ?? exitCode != 0,
            Read_String(document, "subtype"),
            resultText,
            Read_String(document, "session_id"),
            Read_Double(document, "total_cost_usd"),
            Read_Long(document, "duration_ms"),
            Read_Long(document, "duration_api_ms"),
            (int?)Read_Long(document, "num_turns"),
            (int?)Read_Long(document, "api_error_status"),
            stdout,
            stderr,
            elapsed,
            supersededFinals: SupersededFinals_Rule.Select_Superseded(finalLookingTexts ?? [], resultText));
    }

    /// <summary>
    /// The turn's result document, preferring a <c>result</c>-typed line of an NDJSON stream and
    /// falling back to the legacy reading (the last line that parses as a JSON object). Public
    /// because the turn log writes the same document, and two readings of "which line is the
    /// result" is how /tail and the channel come to disagree.
    /// </summary>
    public static JsonObject? Find_TurnDocument_OrNull(string stdout)
    {
        return Read_Stream(stdout).Document ?? Find_ResultDocument_OrNull(stdout);
    }

    static JsonObject? Find_ResultDocument_OrNull(string stdout)
    {
        var lines = stdout.Split('\n');

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();

            if (!line.StartsWith('{'))
                continue;

            try
            {
                if (JsonNode.Parse(line) is JsonObject document)
                    return document;
            }
            catch
            {
                // Not the document — a brace-led line of prose. Keep looking upward.
            }
        }

        return null;
    }

    static string? Read_String(JsonObject document, string key)
    {
        try
        {
            return document[key]?.GetValue<string?>();
        }
        catch
        {
            return null;
        }
    }

    static bool? Read_Bool(JsonObject document, string key)
    {
        try
        {
            return document[key]?.GetValue<bool?>();
        }
        catch
        {
            return null;
        }
    }

    static double? Read_Double(JsonObject document, string key)
    {
        try
        {
            return document[key]?.GetValue<double?>();
        }
        catch
        {
            return null;
        }
    }

    static long? Read_Long(JsonObject document, string key)
    {
        try
        {
            var node = document[key];

            if (node == null)
                return null;

            return (long)node.GetValue<double>();
        }
        catch
        {
            return null;
        }
    }
}
