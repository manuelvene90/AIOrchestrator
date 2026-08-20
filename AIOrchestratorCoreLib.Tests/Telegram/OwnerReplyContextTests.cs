using System.Text.Json.Nodes;
using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TelegramOwnerMessage;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// REPLYING TO A MESSAGE ADDS CONTEXT (owner, 2026-08-20). Before this the quote was dropped and a
/// reply arrived as a bare message — worse than it sounds, because from their side it LOOKED like
/// context had been attached.
///
/// The hard part is not reading the field. In a forum supergroup Telegram attaches a
/// `reply_to_message` to EVERY message in a topic, pointing at the topic's root, so taking it at
/// face value would tag every message they ever send with the same phantom quote. These tests exist
/// mostly to pin that distinction.
/// </summary>
public class OwnerReplyContextTests
{
    const long CHAT = -100123;
    const long OWNER = 42;
    const long THREAD = 799;

    [Fact]
    public void AGenuineReplyCarriesTheQuotedText()
    {
        var parsed = Parse_Message(replyToMessageId: 1050, replyToText: "the branch does not compile");

        Assert.Equal("the branch does not compile", parsed!.ReplyToText);
    }

    /// <summary>
    /// THE PHANTOM QUOTE. Telegram points every forum message at its topic root, whose message_id IS
    /// the thread id. Without this test the feature would attach the topic's opening message to every
    /// message the owner ever sent.
    /// </summary>
    [Fact]
    public void TheTopicRootIsNotAReply()
    {
        var parsed = Parse_Message(replyToMessageId: THREAD, replyToText: "topic opened");

        Assert.Null(parsed!.ReplyToText);
    }

    [Fact]
    public void APlainMessageCarriesNoQuote()
    {
        Assert.Null(Parse_Message(replyToMessageId: null, replyToText: null)!.ReplyToText);
    }

    /// <summary>An empty or whitespace quote is no context at all, and must not draw the marker.</summary>
    [Fact]
    public void AnEmptyQuoteIsNotContext()
    {
        Assert.Null(Parse_Message(replyToMessageId: 1050, replyToText: "   ")!.ReplyToText);
    }

    [Fact]
    public void TheQuoteIsPutInFrontAndMarkedAsAQuote()
    {
        var text = OwnerReplyContext_Formatter.Prepend_OrSame("that one", "the branch does not compile");

        Assert.StartsWith("↩ replying to: \"the branch does not compile\"", text);
        Assert.EndsWith("that one", text);
    }

    /// <summary>
    /// A wall of text pointed at must not be pasted into the channel a second time in full.
    /// </summary>
    [Fact]
    public void ALongQuoteIsCapped()
    {
        var text = OwnerReplyContext_Formatter.Prepend_OrSame("this bit", new string('x', 900));

        Assert.Contains("…", text);
        Assert.True(text.Length < 500, $"a long quote was not capped — the entry was {text.Length} chars");
    }

    [Fact]
    public void NothingRepliedToLeavesTheMessageUntouched()
    {
        Assert.Equal("just a message", OwnerReplyContext_Formatter.Prepend_OrSame("just a message", null));
        Assert.Equal("just a message", OwnerReplyContext_Formatter.Prepend_OrSame("just a message", "  "));
    }

    static ITelegramOwnerMessage? Parse_Message(long? replyToMessageId, string? replyToText)
    {
        var message = new JsonObject
        {
            ["message_id"] = 2000,
            ["text"] = "that one",
            ["chat"] = new JsonObject { ["id"] = CHAT },
            ["from"] = new JsonObject { ["id"] = OWNER },
            ["message_thread_id"] = THREAD,
        };

        if (replyToMessageId != null)
        {
            var repliedTo = new JsonObject { ["message_id"] = replyToMessageId.Value };

            if (replyToText != null)
                repliedTo["text"] = replyToText;

            message["reply_to_message"] = repliedTo;
        }

        var payload = new JsonObject
        {
            ["result"] = new JsonArray(new JsonObject { ["update_id"] = 1, ["message"] = message }),
        };

        var batch = TelegramUpdates_Parser.Parse_OwnerMessages(payload.ToJsonString(), CHAT, OWNER);

        return batch.OwnerMessages.SingleOrDefault();
    }
}
