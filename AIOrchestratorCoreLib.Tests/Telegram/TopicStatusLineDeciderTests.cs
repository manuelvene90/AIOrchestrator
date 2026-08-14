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
    /// THE REPOST'S OWN WORDING FOR THE SAME THING. A repost DELETES the old message first, and
    /// Telegram answers a delete of a message that is not there with "message to delete not found" —
    /// a different sentence for the state the two above already describe.
    ///
    /// Without it that error fell to the generic catch, which backs off and RETRIES: an owner who
    /// deleted the status line by hand left a topic whose text never changes (so the edit path that
    /// clears the id is never taken) reposting into a delete that can never succeed, every backoff
    /// period, for the life of the app. Terminal here instead — the id is forgotten and the next tick
    /// posts a fresh line.
    /// </summary>
    [Fact]
    public void AMessageThatIsAlreadyDeletedIsTerminalToo()
    {
        Assert.True(TopicStatusLine_Decider.Is_MessageGone("Bad Request: message to delete not found"));

        // The exclusion still holds: a message that exists and merely cannot be edited is NOT gone,
        // and clearing the id there posts a second line beside a frozen one.
        Assert.False(TopicStatusLine_Decider.Is_MessageGone("Bad Request: message can't be edited"));
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

    /// <summary>
    /// A REFUSED DELETE IS NOT A GONE MESSAGE, and keeping the two apart is the whole of rev-1's F1.
    ///
    /// Telegram refuses a delete for reasons that leave the message standing: past the 48-hour
    /// deletion window, or without `can_delete_messages`. Treating that as GONE would clear the id
    /// and post a second line beside an undeletable one — the two-lines-in-one-topic defect this
    /// feature exists to prevent, arriving through a third door, and it is the identical unsoundness
    /// that keeps "message can't be edited" out of Is_MessageGone.
    ///
    /// The 48-hour trigger is the one that matters and it needs no missing permission at all: a
    /// buried status line on a quiet orchestration is precisely the thing that sits untouched for two
    /// days.
    /// </summary>
    [Theory]
    [InlineData("Bad Request: message can't be deleted")]
    [InlineData("Bad Request: message can't be deleted for everyone")]
    [InlineData("Bad Request: message can not be deleted")]
    [InlineData("Bad Request: not enough rights to delete a message")]
    [InlineData("CHAT_ADMIN_REQUIRED")]
    public void ARefusedDeleteIsRecognisedAndIsNotTreatedAsGone(string errorMessage)
    {
        Assert.True(TopicStatusLine_Decider.Is_DeleteRefused(errorMessage));
        Assert.False(TopicStatusLine_Decider.Is_MessageGone(errorMessage));
    }

    /// <summary>
    /// And the other direction: a message that is genuinely GONE is not a refusal. Latching a topic
    /// off for that would stop it ever moving its line again over an error that clears itself the
    /// moment a fresh message is posted.
    ///
    /// Asserted BOTH WAYS on both predicates, because either one answering `true` to everything would
    /// satisfy half of this pair on its own.
    /// </summary>
    [Fact]
    public void AGoneMessageIsNotARefusal()
    {
        Assert.False(TopicStatusLine_Decider.Is_DeleteRefused("Bad Request: message to delete not found"));
        Assert.False(TopicStatusLine_Decider.Is_DeleteRefused("Bad Request: message to edit not found"));
        Assert.True(TopicStatusLine_Decider.Is_MessageGone("Bad Request: message to delete not found"));
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
