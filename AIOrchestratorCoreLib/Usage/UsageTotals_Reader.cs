using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSession;
using AIOrchestratorCoreLib.SupervisionPaths;

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

        List<(string Label, string File)> sources =
        [
            ("supervisor", Path.Combine(orchFolder, SESSION_USAGE_FILE)),
            ("communicator", Path.Combine(orchFolder, COMMUNICATOR_USAGE_FILE)),
        ];

        foreach (var member in session.Members)
            sources.Add((member.MemberId, Path.Combine(paths.Get_ImplementerFolder(session.OrchId, member.MemberId), SESSION_USAGE_FILE)));

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

    public static string Format_Tokens(long tokens)
    {
        if (tokens < 1_000)
            return $"{tokens} tok";
        if (tokens < 1_000_000)
            return $"{tokens / 1_000.0:F1}k tok";

        return $"{tokens / 1_000_000.0:F1}M tok";
    }

    /// <summary>Tolerant token extraction — the statusline schema varies by Claude Code version.</summary>
    public static long? Read_Tokens_OrNull(string usageFilePath)
    {
        try
        {
            var root = JsonNode.Parse(Read_Text_Safe(usageFilePath));

            if (root == null)
                return null;

            long total = 0;
            Sum_TokenFields(root, ref total);

            return total > 0 ? total : null;
        }
        catch
        {
            return null;
        }
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
