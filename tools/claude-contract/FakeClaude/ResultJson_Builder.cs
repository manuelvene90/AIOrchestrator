using System.Text.Json;
using System.Text.Json.Nodes;

namespace FakeClaude;

/// <summary>
/// The result document of <c>claude -p --output-format json</c>, in the shape MEASURED against
/// Claude Code 2.1.261 (MEASUREMENTS.md §M1): the key set below is that measurement's key set,
/// including the ones the bridge never reads, so a parser written against the fake meets the same
/// surface as the real thing. There is deliberately NO <c>rate_limits</c> key — the measurement
/// says the print JSON carries none, and a fake that offered one would let the bridge grow a
/// dependency the real CLI cannot satisfy.
/// </summary>
public static class ResultJson_Builder
{
    /// <summary>
    /// <paramref name="totalCostUsdOverride"/> is the STREAM mode's cumulative figure. In print
    /// mode each process reports its own turn, so the turn's cost is the whole truth; in stream mode
    /// one process reports a running total (measured 2026-09-06: 0.017715 → 0.024654 → 0.029215 for
    /// three equal turns), and a fake that reported the per-turn figure there would let a bridge
    /// that never differences the field pass.
    /// </summary>
    public static string Build(FakeClaudeTurn turn, string sessionId, string model, int numTurns, double? totalCostUsdOverride = null)
    {
        var root = new JsonObject
        {
            ["type"] = "result",
            ["subtype"] = turn.IsError ? "error_during_execution" : "success",
            ["is_error"] = turn.IsError,
            ["duration_ms"] = turn.DurationMilliseconds,
            ["duration_api_ms"] = Math.Max(0, turn.DurationMilliseconds - 312),
            ["num_turns"] = numTurns,
            ["result"] = turn.Result,
            ["session_id"] = sessionId,
            ["total_cost_usd"] = totalCostUsdOverride ?? turn.TotalCostUsd,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = 18,
                ["cache_creation_input_tokens"] = 1718,
                ["cache_read_input_tokens"] = 82848,
                ["output_tokens"] = 188,
                ["cache_creation"] = new JsonObject
                {
                    ["ephemeral_1h_input_tokens"] = 1718,
                    ["ephemeral_5m_input_tokens"] = 0,
                },
            },
            ["modelUsage"] = new JsonObject
            {
                [model] = new JsonObject
                {
                    ["inputTokens"] = 18,
                    ["outputTokens"] = 188,
                    ["cacheReadInputTokens"] = 82848,
                    ["cacheCreationInputTokens"] = 1718,
                    ["webSearchRequests"] = 0,
                    ["costUSD"] = turn.TotalCostUsd,
                    ["contextWindow"] = 200000,
                    ["maxOutputTokens"] = 32000,
                    ["thinkingTokens"] = 0,
                    ["canonicalModel"] = model,
                    ["provider"] = "firstParty",
                    ["costBasis"] = "list",
                },
            },
            ["permission_denials"] = new JsonArray(),
            ["uuid"] = Guid.NewGuid().ToString(),
            ["api_error_status"] = turn.ApiErrorStatus,
            ["stop_reason"] = turn.IsError ? null : "end_turn",
            ["terminal_reason"] = null,
            ["queued_turn_count"] = 0,
            ["fast_mode_state"] = "off",
            ["fast_mode_disabled_reason"] = null,
            ["first_content_frame_ms"] = 1200,
            ["time_to_request_ms"] = 400,
            ["ttft_ms"] = 900,
            ["ttft_stream_ms"] = 950,
            ["subagent_stats"] = new JsonObject(),
        };

        if (turn.ExtraJson != null)
        {
            foreach (var pair in turn.ExtraJson)
                root[pair.Key] = pair.Value?.DeepClone();
        }

        foreach (var field in turn.OmitFields)
            root.Remove(field);

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
