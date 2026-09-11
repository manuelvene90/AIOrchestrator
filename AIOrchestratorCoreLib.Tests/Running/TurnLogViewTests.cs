using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.TurnLog;
using AIOrchestratorCoreLib.Sessions;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// /tail and /log — what the owner reads INSTEAD of a screenshot of a terminal, since a
/// bridge-driven session has no terminal. Both are pure text over the turn log: no model, no cost,
/// nothing that can be slow.
/// </summary>
public class TurnLogViewTests
{
    static JsonObject Record(string json, string requestId = "repo-1/imp-1/1")
    {
        var record = (JsonObject)JsonNode.Parse(json)!;

        record[TurnLog_Store.REQUEST_ID_KEY] = requestId;
        record[TurnLog_Store.AT_KEY] = DateTime.UtcNow.ToString("o");

        return record;
    }

    [Fact]
    public void ATailShowsWhatItCalled_WhatItSaid_AndHowTheTurnEnded()
    {
        List<JsonObject> records =
        [
            Record("""{"type":"system","subtype":"init"}"""),
            Record("""{"type":"assistant","message":{"content":[{"type":"tool_use","name":"Bash"}]}}"""),
            Record("""{"type":"assistant","message":{"content":[{"type":"text","text":"reading the parser"}]}}"""),
            Record("""{"type":"result","is_error":false,"result":"REPORT\n\ndone","total_cost_usd":0.0321,"duration_ms":4200}"""),
        ];

        var tail = TurnLog_Formatter.Format_Tail("imp-1", records);

        Assert.Contains("→ Bash", tail);
        Assert.Contains("reading the parser", tail);
        Assert.Contains("turn ok", tail);
        Assert.Contains("4.2 s", tail);
        Assert.Contains("0.0321 USD", tail);

        // The per-turn `init` is bookkeeping: a tail padded with it is a tail nobody reads.
        Assert.DoesNotContain("init", tail);
    }

    [Fact]
    public void TheCostShownIsTheTURNS_NotTheProcessRunningTotal()
    {
        // A stream result states the PROCESS's cumulative cost. The bridge stamps the turn's own
        // figure beside it, and the tail reads that — otherwise /tail says 0.0422 for a turn the
        // channel recorded as 0.0056, which is the disagreement decision 10 forbids. Measured live
        // on 2026-09-06 before the stamp existed.
        var tail = TurnLog_Formatter.Format_Tail("sup",
        [
            Record("""{"type":"result","is_error":false,"result":"ack","total_cost_usd":0.0422,"aiorch_turn_cost_usd":0.0056}"""),
        ]);

        Assert.Contains("0.0056 USD", tail);
        Assert.DoesNotContain("0.0422", tail);
    }

    [Fact]
    public void AnErrorTurnSaysSo_AndAnUnreadableLineIsShownRatherThanDropped()
    {
        var tail = TurnLog_Formatter.Format_Tail("imp-1",
        [
            Record("""{"type":"unparsed","raw":"a line the format changed under us"}"""),
            Record("""{"type":"result","is_error":true,"result":"could not"}"""),
        ]);

        Assert.Contains("turn ERROR", tail);
        Assert.Contains("a line the format changed under us", tail);
    }

    [Fact]
    public void AnEmptyLogSaysWhyItIsEmpty_RatherThanNothing()
    {
        Assert.Contains("no turns recorded yet", TurnLog_Formatter.Format_Tail("imp-1", []));
        Assert.Contains("no turns recorded yet", TurnLog_Formatter.Format_LastTurn("imp-1", []));
    }

    [Fact]
    public void ALogNamesTheTurn_AndKeepsTheHooksATailHides()
    {
        var text = TurnLog_Formatter.Format_LastTurn("sup",
        [
            Record("""{"type":"system","subtype":"hook_response","hook_name":"PreToolUse:Bash"}""", "repo-1/sup/7"),
            Record("""{"type":"result","is_error":false,"result":"ack"}""", "repo-1/sup/7"),
        ]);

        Assert.Contains("turn repo-1/sup/7", text);
        Assert.Contains("hook PreToolUse:Bash", text);
    }

    [Fact]
    public void EverythingIsCappedWellUnderTelegramsLimit_SoTheChunkerRarelySplits()
    {
        var filler = new string('x', 500);
        List<JsonObject> records = [.. Enumerable.Range(0, 400).Select(index =>
            Record("""{"type":"assistant","message":{"content":[{"type":"text","text":"TEXT"}]}}""".Replace("TEXT", $"{filler} {index}")))];

        var tail = TurnLog_Formatter.Format_Tail("imp-1", records);

        Assert.True(tail.Length <= TelegramMessage_Chunker.TELEGRAM_MAX_MESSAGE_LENGTH, $"a tail of {tail.Length} chars would be split");
        Assert.EndsWith("…", tail);
    }

    [Fact]
    public void TheOwnerCanNameASessionTheWayTheyThinkOfIt_AndAmbiguityIsAnsweredWithAList()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiorch-tail-locator-{Guid.NewGuid():N}");
        var paths = SupervisionPaths_Factory.Create(root);

        try
        {
            var store = OrchestrationSessionStore_Factory.Create(paths);
            store.Create_Orchestration("repo-1", "Repo", root);
            store.Add_Member("repo-1", MemberKinds.Implementer);
            var session = store.Add_Member("repo-1", MemberKinds.Reviewer);

            Assert.Equal((SessionRoles.Implementer, "imp-1"), TurnLog_Locator.Resolve_OrNull(session, "1"));
            Assert.Equal((SessionRoles.Implementer, "imp-1"), TurnLog_Locator.Resolve_OrNull(session, "imp-1"));

            // A member id typed in full wins over the bare-number inference: rev-1 is never imp-1.
            Assert.Equal((SessionRoles.Reviewer, "rev-1"), TurnLog_Locator.Resolve_OrNull(session, "rev-1"));
            Assert.Equal((SessionRoles.Supervisor, "sup"), TurnLog_Locator.Resolve_OrNull(session, "sup"));

            // Nothing typed, or a member that is not open: a LIST, never a guess — picking one for
            // the owner is how they read a healthy session's log and conclude the broken one is fine.
            Assert.Null(TurnLog_Locator.Resolve_OrNull(session, string.Empty));
            Assert.Null(TurnLog_Locator.Resolve_OrNull(session, "imp-9"));
            Assert.Contains("imp-1", TurnLog_Locator.Describe_Choices(session, "/tail"));
            Assert.Contains("sup", TurnLog_Locator.Describe_Choices(session, "/tail"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
