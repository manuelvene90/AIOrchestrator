using AIOrchestratorCoreLib.Telegram;
using Xunit;

namespace AIOrchestratorCoreLib.Tests.Telegram;

/// <summary>
/// Contract item 5, and the reason the decision was pulled out of the engine at all: these three
/// properties are what the feature IS, and inside the tick they could only have been asserted in a
/// commit message.
/// </summary>
public class TopicStatusLineDeciderTests
{
    const long EXISTING = 4242;

    /// <summary>A change edits the message that is already there.</summary>
    [Fact]
    public void AChangedLineEdits()
    {
        Assert.Equal(TopicStatusActions.Edit, TopicStatusLine_Decider.Decide("new text", "old text", EXISTING));
    }

    /// <summary>
    /// An identical line does NOTHING. An edit that writes the same text is a wasted API call, and
    /// against the 429 limit already on the ledger it is a real cost rather than a tidiness point.
    /// </summary>
    [Fact]
    public void AnIdenticalLineDoesNothing()
    {
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide("same text", "same text", EXISTING));
    }

    /// <summary>
    /// THE RESTART CASE, and the one that is invisible until somebody restarts the app. The
    /// remembered text is in memory and the id is in session.json — so after a restart there is an
    /// id and NO remembered text, which must EDIT rather than post beside it. A second status
    /// message appearing after every restart is the defect this feature replaces.
    /// </summary>
    [Fact]
    public void ARestartEditsTheExistingMessageRatherThanPostingASecond()
    {
        Assert.Equal(TopicStatusActions.Edit, TopicStatusLine_Decider.Decide("current state", null, EXISTING));
    }

    /// <summary>The genuine first time: no id anywhere, so there is nothing to edit.</summary>
    [Fact]
    public void TheFirstEverLineIsPosted()
    {
        Assert.Equal(TopicStatusActions.Post, TopicStatusLine_Decider.Decide("first state", null, null));
    }

    /// <summary>
    /// Nothing to say stays silent even when a message exists — an orchestration with no ledger and
    /// no members should not get an empty line drawn over its topic.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyLineIsNeverWritten(string statusText)
    {
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide(statusText, "old text", EXISTING));
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide(statusText, null, null));
    }

    /// <summary>
    /// Whitespace-only is not "unchanged" by accident: an empty line and a remembered empty line
    /// must both be silent for the SAME reason, not because they happen to compare equal.
    /// </summary>
    [Fact]
    public void AnEmptyLineIsSilentEvenWhenItMatchesWhatWasWritten()
    {
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide("", "", EXISTING));
    }
}
