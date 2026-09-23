using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Bridge.EngineState;
using AIOrchestratorCoreLib.Telegram.SettingsMenu;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;
using Harness = AIOrchestratorCoreLib.Tests.Bridge.SettingsMenuEngine_Harness;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// D9 — THE "REPLY WITH THE VALUE" STEP, the sharpest rules in plan 04 Task 5. It is the one piece of this
/// feature that can take an owner's message away from the conversation it was meant for, so each rule that
/// bounds it — the window, the cancel, any other command, the topic, the restart, the read-back's precedence —
/// is pinned here through the real inbound loop.
/// </summary>
public class SettingsReplyStepTests : IDisposable
{
    /// <summary>A Phone number (5 – 120): no Kernel confirm, so a typed value is written on arrival.</summary>
    const string NUMBER_PATH = "phone.status.intervalMinutes";

    /// <summary>A Kernel number: its typed value is HELD and confirmed with a second tap (ruling P6).</summary>
    const string KERNEL_NUMBER_PATH = "buttonExpiryMinutes";

    readonly Harness _harness = new("settings-reply-step");

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APromptedNumber_IsTakenAsTheValue_Validated_AndWritten()
    {
        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(NUMBER_PATH);
            await _harness.Owner_Types_Async("45", threadId: null);
        });

        Assert.Equal(45, _harness.Read_ConfigValue_OrNull(NUMBER_PATH)?.GetValue<long>());
        Assert.Contains("Now: 45 — set here", _harness.Telegram.Current_Of_OrNull(_harness.Live_MenuMessageId())!.Value.Text, StringComparison.Ordinal);
        Assert.Equal(0, _harness.Count_RoutedMessages());
    }

    /// <summary>
    /// The Kernel half of the same rule: the typed value is validated, HELD, and drawn as a Confirm; only its Yes
    /// writes it — and the Yes carries no value, only the id (ruling P6).
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AKernelValue_IsHeldUntilItsConfirm_AndOnlyTheYesWritesIt()
    {
        string? heldBeforeTheYes = null;
        JsonNode? writtenBeforeTheYes = null;

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(KERNEL_NUMBER_PATH);
            await _harness.Owner_Types_Async("90", threadId: null);

            writtenBeforeTheYes = _harness.Read_ConfigValue_OrNull(KERNEL_NUMBER_PATH);
            heldBeforeTheYes = _harness.EngineState.Load_OrEmpty().SettingsReplySteps.SingleOrDefault()?.HeldText_OrNull;

            Assert.Contains("Change it to 90?", _harness.Telegram.Current_Of_OrNull(_harness.Live_MenuMessageId())!.Value.Text, StringComparison.Ordinal);

            await _harness.Owner_Taps_Async(Harness.Payload(SettingsMenuViews.Setting, KERNEL_NUMBER_PATH, SettingsMenuEdits.ApplyHeldReply, null), _harness.Live_MenuMessageId());
        });

        Assert.Null(writtenBeforeTheYes);
        Assert.Equal("90", heldBeforeTheYes);
        Assert.Equal(90, _harness.Read_ConfigValue_OrNull(KERNEL_NUMBER_PATH)?.GetValue<long>());
        Assert.Empty(_harness.EngineState.Load_OrEmpty().SettingsReplySteps);
    }

    /// <summary>Decision 21: the refusal is the DEFINITION's, relayed word for word; nothing is written; the step stays for another try.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task APromptedValueTheCatalogueRefuses_IsNotWritten_AndTheOwnerIsToldTheCataloguesReason()
    {
        var reason = Catalog.Find_OrNull(NUMBER_PATH)!.Validate_OrNull(JsonValue.Create(500L));
        var before = 0;

        Assert.NotNull(reason);

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(NUMBER_PATH);

            before = _harness.Telegram.Sent_WithIds.Count;
            await _harness.Owner_Types_Async("500", threadId: null);
        });

        Assert.Null(_harness.Read_ConfigValue_OrNull(NUMBER_PATH));
        Assert.Contains(_harness.Sent_Since(before), sent => sent.Text.Contains(reason!, StringComparison.Ordinal));
        Assert.Single(_harness.EngineState.Load_OrEmpty().SettingsReplySteps);
        Assert.Equal(0, _harness.Count_RoutedMessages());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMessageStartingWithASlash_CancelsTheStep_AndIsHandledAsTheCommandItIs()
    {
        var sentForTheCommand = 0;

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(NUMBER_PATH);

            var before = _harness.Telegram.Sent_WithIds.Count;
            await _harness.Owner_Types_Async("/pending", threadId: null);
            sentForTheCommand = _harness.Sent_Since(before).Count;

            await _harness.Owner_Types_Async("45", threadId: null);
        });

        Assert.True(sentForTheCommand > 0, $"/pending was not answered.{Environment.NewLine}{_harness.Log.Dump()}");
        Assert.Empty(_harness.EngineState.Load_OrEmpty().SettingsReplySteps);
        Assert.Null(_harness.Read_ConfigValue_OrNull(NUMBER_PATH));
        Assert.Equal(1, _harness.Count_RoutedMessages());
    }

    /// <summary>Ruling P15: <c>/cancel</c> is consumed while a step is live — answered, never routed — and the step is gone.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SlashCancel_ClearsTheStep_AndSaysSo()
    {
        var before = 0;

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(NUMBER_PATH);

            before = _harness.Telegram.Sent_WithIds.Count;
            await _harness.Owner_Types_Async("/cancel", threadId: null);
        });

        Assert.Contains(_harness.Sent_Since(before), sent => sent.Text.Contains("nothing was changed", StringComparison.Ordinal));
        Assert.Empty(_harness.EngineState.Load_OrEmpty().SettingsReplySteps);
        Assert.Equal(0, _harness.Count_RoutedMessages());
    }

    /// <summary>
    /// AN EXPIRED STEP MUST NOT EAT THE OWNER'S MESSAGE. A settings prompt left open five minutes ago and
    /// forgotten, swallowing a message meant for the supervisor, is the worst failure this feature can have:
    /// the message is gone, nothing says so, and the owner is waiting for an answer to something that never
    /// arrived.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AfterTheWindowPasses_TheNextMessage_RoutesToTheSupervisorNormally()
    {
        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(NUMBER_PATH);

            _harness.Clock.Advance(TimeSpan.FromMinutes(SettingsReplyStep_Decider.EXPIRY_MINUTES) + TimeSpan.FromSeconds(1));

            await _harness.Owner_Types_Async("45", threadId: null);
        });

        Assert.Equal(1, _harness.Count_RoutedMessages());
        Assert.Null(_harness.Read_ConfigValue_OrNull(NUMBER_PATH));
        Assert.Empty(_harness.EngineState.Load_OrEmpty().SettingsReplySteps);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WhileTheStepIsLive_TheSwallowedMessage_IsAcknowledgedInTheTopic_NeverSilently()
    {
        var before = 0;

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(NUMBER_PATH);

            before = _harness.Telegram.Sent_WithIds.Count;
            await _harness.Owner_Types_Async("45", threadId: null);
        });

        var answer = Assert.Single(_harness.Sent_Since(before));

        Assert.Contains("Saved.", answer.Text, StringComparison.Ordinal);
        Assert.Contains(Catalog.Find_OrNull(NUMBER_PATH)!.Label, answer.Text, StringComparison.Ordinal);
        Assert.Equal(0, _harness.Count_RoutedMessages());
    }

    /// <summary>
    /// Ruling P23: the step is persisted with the engine state, so a restart neither drops it silently nor
    /// restarts its window — it survives with its ORIGINAL deadline, and the next engine takes the value.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task TheStep_SurvivesAnAppRestart_OrIsClearedByIt_ButIsNeverInvisible()
    {
        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () => await Open_Prompt_Async(NUMBER_PATH));

        var persisted = Assert.Single(_harness.EngineState.Load_OrEmpty().SettingsReplySteps);
        var expectedDeadline = _harness.Clock.UtcNow.AddMinutes(SettingsReplyStep_Decider.EXPIRY_MINUTES);

        Assert.Equal(NUMBER_PATH, persisted.Path);
        Assert.InRange(persisted.ExpiresUtc, expectedDeadline.AddSeconds(-1), expectedDeadline);

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            Assert.Equal(persisted.ExpiresUtc, Assert.Single(_harness.EngineState.Load_OrEmpty().SettingsReplySteps).ExpiresUtc);

            await _harness.Owner_Types_Async("45", threadId: null);
        });

        Assert.Equal(45, _harness.Read_ConfigValue_OrNull(NUMBER_PATH)?.GetValue<long>());
        Assert.Equal(0, _harness.Count_RoutedMessages());
    }

    /// <summary>D9: per (chat, topic). A prompt open in General never takes a message typed in an orchestration's topic.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AStepInGeneral_DoesNotSwallowAMessageTypedInAnOrchestrationTopic()
    {
        JsonNode? writtenAfterTheTopicMessage = null;

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(NUMBER_PATH);

            await _harness.Owner_Types_Async("45", Harness.TOPIC_ID);
            writtenAfterTheTopicMessage = _harness.Read_ConfigValue_OrNull(NUMBER_PATH);

            await _harness.Owner_Types_Async("45", threadId: null);
        });

        Assert.Null(writtenAfterTheTopicMessage);
        Assert.Equal(1, _harness.Count_RoutedMessages());
        Assert.Equal(45, _harness.Read_ConfigValue_OrNull(NUMBER_PATH)?.GetValue<long>());
    }

    /// <summary>
    /// Ruling P23: the high-risk read-back is asked FIRST. A live code typed while a settings step is also live is
    /// the code — it confirms the decision — and the step stays pending, untouched, for the value that follows.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AReadBackCode_WinsOverALiveStep_AndTheStepStaysPending()
    {
        const string CODE = "4821";

        _harness.EngineState.Save(_harness.EngineState.Load_OrEmpty() with
        {
            PendingConfirmations =
            [
                new PendingConfirmationRecord
                {
                    Code = CODE,
                    ThreadId = null,
                    OrchId = AIOrchestratorCoreLib.Channels.ChannelDiscovery.GENERAL_ORCH_ID,
                    OptionText = "Push it",
                    QuestionText = "Push the branch?",
                    ExpiresUtc = _harness.Clock.UtcNow.AddMinutes(10),
                },
            ],
        });

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(NUMBER_PATH);
            await _harness.Owner_Types_Async(CODE, threadId: null);
        });

        Assert.True(_harness.Log.Has_Line_Containing("CONFIRMED by read-back code"), _harness.Log.Dump());
        Assert.Null(_harness.Read_ConfigValue_OrNull(NUMBER_PATH));
        Assert.Equal(NUMBER_PATH, Assert.Single(_harness.EngineState.Load_OrEmpty().SettingsReplySteps).Path);
    }

    /// <summary>
    /// AN APP-COMPOSED MESSAGE IS NEVER A VALUE (fix round 1 of d63cd76). /summary typed in an orchestration's
    /// topic becomes a canned request for the GENERAL supervisor — thread null, app-composed — and a live step in
    /// General took it as the setting's value: the summary never reached the general supervisor, and the canned
    /// sentence was offered to the catalogue as a number. <c>Build_GeneralCommandMessage</c>'s own doc records
    /// the tree paying for this class once already.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASummaryTypedInATopic_IsNeverTakenAsTheValueOfAStepInGeneral()
    {
        var configBefore = "";

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(NUMBER_PATH);
            configBefore = File.ReadAllText(_harness.Paths.ConfigFile);

            await _harness.Owner_Types_Async("/summary", Harness.TOPIC_ID);
        });

        Assert.Equal(1, _harness.Count_RoutedMessages());
        Assert.Equal(configBefore, File.ReadAllText(_harness.Paths.ConfigFile));
        Assert.Equal(NUMBER_PATH, Assert.Single(_harness.EngineState.Load_OrEmpty().SettingsReplySteps).Path);
    }

    /// <summary>
    /// A TYPED web.token HELD FOR ITS CONFIRM IS NEVER WRITTEN TO THE ENGINE-STATE FILE (fix round 1 of d63cd76).
    /// The held step is dropped from what is persisted — a restart then answers the Yes with "tap Reply again"
    /// rather than keeping the secret on disk — and nothing was written to config.json either.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AHeldSecret_IsNeverPersistedWithTheEngineState()
    {
        const string SENTINEL = "tok-SENTINEL-7f3a";

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async("web.token");
            await _harness.Owner_Types_Async(SENTINEL, threadId: null);

            Assert.Contains("It is a secret", _harness.Telegram.Current_Of_OrNull(_harness.Live_MenuMessageId())!.Value.Text, StringComparison.Ordinal);
        });

        var persisted = _harness.EngineState.Load_OrEmpty();

        Assert.Empty(persisted.SettingsReplySteps);
        Assert.DoesNotContain(SENTINEL, EngineState_Serializer.To_Json(persisted), StringComparison.Ordinal);
        Assert.DoesNotContain(SENTINEL, File.ReadAllText(_harness.Paths.ConfigFile), StringComparison.Ordinal);
        Assert.DoesNotContain(SENTINEL, _harness.Log.Dump(), StringComparison.Ordinal);
        Assert.DoesNotContain(_harness.Telegram.Sent_WithIds, sent => sent.Text.Contains(SENTINEL, StringComparison.Ordinal));
    }

    /// <summary>
    /// A VALUE THAT WAS SAVED IS NEVER REPORTED AS DROPPED (fix round 1 of d63cd76). When the menu that would show
    /// the result cannot be posted, the write has already happened: the owner is still told, and the log says the
    /// value was saved — never the inbound loop's "this one message is dropped".
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WhenTheResultMenuCannotBePosted_TheSavedValueIsStillAnswered_AndNotLoggedAsDropped()
    {
        var before = 0;

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await Open_Prompt_Async(NUMBER_PATH);

            _harness.Telegram.Fail_Sends_Containing(Harness.MENU_HEADER);
            before = _harness.Telegram.Sent_WithIds.Count;

            await _harness.Owner_Types_Async("45", threadId: null);
        });

        Assert.Equal(45, _harness.Read_ConfigValue_OrNull(NUMBER_PATH)?.GetValue<long>());
        Assert.Contains(_harness.Sent_Since(before), sent => sent.Text.Contains("Saved.", StringComparison.Ordinal));
        Assert.False(_harness.Log.Has_Line_Containing("this one message is dropped"), _harness.Log.Dump());
        Assert.Equal(0, _harness.Count_RoutedMessages());
    }

    /// <summary>/settings in General, then the setting's ✎ Reply — the prompt is up and the step is live.</summary>
    async Task Open_Prompt_Async(string path)
    {
        await _harness.Owner_Types_Async("/settings", threadId: null);
        await _harness.Owner_Taps_Async(Harness.Payload(SettingsMenuViews.Setting, path, SettingsMenuEdits.Reply, null), _harness.Live_MenuMessageId());

        Assert.Contains("Reply with the new value", _harness.Telegram.ButtonEdits.Last().Text, StringComparison.Ordinal);
    }
}
