using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Status.SessionContextUsage;

namespace AIOrchestratorCoreLib.Usage;

/// <summary>
/// Reads the usage figures the status line probe drops beside every session (.usage.json /
/// .communicator.usage.json). ONE implementation for the app's cards, the detail window and the
/// bridge's /tokens command. Every read is tolerant: a missing or half-written file contributes
/// nothing rather than throwing.
/// </summary>
public static partial class UsageTotals_Reader
{
    public const string SESSION_USAGE_FILE = ".usage.json";
    public const string COMMUNICATOR_USAGE_FILE = ".communicator.usage.json";

    [GeneratedRegex(@"^(total_)?(cache_creation_|cache_read_)?(input|output)_tokens$", RegexOptions.Compiled)]
    private static partial Regex TokenField_Regex();

    /// <summary>Every usage probe file under the supervision home (general + all orchestrations).</summary>
    public static IReadOnlyList<string> Find_AllUsageFiles(ISupervisionPaths paths)
    {
        try
        {
            if (!Directory.Exists(paths.Root))
                return [];

            return [.. Directory.EnumerateFiles(paths.Root, "*usage.json", SearchOption.AllDirectories)];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// LIFETIME totals for one orchestration: supervisor + communicator + every member, closed
    /// ones included (the accumulator folds in sessions that respawned and reset their file).
    /// </summary>
    public static (double Cost, long Tokens) Build_OrchestrationTotals(ISupervisionPaths paths, IOrchestrationSession session)
    {
        var costTotal = 0.0;
        long tokenTotal = 0;

        foreach (var source in Build_PerSourceTotals(paths, session))
        {
            costTotal += source.Cost;
            tokenTotal += source.Tokens;
        }

        return (costTotal, tokenTotal);
    }

    /// <summary>
    /// The same LIFETIME figures, kept per SOURCE instead of summed — supervisor, communicator and
    /// every member, in roster order, skipping sources that never wrote a probe file. This is the
    /// breakdown /cost reports; <see cref="Build_OrchestrationTotals"/> is this list summed, so
    /// there is exactly ONE path through the respawn accumulator.
    /// </summary>
    public static IReadOnlyList<(string Label, double Cost, long Tokens)> Build_PerSourceTotals(
        ISupervisionPaths paths,
        IOrchestrationSession session)
    {
        var orchFolder = paths.Get_OrchestrationFolder(session.OrchId);
        var sources = Build_ProbeSources(paths, session);

        List<(string Label, double Cost, long Tokens)> totals = [];

        foreach (var source in sources)
        {
            if (!File.Exists(source.File))
                continue;

            var (cost, tokens) = UsageLifetime_Accumulator.Accumulate(
                orchFolder,
                Path.GetRelativePath(orchFolder, source.File),
                Read_Cost_OrNull(source.File) ?? 0.0,
                Read_Tokens_OrNull(source.File) ?? 0L);

            totals.Add((source.Label, cost, tokens));
        }

        return totals;
    }

    /// <summary>
    /// Every probe file this orchestration could have, labelled: supervisor, communicator, then each
    /// member in roster order. Existence is NOT checked here — a caller that wants only the sessions
    /// which have actually reported filters on its own read.
    ///
    /// ONE DEFINITION OF THE LIST, because the token totals, the cost breakdown and the /context
    /// report must never disagree about which sessions exist or where each one writes. It was two
    /// copies for about an hour on 2026-08-21 and the second one had already drifted: it composed
    /// the member path by hand instead of going through ISupervisionPaths.
    /// </summary>
    public static IReadOnlyList<(string Label, string File)> Build_ProbeSources(ISupervisionPaths paths, IOrchestrationSession session)
    {
        var orchFolder = paths.Get_OrchestrationFolder(session.OrchId);

        List<(string Label, string File)> sources =
        [
            ("supervisor", Path.Combine(orchFolder, SESSION_USAGE_FILE)),
            ("communicator", Path.Combine(orchFolder, COMMUNICATOR_USAGE_FILE)),
        ];

        foreach (var member in session.Members)
            sources.Add((member.MemberId, Path.Combine(paths.Get_ImplementerFolder(session.OrchId, member.MemberId), SESSION_USAGE_FILE)));

        return sources;
    }

    public static string Format_Tokens(long tokens)
    {
        if (tokens < 1_000)
            return $"{tokens} tok";
        if (tokens < 1_000_000)
            return $"{tokens / 1_000.0:F1}k tok";

        return $"{tokens / 1_000_000.0:F1}M tok";
    }

    /// <summary>
    /// Tolerant token extraction - the statusline schema varies by Claude Code version.
    ///
    /// IT COUNTED EVERY TOKEN TWICE UNTIL 2026-08-21, and the payload is why. Claude Code reports
    /// `context_window.total_input_tokens` + `total_output_tokens`, and then ITEMISES those same
    /// tokens under `current_usage` as input + output + cache_creation + cache_read. A recursive
    /// walk that added every field matching the token-name pattern therefore added the totals and
    /// their own breakdown: 685,316 reported against 342,658 real, exactly 2.00x, on every live
    /// probe file checked. /tokens, /cost's token figure and the budget alarm all read this.
    ///
    /// So the totals pair is now read DIRECTLY and the walk is only a fallback for payloads that do
    /// not carry one. The recursive walk stays because tolerance was the point of it - an older or
    /// newer Claude Code that keeps its token counts somewhere else is still read - but it can no
    /// longer see one number through two names.
    /// </summary>
    public static long? Read_Tokens_OrNull(string usageFilePath)
    {
        try
        {
            var root = JsonNode.Parse(Read_Text_Safe(usageFilePath));

            if (root == null)
                return null;

            var fromWindow = Read_ContextWindowTokens_OrNull(root);

            if (fromWindow != null)
                return fromWindow;

            long total = 0;
            Sum_TokenFields(root, ref total);

            return total > 0 ? total : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The authoritative pair, or the itemised breakdown when a payload carries only that - NEVER
    /// both, which is the whole point. Null when there is no context_window at all, which is what
    /// sends the caller to the tolerant walk.
    /// </summary>
    static long? Read_ContextWindowTokens_OrNull(JsonNode root)
    {
        var window = root["context_window"];

        if (window == null)
            return null;

        long total = 0;
        var sawTotal = false;

        foreach (var fieldName in new[] { "total_input_tokens", "total_output_tokens" })
        {
            if (window[fieldName] is JsonValue value && value.TryGetValue<long>(out var count))
            {
                total += count;
                sawTotal = true;
            }
        }

        // A window with no totals pair but an itemised current_usage: sum the itemisation instead.
        // It describes the same tokens, so it is an ALTERNATIVE to the pair and never an addition.
        if (!sawTotal)
        {
            var current = window["current_usage"];

            if (current == null)
                return null;

            Sum_TokenFields(current, ref total);
        }

        return total > 0 ? total : null;
    }

    public static double? Read_Cost_OrNull(string usageFilePath)
    {
        try
        {
            var costNode = JsonNode.Parse(Read_Text_Safe(usageFilePath))?["cost"]?["total_cost_usd"];

            if (costNode == null)
                return null;

            return costNode.GetValue<double>();
        }
        catch
        {
            return null;
        }
    }

    public static ISessionContextUsage? Read_ContextUsage_OrNull(string usageFilePath)
    {
        return SessionContextUsage_Factory.Create_OrNull(usageFilePath);
    }

    static void Sum_TokenFields(JsonNode node, ref long total)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var pair in jsonObject)
            {
                if (pair.Value == null)
                    continue;

                if (TokenField_Regex().IsMatch(pair.Key) && pair.Value is JsonValue value && value.TryGetValue<long>(out var count))
                    total += count;
                else
                    Sum_TokenFields(pair.Value, ref total);
            }
        }
    }

    /// <summary>Shared-read: these files are rewritten by live sessions on every status-line render.</summary>
    public static string Read_Text_Safe(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return string.Empty;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch
        {
            return string.Empty;
        }
    }
}
