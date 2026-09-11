using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Channels.ChannelEntry;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnResult;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// A FINAL REPORT IS NEVER SUPERSEDED. Measured 3× on 2026-09-09/10: a session wrote its report, a
/// BACKGROUND sub-agent returned afterwards, the CLI resumed the session, a later message was
/// produced — and only the <c>result</c> event's text was ever filed, so a 19,771-character report
/// and a nine-agent review were lost while the turn reported success.
///
/// Both transports are driven here through FakeClaude, whose scenario can now script N assistant
/// messages in one turn (<c>assistant_messages</c>, <c>tool_use_between</c>). Nothing touches the
/// real CLI.
/// </summary>
public class SupersededFinalTests
{
    /// <summary>
    /// Two text-only assistant messages with a tool call between them, and a result that is the
    /// SECOND — the defect's own shape: report written, background sub-agent returns, later message
    /// becomes the entry.
    /// </summary>
    const string SUPERSEDING_TURN = """
    {"result":"LATE — the sub-agent came back\n\nit found nothing",
     "assistant_messages":["REPORT — the parser\n\nnineteen thousand characters of it","LATE — the sub-agent came back\n\nit found nothing"],
     "tool_use_between":true}
    """;

    /// <summary>
    /// A STREAM SESSION SPENDS ITS FIRST MESSAGE ON THE BOOT, a print one does not — so the briefed
    /// turn is the second scenario turn for one transport and the first for the other. Everything
    /// else about the two runs is identical, which is the point of the pair.
    /// </summary>
    static string Superseding_Scenario(SessionRunners runner)
    {
        return runner == SessionRunners.Stream
            ? $$"""{"turns":[{"result":"imp-1 online"},{{SUPERSEDING_TURN}}]}"""
            : $$"""{"turns":[{{SUPERSEDING_TURN}}]}""";
    }

