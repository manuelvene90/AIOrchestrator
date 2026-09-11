using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// NOTHING FROM ANOTHER CHAT IS ACTED ON — brief F6.
///
/// <para>
/// Named for its SUBJECT (<c>TelegramUpdates_Parser</c>, chat filtering) rather than as a scenario
/// sentence: the conventions call the scenario-sentence class names in <c>Bridge/</c> a drift to
/// fix when passing, not to imitate, and every other file in this folder is <c>&lt;Subject&gt;Tests</c>.
/// </para>
///
/// <para>
/// The owner-message parser has filtered on <c>chat.id</c> since it was written. The other two
/// paths never did: a button tap was accepted on the strength of <c>from.id</c> alone, and a
/// <c>forum_topic_edited</c> service message on no check at all.
/// </para>
/// <para>
/// THE SERVICE-MESSAGE ONE IS THE SHARP END, because its output is fed to <c>deleteMessage</c>. A
/// bot added to any other group — which is one tap by any admin there — made this bridge attempt a
/// delete in that group every time someone renamed a topic in it. It would usually fail for want
/// of rights, and that is not a defence: an unauthorised write that is refused is still one that
/// was attempted, and nothing here would ever have said so.
/// </para>
/// <para>
/// The tap one is quieter but the same shape. "From the owner" says WHO, not WHERE, and the owner
/// is a person who is in other chats. A foreign tap normally carries a payload this app never
/// minted, so it resolves to nothing — but that is a property of the button registry, not a
/// boundary, and the tap is answered and logged as the owner's before anything looks at it.
/// </para>
/// </summary>
public class TelegramUpdatesParserChatFilterTests
{
    const long SUPERGROUP_ID = -1001234567890;
    const long FOREIGN_CHAT_ID = -1009999999999;
    const long OWNER_ID = 42;

    [Fact]
    public void ATapInOurSupergroup_IsAccepted()
    {
        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(Tap_Json(SUPERGROUP_ID), SUPERGROUP_ID, OWNER_ID);

        var tap = Assert.Single(batch.CallbackTaps);
        Assert.Equal("opt:1", tap.Data);
    }

    /// <summary>The owner, tapping a button, in a group that is not ours.</summary>
    [Fact]
    public void ATapFromAnotherChat_IsIgnored_EvenThoughItIsTheOwnerTapping()
    {
        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(Tap_Json(FOREIGN_CHAT_ID), SUPERGROUP_ID, OWNER_ID);

        Assert.Empty(batch.CallbackTaps);
    }

