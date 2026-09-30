using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramApiClient;
using AIOrchestratorCoreLib.Telegram.TopicTraffic;
using AIOrchestratorCoreLib.Tests.Bridge;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// THE CHOKEPOINT (owner, 2026-09-30): every message the app SENDS into a topic is recorded as traffic by
/// the client the engine holds, so no send site can bury PULSE without the app knowing. Until then a dozen
/// sites — alerts, documents, photos, screenshots, narration, receipts — never recorded their message and
/// PULSE stayed twenty messages up.
/// </summary>
public class TopicTrafficRecordingClientTests
{
    const long TOPIC = 4242;

    readonly FailableTelegram_Fake _inner = new();
    readonly ITopicTraffic _traffic = TopicTraffic_Factory.Create_Empty();
    readonly ITelegramApiClient _client;

    public TopicTrafficRecordingClientTests()
    {
        _client = TelegramApiClient_Factory.Create_RecordingTopicTraffic(_inner, _traffic);
    }

    /// <summary>
    /// EVERY SEND THAT RETURNS AN ID is recorded under its topic, and the id is handed back untouched —
    /// the plain send, the HTML send, both replies, and the three shapes that carry buttons.
    /// </summary>
    [Fact]
    public async Task EveryIdentifiedSend_IsRecordedAsTheNewestMessage()
    {
        IReadOnlyList<(string Data, string Label)> buttons = [("d", "l")];
        IReadOnlyList<IReadOnlyList<(string Data, string Label)>> rows = [buttons];

        List<Func<Task<long?>>> sends =
        [
            () => _client.Send_Message_Async(TOPIC, "plain", TelegramSendSounds.Silent, default),
            () => _client.Send_HtmlMessage_Async(TOPIC, "html", TelegramSendSounds.Silent, default),
            () => _client.Send_HtmlReply_Async(TOPIC, "html reply", 1, TelegramSendSounds.Silent, default),
            () => _client.Send_MessageReply_Async(TOPIC, "plain reply", 1, TelegramSendSounds.Silent, default),
            () => _client.Send_HtmlMessageWithButtons_Async(TOPIC, "html buttons", buttons, TelegramSendSounds.Silent, default),
            () => _client.Send_MessageWithButtons_Async(TOPIC, "buttons", buttons, TelegramSendSounds.Silent, default),
            () => _client.Send_MessageWithButtonRows_Async(TOPIC, "rows", rows, TelegramSendSounds.Silent, default),
        ];

        foreach (var send in sends)
        {
            var id = await send();

            Assert.NotNull(id);
            Assert.Equal(id, _traffic.Find_Newest_OrAssumeBuried(TOPIC, null)?.MessageId);
        }
    }

    /// <summary>
    /// A PHOTO OR A DOCUMENT comes back with no id through this client, and it buries the line all the
    /// same: this is the path the entry photos, attachments, the screenshot and the undelivered digest take.
    /// </summary>
    [Fact]
    public async Task APhotoOrADocument_BuriesTheLine_ThoughItHasNoId()
    {
        var pulseId = await _client.Send_MessageWithButtonRows_Async(TOPIC, "PULSE", [], TelegramSendSounds.Silent, default);

        Assert.False(TopicStatusLine_Planner.Is_Buried(pulseId, _traffic.Find_Newest_OrAssumeBuried(TOPIC, pulseId)));

        await _client.Send_Photo_Async(TOPIC, "shot.png", TelegramSendSounds.Silent, default);

        Assert.True(TopicStatusLine_Planner.Is_Buried(pulseId, _traffic.Find_Newest_OrAssumeBuried(TOPIC, pulseId)));

        var repostedId = await _client.Send_MessageWithButtonRows_Async(TOPIC, "PULSE", [], TelegramSendSounds.Silent, default);

        Assert.False(TopicStatusLine_Planner.Is_Buried(repostedId, _traffic.Find_Newest_OrAssumeBuried(TOPIC, repostedId)));

        await _client.Send_Document_Async(TOPIC, "entry.md", [1, 2, 3], "caption", TelegramSendSounds.Silent, default);

        Assert.True(TopicStatusLine_Planner.Is_Buried(repostedId, _traffic.Find_Newest_OrAssumeBuried(TOPIC, repostedId)));
    }

    /// <summary>A send that THREW put nothing in the topic, so it records nothing.</summary>
    [Fact]
    public async Task AFailedSend_RecordsNothing()
    {
        _inner.Fail_Sends_Containing("doomed");

        await Assert.ThrowsAnyAsync<Exception>(() => _client.Send_Message_Async(TOPIC, "doomed", TelegramSendSounds.Silent, default));

        Assert.Null(_traffic.Find_Newest_OrAssumeBuried(TOPIC, null));
    }

    /// <summary>
    /// AN EDIT, A DELETE OR A REACTION PUTS NOTHING NEW AT THE BOTTOM, so it buries nothing — the status
    /// line's own in-place edit included, which would otherwise read as burying itself.
    /// </summary>
    [Fact]
    public async Task EditsDeletesAndReactions_BuryNothing()
    {
        var pulseId = await _client.Send_MessageWithButtonRows_Async(TOPIC, "PULSE", [], TelegramSendSounds.Silent, default);

        await _client.Edit_MessageTextWithButtonRows_Async(pulseId!.Value, "PULSE edited", [], default);
        await _client.Edit_MessageText_Async(pulseId.Value, "PULSE edited again", default);
        await _client.Set_MessageReaction_Async(pulseId.Value, "👀", default);
        await _client.Send_TypingAction_Async(TOPIC, default);

        Assert.False(TopicStatusLine_Planner.Is_Buried(pulseId, _traffic.Find_Newest_OrAssumeBuried(TOPIC, pulseId)));
    }
}