    static void Append_Supervisor(PrintRunnerTestHarness harness, string orchId, string memberId, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, subject, body, DateTime.Now));
    }

    static IReadOnlyList<IChannelEntry> Run_OneBriefedTurn(PrintRunnerTestHarness harness, SessionRunners runner, string scenario)
    {
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: runner);
        harness.Write_Scenario(scenario);
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF — parser", "Write the parser.");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        dispatcher.Stop_Async().GetAwaiter().GetResult();

        return ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
    }

    static void Assert_BothWereFiled_InOrder_AndTheSessionWasTold(IReadOnlyList<IChannelEntry> entries)
    {
        var all = entries.ToList();
        var member = all.Where(entry => entry.Author == ChannelAuthors.Implementer).ToList();

        Assert.Equal(2, member.Count);
        Assert.Equal("REPORT — the parser", member[0].Subject);
        Assert.Equal("nineteen thousand characters of it", member[0].Body);
        Assert.Equal("LATE — the sub-agent came back", member[1].Subject);
        Assert.Equal("it found nothing", member[1].Body);

        // THE ORDER IS THE ONE THE SESSION WROTE THEM IN — the superseded final first.
        Assert.True(all.IndexOf(member[0]) < all.IndexOf(member[1]));

        var notice = Assert.Single(entries, entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains(PrintTurn_Words.SUPERSEDED_FINAL_SUBJECT, StringComparison.Ordinal));
        Assert.Contains("sub-agent", notice.Body);
        Assert.True(all.IndexOf(member[1]) < all.IndexOf(notice));
    }

    [Fact]
    public async Task AFinalLookingMessageFollowedByALaterOne_IsFiledAsWell_AndTheSessionIsTold()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");

        Assert_BothWereFiled_InOrder_AndTheSessionWasTold(Run_OneBriefedTurn(harness, SessionRunners.Stream, Superseding_Scenario(SessionRunners.Stream)));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task AFinalMessageTHATISTheResult_IsFiledOnce_AndNothingIsSaidAboutIt()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");

        // ONE assistant message, and the result repeats it — the shape of every ordinary turn.
        var entries = Run_OneBriefedTurn(harness, SessionRunners.Stream, """
        {"turns":[
          {"result":"imp-1 online"},
          {"result":"REPORT — the parser\n\ndone","assistant_messages":["REPORT — the parser\n\ndone"]}
        ]}
        """);

        var member = Assert.Single(entries, entry => entry.Author == ChannelAuthors.Implementer);
        Assert.Equal("REPORT — the parser", member.Subject);
        Assert.DoesNotContain(entries, entry => entry.Subject.Contains(PrintTurn_Words.SUPERSEDED_FINAL_SUBJECT, StringComparison.Ordinal));

        await Task.CompletedTask;
    }

    [Fact]
    public async Task ThePrintTransportSeesTheSameThing_BecauseItReadsTheSameStream()
    {
        using var harness = new PrintRunnerTestHarness("implementer");

        Assert_BothWereFiled_InOrder_AndTheSessionWasTold(Run_OneBriefedTurn(harness, SessionRunners.Print, Superseding_Scenario(SessionRunners.Print)));

        await Task.CompletedTask;
    }

    // ----- the rule and the reader, on their own -----

    static JsonObject Assistant(string text, string? stopReason = "end_turn", string? parentToolUseId = null)
    {
        var message = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        };

        if (stopReason != null)
            message["stop_reason"] = stopReason;

        var json = new JsonObject { ["type"] = "assistant", ["message"] = message };

        if (parentToolUseId != null)
            json["parent_tool_use_id"] = parentToolUseId;

        return json;
    }

    [Fact]
    public void AnAssistantMessageThatAsksForATool_IsNotAFinalMessage()
    {
        var toolCall = new JsonObject
        {
            ["type"] = "assistant",
            ["message"] = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = new JsonArray(
                    new JsonObject { ["type"] = "text", ["text"] = "Let me look." },
                    new JsonObject { ["type"] = "tool_use", ["id"] = "toolu_1", ["name"] = "Read" }),
                ["stop_reason"] = "tool_use",
            },
        };

        Assert.False(SupersededFinals_Rule.Is_FinalLooking(toolCall));
        Assert.True(SupersededFinals_Rule.Is_FinalLooking(Assistant("a report")));
    }

    [Fact]
    public void ASubAgentsMessage_IsNeverAFinalMessageOfTheSession()
    {
        Assert.False(SupersededFinals_Rule.Is_FinalLooking(Assistant("the sub-agent's own words", parentToolUseId: "toolu_9")));

        var sidechain = Assistant("the sub-agent's own words");
        sidechain["isSidechain"] = true;
        Assert.False(SupersededFinals_Rule.Is_FinalLooking(sidechain));
    }

    [Fact]
    public void AMessageTheResultBeginsWith_IsNotSuperseded_AndOneItDoesNotIs()
    {
        Assert.Empty(SupersededFinals_Rule.Select_Superseded(["the report"], "the report"));
        Assert.Empty(SupersededFinals_Rule.Select_Superseded(["the report"], "the report, plus a postscript"));
        Assert.Equal(["the report"], SupersededFinals_Rule.Select_Superseded(["the report"], "done"));
        Assert.Equal(["one", "two"], SupersededFinals_Rule.Select_Superseded(["one", "two", "three"], "three"));
    }

    [Fact]
    public void TheStreamReader_TakesTheRESULTLine_NotTheLastJsonLine()
    {
        // The Stop hook's response lands AFTER the result — measured, and the reason "the last JSON
        // object" is the wrong reading of an NDJSON turn.
        var stdout = string.Join('\n',
            """{"type":"system","subtype":"init","session_id":"s"}""",
            "not json at all",
            Assistant("the earlier report").ToJsonString(),
            Assistant("the later message").ToJsonString(),
            """{"type":"result","subtype":"success","is_error":false,"result":"the later message","session_id":"s","total_cost_usd":0.5,"num_turns":2}""",
            """{"type":"system","subtype":"hook_response","hook_name":"Stop","session_id":"s"}""");

        var result = TurnResult_Parser.Parse_Stream(0, timedOut: false, stdout, string.Empty, TimeSpan.FromSeconds(3));

        Assert.Equal("the later message", result.ResultText);
        Assert.Equal("s", result.SessionId);
        Assert.Equal(0.5, result.TotalCostUsd);
        Assert.False(result.IsError);
        Assert.Equal(["the earlier report"], result.SupersededFinals);
    }

    [Fact]
    public void AStreamWithNoResultLine_StillReportsTheTurn()
    {
        var result = TurnResult_Parser.Parse_Stream(1, timedOut: false, "Error: something went wrong\n", "stderr text", TimeSpan.FromSeconds(1));

        Assert.True(result.IsError);
        Assert.Equal("Error: something went wrong", result.ResultText);
        Assert.Empty(result.SupersededFinals);
    }
}
