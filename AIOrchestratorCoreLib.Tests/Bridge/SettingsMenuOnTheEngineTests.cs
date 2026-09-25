using AIOrchestratorCoreLib.Configuration.SettingsCatalog;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.SettingsMenu;
using Xunit;

using Catalog = global::AIOrchestratorCoreLib.Configuration.SettingsCatalog.SettingsCatalog;

using Harness = AIOrchestratorCoreLib.Tests.Bridge.SettingsMenuEngine_Harness;

namespace AIOrchestratorCoreLib.Tests.Bridge;

/// <summary>
/// THE /settings MENU THROUGH THE REAL ENGINE (plan 04 Task 5, spec §8.2). The owner, 2026-09-23: a /settings
/// dialog in the General topic, so both they and the fork's author — headless on Linux — can change every
/// behavioural setting from the phone. Every fact drives the inbound loop the way the phone does (a typed
/// command, a tap on a payload) and reads back what the phone would show and what config.json holds.
/// </summary>
public class SettingsMenuOnTheEngineTests : IDisposable
{
    /// <summary>A Phone toggle — no Kernel confirm between the tap and the write.</summary>
    const string TOGGLE_PATH = "phone.appMessagesRing";

    readonly Harness _harness = new("settings-menu");

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SlashSettings_InGeneral_SendsOneMessageWithTheCategories()
    {
        IReadOnlyList<(long Id, string Text, TelegramSendSounds Sound)> sent = [];

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            var before = _harness.Telegram.Sent_WithIds.Count;

            await _harness.Owner_Types_Async("/settings", threadId: null);
            sent = _harness.Sent_Since(before);
        });

        var menu = Assert.Single(sent);
        var shown = _harness.Telegram.Current_Of_OrNull(menu.Id)!.Value;

        Assert.Contains(Harness.MENU_HEADER, shown.Text, StringComparison.Ordinal);
        Assert.Contains("Kernel (", shown.Labels, StringComparison.Ordinal);
        Assert.Contains("Phone (", shown.Labels, StringComparison.Ordinal);
        Assert.Equal(0, _harness.Count_RoutedMessages());
    }

    /// <summary>
    /// D3: machine settings live in General only. In an orchestration topic the command answers with THAT
    /// orchestration's own rows, read-only — no keyboard at all — and points at the dials that change them.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SlashSettings_InAnOrchestrationTopic_AnswersWithThatOrchestrationsStateAndPointsAtTheDials()
    {
        var configBefore = File.ReadAllText(_harness.Paths.ConfigFile);
        IReadOnlyList<(long Id, string Text, TelegramSendSounds Sound)> sent = [];

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            var before = _harness.Telegram.Sent_WithIds.Count;

            await _harness.Owner_Types_Async("/settings", Harness.TOPIC_ID);
            sent = _harness.Sent_Since(before);
        });

        var answer = Assert.Single(sent);
        var shown = _harness.Telegram.Current_Of_OrNull(answer.Id)!.Value;

        Assert.Contains("This orchestration's settings", shown.Text, StringComparison.Ordinal);
        Assert.All(
            Catalog.ALL.Where(definition => definition.Path.StartsWith("session.", StringComparison.Ordinal)),
            definition => Assert.Contains(definition.Label, shown.Text, StringComparison.Ordinal));
        Assert.Contains("/model", shown.Text, StringComparison.Ordinal);
        Assert.Contains("/effort", shown.Text, StringComparison.Ordinal);
        Assert.Equal("", shown.Labels);
        Assert.DoesNotContain(Harness.MENU_HEADER, shown.Text, StringComparison.Ordinal);
        Assert.Equal(configBefore, File.ReadAllText(_harness.Paths.ConfigFile));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATapOnACategory_EDITS_TheSameMessage_AndDoesNotSendASecondOne()
    {
        long menuId = 0;
        IReadOnlyList<(long Id, string Text, TelegramSendSounds Sound)> sentAfterTheTap = [];

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await _harness.Owner_Types_Async("/settings", threadId: null);
            menuId = _harness.Live_MenuMessageId();

            var before = _harness.Telegram.Sent_WithIds.Count;

            await _harness.Owner_Taps_Async(SettingsButton_Data.Build(SettingsMenuViews.Category, SettingCategories.Phone, null, 0, null, null), menuId);
            sentAfterTheTap = _harness.Sent_Since(before);
        });

        Assert.Empty(sentAfterTheTap);
        Assert.Contains(_harness.Telegram.ButtonEdits, edit => edit.MessageId == menuId && edit.Text.Contains("Phone", StringComparison.Ordinal));
        Assert.Equal(1, _harness.Telegram.Answered_Callbacks);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATapOnAToggle_WritesTheKey_AndTheEditedMenuShowsTheNewValueAndItsNewOrigin()
    {
        long menuId = 0;

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await _harness.Owner_Types_Async("/settings", threadId: null);
            menuId = _harness.Live_MenuMessageId();

            await _harness.Owner_Taps_Async(Harness.Payload(SettingsMenuViews.Setting, TOGGLE_PATH, SettingsMenuEdits.Set, "off"), menuId);
        });

        Assert.Equal(false, _harness.Read_ConfigValue_OrNull(TOGGLE_PATH)?.GetValue<bool>());

        var shown = _harness.Telegram.Current_Of_OrNull(menuId)!.Value.Text;

        Assert.Contains("Now: off — set here", shown, StringComparison.Ordinal);
        Assert.Contains("Saved.", shown, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATapOnReset_DeletesTheKey_AndTheMenuShowsThePresetOrShippedValueAgain()
    {
        using var harness = new Harness("settings-reset", ",\"phone\":{\"appMessagesRing\":false}");
        long menuId = 0;

        await harness.Run_WhileAsync(harness.Build_Engine(), async () =>
        {
            await harness.Owner_Types_Async("/settings", threadId: null);
            menuId = harness.Live_MenuMessageId();

            await harness.Owner_Taps_Async(Harness.Payload(SettingsMenuViews.Setting, TOGGLE_PATH, SettingsMenuEdits.Reset, null), menuId);
        });

        Assert.Null(harness.Read_ConfigValue_OrNull(TOGGLE_PATH));
        Assert.Contains("Now: on — shipped default", harness.Telegram.Current_Of_OrNull(menuId)!.Value.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// D2 (b): the three keys that decide how this menu reaches the owner are refused on the phone — and the
    /// refusal is re-asked on the READING before any write, so a forged or stale payload that names one changes
    /// nothing, byte for byte, and the owner is told why.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ATapOnAFencedKernelKey_ChangesNothing_AndAnswersWhy()
    {
        var configBefore = File.ReadAllText(_harness.Paths.ConfigFile);

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await _harness.Owner_Types_Async("/settings", threadId: null);

            await _harness.Owner_Taps_Async(Harness.Payload(SettingsMenuViews.Setting, "telegramInbound", SettingsMenuEdits.Set, "off"), _harness.Live_MenuMessageId());
        });

        Assert.Equal(configBefore, File.ReadAllText(_harness.Paths.ConfigFile));
        Assert.Contains(SettingsMenu_Builder.PHONE_FENCE_NOTE, _harness.Telegram.Answered_CallbackTexts);
    }

    /// <summary>A settings menu is not news: every message this feature sends goes out silent, in General and in a topic.</summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task EverySettingsMessage_IsSilent()
    {
        IReadOnlyList<(long Id, string Text, TelegramSendSounds Sound)> sent = [];

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            var before = _harness.Telegram.Sent_WithIds.Count;

            await _harness.Owner_Types_Async("/settings", threadId: null);
            await _harness.Owner_Types_Async("/settings", Harness.TOPIC_ID);
            await _harness.Owner_Taps_Async(Harness.Payload(SettingsMenuViews.Setting, "phone.status.intervalMinutes", SettingsMenuEdits.Reply, null), _harness.Live_MenuMessageId());
            await _harness.Owner_Types_Async("500", threadId: null);
            await _harness.Owner_Types_Async("45", threadId: null);
            await _harness.Owner_Types_Async("/settings", threadId: null);

            sent = _harness.Sent_Since(before);
        });

        Assert.True(sent.Count >= 5, $"expected at least five settings messages, got {sent.Count}");
        Assert.All(sent, message => Assert.Equal(TelegramSendSounds.Silent, message.Sound));
    }

    /// <summary>
    /// FIVE TAPS IN FIVE SECONDS ALL LAND (D7). MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE is 30 seconds and
    /// Hold_UnlessThisMessageMayBeEdited THROWS TelegramHeldException rather than sleeping, so without the
    /// exemption taps two through five are swallowed by a rate limit measured against a background edit loop
    /// — a different traffic shape entirely. This test is the one that proves the exemption is narrow AND
    /// that it works. The fake's edits pass through the REAL gate on a budget with production's own gap.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task FiveRapidTaps_AllTakeEffect_BecauseTheLiveMenuIsExemptFromThePerMessageGap()
    {
        long menuId = 0;
        IReadOnlyList<(long Id, string Text, TelegramSendSounds Sound)> sentAfterTheTaps = [];
        var started = DateTime.UtcNow;

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await _harness.Owner_Types_Async("/settings", threadId: null);
            menuId = _harness.Live_MenuMessageId();

            var before = _harness.Telegram.Sent_WithIds.Count;

            started = DateTime.UtcNow;
            await _harness.Owner_Taps_Async(SettingsButton_Data.Build(SettingsMenuViews.Category, SettingCategories.Phone, null, 0, null, null), menuId);
            await _harness.Owner_Taps_Async(SettingsButton_Data.Build(SettingsMenuViews.Category, SettingCategories.Kernel, null, 0, null, null), menuId);
            await _harness.Owner_Taps_Async(SettingsButton_Data.Build(SettingsMenuViews.Category, SettingCategories.Kernel, null, 1, null, null), menuId);
            await _harness.Owner_Taps_Async(Harness.Payload(SettingsMenuViews.Setting, TOGGLE_PATH, null, null), menuId);
            await _harness.Owner_Taps_Async(SettingsButton_Data.Build(SettingsMenuViews.Categories, null, null, 0, null, null), menuId);

            sentAfterTheTaps = _harness.Sent_Since(before);
        });

        Assert.True(DateTime.UtcNow - started < TokenBucket_Gate.MINIMUM_GAP_BETWEEN_EDITS_OF_ONE_MESSAGE, "the five taps took longer than the gap itself — this run proves nothing about it");
        Assert.Equal(5, _harness.Telegram.ButtonEdits.Count(edit => edit.MessageId == menuId));
        Assert.Equal(0, _harness.Telegram.Count_HeldEdits_Of(menuId));
        Assert.Empty(sentAfterTheTaps);
    }

    /// <summary>
    /// THE EXEMPTION DIES WITH ITS MENU (D7). A menu replaced by a fresh /settings is an ordinary message again:
    /// the same real gate that let the live menu take edit after edit now owes it the rest of its thirty
    /// seconds, while the menu that replaced it takes two edits in a row.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task AMessageThatIsNotTheLiveMenu_StillGetsTheThirtySecondGap()
    {
        long firstMenu = 0, secondMenu = 0;

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await _harness.Owner_Types_Async("/settings", threadId: null);
            firstMenu = _harness.Live_MenuMessageId();

            await _harness.Owner_Taps_Async(SettingsButton_Data.Build(SettingsMenuViews.Category, SettingCategories.Phone, null, 0, null, null), firstMenu);

            await _harness.Owner_Types_Async("/settings", threadId: null);
            secondMenu = _harness.Live_MenuMessageId();
        });

        Assert.NotEqual(firstMenu, secondMenu);

        var now = DateTime.UtcNow;

        Assert.True(_harness.Budget.Reserve_MessageEdit(firstMenu, now) > TimeSpan.Zero, "the replaced menu kept its exemption — a permanently un-gapped message id");
        Assert.Equal(TimeSpan.Zero, _harness.Budget.Reserve_MessageEdit(secondMenu, now));
        Assert.Equal(TimeSpan.Zero, _harness.Budget.Reserve_MessageEdit(secondMenu, now.AddSeconds(1)));
    }

    /// <summary>
    /// The status line's own fallback shape: an edit Telegram refuses is not the end of the menu. The view the
    /// owner asked for is reposted as a new message, and that message is the live menu from then on.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task WhenTheEditIsRefusedAnyway_TheMenuRepostsRatherThanVanishing()
    {
        long refusedMenu = 0, repostedMenu = 0;

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await _harness.Owner_Types_Async("/settings", threadId: null);
            refusedMenu = _harness.Live_MenuMessageId();
            _harness.Telegram.Refuse_Edits_Of(refusedMenu);

            await _harness.Owner_Taps_Async(SettingsButton_Data.Build(SettingsMenuViews.Category, SettingCategories.Phone, null, 0, null, null), refusedMenu);
            repostedMenu = _harness.Live_MenuMessageId();

            await _harness.Owner_Taps_Async(SettingsButton_Data.Build(SettingsMenuViews.Category, SettingCategories.Pulse, null, 0, null, null), repostedMenu);
        });

        Assert.NotEqual(refusedMenu, repostedMenu);
        Assert.Contains("Phone", _harness.Telegram.Sent_WithIds.Single(sent => sent.Id == repostedMenu).Text, StringComparison.Ordinal);
        Assert.Contains(_harness.Telegram.ButtonEdits, edit => edit.MessageId == repostedMenu && edit.Text.Contains("Pulse", StringComparison.Ordinal));
    }

    /// <summary>
    /// A TAP ON THE SETTINGS MENU NEVER BECOMES A SYNTHETIC OWNER MESSAGE. That is exactly what decision 24
    /// records happening to the dials through the generic opt- path, and the handler order is the only thing
    /// that prevents it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ASettingsTap_IsHandledBeforeTheGenericOptionPath_AndReachesNoChannel()
    {
        var ownerChannelBefore = File.ReadAllText(_harness.Paths.Get_OwnerChannelFile(_harness.OrchId));

        await _harness.Run_WhileAsync(_harness.Build_Engine(), async () =>
        {
            await _harness.Owner_Types_Async("/settings", threadId: null);

            var menuId = _harness.Live_MenuMessageId();

            await _harness.Owner_Taps_Async(SettingsButton_Data.Build(SettingsMenuViews.Category, SettingCategories.Phone, null, 0, null, null), menuId);
            await _harness.Owner_Taps_Async("set:v:Kernel:9999:beef:0", menuId);
        });

        Assert.Equal(0, _harness.Count_RoutedMessages());
        Assert.Equal(ownerChannelBefore, File.ReadAllText(_harness.Paths.Get_OwnerChannelFile(_harness.OrchId)));
        Assert.False(File.Exists(_harness.Paths.GeneralChannelFile) && File.ReadAllText(_harness.Paths.GeneralChannelFile).Contains("Phone", StringComparison.Ordinal));
        Assert.False(_harness.Log.Has_Line_Containing("Callback REFUSED"), _harness.Log.Dump());
        Assert.Contains(SettingsButton_Data.STALE_ANSWER, _harness.Telegram.Answered_CallbackTexts);
    }
}
