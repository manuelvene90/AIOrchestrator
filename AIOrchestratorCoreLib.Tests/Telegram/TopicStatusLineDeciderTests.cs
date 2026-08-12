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

    /// <summary>
    /// N2: NOTHING TO SAY, WITH A MESSAGE ALREADY UP, IS NOT NOTHING TO DO. Closing the last live
    /// member left the line's last words standing — a member's row, with a duration that keeps
    /// reading, for a member that no longer exists. Silence freezes it; an edit takes it down.
    /// </summary>
    [Fact]
    public void AnEmptiedLineWithAMessageUpIsEditedDownRatherThanLeftFrozen()
    {
        Assert.Equal(TopicStatusActions.Edit, TopicStatusLine_Decider.Decide("", "imp-1  fix the parser  12 min", EXISTING));
        Assert.Equal(TopicStatusActions.Edit, TopicStatusLine_Decider.Decide("   ", "imp-1  fix the parser  12 min", EXISTING));
    }

    /// <summary>But with NO message posted, an empty line is still silence.</summary>
    [Fact]
    public void AnEmptiedLineWithNoMessageStaysSilent()
    {
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide("", null, null));
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide("   ", "anything", null));
    }

    /// <summary>
    /// N1: these were unreachable from the suite — `internal sealed`, no InternalsVisibleTo — so a
    /// finder deleted three engine guards at once and 610 stayed green. Pure string questions belong
    /// where they can be asked.
    /// </summary>
    [Fact]
    public void NotModifiedIsSuccessAndAMissingMessageIsTerminal()
    {
        Assert.True(TopicStatusLine_Decider.Is_MessageAlreadyCurrent("Bad Request: message is not modified"));
        Assert.False(TopicStatusLine_Decider.Is_MessageAlreadyCurrent("Bad Request: message to edit not found"));

        Assert.True(TopicStatusLine_Decider.Is_MessageGone("Bad Request: message to edit not found"));
        Assert.True(TopicStatusLine_Decider.Is_MessageGone("MESSAGE_ID_INVALID"));
    }

    /// <summary>
    /// N7: "message can't be edited" means the message EXISTS and is not editable. Treating it as
    /// gone cleared the id and posted a second line while the frozen one stayed up — two status
    /// lines in one topic, which is the defect this feature exists to prevent, by another door.
    /// </summary>
    [Fact]
    public void AMessageThatExistsButCannotBeEditedIsNotTreatedAsGone()
    {
        Assert.False(TopicStatusLine_Decider.Is_MessageGone("Bad Request: message can't be edited"));
    }

    /// <summary>The genuine first time: no id anywhere, so there is nothing to edit.</summary>
    [Fact]
    public void TheFirstEverLineIsPosted()
    {
        Assert.Equal(TopicStatusActions.Post, TopicStatusLine_Decider.Decide("first state", null, null));
    }

    /// <summary>
    /// Nothing to say, and nothing posted yet, stays silent. UPDATED for N2: with a message already
    /// up, "nothing to say" is no longer nothing to do — leaving it would freeze the last row it
    /// printed, so that case now EDITS and is asserted just above.
    ///
    /// This is also the discriminating case for the whitespace guard: without it, whitespace against
    /// a different remembered value and no id would POST.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyLineIsNeverWrittenWhenNothingIsPosted(string statusText)
    {
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide(statusText, "a real line", null));
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide(statusText, null, null));
    }

    /// <summary>
    /// The identical-text guard, alone: only it can produce None here, because the text is neither
    /// empty nor different. Its sibling above pins the whitespace guard the same way, so neither
    /// case can pass for the other's reason — the failure this pair replaced.
    /// </summary>
    [Fact]
    public void IdenticalNonEmptyTextIsSilentThroughTheOtherGuard()
    {
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide("a real line", "a real line", EXISTING));
    }
}
