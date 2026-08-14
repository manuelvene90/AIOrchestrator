using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// ONE MESSAGE IN GENERAL, EDITED IN PLACE, showing every open orchestration — the owner's answer to
/// "what is going on across all of them" without asking for it and without a waterfall. A repeat that
/// notifies is the thing this system exists to prevent (decision 14), so the dashboard is one line
/// that changes rather than a new message per update.
///
/// The decision of WHETHER to write is <see cref="TopicStatusLine_Decider"/>'s, unchanged and shared
/// with the per-topic status line: a second copy of "post, edit, or nothing" is exactly the drift
/// that cost this project its window titles tonight.
/// </summary>
public class GeneralDashboardTests
{
    [Fact]
    public void Compose_OpenOrchestrations_LeadsWithAHeadingThenOneLinePerOrchestration()
    {
        var text = GeneralDashboard_Composer.Compose("arb-fix: 3/7 done (42%)\ncrm-2: 1/2 done (50%)");

        var lines = text.Split('\n');

        Assert.Equal(GeneralDashboard_Composer.HEADING, lines[0]);
        Assert.Equal("arb-fix: 3/7 done (42%)", lines[1]);
        Assert.Equal("crm-2: 1/2 done (50%)", lines[2]);
    }

    /// <summary>
    /// NO CLOCK IN THE TEXT, and this is the load-bearing omission. The decider writes only when the
    /// text CHANGED, so a timestamp would make every tick a change: an edit every two seconds
    /// against a rate limit that is already on the ledger, to tell the owner nothing new.
    /// </summary>
    [Fact]
    public void Compose_TheSameSituationTwice_ProducesTheIdenticalText()
    {
        const string BODY = "arb-fix: 3/7 done (42%)";

        Assert.Equal(GeneralDashboard_Composer.Compose(BODY), GeneralDashboard_Composer.Compose(BODY));
    }

    /// <summary>
    /// Nothing open is a REAL state and the dashboard says so, rather than emptying: the decider
    /// treats empty text as "nothing to say", which on an existing message would leave the last busy
    /// reading up for ever — the owner would read a finished machine as still working.
    /// </summary>
    [Fact]
    public void Compose_NothingOpen_StillSaysSomething_SoAStaleReadingIsNeverLeftUp()
    {
        var text = GeneralDashboard_Composer.Compose("no open orchestrations");

        Assert.Contains("no open orchestrations", text);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void Decide_FirstEverTick_Posts()
    {
        var action = TopicStatusLine_Decider.Decide(GeneralDashboard_Composer.Compose("arb-fix: 1/2"), null, null);

        Assert.Equal(TopicStatusActions.Post, action);
    }

    /// <summary>
    /// After a restart there IS a stored message id and NO remembered text, which must EDIT the
    /// message that is already up. A second dashboard appearing after every restart is precisely the
    /// waterfall this replaces — the same restart case the per-topic status line was built around.
    /// </summary>
    [Fact]
    public void Decide_AfterARestart_EditsTheExistingMessage_RatherThanPostingBesideIt()
    {
        var action = TopicStatusLine_Decider.Decide(GeneralDashboard_Composer.Compose("arb-fix: 1/2"), null, 4242L);

        Assert.Equal(TopicStatusActions.Edit, action);
    }

    [Fact]
    public void Decide_NothingChanged_DoesNothing()
    {
        var text = GeneralDashboard_Composer.Compose("arb-fix: 1/2");

        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide(text, text, 4242L));
    }

    [Fact]
    public void Store_RoundTripsTheMessageId()
    {
        Assert.Equal(4242L, GeneralDashboard_Store.Parse_MessageId_OrNull(GeneralDashboard_Store.To_Json(4242L)));
    }

    /// <summary>
    /// Every unusable input answers null, and none of them throws: an unreadable id costs one
    /// duplicate message, while an exception on this path costs the bridge that reads it at startup.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{}")]
    [InlineData("{\"messageId\": null}")]
    [InlineData("{\"messageId\": \"nonsense\"}")]
    [InlineData("{\"somethingElse\": 12}")]
    public void Store_UnusableInput_AnswersNull_WithoutThrowing(string? json)
    {
        Assert.Null(GeneralDashboard_Store.Parse_MessageId_OrNull(json));
    }
}
