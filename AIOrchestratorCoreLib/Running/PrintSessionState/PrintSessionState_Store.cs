using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Running.ExecutedTurn;
using AIOrchestratorCoreLib.Running.TurnCursor;
using AIOrchestratorCoreLib.Storage;
using AIOrchestratorCoreLib.SupervisionPaths;

namespace AIOrchestratorCoreLib.Running.PrintSessionState;

/// <summary>
/// Reads and writes <c>print-session.json</c>, and knows where it lives for each role: beside a
/// member's channel, in the general supervisor's home, and (for the singleton roles) beside the
/// orchestration's session.json under a role-prefixed name. The file's presence is what tells the
/// watchdog "this slot has no pid file BY DESIGN", so the location rule lives here and nowhere else.
///
/// <para>
/// WHICH CHANNELS a session is woken by is NOT here — that is
/// <see cref="TurnSource.TurnSources_Resolver"/>, because for a supervisor the answer depends on the
/// roster and changes while the session runs. This file stores how far each of them has been delivered.
/// </para>
/// </summary>
public static class PrintSessionState_Store
{
    public const string STATE_FILE_NAME = "print-session.json";

    public static string Get_StateFile(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return role switch
        {
            SessionRoles.Implementer or SessionRoles.Reviewer or SessionRoles.Solo => Path.Combine(paths.Get_ImplementerFolder(orchId, memberId), STATE_FILE_NAME),
            SessionRoles.General => Path.Combine(paths.GeneralFolder, STATE_FILE_NAME),
            SessionRoles.Supervisor => Path.Combine(paths.Get_OrchestrationFolder(orchId), $".supervisor.{STATE_FILE_NAME}"),
            SessionRoles.Communicator => Path.Combine(paths.Get_OrchestrationFolder(orchId), $".communicator.{STATE_FILE_NAME}"),
            _ => throw new Exception($"Unhandled SessionRoles: {role}"),
        };
    }

    public static bool Exists(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        return File.Exists(Get_StateFile(paths, role, orchId, memberId));
    }

