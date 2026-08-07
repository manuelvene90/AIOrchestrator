using System.Text;
using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Usage;

namespace AIOrchestratorCoreLib.Status;

/// <summary>
/// Says CONCRETELY what a session is doing right now, by reading the last tool call out of its live
/// transcript — "editing BridgeEngineModel.cs", "running dotnet test", not "working".
///
/// This replaces the communicator session, which cost $74/day per orchestration and 196 turns to
/// produce 37 identical STATUS entries. The transcript is the same source the communicator read;
/// the difference is that extracting the last tool call costs nothing and cannot be wrong about
/// what it saw, whereas a session summarising its own reading burns a turn each time.
///
/// Everything here is best-effort: an unreadable or missing transcript yields null, and the caller
/// falls back to a plain "still working" line. Narration must never throw into the bridge loop.
/// </summary>
public static class SupervisorActivity_Describer
{
    /// <summary>Transcripts reach megabytes; only the tail can possibly hold the current call.</summary>
    const int TAIL_BYTES = 64 * 1024;

    public static string? Describe_OrNull(string usageFilePath)
    {
        try
        {
            if (!File.Exists(usageFilePath))
                return null;

            var transcriptPath = RateLimits_Reader.Read_TranscriptPath_OrNull(UsageTotals_Reader.Read_Text_Safe(usageFilePath));

            if (transcriptPath == null || !File.Exists(transcriptPath))
                return null;

            return Describe_FromTranscript_OrNull(Read_Tail(transcriptPath));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Split out so it can be tested without a real session on disk.</summary>
    public static string? Describe_FromTranscript_OrNull(string transcriptTail)
    {
        var lines = transcriptTail.Split('\n');

        // Last complete JSON line wins — the tail may start mid-line, and the newest call is last.
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var description = Describe_Line_OrNull(lines[i]);

            if (description != null)
                return description;
        }

        return null;
    }

    static string? Describe_Line_OrNull(string line)
    {
        if (!line.TrimStart().StartsWith('{'))
            return null;

        JsonObject? root;

        try
        {
            root = JsonNode.Parse(line) as JsonObject;
        }
        catch
        {
            return null;
        }

        if (root?["message"]?["content"] is not JsonArray blocks)
            return null;

        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            if (blocks[i] is not JsonObject block || block["type"]?.GetValue<string>() != "tool_use")
                continue;

            return Describe_ToolUse(block);
        }

        return null;
    }

    static string Describe_ToolUse(JsonObject block)
    {
        var toolName = block["name"]?.GetValue<string>() ?? "a tool";
        var input = block["input"] as JsonObject;

        return toolName switch
        {
            "Edit" or "Write" or "NotebookEdit" => $"editing {File_Name(input, "file_path")}",
            "Read" => $"reading {File_Name(input, "file_path")}",
            "Bash" or "PowerShell" => $"running {Short_Command(input)}",
            "Monitor" => "arming its watcher",
            "Grep" or "Glob" => $"searching for {Text_Field(input, "pattern") ?? "something"}",
            "Task" or "Agent" => "running a sub-agent",
            "TodoWrite" or "TaskUpdate" or "TaskCreate" => "updating its task list",
            "WebFetch" or "WebSearch" => "looking something up online",
            _ => $"using {toolName}",
        };
    }

    static string File_Name(JsonObject? input, string field)
    {
        var value = Text_Field(input, field);

        if (value == null)
            return "a file";

        return Path.GetFileName(value.Replace('\\', '/'));
    }

    /// <summary>The first few words of a command — enough to recognise, short enough for a phone.</summary>
    static string Short_Command(JsonObject? input)
    {
        var command = Text_Field(input, "command");

        if (command == null)
            return "a command";

        var firstLine = command.Split('\n')[0].Trim();
        var words = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = string.Join(' ', words.Take(4));

        return kept.Length > 60 ? kept[..60] : kept;
    }

    static string? Text_Field(JsonObject? input, string field)
    {
        var node = input?[field];

        if (node == null)
            return null;

        try
        {
            var value = node.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    static string Read_Tail(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var length = stream.Length;
        var take = (int)Math.Min(TAIL_BYTES, length);

        stream.Seek(length - take, SeekOrigin.Begin);

        var buffer = new byte[take];
        var read = stream.Read(buffer, 0, take);

        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}