    /// <summary>
    /// An inline-mode tap carries no <c>message</c>, so there is no chat to check. Refused: this
    /// bridge only ever puts buttons in its own supergroup, so a tap with nowhere to belong is not
    /// one of ours.
    /// </summary>
    [Fact]
    public void ATapWithNoMessageAtAll_IsIgnored()
    {
        var json = "{\"ok\":true,\"result\":[{\"update_id\":10,\"callback_query\":{\"id\":\"q1\",\"data\":\"opt:1\","
            + "\"from\":{\"id\":" + OWNER_ID + "}}}]}";

        Assert.Empty(TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID).CallbackTaps);
    }

    [Fact]
    public void ATopicRenameInOurSupergroup_IsCollectedForCleanup()
    {
        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(TopicEdited_Json(SUPERGROUP_ID), SUPERGROUP_ID, OWNER_ID);

        Assert.Equal([777L], batch.TopicServiceMessageIds);
    }

    /// <summary>
    /// THE ONE THAT WOULD HAVE WRITTEN INTO A STRANGER'S CHAT. Its id goes to deleteMessage.
    /// </summary>
    [Fact]
    public void ATopicRenameInAnotherChat_IsNeverQueuedForDeletion()
    {
        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(TopicEdited_Json(FOREIGN_CHAT_ID), SUPERGROUP_ID, OWNER_ID);

        Assert.Empty(batch.TopicServiceMessageIds);
    }

    /// <summary>
    /// THE OFFSET STILL MOVES. Filtering an update out must never mean refusing to acknowledge it:
    /// an update that is parsed to nothing and does not advance MaxUpdateId is re-served by
    /// Telegram for ever, which is a poll loop that can never get past a foreign chat's traffic.
    /// </summary>
    [Fact]
    public void AFilteredUpdate_StillAdvancesTheOffset()
    {
        foreach (var json in new[] { Tap_Json(FOREIGN_CHAT_ID), TopicEdited_Json(FOREIGN_CHAT_ID) })
        {
            var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

            Assert.Equal(10L, batch.MaxUpdateId);
        }
    }

    /// <summary>A message from another chat was already refused; asserted so the set stays complete.</summary>
    [Fact]
    public void AMessageFromAnotherChat_IsStillIgnored()
    {
        var json = "{\"ok\":true,\"result\":[{\"update_id\":10,\"message\":{\"message_id\":5,\"text\":\"hello\","
            + "\"chat\":{\"id\":" + FOREIGN_CHAT_ID + "},\"from\":{\"id\":" + OWNER_ID + "}}}]}";

        Assert.Empty(TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID).OwnerMessages);
    }

    /// <summary>
    /// Built by concatenation rather than as a raw interpolated string: the JSON here closes four
    /// braces in a row, which collides with the interpolation delimiters at every <c>$</c> count.
    /// </summary>

    /// <summary>
    /// A chat id that is not a number must be FILTERED, never thrown over: Parse_OwnerMessages has
    /// no catch of its own and the inbound loop backs off WITHOUT advancing the offset, so one
    /// malformed update from a proxy would be re-served for ever — the batch-replay hazard brief B
    /// exists to remove, reached through the filter added here.
    /// </summary>
    [Fact]
    public void AChatIdThatIsNotANumber_IsFilteredOut_NotThrownOver()
    {
        var json = "{\"ok\":true,\"result\":[{\"update_id\":10,\"callback_query\":{\"id\":\"q1\",\"data\":\"opt:1\","
            + "\"from\":{\"id\":" + OWNER_ID + "},\"message\":{\"message_id\":9,\"chat\":{\"id\":\"not-a-number\"}}}}]}";

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        Assert.Empty(batch.CallbackTaps);
        Assert.Equal(10L, batch.MaxUpdateId);
    }

    /// <summary>
    /// A TAP IN THE GENERAL TOPIC carries no <c>message_thread_id</c> — General is recognised
    /// throughout this app by that absence. The chat fence reads only <c>chat.id</c>, so it must
    /// not care; the branch is untested elsewhere and it is the one F6 would most plausibly break.
    /// </summary>
    [Fact]
    public void ATapInTheGeneralTopic_HasNoThread_AndIsStillAccepted()
    {
        var json = "{\"ok\":true,\"result\":[{\"update_id\":11,\"callback_query\":{\"id\":\"q2\",\"data\":\"opt:9\","
            + "\"from\":{\"id\":" + OWNER_ID + "},\"message\":{\"message_id\":42,\"chat\":{\"id\":" + SUPERGROUP_ID + "}}}}]}";

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(json, SUPERGROUP_ID, OWNER_ID);

        var tap = Assert.Single(batch.CallbackTaps);

        Assert.Null(tap.MessageThreadId);
        Assert.Equal("opt:9", tap.Data);
    }

    static string Tap_Json(long chatId)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":10,\"callback_query\":{\"id\":\"q1\",\"data\":\"opt:1\","
            + "\"from\":{\"id\":" + OWNER_ID + "},"
            + "\"message\":{\"message_id\":99,\"message_thread_id\":5,\"chat\":{\"id\":" + chatId + "}}}}]}";
    }

    static string TopicEdited_Json(long chatId)
    {
        return "{\"ok\":true,\"result\":[{\"update_id\":10,\"message\":{\"message_id\":777,"
            + "\"chat\":{\"id\":" + chatId + "},\"from\":{\"id\":" + OWNER_ID + "},"
            + "\"forum_topic_edited\":{\"name\":\"renamed\"}}}]}";
    }
}
