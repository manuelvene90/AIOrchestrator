using AIOrchestratorCoreLib.Running.ReplyLinks;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Running.ReplyLinks;

/// <summary>The two halves of a reply link meet here — see <see cref="IReplyLinks"/>.</summary>
public class ReplyLinksTests
{
    const string CHANNEL = "/tmp/aiorch-reply-links/fincanva-5/owner-channel.md";

    [Fact]
    public void AnAnswerToARecordedOwnerMessage_FindsThatMessage()
    {
        var links = ReplyLinks_Factory.Create_InMemory();

        links.Record_OwnerMessage(CHANNEL, entryIndex: 808, telegramMessageId: 5001);
        links.Record_Answer(CHANNEL, answerEntryIndex: 809, answeredOwnerEntryIndex: 808);

        Assert.Equal(5001, links.Find_AnsweredTelegramMessage_OrNull(CHANNEL, 809));
    }

    [Fact]
    public void AnEntryNobodyLinked_FindsNothing()
    {
        var links = ReplyLinks_Factory.Create_InMemory();
        links.Record_OwnerMessage(CHANNEL, 808, 5001);

        Assert.Null(links.Find_AnsweredTelegramMessage_OrNull(CHANNEL, 809));
    }

    [Fact]
    public void AnAnswerToAnOwnerEntryWithNoTelegramMessage_FindsNothing()
    {
        // A restart forgets the Telegram ids; the entry then goes out unthreaded, as before.
        var links = ReplyLinks_Factory.Create_InMemory();
        links.Record_Answer(CHANNEL, 809, 808);

        Assert.Null(links.Find_AnsweredTelegramMessage_OrNull(CHANNEL, 809));
    }

    [Fact]
    public void TwoSpellingsOfOneFile_AreOneChannel()
    {
        var links = ReplyLinks_Factory.Create_InMemory();

        links.Record_OwnerMessage("/tmp/aiorch-reply-links/fincanva-5/../fincanva-5/owner-channel.md", 808, 5001);
        links.Record_Answer(CHANNEL, 809, 808);

        Assert.Equal(5001, links.Find_AnsweredTelegramMessage_OrNull(CHANNEL, 809));
    }

    [Fact]
    public void ChannelsDoNotShareIndices()
    {
        var links = ReplyLinks_Factory.Create_InMemory();

        links.Record_OwnerMessage(CHANNEL, 808, 5001);
        links.Record_Answer("/tmp/aiorch-reply-links/fincanva-6/owner-channel.md", 809, 808);

        Assert.Null(links.Find_AnsweredTelegramMessage_OrNull("/tmp/aiorch-reply-links/fincanva-6/owner-channel.md", 809));
    }

    [Fact]
    public void TheOldestLinksAreDropped_NotTheNewest()
    {
        var links = ReplyLinks_Factory.Create_InMemory();

        for (var index = 1; index <= 300; index++)
        {
            links.Record_OwnerMessage(CHANNEL, index * 2, 10_000 + index);
            links.Record_Answer(CHANNEL, index * 2 + 1, index * 2);
        }

        Assert.Null(links.Find_AnsweredTelegramMessage_OrNull(CHANNEL, 3));
        Assert.Equal(10_300, links.Find_AnsweredTelegramMessage_OrNull(CHANNEL, 601));
    }
}
