using AIOrchestratorCoreLib.Telegram;
using AIOrchestratorCoreLib.Telegram.TopicTraffic;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram.TopicTraffic;

/// <summary>
/// THE RECORD THE STATUS LINE'S MOVE IS DECIDED ON (owner, 2026-09-30): what is newest in each topic, and
/// when the session last spoke there. The planner cases pin the rule; these pin the facts it is handed.
/// </summary>
public class TopicTrafficTests
{
    const long TOPIC = 4242;
    const long OTHER_TOPIC = 5353;
    const long PULSE_ID = 100;

    static readonly DateTime T0 = new(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheHighestIdWins_NotTheLastRecorded()
    {
        var traffic = TopicTraffic_Factory.Create_Empty();

        traffic.Note_Message(TOPIC, 120);
        traffic.Note_Message(TOPIC, 110);

        Assert.Equal(120, traffic.Find_Newest_OrAssumeBuried(TOPIC, PULSE_ID)!.Value.MessageId);
    }

    [Fact]
    public void TopicsAreKeptApart_AndGeneralIsItsOwnKey()
    {
        var traffic = TopicTraffic_Factory.Create_Empty();

        traffic.Note_Message(TOPIC, 120);
        traffic.Note_Message(null, 130);

        Assert.Equal(120, traffic.Find_Newest_OrAssumeBuried(TOPIC, null)!.Value.MessageId);
        Assert.Equal(130, traffic.Find_Newest_OrAssumeBuried(null, null)!.Value.MessageId);
        Assert.Null(traffic.Find_Newest_OrAssumeBuried(OTHER_TOPIC, null));
    }

    /// <summary>
    /// A SEND WHOSE ID TELEGRAM DID NOT HAND BACK (sendPhoto, sendDocument) STILL BURIES THE LINE. The
    /// stand-in is a lower bound on the real id — above the newest known, never above what the line's own
    /// repost will get — so a line that was the last message reads as buried, and its repost reads as the
    /// bottom again.
    /// </summary>
    [Fact]
    public void AnUnidentifiedSend_BuriesALineThatWasTheLastMessage()
    {
        var traffic = TopicTraffic_Factory.Create_Empty();

        traffic.Note_Message(TOPIC, PULSE_ID);

        Assert.False(TopicStatusLine_Planner.Is_Buried(PULSE_ID, traffic.Find_Newest_OrAssumeBuried(TOPIC, PULSE_ID)));

        traffic.Note_UnidentifiedMessage(TOPIC);

        Assert.True(TopicStatusLine_Planner.Is_Buried(PULSE_ID, traffic.Find_Newest_OrAssumeBuried(TOPIC, PULSE_ID)));

        // The photo really got PULSE_ID + 1, and the repost after it gets + 2: the fresh line is the bottom.
        traffic.Note_Message(TOPIC, PULSE_ID + 2);

        Assert.False(TopicStatusLine_Planner.Is_Buried(PULSE_ID + 2, traffic.Find_Newest_OrAssumeBuried(TOPIC, PULSE_ID + 2)));
    }

    /// <summary>
    /// AFTER A RESTART, a line up from the previous process is ASSUMED buried, once: the owner's complaint
    /// was a line twenty messages up, and with nothing recorded nothing would ever say so. The assumption
    /// is recorded, so the repost that follows resolves it — even when Telegram gives that repost the very
    /// next id, because EQUAL is not buried.
    /// </summary>
    [Fact]
    public void AfterARestart_ALineUpFromBefore_IsAssumedBuriedOnce()
    {
        var traffic = TopicTraffic_Factory.Create_Empty();

        var assumed = traffic.Find_Newest_OrAssumeBuried(TOPIC, PULSE_ID);

        Assert.True(TopicStatusLine_Planner.Is_Buried(PULSE_ID, assumed));

        traffic.Note_Message(TOPIC, PULSE_ID + 1);

        Assert.False(TopicStatusLine_Planner.Is_Buried(PULSE_ID + 1, traffic.Find_Newest_OrAssumeBuried(TOPIC, PULSE_ID + 1)));
    }

    /// <summary>With no line up, and nothing recorded, there is nothing to assume.</summary>
    [Fact]
    public void WithNoLineUp_NothingIsAssumed()
    {
        var traffic = TopicTraffic_Factory.Create_Empty();

        Assert.Null(traffic.Find_Newest_OrAssumeBuried(TOPIC, null));

        // …and an unidentified send into a topic nothing is known about records nothing either: the
        // restart assumption covers that topic's line the first time it is asked about.
        traffic.Note_UnidentifiedMessage(TOPIC);

        Assert.Null(traffic.Find_Newest_OrAssumeBuried(TOPIC, null));
    }

    /// <summary>
    /// THE SESSION'S CLOCK, apart from the newest message: the owner's and the app's messages never touch
    /// it, and a stamp arriving out of order never moves it backwards.
    /// </summary>
    [Fact]
    public void TheSessionsLastMessage_IsTheLatestStamp_AndOnlyTheSessionsOwn()
    {
        var traffic = TopicTraffic_Factory.Create_Empty();

        Assert.Null(traffic.Find_LastSessionMessageAtUtc_OrNull(TOPIC));

        traffic.Note_Message(TOPIC, 120);

        Assert.Null(traffic.Find_LastSessionMessageAtUtc_OrNull(TOPIC));

        traffic.Note_SessionMessage(TOPIC, T0.AddSeconds(30));
        traffic.Note_SessionMessage(TOPIC, T0);

        Assert.Equal(T0.AddSeconds(30), traffic.Find_LastSessionMessageAtUtc_OrNull(TOPIC));
        Assert.Null(traffic.Find_LastSessionMessageAtUtc_OrNull(OTHER_TOPIC));
    }
}
