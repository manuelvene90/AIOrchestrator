using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Channels;
using AIOrchestratorCoreLib.Limits;
using AIOrchestratorCoreLib.Running;
using AIOrchestratorCoreLib.Running.PrintSessionState;
using AIOrchestratorCoreLib.Running.TurnLog;
using AIOrchestratorCoreLib.Sessions;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running;

/// <summary>
/// The stream runner end to end on the FakeClaude stub — one living process per session, messages
/// on its stdin, everything asserted from the fake's own invocation log (which now records the
/// process start and each message separately, so "did it start a second process" is answerable).
/// Nothing here touches the real CLI; the Live twin of these measures is
/// <c>tools/claude-contract/ClaudeContract.Tests/Live/ClaudeCliStreamContractLiveTests</c>.
/// </summary>
public class StreamTurnDispatcherTests
{
    const string STREAM_START = "stream-start";
    const string STREAM_MESSAGE = "stream-message";

    static void Append_Supervisor(PrintRunnerTestHarness harness, string orchId, string memberId, string subject, string body)
    {
        Assert.True(ChannelAppender.Append_SessionEntry(harness.Paths.Get_ImplementerChannelFile(orchId, memberId), ChannelAuthors.Supervisor, subject, body, DateTime.Now));
    }

    static void Append_Owner(PrintRunnerTestHarness harness, string orchId, string text)
    {
        Assert.True(ChannelAppender.Append_OwnerEntry(harness.Paths.Get_OwnerChannelFile(orchId), text, DateTime.Now));
    }

    static IReadOnlyList<JsonObject> Lines(PrintRunnerTestHarness harness, string kind)
    {
        return [.. harness.Read_Invocations().Where(line => line["line_kind"]?.GetValue<string>() == kind)];
    }

