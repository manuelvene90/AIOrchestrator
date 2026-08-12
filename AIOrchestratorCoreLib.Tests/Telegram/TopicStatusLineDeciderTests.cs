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
    /// THE SECOND TICK OF AN EMPTIED ORCHESTRATION — the case that was missing, and the whole loop.
    ///
    /// Tick 1 emptied the line and the builder emitted the bare title, which the cache now holds.
    /// Tick 2 builds the same title, so the identical-text rule stops it. The earlier version
    /// short-circuited on emptiness ABOVE that check and compared an empty string against the cached
    /// title forever: same title, same message, a rejected edit every two seconds for as long as the
    /// app ran — and the not-modified branch advanced the cache without setting the backoff, so
    /// nothing gated it.
    ///
    /// The state is STABLE, unlike the original spin, which self-cleared the moment a duration
    /// changed. This one would never have cleared.
    /// </summary>
    [Fact]
    public void ASettledEmptiedOrchestrationStopsAfterOneEdit()
    {
        const string bareTitle = "CRM invoice crash";

        Assert.Equal(TopicStatusActions.Edit, TopicStatusLine_Decider.Decide(bareTitle, "imp-1  fix the parser  12 min", EXISTING));
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide(bareTitle, bareTitle, EXISTING));
    }

    /// <summary>
    /// Emptiness reaching the decider means there is nothing to say AND nothing posted — the builder
    /// emits the title instead when a message is up, so "empty with a message" is not a producible
    /// state and is deliberately not special-cased here. Special-casing it is what caused the spin.
    /// </summary>
    [Fact]
    public void EmptinessIsSilenceBecauseTheBuilderNeverEmitsItWithAMessageUp()
    {
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide("", null, null));
        Assert.Equal(TopicStatusActions.None, TopicStatusLine_Decider.Decide("   ", "anything", null));
    }

    /// <summary>
    /// SPLIT, because one test asserting both predicates is one state with two routes — item 20, in
    /// the tests written to answer an item 20 finding. Two mutations reddened the same method, which
    /// told us something failed and not which.
    /// </summary>
    [Fact]
    public void NotModifiedIsSuccess()
    {
        Assert.True(TopicStatusLine_Decider.Is_MessageAlreadyCurrent("Bad Request: message is not modified"));
        Assert.False(TopicStatusLine_Decider.Is_MessageAlreadyCurrent("Bad Request: message to edit not found"));
    }

    [Fact]
    public void AMissingMessageIsTerminal()
    {
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

        // BOTH DIRECTIONS. On its own the Assert.False above is satisfied by an always-false
        // Is_MessageGone — it pins the exclusion while proving nothing about the inclusion, so a
        // predicate that had stopped working entirely would keep it green.
        Assert.True(TopicStatusLine_Decider.Is_MessageGone("Bad Request: message to edit not found"));
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
