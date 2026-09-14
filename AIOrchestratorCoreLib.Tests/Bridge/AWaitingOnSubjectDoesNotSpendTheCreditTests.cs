using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using AIOrchestratorCoreLib.Time.Clock;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE OWNER'S ANSWER CREDIT IS NOT SPENT ON A TURN-END DECLARATION — pinned where it is DECIDED,
/// which is the engine, not the predicate.
///
/// <para>
/// The credit is one-shot: raised when a message of the owner's lands in the channel, consumed by the
/// first entry the mirror pushes for them. Sessions write their turn-end declaration — "WAITING ON the
/// re-review — fix landed" — in the seconds BEFORE the actual answer, and on 2026-09-10 that
/// declaration took the credit three times in one topic; the answer that followed was filed as
/// narration and never reached the phone, and the owner re-typed their question each time.
/// </para>
/// <para>
/// WHY THIS FILE EXISTS SEPARATELY FROM <see cref="OwnerPushPolicyTests"/>. That one pins
/// <c>Is_TurnEndDeclaration</c> as a predicate, and a predicate nobody asks is worth nothing: delete
/// the <c>&amp;&amp; !OwnerPush_Policy.Is_TurnEndDeclaration(entry.Subject)</c> from the engine's
/// consumption site and every one of those cases stays green. This asserts the WIRING.
/// </para>
/// <para>
/// THE ORACLE IS THE PERSISTED SNAPSHOT (<c>OwnerAwaitingAnswer</c>), the same one
/// <see cref="StartOrchestrationCarriesTheTaskTests"/> and <see cref="DecisionStateSurvivesARestartTests"/>
/// already read. It is deliberately NOT "was the entry pushed": this build pushes everything the
/// supervisor writes (owner's ruling, 2026-09-09), so a push assertion would be true whichever way the
/// guard went and would pin nothing at all. The credit's own state is the only thing the guard moves.
/// </para>
/// <para>
/// ORDERING IS WHAT MAKES THE ASSERTIONS SAFE: each entry's text is asserted SENT first, and the
/// consumption site runs immediately after that send inside the same iteration — so by the time the
/// text is visible on the fake, the engine has already decided about the credit. Neither assertion can
/// read a snapshot taken before the decision.
/// </para>
/// </summary>
public class AWaitingOnSubjectDoesNotSpendTheCreditTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445511;
    const long OWNER_USER_ID = 555000115;
    const long TOPIC_ID = 4646;

    /// <summary>The turn-end declaration, in the shape the run-to-the-end hook accepts. Subject, not body.</summary>
    const string TURN_END_SUBJECT = "WAITING ON the task 6 re-review - fix landed 7af0aafe";

    const string STATUS_TEXT = "Task 6 fix round landed: 116 runtime tests, review running.";

    /// <summary>Carries no question mark and no marker — under this build's policy it is an ordinary entry.</summary>
    const string ANSWER_TEXT = "Agreed, three rules: every futures root carries a default roll rule.";

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly IOrchestrationLauncher _launcher;
    readonly IBridgeEngine _engine;
    readonly FailableTelegram_Fake _telegram;
    readonly RecordingLog_Fake _log;
    readonly IEngineStateStore _engineState = EngineStateStore_Factory.Create_InMemory();

    public AWaitingOnSubjectDoesNotSpendTheCreditTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-turn-end-credit-tests-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
        _log = new RecordingLog_Fake();
        _telegram = new FailableTelegram_Fake();

        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);

        // TERMINAL runners by default, and no print session is registered: the engine's print
        // dispatcher reaches for a real `claude`, which is a second subject and a second way to fail.
        _launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        _engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, configProvider, _store, _launcher, _log, _telegram,
            _engineState, Clock_Factory.Create_System(), BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AStatusLineLeavesTheCreditOpen_AndTheEntryAfterItSpendsIt()
    {
        var orchId = await Start_WithChannelAlreadySeen_Async();

        // 1 — the owner asks. The credit is raised when the message LANDS in the channel.
        _telegram.Queue_OwnerMessage(Build_OwnerMessageJson("how should futures roll by default"));

        Assert.True(
            await Run_Until_Async(() => _log.Has_Info_Containing("Owner message delivered"), BridgeTestTiming.Window_ForAggregation(60)),
            $"the owner's message was never delivered, so the credit was never raised.{Environment.NewLine}{_log.Dump()}");

        Assert.Contains(orchId, _engineState.Load_OrEmpty().OwnerAwaitingAnswer);

        // 2 — the session's boundary, in the order sessions actually write it: the turn-end
        // declaration first, the answer after it.
        Append_SupervisorEntry(orchId, 2, TURN_END_SUBJECT, STATUS_TEXT);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(STATUS_TEXT), 20_000),
            $"the status line never left, so nothing has yet been decided about the credit.{Environment.NewLine}{_log.Dump()}");

        Assert.Contains(orchId, _engineState.Load_OrEmpty().OwnerAwaitingAnswer);

        // 3 — the answer. An ordinary subject, so THIS is what the credit was for.
        Append_SupervisorEntry(orchId, 3, "defaults per root, C is the continuous automatically", ANSWER_TEXT);

        Assert.True(
            await Run_Until_Async(() => _telegram.Has_Sent_Containing(ANSWER_TEXT), 20_000),
            $"the answer never left, so the consumption site was never reached for it.{Environment.NewLine}{_log.Dump()}");

        Assert.DoesNotContain(orchId, _engineState.Load_OrEmpty().OwnerAwaitingAnswer);
    }

    void Seed_OwnerChannel(string orchId)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");
    }

    /// <summary>An unseen file is registered at its current end and emits nothing, so the channel is baselined first.</summary>
    async Task<string> Start_WithChannelAlreadySeen_Async()
    {
        var session = _launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);
        Seed_OwnerChannel(session.OrchId);

        await Run_For_Async(BridgeTestTiming.Window_ForTicks(3));

        return session.OrchId;
    }

    void Append_SupervisorEntry(string orchId, int index, string subject, string body)
    {
        var channelFile = _paths.Get_OwnerChannelFile(orchId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        File.AppendAllText(channelFile, $"\n## [{index}] FROM supervisor — {stamp} — {subject}\n{body}\n");
    }

    static string Build_OwnerMessageJson(string text)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":5001,\"message\":{\"message_id\":117,"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}]}}";
    }

    async Task Run_For_Async(int milliseconds)
    {
        await Run_Until_Async(() => false, milliseconds);
    }

    async Task<bool> Run_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = _engine.Run_Async(cancellation.Token);
        var satisfied = false;

        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
            {
                satisfied = true;
                break;
            }

            await Task.Delay(100);
        }

        await cancellation.CancelAsync();

        try
        {
            await loop;
        }
        catch (OperationCanceledException)
        {
            // The only way these loops end.
        }

        return satisfied || condition();
    }
}