    [Fact]
    public async Task AStreamSession_BootsWithItsRoleCommand_ThenAnswersTheBrief_INONEPROCESS()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);
        harness.Write_Scenario("""{"turns":[{"result":"imp-1 online"},{"result":"REPORT — parser\n\ndone"}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF — parser", "Write the parser.");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        var start = Assert.Single(Lines(harness, STREAM_START));
        var args = PrintRunnerTestHarness.Args(start);

        // A1b.2 — the command line, in full. --verbose is not decoration: the CLI refuses
        // --output-format stream-json without it.
        Assert.Equal(
            ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--include-hook-events", "--replay-user-messages",
             "--name", $"{orchId}-{memberId}", "--session-id", state.SessionId, "--model", "haiku", "--dangerously-skip-permissions"],
            args);

        Assert.Equal("stream", start["env"]!["AIORCH_RUNNER"]!.GetValue<string>());
        Assert.Equal(harness.Paths.Root, start["env"]!["AIORCH_SUPERVISION_ROOT"]!.GetValue<string>());
        Assert.Equal(orchId, start["env"]!["AIORCH_ID"]!.GetValue<string>());

        // TWO messages, ONE process: the role command boots the session (there is no positional
        // prompt in this mode), the pending entries follow on the same stdin.
        var messages = Lines(harness, STREAM_MESSAGE);
        Assert.Equal(2, messages.Count);
        Assert.Equal($"/implementer {orchId}/{memberId}", messages[0]["prompt"]!.GetValue<string>());
        Assert.Contains($"[bridge turn {orchId}/{memberId}/1]", messages[1]["prompt"]!.GetValue<string>());
        Assert.Contains("BRIEF — parser", messages[1]["prompt"]!.GetValue<string>());

        // The dispatcher saw ONE turn: the boot is part of the first turn, not a turn of its own.
        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        var memberEntry = Assert.Single(entries, entry => entry.Author == ChannelAuthors.Implementer);
        Assert.Equal("REPORT — parser", memberEntry.Subject);
        Assert.Equal("done", memberEntry.Body);

        var ended = Assert.Single(entries, entry => entry.Author == ChannelAuthors.App);
        Assert.Contains($"{PrintTurn_Words.TURN_ENDED_SUBJECT} {memberId} turn 1 — success", ended.Subject);
    }

    [Fact]
    public async Task ASecondBrief_RidesTHESAMEPROCESS_WhichIsTheWholePointOfThisRunner()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);
        harness.Write_Scenario("""{"default":{"result":"ack\n\nbody"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "first");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));

        Append_Supervisor(harness, orchId, memberId, "GO AHEAD", "second");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        // ONE start for two turns — and no second boot: the transcript already has the role command.
        Assert.Single(Lines(harness, STREAM_START));

        var messages = Lines(harness, STREAM_MESSAGE);
        Assert.Equal(3, messages.Count);
        Assert.Contains($"[bridge turn {orchId}/{memberId}/2]", messages[2]["prompt"]!.GetValue<string>());
        Assert.Contains("GO AHEAD", messages[2]["prompt"]!.GetValue<string>());
    }

    [Fact]
    public async Task TheBootsCost_IsAddedIntoTheFirstTurn_BecauseItWasSpentOnIt()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);

        // The fake reports the PROCESS's running total, as the real CLI does: 0.02 then 0.05.
        harness.Write_Scenario("""{"turns":[{"result":"online","total_cost_usd":0.02},{"result":"REPORT\n\ndone","total_cost_usd":0.03}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        // 0.02 (boot) + 0.03 (the brief) — the difference, then the sum. Reported straight from the
        // cumulative field it would have been 0.05 for the brief alone and 0.07 with the boot.
        var ended = Assert.Single(ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId)), entry => entry.Author == ChannelAuthors.App);
        Assert.Contains("cost_usd: 0.0500", ended.Body);
    }

    [Fact]
    public async Task AProcessThatDiesMidTurn_IsRetriedOnAFreshOne_RESUMINGTheSameTranscript()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);

        // Message 1 boots; message 2 kills the process mid-conversation. The retry does NOT re-boot
        // — it resumes a transcript that already carries the role command — so its FIRST message is
        // the brief again, which is scenario turn 3.
        harness.Write_Scenario("""{"turns":[{"result":"online"},{"exit_code":9,"stderr":"stream died"},{"result":"REPORT\n\nrecovered"}]}""");
        var dispatcher = harness.Create_Dispatcher(TimeSpan.FromMilliseconds(50));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var starts = Lines(harness, STREAM_START);
        Assert.Equal(2, starts.Count);

        var state = harness.Read_State(SessionRoles.Implementer, orchId, memberId);
        var retryArgs = PrintRunnerTestHarness.Args(starts[1]);

        // --resume, never --session-id again: the id is spent, and the CLI refuses a re-claim
        // outright (measured). Resuming also keeps whatever the killed attempt had already done.
        Assert.Contains("--resume", retryArgs);
        Assert.Equal(state.SessionId, retryArgs[retryArgs.IndexOf("--resume") + 1]);
        Assert.DoesNotContain("--session-id", retryArgs);

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId));
        Assert.Single(entries, entry => entry.Author == ChannelAuthors.Implementer && entry.Subject == "REPORT");
        Assert.Contains(entries, entry => entry.Author == ChannelAuthors.App && entry.Subject.Contains("turn 1 — error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ThreeDeathsInARow_FALLBACKTOPRINT_AndTheLogSaysSo()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);

        List<string> logged = [];
        harness.Log.EntryLogged += entry => logged.Add(entry.Message);

        // Every stream message after the boot kills the process. Three of those and the session
        // stops using the transport — a turn the CLI merely answered badly never would.
        harness.Write_Scenario("""{"default":{"exit_code":9,"stderr":"stream died"},"turns":[{"result":"online"},{"exit_code":9},{"result":"online"},{"exit_code":9},{"result":"online"},{"exit_code":9},{"result":"REPORT\n\nvia print"}]}""");
        var dispatcher = harness.Create_Dispatcher(TimeSpan.FromMilliseconds(20));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => Lines(harness, "invocation").Count > 0, PrintRunnerTestHarness.GENEROUS),
            $"the session never fell back to print. Log:\n{string.Join("\n", logged)}");

        await dispatcher.Stop_Async();

        // A plain `claude -p` invocation is the print runner: the ladder walked down one rung,
        // stepping over `bg`, which this stage does not implement.
        var printArgs = PrintRunnerTestHarness.Args(Lines(harness, "invocation")[0]);
        Assert.Contains("--output-format", printArgs);
        Assert.Equal(PrintTurnCommand_Builder.OUTPUT_FORMAT, printArgs[printArgs.IndexOf("--output-format") + 1]);
        Assert.DoesNotContain("--input-format", printArgs);

        Assert.Contains(logged, message => message.Contains("falls back from runner 'stream' to 'print'", StringComparison.Ordinal));
        Assert.Contains(logged, message => message.Contains("over 'bg'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARateLimitEvent_BecomesAReadingTheEXISTINGLimitsPathCanRead()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);

        var fiveHourResetsAt = DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds();
        var sevenDayResetsAt = DateTimeOffset.UtcNow.AddDays(4).ToUnixTimeSeconds();

        harness.Write_Scenario(
            """{"turns":[{"result":"online"},{"result":"REPORT\n\ndone","rate_limit":{"unifiedWindows":{"five_hour":{"utilization":0.73,"resetsAt":FIVE},"seven_day":{"utilization":0.21,"resetsAt":SEVEN}}}}]}"""
                .Replace("FIVE", fiveHourResetsAt.ToString())
                .Replace("SEVEN", sevenDayResetsAt.ToString()));
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        // Found by the SAME discovery the alerts and /limits already use — no new consumer.
        var files = RateLimits_Reader.Find_UsageFiles_WithLiveWindow(harness.Paths, DateTime.Now);
        Assert.NotEmpty(files);

        var worst = RateLimits_Reader.Read_WorstAcrossSessions(files, DateTime.Now);
        Assert.Equal(73, worst.Single(window => window.Window == "5h").Percent, 2);
        Assert.Equal(21, worst.Single(window => window.Window == "weekly").Percent, 2);

        // And by the tolerant reader behind the automatic alerts and the dispatch pause.
        var raw = File.ReadAllText(files[0]);
        var windows = LimitData_Parser.Extract_LimitWindows(raw);
        var fiveHour = windows.Single(pair => pair.Key.Contains("five_hour", StringComparison.Ordinal));

        Assert.Equal(73, fiveHour.Value.Percent, 2);
        Assert.NotNull(fiveHour.Value.WindowResetsAtUtc);

        // Which is what makes stage 3's pause fire from a headless session at all.
        Assert.NotNull(DispatchPause_Gate.Decide_PauseUntil_OrNull(fiveHour.Value.Percent, fiveHour.Value.WindowResetsAtUtc, 70, DateTime.UtcNow));
    }

    [Fact]
    public async Task AProcessThatGoesQUIET_IsKilledByTheHeartbeat_LongBeforeTheTurnTimeout()
    {
        // Silence limit 1 s, turn timeout 5 minutes: without the heartbeat this test would take
        // five minutes, which is exactly the owner's complaint about a hung phone line.
        using var harness = new PrintRunnerTestHarness("implementer:stream", turnTimeoutMinutes: 5, streamSilenceSeconds: 1);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);

        List<string> logged = [];
        harness.Log.EntryLogged += entry => logged.Add(entry.Message);

        harness.Write_Scenario("""{"turns":[{"result":"online"},{"result":"too late","delay_ms":20000},{"result":"online"},{"result":"REPORT\n\nsecond attempt"}]}""");
        var dispatcher = harness.Create_Dispatcher(TimeSpan.FromMilliseconds(50));

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, TimeSpan.FromSeconds(45)),
            $"the mute turn was not cut short. Log:\n{string.Join("\n", logged)}");

        await dispatcher.Stop_Async();

        Assert.Contains(logged, message => message.Contains("said nothing for", StringComparison.Ordinal));
        Assert.Single(ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId)), entry => entry.Author == ChannelAuthors.Implementer);
    }

    [Fact]
    public async Task ASecondTurnAfterAQuietGap_IsNotKilledForTheGap()
    {
        // The measured production signature (2026-09-09): silence is a per-TURN clock, but the
        // heartbeat used to be stamped only at construction and on bytes received — never when a
        // new prompt is sent. An idle gap between two owner messages, longer than the silence
        // limit, left the NEXT turn reading a stale heartbeat from the turn before and being
        // declared silent before it had even been given a chance to answer.
        // TWO SECONDS, NOT 300 MILLISECONDS, and the claim is untouched by the change. The gap below
        // still has to exceed the silence limit for this test to mean anything — what the old number
        // bought was speed, and what it cost was reliability: starting FakeClaude on a loaded machine
        // takes longer than 300 ms, so the limit was inside the noise of the harness itself. This
        // test's own comment already records one narrowing for the same reason (2026-09-09); it went
        // red again on 2026-09-10 under a full parallel suite.
        using var harness = new PrintRunnerTestHarness("implementer:stream", turnTimeoutMinutes: 5, streamSilenceSeconds: 2.0);
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);

        List<string> logged = [];
        harness.Log.EntryLogged += entry => logged.Add(entry.Message);

        harness.Write_Scenario("""{"default":{"result":"ack\n\nbody"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "first");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));

        var loggedWhenTheFirstTurnEnded = logged.Count;
        var startsWhenTheFirstTurnEnded = Lines(harness, STREAM_START).Count;

        // The quiet stretch: well past the 300 ms silence limit, and nobody has asked the process
        // anything since its last answer — exactly the shape of the idle gap between two owner
        // messages on the VPS.
        Thread.Sleep(4000);

        Append_Supervisor(harness, orchId, memberId, "GO AHEAD", "second");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS),
            $"the second turn never completed. Log:\n{string.Join("\n", logged)}");

        await dispatcher.Stop_Async();

        // The bug kills the healthy process for a gap it did not cause: caught here by the
        // transport-failure log line the kill always writes, and by the reboot a killed process
        // forces — ONE process the whole test, or the fix did not take.
        // SCOPED TO WHAT HAPPENS AFTER THE GAP, and only that. The first version asserted over the whole
        // run and went red under a full parallel suite (measured 2026-09-09): the silence limit is 300 ms
        // and starting the FakeClaude process can take longer than that on a loaded machine, so turn 1
        // was killed for its own start-up and the test blamed the gap. The bug this pins is that the
        // SECOND turn must not be killed for a quiet stretch it did not cause — so the evidence is the
        // log written after the first turn finished, and the process that served the second turn.
        var afterFirstTurn = logged.Skip(loggedWhenTheFirstTurnEnded).ToList();

        Assert.DoesNotContain(afterFirstTurn, message => message.Contains("said nothing for", StringComparison.Ordinal));
        Assert.Equal(startsWhenTheFirstTurnEnded, Lines(harness, STREAM_START).Count);
    }

    /// <summary>
    /// Two briefs, and between them the session answers something nobody sent — a background task it
    /// started finishing (<paramref name="scenario"/> says when the reply reaches the bridge). What the
    /// implementer's channel must hold afterwards, in order: the first report, the unprompted reply
    /// filed on its own, the second report. The defect put the unprompted reply in the second
    /// report's place and left the second report in the pipe for the turn after.
    /// </summary>
    static async Task<(IReadOnlyList<AIOrchestratorCoreLib.Channels.ChannelEntry.IChannelEntry> Entries, IReadOnlyList<string> Logged)> Run_TwoBriefs_WithAnUnpromptedReply(string scenario)
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);

        List<string> logged = [];
        harness.Log.EntryLogged += entry => logged.Add(entry.Message);

        harness.Write_Scenario(scenario);
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "first");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));

        Append_Supervisor(harness, orchId, memberId, "GO AHEAD", "second");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var entries = ChannelEntry_Parser.Parse_All(harness.Read_Channel(orchId, memberId)).Where(entry => entry.Author == ChannelAuthors.Implementer).ToList();
        return (entries, logged);
    }

    [Fact]
    public async Task AReplyNobodyWaitedFor_IsFiledOnItsOwn_AndTheNextBriefGetsITSAnswer()
    {
        // fincanva-5, 2026-09-11: the unprompted reply was written at 12:51 and was already waiting
        // in the pipe when the owner's next message went in at 12:56.
        var (entries, logged) = await Run_TwoBriefs_WithAnUnpromptedReply(
            """{"turns":[{"result":"online"},{"result":"REPORT — one\n\nfirst","unprompted_result_after":"UNPROMPTED — staging\n\nnow serves the table"},{"result":"REPORT — two\n\nsecond"}]}""");

        Assert.Equal(["REPORT — one", "UNPROMPTED — staging", "REPORT — two"], entries.Select(entry => entry.Subject));
        Assert.Contains(logged, message => message.Contains("had written 1 message(s) on its own", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AReplyThatLandsAfterTheMessageButBeforeItsEcho_IsNotItsAnswer()
    {
        // The other ordering: the unprompted turn finished while the bridge's message sat queued, so
        // its result arrives AFTER the write — nothing was waiting to be drained, and only the echo
        // says the result is not this message's.
        var (entries, _) = await Run_TwoBriefs_WithAnUnpromptedReply(
            """{"turns":[{"result":"online"},{"result":"REPORT — one\n\nfirst"},{"result":"REPORT — two\n\nsecond","unprompted_result_before":"UNPROMPTED — staging\n\nnow serves the table"}]}""");

        Assert.Equal(["REPORT — one", "UNPROMPTED — staging", "REPORT — two"], entries.Select(entry => entry.Subject));
    }

    [Fact]
    public async Task ACliThatNeverEchoes_StillGetsEveryAnswer_AndAWaitingReplyIsStillSetAside()
    {
        // The direction that must not break: demanding an echo from a CLI that never sends one would
        // turn every answer into an "unprompted" reply. Without the echo the first result is the
        // answer, as before — and a reply already waiting before the write is still not one.
        var (entries, _) = await Run_TwoBriefs_WithAnUnpromptedReply(
            """{"default":{"no_replay":true},"turns":[{"result":"online"},{"result":"REPORT — one\n\nfirst","unprompted_result_after":"UNPROMPTED — staging\n\nnow serves the table"},{"result":"REPORT — two\n\nsecond"}]}""");

        Assert.Equal(["REPORT — one", "UNPROMPTED — staging", "REPORT — two"], entries.Select(entry => entry.Subject));
    }

    /// <summary>
    /// THE TURN SAYS WHICH OWNER MESSAGE ITS ANSWER ANSWERS, so the phone can thread it (owner,
    /// 2026-09-11: "non c'è modo di linkare domande e risposte? Tipo con un rispondi?"). The bridge's
    /// half — which Telegram message became the owner entry — is seeded here as the bridge would.
    /// </summary>
    [Fact]
    public async Task TheSupervisorsAnswer_IsLinkedToTheOwnerMessageItsTurnWasGiven()
    {
        using var harness = new PrintRunnerTestHarness("supervisor:stream");
        var memberId = harness.Register_Supervisor();
        harness.Write_Scenario("""{"default":{"result":"ack\n\nnoted"}}""");
        var dispatcher = harness.Create_Dispatcher();
        var ownerChannel = harness.Paths.Get_OwnerChannelFile("repo-1");

        Append_Owner(harness, "repo-1", "start on the parser");
        var ownerEntry = Assert.Single(ChannelEntry_Parser.Parse_All(File.ReadAllText(ownerChannel)), entry => entry.Author == ChannelAuthors.Owner);
        dispatcher.ReplyLinks.Record_OwnerMessage(ownerChannel, ownerEntry.Index, telegramMessageId: 4242);

        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, "repo-1", memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var answer = Assert.Single(ChannelEntry_Parser.Parse_All(File.ReadAllText(ownerChannel)), entry => entry.Author == ChannelAuthors.Supervisor);

        Assert.Equal(4242, dispatcher.ReplyLinks.Find_AnsweredTelegramMessage_OrNull(ownerChannel, answer.Index));
        Assert.Null(dispatcher.ReplyLinks.Find_AnsweredTelegramMessage_OrNull(ownerChannel, ownerEntry.Index));
    }

    [Fact]
    public async Task TheSupervisor_RunsInTheStream_AndIsWokenByTheOwnerChannel()
    {
        // The role this whole runner exists for. It is NOT print-runnable (the print table has never
        // listed it) and the two turns below are the owner's two consecutive messages.
        using var harness = new PrintRunnerTestHarness("supervisor:stream");
        var memberId = harness.Register_Supervisor();
        harness.Write_Scenario("""{"default":{"result":"ack\n\nnoted"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Owner(harness, "repo-1", "start on the parser");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, "repo-1", memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));

        Append_Owner(harness, "repo-1", "and then the tests");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Supervisor, "repo-1", memberId).ExecutedTurns.Count == 2, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        Assert.Single(Lines(harness, STREAM_START));
        Assert.Equal(3, Lines(harness, STREAM_MESSAGE).Count);
        Assert.Equal($"/supervisor repo-1", Lines(harness, STREAM_MESSAGE)[0]["prompt"]!.GetValue<string>());

        var entries = ChannelEntry_Parser.Parse_All(File.ReadAllText(harness.Paths.Get_OwnerChannelFile("repo-1")));
        Assert.Equal(2, entries.Count(entry => entry.Author == ChannelAuthors.Supervisor));
    }

    [Fact]
    public async Task EveryEventOfTheStream_LandsInTheTurnLog_WhichIsWhatSlashTailReads()
    {
        using var harness = new PrintRunnerTestHarness("implementer:stream");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer, runner: SessionRunners.Stream);
        harness.Write_Scenario("""{"turns":[{"result":"online"},{"result":"REPORT\n\ndone"}]}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var logFile = TurnLog_Store.Get_File(harness.Paths, SessionRoles.Implementer, orchId, memberId);
        var records = TurnLog_Store.Read_LastRecords(logFile, 500);

        Assert.Contains(records, record => record["type"]?.GetValue<string>() == "result");

        // THE TURN'S OWN COST, stamped beside the CLI's process-cumulative figure: /tail reads this
        // one, so the tail and the channel cannot report two different numbers for one turn.
        var results = records.Where(record => record["type"]?.GetValue<string>() == "result").ToList();
        Assert.All(results, record => Assert.NotNull(record[AIOrchestratorCoreLib.Running.StreamTurn.StreamSessionProcess_Words.TURN_COST_KEY]));
        Assert.Contains(records, record => record["subtype"]?.GetValue<string>() == "hook_response");
        Assert.All(records, record => Assert.Equal($"{orchId}/{memberId}/1", TurnLog_Store.Read_RequestId_OrNull(record)));
    }

    [Fact]
    public async Task APrintTurn_LandsInTheSameTurnLog_SoTailAnswersForEitherRunner()
    {
        using var harness = new PrintRunnerTestHarness("implementer");
        var (orchId, memberId) = harness.Register_Member(MemberKinds.Implementer);
        harness.Write_Scenario("""{"default":{"result":"online\n\nbody"}}""");
        var dispatcher = harness.Create_Dispatcher();

        Append_Supervisor(harness, orchId, memberId, "BRIEF", "go");
        Assert.True(PrintRunnerTestHarness.Drive_Until(dispatcher, () => harness.Read_State(SessionRoles.Implementer, orchId, memberId).ExecutedTurns.Count == 1, PrintRunnerTestHarness.GENEROUS));
        await dispatcher.Stop_Async();

        var records = TurnLog_Store.Read_LastRecords(TurnLog_Store.Get_File(harness.Paths, SessionRoles.Implementer, orchId, memberId), 50);
        var record = Assert.Single(records);

        Assert.Equal(TurnLog_Store.KIND_TURN, record[TurnLog_Store.KIND_KEY]!.GetValue<string>());
        Assert.Equal("online\n\nbody", record["result"]!.GetValue<string>());
    }
}