    /// <summary>
    /// Clears a registration — the role is no longer bridge-driven, so the session goes back to a
    /// terminal. Returns whether a file was actually removed. Best-effort: a file that cannot be
    /// deleted is reported by the caller, never thrown at a spawn that is otherwise fine.
    /// </summary>
    public static bool Delete_IfExists(ISupervisionPaths paths, SessionRoles role, string orchId, string memberId)
    {
        var stateFile = Get_StateFile(paths, role, orchId, memberId);

        try
        {
            if (!File.Exists(stateFile))
                return false;

            File.Delete(stateFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Null for an absent file. A corrupt one throws — that is a session whose identity is gone, not a default.</summary>
    public static IPrintSessionState? Read_OrNull(string stateFile)
    {
        if (!File.Exists(stateFile))
            return null;

        var root = JsonNode.Parse(File.ReadAllText(stateFile)) as JsonObject
            ?? throw new Exception($"Print session state at '{stateFile}' is not a JSON object");

        List<IExecutedTurn> executed = [];

        if (root["executed_turns"] is JsonArray turns)
        {
            foreach (var node in turns)
            {
                if (node is JsonObject turn)
                    executed.Add(Parse_Turn(turn, stateFile));
            }
        }

        return PrintSessionState_Factory.Create(
            Read_String(root, "session_id", stateFile),
            root["session_started"]?.GetValue<bool>() ?? executed.Count > 0,
            SessionRole_Names.Parse_OrNull(Read_String(root, "role", stateFile)) ?? throw new Exception($"Print session state at '{stateFile}' names an unknown role '{root["role"]}'"),
            Read_String(root, "orch_id", stateFile),
            Read_String(root, "member_id", stateFile),
            Read_String(root, "working_directory", stateFile),
            root["model"]?.GetValue<string?>(),
            Read_String(root, "channel_file", stateFile),
            Parse_Cursors(root, stateFile),
            root["next_turn_number"]?.GetValue<int>() ?? Math.Max(1, executed.Count + 1),
            root["failed_attempts"]?.GetValue<int>() ?? 0,
            executed,
            Parse_RetryNotBefore_OrNull(root));
    }

    public static void Write(string stateFile, IPrintSessionState state)
    {
        var turns = new JsonArray();

        foreach (var turn in state.ExecutedTurns)
        {
            turns.Add(new JsonObject
            {
                ["turn"] = turn.TurnNumber,
                ["request_id"] = turn.RequestId,
                ["first_entry_index"] = turn.FirstEntryIndex,
                ["last_entry_index"] = turn.LastEntryIndex,
                ["ended_utc"] = turn.EndedUtc.ToString("o"),
                ["outcome"] = turn.Outcome,
                ["cost_usd"] = turn.CostUsd,
                ["session_id"] = turn.SessionId,
            });
        }

        var sources = new JsonArray();

        foreach (var cursor in state.Cursors)
        {
            var delivered = new JsonArray();

            // Sorted so two writes of the same state produce the same bytes: a file that reshuffles a
            // hash set on every turn is one nobody can diff, and this one is read by hand when a session
            // is not being woken by something everyone can see in the channel.
            foreach (var identity in cursor.Delivered.OrderBy(identity => identity, StringComparer.Ordinal))
                delivered.Add(identity);

            sources.Add(new JsonObject
            {
                ["key"] = cursor.SourceKey,
                ["channel_file"] = cursor.ChannelFilePath,
                ["high_water_index"] = cursor.HighWaterIndex,
                ["delivered"] = delivered,
            });
        }

        var root = new JsonObject
        {
            ["session_id"] = state.SessionId,
            ["session_started"] = state.SessionStarted,
            ["role"] = SessionRole_Names.Get_ConfigKey(state.Role),
            ["orch_id"] = state.OrchId,
            ["member_id"] = state.MemberId,
            ["working_directory"] = state.WorkingDirectory,
            ["model"] = state.Model,
            ["channel_file"] = state.ChannelFilePath,
            ["sources"] = sources,
            ["next_turn_number"] = state.NextTurnNumber,
            ["failed_attempts"] = state.FailedAttempts,
            ["retry_not_before_utc"] = state.RetryNotBeforeUtc?.ToString("o"),
            ["executed_turns"] = turns,
        };

        Atomic_FileWriter.Write_AllText(stateFile, root.ToJsonString(JsonWriting.INDENTED));
    }

    /// <summary>
    /// The cursors, or none. NO CURSORS IS A MEANINGFUL STATE and not a defect: it is what a file
    /// written before this shape existed reads as, and what a session registered without a roster reads
    /// as. The dispatcher baselines every unseen source on sight and says how much history that absorbed,
    /// which is the safe direction — the alternative, treating an absent cursor as "nothing delivered",
    /// hands a whole channel's backlog to a session as if it had just been said to it.
    /// </summary>
    static IReadOnlyList<ITurnCursor> Parse_Cursors(JsonObject root, string stateFile)
    {
        List<ITurnCursor> cursors = [];

        if (root["sources"] is not JsonArray sources)
            return cursors;

        foreach (var node in sources)
        {
            if (node is not JsonObject source)
                continue;

            HashSet<string> delivered = [];

            if (source["delivered"] is JsonArray identities)
            {
                foreach (var identity in identities)
                {
                    if (identity?.GetValue<string>() is string text && text.Length > 0)
                        delivered.Add(text);
                }
            }

            cursors.Add(TurnCursor_Factory.Create(
                Read_String(source, "key", stateFile),
                Read_String(source, "channel_file", stateFile),
                source["high_water_index"]?.GetValue<int>() ?? 0,
                delivered));
        }

        return cursors;
    }

    /// <summary>
    /// The scheduled retry, or none. UNREADABLE READS AS NONE, like every other tolerant read here:
    /// a stamp this version cannot parse must not become a confident instant, and the cost of losing
    /// it is one more refusal that reschedules itself — the cost of misreading it is a session that
    /// waits until a date nobody chose.
    /// </summary>
    static DateTime? Parse_RetryNotBefore_OrNull(JsonObject root)
    {
        if (root["retry_not_before_utc"]?.GetValue<string>() is not string text || text.Length == 0)
            return null;

        return DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    static IExecutedTurn Parse_Turn(JsonObject turn, string stateFile)
    {
        return ExecutedTurn_Factory.Create(
            turn["turn"]?.GetValue<int>() ?? throw new Exception($"Print session state at '{stateFile}' has an executed turn without a number"),
            Read_String(turn, "request_id", stateFile),
            turn["first_entry_index"]?.GetValue<int>() ?? 0,
            turn["last_entry_index"]?.GetValue<int>() ?? 0,
            DateTime.TryParse(turn["ended_utc"]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var ended) ? ended.ToUniversalTime() : DateTime.MinValue,
            turn["outcome"]?.GetValue<string>() ?? "unknown",
            turn["cost_usd"]?.GetValue<double?>(),
            turn["session_id"]?.GetValue<string?>());
    }

    static string Read_String(JsonObject node, string key, string stateFile)
    {
        return node[key]?.GetValue<string>() ?? throw new Exception($"Print session state at '{stateFile}' is missing '{key}'");
    }
}
