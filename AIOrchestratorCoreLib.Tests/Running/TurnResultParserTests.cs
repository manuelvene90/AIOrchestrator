using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnResult;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>Tolerant like the limit parser: a missing field is null, never an exception; no JSON is still a result.</summary>
public class TurnResultParserTests
{
    const string MEASURED_JSON =
        """{"type":"result","subtype":"success","is_error":false,"duration_ms":3547,"duration_api_ms":3235,"num_turns":2,"result":"DONE","session_id":"2664d952-b685-4d1b-bedc-23d4dd973942","total_cost_usd":0.0126788,"usage":{"input_tokens":18},"api_error_status":null}""";

    [Fact]
    public void TheMeasuredShape_IsReadInFull()
    {
        var result = TurnResult_Parser.Parse(0, false, MEASURED_JSON + "\n", "", TimeSpan.FromSeconds(7.7));

        Assert.True(TurnOutcomes.Is_Success(result));
        Assert.Equal("DONE", result.ResultText);
        Assert.Equal("2664d952-b685-4d1b-bedc-23d4dd973942", result.SessionId);
        Assert.Equal(0.0126788, result.TotalCostUsd);
        Assert.Equal(3547, result.DurationMs);
        Assert.Equal(3235, result.DurationApiMs);
        Assert.Equal(2, result.NumTurns);
        Assert.Null(result.ApiErrorStatus);
        Assert.Equal("success", TurnOutcomes.Describe(result));
    }

    [Fact]
    public void WarningsBeforeTheDocument_AreSkipped()
    {
        var result = TurnResult_Parser.Parse(0, false, "Warning: no stdin data received in 3s, proceeding without it\n{ not json\n" + MEASURED_JSON, "", TimeSpan.Zero);

        Assert.Equal("DONE", result.ResultText);
    }

    [Fact]
    public void MissingFields_AreNull_NotErrors()
    {
        var result = TurnResult_Parser.Parse(0, false, """{"type":"result","result":"ok"}""", "", TimeSpan.Zero);

        Assert.Equal("ok", result.ResultText);
        Assert.Null(result.TotalCostUsd);
        Assert.Null(result.DurationMs);
        Assert.Null(result.SessionId);
        Assert.False(result.IsError);
        Assert.True(TurnOutcomes.Is_Success(result));
    }

    [Fact]
    public void ApiError_IsAnError_WithItsStatus()
    {
        var result = TurnResult_Parser.Parse(1, false, """{"type":"result","subtype":"error_during_execution","is_error":true,"api_error_status":429,"result":""}""", "rate limited", TimeSpan.Zero);

        Assert.False(TurnOutcomes.Is_Success(result));
        Assert.Equal(429, result.ApiErrorStatus);
        Assert.Equal("error", TurnOutcomes.Describe(result));
    }

    [Fact]
    public void NoJson_TextBecomesTheResult_AndTheExitCodeDecides()
    {
        var ok = TurnResult_Parser.Parse(0, false, "plain text answer\n", "", TimeSpan.Zero);
        Assert.Equal("plain text answer", ok.ResultText);
        Assert.True(TurnOutcomes.Is_Success(ok));

        var failed = TurnResult_Parser.Parse(2, false, "", "boom", TimeSpan.Zero);
        Assert.Null(failed.ResultText);
        Assert.True(failed.IsError);
        Assert.Equal("error", TurnOutcomes.Describe(failed));
    }

    [Fact]
    public void Timeout_IsItsOwnOutcome()
    {
        var result = TurnResult_Parser.Parse(-1, true, "", "", TimeSpan.FromMinutes(30));

        Assert.Equal("timeout", TurnOutcomes.Describe(result));
        Assert.True(result.IsError);
    }
}
