using AIOrchestratorCoreLib.Bridge.BridgeEngine;
using AIOrchestratorCoreLib.Configuration.OrchestratorConfigProvider;
using AIOrchestratorCoreLib.Launching.OrchestrationLauncher;
using AIOrchestratorCoreLib.Sessions.OrchestrationSessionStore;
using AIOrchestratorCoreLib.SupervisionPaths;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Tests.Launching;
using AIOrchestratorCoreLib.Tests.TestSupport;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// A HOT EDIT OF <c>general.buttons</c> REACHES THE PHONE, though the dashboard's text does not move
/// (plan 03 Task 5, review finding 2026-09-23). The catalogue registers the key as needing no restart,
/// and the General dashboard decided whether to repaint on its TEXT alone: the composer puts no clock in
/// that text, so on a quiet machine a removed button stayed up and an added one never showed. PULSE had
/// already learned this lesson — it decides on <see cref="TopicStatusLine_RenderKey"/>, text and bar
/// together — and the dashboard now decides on the same key.
///
/// <para>
/// THE TEXT IS PROVEN STILL FIRST. Several ticks pass after the post with no edit of the dashboard at
/// all, and the edit that follows the config change carries the identical text — so the repaint can
/// only have come from the bar. Without that, a dashboard whose text happened to move would pass this
/// test with the render key removed: two routes to one green, which decision 20 forbids.
/// </para>
/// </summary>
public class AChangedGeneralBarRepaintsTheDashboardTests : IDisposable
{
    const long SUPERGROUP_CHAT_ID = -1002233445566;
    const long OWNER_USER_ID = 555000111;
    const long TOPIC_ID = 4242;

    readonly string _tempRoot;
    readonly string _tempRepo;
    readonly ISupervisionPaths _paths;
    readonly IOrchestrationSessionStore _store;
    readonly ScriptedInbound_Fake _telegram = new();
    readonly RecordingLog_Fake _log = new();

    public AChangedGeneralBarRepaintsTheDashboardTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"aiorch-general-bar-repaint-{Guid.NewGuid():N}");
        _tempRepo = Path.Combine(_tempRoot, "repo");
        Directory.CreateDirectory(_tempRepo);

        _paths = SupervisionPaths_Factory.Create(_tempRoot);
        Directory.CreateDirectory(_paths.RequestsFolder);

        File.WriteAllText(_paths.SecretsFile, "{\"telegramBotToken\":\"test-token\"}");

        _store = OrchestrationSessionStore_Factory.Create(_paths);
    }

    public void Dispose()
    {
        TempTree.Delete_BestEffort(_tempRoot);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Added and removed buttons both — "[]" is the D4 half: an emptied list must take the old bar OFF
    /// the message (an edit with no rows, which the client sends as no reply_markup), not leave it up.
    /// </summary>
    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData("[\"summary\",\"limits\",\"dnd_all\"]", "📊 /summary | 📉 /limits | 🌙 /dnd_all", 3)]
    [InlineData("[]", "", 0)]
    public async Task AnEditOfGeneralButtons_RepaintsTheDashboard_ThoughItsTextDidNotMove(
        string editedButtonsJson, string expectedLabels, int expectedButtonCount)
    {
        Write_Config("[\"summary\",\"pending\"]", stampAheadSeconds: null);

        var engine = Build_Engine();
        string? postedText = null;

        await Run_WhileAsync(engine, async () =>
        {
            Assert.True(
                await Wait_Until_Async(() => _telegram.Find_SentContaining(GeneralDashboard_Composer.HEADING) != null, 25_000),
                $"the General dashboard was never posted.{Environment.NewLine}{_telegram.Dump_Sent()}{Environment.NewLine}{_log.Dump()}");

            postedText = _telegram.Find_SentContaining(GeneralDashboard_Composer.HEADING);

            // THE TEXT IS STILL: ticks pass and the dashboard is not edited, so what follows is the bar's.
            await Wait_Until_Async(() => false, BridgeTestTiming.Window_ForTicks(10));

            Assert.True(
                Dashboard_Edits().Count == 0,
                $"the dashboard was edited before the config changed, so this probe cannot tell a bar repaint from a text one.{Environment.NewLine}"
                + string.Join(Environment.NewLine, Dashboard_Edits().Select(edit => edit.Text)));

            // AHEAD OF NOW, so the provider's write-stamp check sees a new file whatever the clock's
            // resolution — the same move OrchestratorConfigProviderTests makes.
            Write_Config(editedButtonsJson, stampAheadSeconds: 2);

            Assert.True(
                await Wait_Until_Async(() => Dashboard_Edits().Count > 0, 20_000),
                $"general.buttons changed and the dashboard was never repainted — the old bar is still on the phone.{Environment.NewLine}{_log.Dump()}");
        });

        var edit = Dashboard_Edits()[^1];

        Assert.Equal(postedText, edit.Text);
        Assert.Equal(expectedLabels, edit.Labels);
        Assert.Equal(expectedButtonCount, edit.ButtonCount);

        // EDITED IN PLACE, not re-posted: one dashboard message, ever.
        Assert.Equal(1, _telegram.Count_Sent_Containing(GeneralDashboard_Composer.HEADING));
        Assert.Single(Dashboard_Edits().Select(dashboardEdit => dashboardEdit.MessageId).Distinct());
    }

    IReadOnlyList<(long MessageId, string Text, int ButtonCount, string Labels)> Dashboard_Edits()
    {
        return [.. _telegram.ButtonEdits.Where(edit => edit.Text.Contains(GeneralDashboard_Composer.HEADING, StringComparison.Ordinal))];
    }

    void Write_Config(string generalButtonsJson, int? stampAheadSeconds)
    {
        File.WriteAllText(
            _paths.ConfigFile,
            $"{{\"repos\":[],\"telegramSupergroupChatId\":{SUPERGROUP_CHAT_ID},"
            + $"\"telegramOwnerUserId\":{OWNER_USER_ID},\"general\":{{\"buttons\":{generalButtonsJson}}}}}");

        if (stampAheadSeconds != null)
            File.SetLastWriteTimeUtc(_paths.ConfigFile, DateTime.UtcNow.AddSeconds(stampAheadSeconds.Value));
    }

    IBridgeEngine Build_Engine()
    {
        var configProvider = OrchestratorConfigProvider_Factory.Create(_paths);
        var launcher = OrchestrationLauncher_Factory.Create(_paths, configProvider, _store, new RecordingSpawner_Fake(), _log);

        var session = launcher.Start_Orchestration("Repo", _tempRepo);
        _store.Set_TelegramTopicId(session.OrchId, TOPIC_ID);

        var channelFile = _paths.Get_OwnerChannelFile(session.OrchId);

        if (!File.Exists(channelFile))
            File.WriteAllText(channelFile, "# OWNER CHANNEL\n\n---\n");

        return BridgeEngine_Factory.Create_WithTelegramClient(_paths, configProvider, _store, launcher, _log, _telegram, BridgeTestTiming.Fast());
    }

    static async Task Run_WhileAsync(IBridgeEngine engine, Func<Task> body)
    {
        using var cancellation = new CancellationTokenSource();

        var loop = engine.Run_Async(cancellation.Token);

        try
        {
            await body();
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
                // The only way these loops end.
            }
        }
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
