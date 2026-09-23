using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Configuration;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// <c>/screens</c> THROUGH THE REAL ENGINE, WITH A config.json IT CANNOT SAVE (plan 04 Task 2c, ruling P35).
///
/// <para>
/// Until Task 2c, <c>OrchestratorConfig_Loader.Save</c> read a held config.json as EMPTY and wrote it back holding
/// only its own keys. On Windows the rename onto the held file then failed too, and the throw escaped the command:
/// the update handler logged "Handling update … failed" and the owner, who had just typed <c>/screens</c>, heard
/// nothing at all. Now Save refuses before writing anything, and the toggle answers where the owner typed that it
/// was NOT saved — decision 15 lets it reach Telegram because the owner caused it by typing.
/// </para>
/// <para>
/// Driven through the engine with the scripted client <see cref="TheInboundLoopSurvivesItsOwnBatchTests"/> uses,
/// because the claim is about what the command handler does with the throw — a unit test of the writer cannot see
/// whether the owner was answered or the update was dropped.
/// </para>
/// </summary>
public class StatusScreenshotsCommandTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 7474;

    readonly string _tempRoot;
    readonly ISupervisionPaths _paths;
    readonly IOrchestratorConfigProvider _configProvider;
    readonly RecordingLog_Fake _log = new();
    readonly ScriptedInbound_Fake _telegram = new();
    readonly IBridgeEngine _engine;

    public StatusScreenshotsCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-screens-command-{Guid.NewGuid():N}");
        var tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"somethingAHandAdded\":{{\"kept\":true}}}}");

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        var store = OrchestrationSessionStore_Factory.Create(_paths);
        _configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, _configProvider, store, new RecordingSpawner_Fake(), _log);

        _engine = BridgeEngine_Factory.Create_WithDecisionState(
            _paths, _configProvider, store, launcher, _log, _telegram,
            EngineStateStore_Factory.Create_InMemory(), new FixedClock_Fake(DateTime.UtcNow),
            BridgeTestTiming.Fast());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch (IOException)
        {
            // A temp folder that will not delete is not a test result.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// HELD: the owner is told the toggle was not saved, one warning names why, nothing throws out of the update,
    /// and config.json is byte for byte. RELEASED: the same command turns screenshots on and keeps the owner's
    /// hand-edited key.
    /// </summary>
    [RequiresExclusiveOpenEnforcementFact]
    [Trait("Speed", "Slow")]
    public async Task Screens_WithConfigJsonHeld_AnswersThatNothingWasSaved_AndWorksOnceReleased()
    {
        using var cancellation = new CancellationTokenSource();
        var loop = _engine.Run_Async(cancellation.Token);

        try
        {
            Assert.True(
                await Wait_Until_Async(() => _telegram.UpdateCalls >= 1, 15_000),
                $"the inbound loop never polled.{Environment.NewLine}{_log.Dump()}");

            // The provider is warmed before the hold, as it is in the app: it caches by write stamp.
            Assert.False(_configProvider.Get_Current().TelegramStatusScreenshots);
            var before = File.ReadAllBytes(_paths.ConfigFile);

            using (new FileStream(_paths.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                _telegram.Queue_Updates("{\"ok\":true,\"result\":[" + Message_Json("/screens", 9101, 301) + "]}");

                Assert.True(
                    await Wait_Until_Async(() => _telegram.Count_Sent_Containing("could not be saved") >= 1, 20_000),
                    $"the owner was never told the toggle was not saved.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");
            }

            Assert.Equal(1, _telegram.Count_Sent_Containing("could not be saved"));
            Assert.Equal(0, _telegram.Count_Sent_Containing("Status screenshots ON"));
            Assert.False(_log.Has_Line_Containing("Handling update 9101 failed"), _log.Dump());
            Assert.True(_log.Has_Line_Containing("WARN  [] /screens"), $"no warning line for the refused save.{Environment.NewLine}{_log.Dump()}");
            Assert.Equal(before, File.ReadAllBytes(_paths.ConfigFile));
            Assert.False(OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramStatusScreenshots);

            _telegram.Queue_Updates("{\"ok\":true,\"result\":[" + Message_Json("/screens", 9102, 302) + "]}");

            Assert.True(
                await Wait_Until_Async(() => _telegram.Count_Sent_Containing("Status screenshots ON") == 1, 20_000),
                $"the released toggle never landed.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");

            var saved = File.ReadAllText(_paths.ConfigFile);
            Assert.True(OrchestratorConfig_Loader.Load_OrEmpty(_paths).TelegramStatusScreenshots);
            Assert.Contains("somethingAHandAdded", saved, StringComparison.Ordinal);
        }
        finally
        {
            await cancellation.CancelAsync();

            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
                // The only way this loop ends.
            }
        }
    }

    static string Message_Json(string text, long updateId, long messageId)
    {
        return $"{{\"update_id\":{updateId},\"message\":{{\"message_id\":{messageId},"
            + $"\"message_thread_id\":{TOPIC_ID},\"from\":{{\"id\":{OWNER_USER_ID}}},"
            + $"\"chat\":{{\"id\":{SUPERGROUP_CHAT_ID}}},\"text\":\"{text}\"}}}}";
    }

    static async Task<bool> Wait_Until_Async(Func<bool> condition, int maxMilliseconds)
    {
        for (var waited = 0; waited < maxMilliseconds; waited += 100)
        {
            if (condition())
                return true;

            await Task.Delay(100);
        }

        return condition();
    }
}
